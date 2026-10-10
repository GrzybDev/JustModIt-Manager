namespace JustModIt.Manager.Creator.Tests;

public sealed class FileNamesTests
{
    public static TheoryData<string?, string> Names => new()
    {
        { "Example Game", "Example Game.examplemod" },
        { "Example: Chapter 1", "Example Chapter 1.examplemod" },
        { "What? <Really> \"Yes\" / No | Maybe*", "What Really Yes No Maybe.examplemod" },
        { "  Spaced   out  ", "Spaced out.examplemod" },
        { "Version 2.0.", "Version 2.0.examplemod" },
        { "Café Déjà Vu", "Café Déjà Vu.examplemod" },
        { "Aux", "package.examplemod" },
        { "nul.txt", "package.examplemod" },
        { ":?*", "package.examplemod" },
        { "", "package.examplemod" },
        { null, "package.examplemod" },
        { new string('x', 300), new string('x', 100) + ".examplemod" },
        { new string('x', 99) + "\U0001F3AE game", new string('x', 99) + ".examplemod" },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void Suggests_a_name_every_system_accepts(string? name, string expected) =>
        FileNames.Suggest(name, "package", ".examplemod").Should().Be(expected);
}
