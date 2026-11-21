using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.Versioning;

namespace JustModIt.Manager.Platform.Linux.Tests;

[SupportedOSPlatform("linux")]
public sealed class ProcessDescriptorsTests
{
    [Fact]
    public async Task Programs_started_after_the_call_inherit_none_of_its_descriptors()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "close_range and /proc/<pid>/fd exist only on Linux.");
        // An inheritable pipe stands for the one the AppImage's runtime hands the app, since .NET opens its own close-on-exec.
        using var pipe = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var inherited = pipe.ClientSafePipeHandle.DangerousGetHandle().ToString(CultureInfo.InvariantCulture);

        (await DescriptorsOfAChildAsync()).Should().Contain(inherited);

        ProcessDescriptors.KeepFromChildren().Should().BeNull();

        (await DescriptorsOfAChildAsync()).Should().OnlyContain(descriptor => int.Parse(descriptor, CultureInfo.InvariantCulture) < 3,
            "a child keeps only its standard input and output");
    }

    private static async Task<string[]> DescriptorsOfAChildAsync()
    {
        // The shell lists its own once it has started, since its loader opens libraries while it starts; ls is another process.
        using var shell = Process.Start(new ProcessStartInfo("/bin/sh")
        {
            ArgumentList = { "-c", "ls /proc/$$/fd; true" },
            RedirectStandardOutput = true,
            UseShellExecute = false,
        })!;
        var listed = await shell.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await shell.WaitForExitAsync(TestContext.Current.CancellationToken);
        return listed.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
