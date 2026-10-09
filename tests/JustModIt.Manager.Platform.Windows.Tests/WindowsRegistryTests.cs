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
    public void Text_and_number_values_are_read_and_other_types_skipped()
    {
        using (var key = Win32.Registry.CurrentUser.CreateSubKey(_path))
        {
            key.SetValue("path", @"D:\Games\Witcher", Win32.RegistryValueKind.String);
            key.SetValue("expanded", @"%SystemRoot%\system32", Win32.RegistryValueKind.ExpandString);
            key.SetValue("languages", (string[])["pl-PL", "en-US"], Win32.RegistryValueKind.MultiString);
            key.SetValue("updating", unchecked((int)0xFFFFFFFF), Win32.RegistryValueKind.DWord);
            key.SetValue("buildId", 59534219748634025L, Win32.RegistryValueKind.QWord);
            key.SetValue("blob", new byte[] { 1, 2, 3 }, Win32.RegistryValueKind.Binary);
        }

        var snapshot = _registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, _path)!;

        snapshot.GetText("path").Should().Be(@"D:\Games\Witcher");
        snapshot.GetText("expanded").Should().Be(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32"));
        snapshot.GetValue("languages").Should().BeNull();
        snapshot.GetValue("updating")!.AsNumber().Should().Be(uint.MaxValue);
        snapshot.GetText("buildId").Should().Be("59534219748634025");
        snapshot.GetValue("blob").Should().BeNull();
    }

    [Fact]
    public void Value_names_ignore_letter_case()
    {
        using (var key = Win32.Registry.CurrentUser.CreateSubKey(_path))
        {
            key.SetValue("GAMENAME", "Café Racer");
        }

        _registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, _path)!.GetText("gameName").Should().Be("Café Racer");
    }

    [Fact]
    public void Subkeys_are_listed()
    {
        using (var key = Win32.Registry.CurrentUser.CreateSubKey(_path))
        {
            key.CreateSubKey("1207658924").Dispose();
            key.CreateSubKey("Settings").Dispose();
        }

        _registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Default, _path)!.SubKeyNames
            .Should().BeEquivalentTo("1207658924", "Settings");
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
