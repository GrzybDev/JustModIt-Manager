using System.Diagnostics;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class ProcessAndLockTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<string> _links = [];

    public ProcessAndLockTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only.");
        Directory.CreateDirectory(_directory);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static string OwnExecutable => Environment.ProcessPath!;

    [Fact]
    public void This_process_is_found_running_from_its_folder_also_through_a_junction()
    {
        var own = WindowsProcessSource.Instance.Snapshot().Single(process => process.Id == Environment.ProcessId);
        own.Name.Should().BeEquivalentTo(Path.GetFileNameWithoutExtension(OwnExecutable));
        own.SessionId.Should().Be(WindowsProcessSource.Instance.CurrentSessionId);
        var folder = Path.GetDirectoryName(OwnExecutable)!;
        var junction = Path.Combine(_directory, "junction");
        CreateJunction(junction, folder);
        var detector = new RunningGameDetector(WindowsProcessSource.Instance, PhysicalFileSystem.Instance, TimeProvider.System);

        detector.FindProcessesIn(folder, Token).Should().Contain(process => process.ProcessId == Environment.ProcessId);
        detector.FindProcessesIn(junction, Token).Should().Contain(process => process.ProcessId == Environment.ProcessId);
        detector.FindProcessesIn(_directory, Token).Should().BeEmpty();
    }

    [Fact]
    public async Task Waiting_ends_when_the_process_exits()
    {
        using var child = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 2 127.0.0.1 > nul") { CreateNoWindow = true })!;

        var waited = await WindowsProcessSource.Instance.WaitForExitAsync(child.Id, "cmd", Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        waited.Should().BeTrue();
        child.HasExited.Should().BeTrue();
        (await WindowsProcessSource.Instance.WaitForExitAsync(int.MaxValue - 1, "cmd", Token)).Should().BeTrue();
    }

    [Fact]
    public async Task A_process_id_held_by_another_program_counts_as_exited() =>
        (await WindowsProcessSource.Instance.WaitForExitAsync(Environment.ProcessId, "ExampleGame", Token)
            .WaitAsync(TimeSpan.FromSeconds(10), Token)).Should().BeTrue();

    [Fact]
    public void The_program_holding_a_file_is_named()
    {
        var path = Path.Combine(_directory, "held.pak");
        File.WriteAllText(path, "data");
        var free = Path.Combine(_directory, "free.pak");
        File.WriteAllText(free, "data");

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var report = new FileLockProbe(RestartManagerLockHolders.Instance).Probe([path, free], Token);

            report.InUse.Should().Equal(path);
            report.Holders.Should().ContainEquivalentOf(Path.GetFileName(OwnExecutable));
        }

        RestartManagerLockHolders.Instance.FindHolders([path]).Should().BeEmpty();
    }

    public void Dispose()
    {
        foreach (var link in _links)
        {
            Directory.Delete(link);
        }

        Directory.Delete(_directory, recursive: true);
    }

    private void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", link, target },
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.SkipWhen(process.ExitCode != 0, "Junctions cannot be created on this machine.");
        _links.Add(link);
        PathComparison.AreSame(PhysicalFileSystem.Instance.GetCanonicalPath(link), target).Should().BeTrue();
    }
}
