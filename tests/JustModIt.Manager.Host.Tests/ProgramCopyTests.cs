using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Host.Tests;

public sealed class ProgramCopyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<ProcessEntry> _running = [];

    private string Program => Path.Combine(_root, "mount", "usr", "bin");

    private string Copies => Path.Combine(_root, "cache", "program");

    [Fact]
    public void The_program_is_copied_once_per_version_and_again_when_it_changed()
    {
        Directory.CreateDirectory(Program);
        File.WriteAllText(Path.Combine(Program, "ExampleApp"), "program 1.0.0");
        File.WriteAllText(Path.Combine(Program, "libexample.so"), "library");
        File.WriteAllText(Path.Combine(Program, "sq.version"), "manifest");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(Program, "ExampleApp"), (UnixFileMode)0b111_101_101);
        }

        var copy = Prepare("1.0.0");

        copy.Should().Be(Path.Combine(Copies, "1.0.0", "usr", "bin"));
        Directory.EnumerateFiles(copy).Select(Path.GetFileName).Should().BeEquivalentTo("ExampleApp", "libexample.so", "sq.version");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(Path.Combine(copy, "ExampleApp")).Should().Be((UnixFileMode)0b111_101_101);
        }

        var marker = Path.Combine(Copies, "1.0.0", "marker");
        File.WriteAllText(marker, "");
        Prepare("1.0.0");
        File.Exists(marker).Should().BeTrue("a copy that holds the program's files is used as it is");

        File.WriteAllText(Path.Combine(Program, "ExampleApp"), "program 1.0.0, built again");
        Prepare("1.0.0");
        File.Exists(marker).Should().BeFalse();
        File.ReadAllText(Path.Combine(copy, "ExampleApp")).Should().Be("program 1.0.0, built again");
    }

    [Fact]
    public void A_copy_goes_once_nothing_runs_from_it_and_a_partial_one_once_its_process_ended()
    {
        Directory.CreateDirectory(Program);
        File.WriteAllText(Path.Combine(Program, "ExampleApp"), "program");
        var old = Path.Combine(Copies, "0.9.0", "usr", "bin");
        var cutShort = Path.Combine(Copies, "4100.partial");
        var copying = Path.Combine(Copies, "4200.partial");
        foreach (var folder in new[] { old, cutShort, copying })
        {
            Directory.CreateDirectory(folder);
        }

        // A window of the old version loads a library from its copy on first use, and another start is copying.
        _running.Add(new ProcessEntry(4300, "ExampleApp", 1000, Path.Combine(old, "ExampleApp")));
        _running.Add(new ProcessEntry(4200, "ExampleApp", 1000, Path.Combine(_root, "mount", "usr", "bin", "ExampleApp")));
        Prepare("1.0.0");
        Directory.EnumerateDirectories(Copies).Select(Path.GetFileName).Should().BeEquivalentTo("0.9.0", "1.0.0", "4200.partial");

        _running.Clear();
        Prepare("1.0.0");
        Directory.EnumerateDirectories(Copies).Select(Path.GetFileName).Should().Equal("1.0.0");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string Prepare(string version) => ProgramCopy.Prepare(Program, Copies, version, () => _running);
}
