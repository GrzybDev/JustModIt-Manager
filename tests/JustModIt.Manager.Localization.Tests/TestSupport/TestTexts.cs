using System.Xml.Linq;

namespace JustModIt.Manager.Localization.Tests.TestSupport;

/// <summary>Tests that switch the app's language with <see cref="Text.Use"/> run one at a time.</summary>
[CollectionDefinition(nameof(TestTexts))]
public sealed class TestTexts
{
    /// <summary>The Polish texts of this project, merged by its build as an app's are: with the project's own override.</summary>
    public static TextCatalog Polish { get; } = TextCatalog.Load("pl-PL", typeof(TestTexts).Assembly);

    public static string LocalizationResources => Path.Combine(RepositoryRoot, "src", "JustModIt.Manager.Localization", "Resources");

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    /// <summary>The translations the platform ships, read from their files.</summary>
    public static IEnumerable<(string Language, Dictionary<string, string> Texts)> Translations() =>
        Directory.EnumerateFiles(LocalizationResources, "Strings.*.resx")
            .Select(path => (Path.GetFileNameWithoutExtension(path)["Strings.".Length..], Read(path)));

    private static Dictionary<string, string> Read(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(data => (string)data.Attribute("name")!, data => (string)data.Element("value")!, StringComparer.Ordinal);

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No solution file above {AppContext.BaseDirectory}.");
    }
}
