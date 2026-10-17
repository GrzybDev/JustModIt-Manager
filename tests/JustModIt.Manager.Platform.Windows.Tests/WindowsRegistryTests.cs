using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryTests : IDisposable
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion";

    private readonly string _path = $@"Software\ExampleApp.Tests.{Guid.NewGuid():N}";
    private readonly WindowsRegistry _registry = WindowsRegistry.Instance;

    public WindowsRegistryTests() => Assert.SkipUnless(OperatingSystem.IsWindows(), "The registry exists only on Windows.");

    [Fact]
    public void A_key_reads_back_with_every_value_type_its_subkeys_and_names_in_any_case()
    {
        using (var key = Win32.Registry.CurrentUser.CreateSubKey(_path))
        {
            key.SetValue("path", @"D:\Games\Lantern", Win32.RegistryValueKind.String);
            key.SetValue("expanded", @"%SystemRoot%\system32", Win32.RegistryValueKind.ExpandString);
            key.SetValue("languages", (string[])["pl-PL", "en-US"], Win32.RegistryValueKind.MultiString);
            key.SetValue("updating", unchecked((int)0xFFFFFFFF), Win32.RegistryValueKind.DWord);
            key.SetValue("buildId", 54321098765432109L, Win32.RegistryValueKind.QWord);
            key.SetValue("blob", new byte[] { 1, 2, 3 }, Win32.RegistryValueKind.Binary);
            key.CreateSubKey("1100000001").Dispose();
            key.CreateSubKey("Settings").Dispose();
        }

        var snapshot = _registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, _path)!;

        snapshot.SubKeyNames.Should().BeEquivalentTo("1100000001", "Settings");
        snapshot.GetText("PATH").Should().Be(@"D:\Games\Lantern");
        snapshot.GetText("expanded").Should().Be(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32"));
        snapshot.GetValue("languages").Should().BeNull();
        snapshot.GetValue("updating")!.AsNumber().Should().Be(uint.MaxValue);
        snapshot.GetText("buildId").Should().Be("54321098765432109");
        snapshot.GetValue("blob").Should().BeNull();
    }

    [Fact]
    public void A_missing_key_reads_as_null() =>
        _registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, _path).Should().BeNull();

    [Fact]
    public void The_32_and_64_bit_views_are_distinct()
    {
        Assert.SkipUnless(Environment.Is64BitOperatingSystem, "Only 64-bit Windows has two views.");

        var programFiles32 = _registry.ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, CurrentVersionKey)!
            .GetText("ProgramFilesDir");
        var programFiles64 = _registry.ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, CurrentVersionKey)!
            .GetText("ProgramFilesDir");

        programFiles32.Should().Be(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        programFiles64.Should().Be(Environment.GetEnvironmentVariable("ProgramW6432"));
    }

    public void Dispose() => Win32.Registry.CurrentUser.DeleteSubKeyTree(_path, throwOnMissingSubKey: false);
}
