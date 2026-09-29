using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Core.Tests.Safety;

public sealed class RunningGameDetectorTests
{
    private const string Game = @"D:\Games\Example";
    private const int OwnSession = 1;

    private readonly InMemoryFileSystem _files = new();
    private readonly FakeProcessSource _processes = new();
    private readonly FakeTimeProvider _time = new();

    public RunningGameDetectorTests() => _files.AddDirectory(Game);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void A_process_started_from_the_folder_or_below_it_is_found()
    {
        _processes.Add(10, "Example", @"D:\Games\Example\Example.exe");
        _processes.Add(11, "Launcher", @"D:\Games\Example\bin\x64\Launcher.exe");
        _processes.Add(12, "Other", @"D:\Games\Other\Other.exe");

        Find(Game).Should().Equal(
            new RunningProcess(10, "Example.exe", @"D:\Games\Example\Example.exe"),
            new RunningProcess(11, "Launcher.exe", @"D:\Games\Example\bin\x64\Launcher.exe"));
    }

    [Fact]
    public void The_folder_matches_in_another_case_with_a_trailing_separator_but_not_as_a_name_prefix()
    {
        _processes.Add(10, "Example", @"d:\games\EXAMPLE\Example.exe");
        _processes.Add(11, "Sequel", @"D:\Games\Example 2\Sequel.exe");

        Find(@"D:\Games\Example\").Select(process => process.ProcessId).Should().Equal(10);
        Find(@"D:\Games\Exam").Should().BeEmpty();
    }

    [Fact]
    public void A_process_started_through_a_link_to_the_folder_is_found()
    {
        _files.AddDirectoryLink(@"C:\Link", Game);
        _processes.Add(10, "Example", @"C:\Link\Example.exe");

        Find(Game).Select(process => process.ProcessId).Should().Equal(10);
        Find(@"C:\Link").Select(process => process.ProcessId).Should().Equal(10);
    }

    [Fact]
    public void A_hidden_process_of_the_own_session_is_matched_by_an_executable_in_the_folder()
    {
        _files.AddFile(@"D:\Games\Example\Binaries\Win64\Example-Win64-Shipping.exe", "");
        _files.AddFile(@"D:\Games\Example\a\b\c\d\Deep.exe", "");
        _processes.Add(10, "example-win64-shipping", imagePath: null);
        _processes.Add(11, "Example-Win64-Shipping", imagePath: null, sessionId: 2);
        _processes.Add(12, "Unrelated", imagePath: null);
        _processes.Add(13, "Deep", imagePath: null);

        Find(Game).Should().Equal(new RunningProcess(10, "Example-Win64-Shipping.exe", ExecutablePath: null));
    }

    [Fact]
    public void An_idle_folder_has_no_processes()
    {
        _processes.Add(10, "Other", @"C:\Programs\Other.exe");

        Find(Game).Should().BeEmpty();
    }

    [Fact]
    public async Task Waiting_ends_when_every_process_has_exited()
    {
        var first = new RunningProcess(10, "Example.exe", @"D:\Games\Example\Example.exe");
        var second = new RunningProcess(11, "Launcher.exe", @"D:\Games\Example\Launcher.exe");
        _processes.Add(10, "Example", first.ExecutablePath);
        _processes.Add(11, "Launcher", second.ExecutablePath);

        var waiting = Detector().WaitForExitAsync([first, second], Token);
        await Eventually.SettleAsync();
        _processes.Exit(10);
        await Eventually.SettleAsync();
        waiting.IsCompleted.Should().BeFalse();
        _processes.Exit(11);

        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    [Fact]
    public async Task A_process_whose_id_went_to_another_program_has_exited()
    {
        _processes.Add(10, "Reused", @"C:\Programs\Reused.exe");
        _processes.Add(11, "Example.bin", @"D:\Games\Example\Example.bin");

        await Detector().WaitForExitAsync([new RunningProcess(10, "Example.exe", @"D:\Games\Example\Example.exe")], Token)
            .WaitAsync(TimeSpan.FromSeconds(10), Token);
        var waiting = Detector().WaitForExitAsync([new RunningProcess(11, "Example.bin", @"D:\Games\Example\Example.bin")], Token);
        await Eventually.SettleAsync();

        waiting.IsCompleted.Should().BeFalse("only the .exe of an executable is left out of its process name");
        _processes.Exit(11);
        await waiting.WaitAsync(TimeSpan.FromSeconds(10), Token);
    }

    [Fact]
    public async Task A_process_that_cannot_be_waited_on_is_looked_for_again_until_it_is_gone()
    {
        _processes.Add(10, "Example", @"D:\Games\Example\Example.exe", waitable: false);

        var waiting = Detector().WaitForExitAsync([new RunningProcess(10, "Example.exe", @"D:\Games\Example\Example.exe")], Token);
        await Eventually.SettleAsync();
        _time.Advance(RunningGameDetector.RecheckInterval);
        await Eventually.SettleAsync();
        waiting.IsCompleted.Should().BeFalse();
        _processes.Replace(10, "Reused");

        await Eventually.HoldsAsync(() => waiting.IsCompleted, "the reused id is not taken for the game",
            () => _time.Advance(RunningGameDetector.RecheckInterval));
        await waiting;
    }

    private RunningGameDetector Detector() => new(_processes, _files, _time);

    private ValueList<RunningProcess> Find(string directory) => Detector().FindProcessesIn(directory, Token);

    private sealed class FakeProcessSource : IProcessSource
    {
        private readonly Lock _gate = new();
        private readonly List<(ProcessEntry Entry, bool Waitable)> _processes = [];
        private readonly Dictionary<int, TaskCompletionSource<bool>> _exits = [];

        public int CurrentSessionId => OwnSession;

        public void Add(int id, string name, string? imagePath, int sessionId = OwnSession, bool waitable = true)
        {
            lock (_gate)
            {
                _processes.Add((new ProcessEntry(id, name, sessionId, imagePath), waitable));
            }
        }

        public void Replace(int id, string name)
        {
            lock (_gate)
            {
                _processes.RemoveAll(process => process.Entry.Id == id);
                _processes.Add((new ProcessEntry(id, name, OwnSession, null), true));
            }
        }

        public void Exit(int id)
        {
            TaskCompletionSource<bool>? exit;
            lock (_gate)
            {
                _processes.RemoveAll(process => process.Entry.Id == id);
                _exits.Remove(id, out exit);
            }

            exit?.TrySetResult(true);
        }

        public IReadOnlyList<ProcessEntry> Snapshot()
        {
            lock (_gate)
            {
                return [.. _processes.Select(process => process.Entry)];
            }
        }

        public Task<bool> WaitForExitAsync(int processId, string name, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_processes.FirstOrDefault(process => process.Entry.Id == processId) is not { Entry: not null } running
                    || !running.Entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(true);
                }

                if (!running.Waitable)
                {
                    return Task.FromResult(false);
                }

                var exit = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _exits[processId] = exit;
                return exit.Task.WaitAsync(cancellationToken);
            }
        }
    }
}
