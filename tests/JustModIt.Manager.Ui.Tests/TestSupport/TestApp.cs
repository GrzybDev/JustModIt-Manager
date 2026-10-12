using System.Xml.Linq;
using Avalonia;
using Avalonia.Headless;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui;
using JustModIt.Manager.Ui.Tests.TestSupport;

[assembly: AvaloniaTestApplication(typeof(TestApp))]

// The app's texts and the motion setting are process-wide, and tests wait on real time for animations.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>
/// The headless application every test runs in: the real theme drawn by Skia, so captures show what the window shows,
/// and the texts of an app in Polish with its own terms, as the mockups are written.
/// </summary>
public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp()
    {
        Text.Use(PolishApp());
        return AppBuilder.Configure<App>()
            .UseSkia()
            .UseHarfBuzz()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
    }

    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    // The platform's English base, its Polish translation and the Polish app's own words over both.
    public static TextCatalog PolishApp() => Polish(Path.Combine("src", "MenadzerSpolszczen", "Resources", "Strings.pl.resx"));

    // The platform's texts alone, as an app without words of its own shows them.
    public static TextCatalog SharedPolish() => Polish();

    private static TextCatalog Polish(params string[] overrides)
    {
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in new[]
        {
            Path.Combine("src", "JustModIt.Manager.Localization", "Resources", "Strings.resx"),
            Path.Combine("src", "JustModIt.Manager.Localization", "Resources", "Strings.pl.resx"),
        }.Concat(overrides))
        {
            foreach (var data in XDocument.Load(Path.Combine(RepositoryRoot, file)).Root!.Elements("data"))
            {
                texts[(string)data.Attribute("name")!] = (string)data.Element("value")!;
            }
        }

        return new TextCatalog("pl", texts);
    }

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
