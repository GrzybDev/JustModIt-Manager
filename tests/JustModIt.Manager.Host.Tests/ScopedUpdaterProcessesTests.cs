using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Host.Tests;

public sealed class ScopedUpdaterProcessesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    [SupportedOSPlatform("linux")]
    public void The_updater_runs_from_a_copy_outside_the_appimages_mount_in_a_scope_of_its_own()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The updater runs from an AppImage's mount only on Linux.");
        // The AppImage's runtime mounts its files at a temporary folder and runs the app and its updater from usr/bin there.
        var mount = Directory.CreateDirectory(Path.Combine(_root, ".mount_ExampleApp", "usr", "bin")).FullName;
        File.WriteAllText(Path.Combine(mount, "UpdateNix"), "updater");
        File.WriteAllText(Path.Combine(mount, "sq.version"), "manifest");
        var copy = Path.Combine(_root, "velopack", "ExampleApp", "updater", "usr", "bin");
        var shell = new RecordingShell();
        var processes = new ScopedUpdaterProcesses(shell, () => copy);
        string[] arguments = ["--silent", "apply", "--waitPid", "42", "--", "--background"];

        processes.StartProcess(Path.Combine(mount, "UpdateNix"), arguments, mount, showWindow: false);

        var (executable, ran, folder) = shell.Ran.Should().ContainSingle().Subject;
        (executable, folder).Should().Be((Path.Combine(copy, "UpdateNix"), copy));
        ran.Should().Equal(arguments);
        (File.ReadAllText(Path.Combine(copy, "UpdateNix")), File.ReadAllText(Path.Combine(copy, "sq.version"))).Should().Be(("updater", "manifest"));

        shell.Fails = true;
        processes.Invoking(updater => updater.StartProcess(Path.Combine(mount, "UpdateNix"), arguments, mount, showWindow: false))
            .Should().Throw<IOException>("an updater that never started fails the update, so the agent keeps running");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private sealed class RecordingShell : IDesktopShell
    {
        public List<(string Executable, IReadOnlyList<string> Arguments, string? Folder)> Ran { get; } = [];

        public bool Fails { get; set; }

        public bool Open(string target) => throw new InvalidOperationException("The updater opens nothing.");

        public bool Start(ProgramStart program) => throw new InvalidOperationException("The updater starts no game.");

        public int? Run(string executable, IReadOnlyList<string> arguments, string? workingDirectory = null)
        {
            Ran.Add((executable, arguments, workingDirectory));
            return Fails ? null : 42;
        }
    }
}
