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

    /// <summary>The texts the apps put over the platform's, in their own words.</summary>
    public static IEnumerable<(string App, Dictionary<string, string> Texts)> AppOverrides() =>
        Directory.EnumerateDirectories(Path.Combine(RepositoryRoot, "src"))
            .Where(project => !string.Equals(Path.Combine(project, "Resources"), LocalizationResources, StringComparison.OrdinalIgnoreCase))
            .Select(project => Path.Combine(project, "Resources"))
            .Where(Directory.Exists)
            .SelectMany(resources => Directory.EnumerateFiles(resources, "Strings.*.resx"))
            .Select(path => (Path.GetRelativePath(RepositoryRoot, path), Read(path)));

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
