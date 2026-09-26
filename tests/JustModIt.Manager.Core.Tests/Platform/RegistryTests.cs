using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Platform;

public sealed class RegistryTests
{
    private const string GamesKey = @"SOFTWARE\GOG.com\Games";

    [Fact]
    public void Numbers_read_as_their_decimal_digits()
    {
        RegistryValue.FromDWord(1207658924).AsText().Should().Be("1207658924");
        RegistryValue.FromQWord(59534219748634025).AsText().Should().Be("59534219748634025");
        RegistryValue.FromDWord(uint.MaxValue).AsNumber().Should().Be(uint.MaxValue);
    }

    [Fact]
    public void Only_numbers_have_a_number_and_only_text_and_numbers_have_a_string()
    {
        RegistryValue.FromText("12").AsNumber().Should().BeNull();
        RegistryValue.FromMultiText(["a", "b"]).AsText().Should().BeNull();
        RegistryValue.FromBinary([1, 2]).AsText().Should().BeNull();
        RegistryValue.FromText("text").Lines.Should().BeEmpty();
    }

    [Fact]
    public void A_snapshot_finds_values_ignoring_letter_case()
    {
        var snapshot = new RegistryKeySnapshot(GamesKey, [], [new("PATH", RegistryValue.FromText(@"D:\Games\Witcher"))]);

        snapshot.GetText("path").Should().Be(@"D:\Games\Witcher");
        snapshot.GetValue("missing").Should().BeNull();
    }

    [Fact]
    public void The_in_memory_registry_lists_direct_subkeys_only()
    {
        var registry = new InMemoryRegistry()
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924", "gameName", "Game One")
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924\Extras", "path", "x")
            .CreateKey(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\Settings");

        var games = registry.ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, GamesKey.ToLowerInvariant());

        games!.SubKeyNames.Should().BeEquivalentTo("1207658924", "Settings");
        registry.ReadKey(RegistryHive.LocalMachine, RegistryView.Registry32, $@"{GamesKey}\1207658924")!
            .GetText("GAMENAME").Should().Be("Game One");
    }

    [Fact]
    public void The_in_memory_registry_keeps_views_and_hives_apart()
    {
        var registry = new InMemoryRegistry()
            .SetText(RegistryHive.LocalMachine, RegistryView.Registry32, GamesKey, "source", "32-bit");

        registry.ReadKey(RegistryHive.LocalMachine, RegistryView.Registry64, GamesKey).Should().BeNull();
        registry.ReadKey(RegistryHive.LocalMachine, RegistryView.Default, GamesKey).Should().BeNull();
        registry.ReadKey(RegistryHive.CurrentUser, RegistryView.Registry32, GamesKey).Should().BeNull();
    }
}
