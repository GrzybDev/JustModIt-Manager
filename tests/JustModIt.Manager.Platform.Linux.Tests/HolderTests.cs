using System.Diagnostics;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Safety;

namespace JustModIt.Manager.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class HolderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    public HolderTests()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "The holders are read from /proc, which only Linux has.");
        Directory.CreateDirectory(_directory);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_program_holding_a_file_is_named_but_this_process_never_is()
    {
        var held = Write("held.pak");
        var free = Write("free.pak");
        var own = Write("own.pak");
        await using var ownFile = File.OpenRead(own);
        // The shell opens the file and then becomes sleep, which keeps it open.
        using var child = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "exec 3<\"$1\"; exec sleep 30", "sh", held } })!;
        try
        {
            var probe = new FileLockProbe(ProcLockHolders.Instance);
            var report = probe.Probe([held, free, own], Token);
            for (var attempt = 0; !report.Holders.Contains("sleep") && attempt < 100; attempt++)
            {
                await Task.Delay(100, Token);
                report = probe.Probe([held, free, own], Token);
            }

            report.InUse.Should().Equal(held);
            report.Holders.Should().Equal("sleep");
            ProcLockHolders.Instance.FindHolders([held, free, own]).Should().Equal(new FileHolder("sleep", held));
        }
        finally
        {
            child.Kill();
            await child.WaitForExitAsync(Token);
        }
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string Write(string name)
    {
        var path = Path.Join(_directory, name);
        File.WriteAllText(path, "data");
        return path;
    }
}
