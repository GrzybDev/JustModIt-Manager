namespace JustModIt.Manager.Creator.Tests;

public sealed class FileNamesTests
{
    [Theory]
    [InlineData("Example Game", "Example Game.examplemod")]
    [InlineData("Example: Chapter 1", "Example Chapter 1.examplemod")]
    [InlineData("What? <Really> \"Yes\" / No | Maybe*", "What Really Yes No Maybe.examplemod")]
    [InlineData("  Spaced   out  ", "Spaced out.examplemod")]
    [InlineData("Version 2.0.", "Version 2.0.examplemod")]
    [InlineData("Café Déjà Vu", "Café Déjà Vu.examplemod")]
    [InlineData("Aux", "package.examplemod")]
    [InlineData("nul.txt", "package.examplemod")]
    [InlineData(":?*", "package.examplemod")]
    [InlineData("", "package.examplemod")]
    [InlineData(null, "package.examplemod")]
    public void Suggests_a_name_every_system_accepts(string? name, string expected) =>
        FileNames.Suggest(name, "package", ".examplemod").Should().Be(expected);

    [Fact]
    public void Shortens_a_long_name() =>
        FileNames.Suggest(new string('x', 300), "package", ".exampleproj").Should().Be(new string('x', 100) + ".exampleproj");

    [Fact]
    public void Shortens_a_long_name_without_splitting_a_character() =>
        FileNames.Suggest(new string('x', 99) + "\U0001F3AE game", "package", ".exampleproj").Should().Be(new string('x', 99) + ".exampleproj");
}
