using System.Text.Json;
using System.Text.RegularExpressions;
using JustModIt.Manager.Host.Tests.TestSupport;

namespace JustModIt.Manager.Host.Tests;

public sealed partial class ThirdPartyNoticesTests
{
    private const string Runtime = "/win-x64";

    // Packages in the apps' lock files that never reach a win-x64 release; every other one needs a notice.
    private static readonly HashSet<string> NotShipped = new(StringComparer.Ordinal)
    {
        // Build tools: the trimmer and Avalonia's MSBuild tasks.
        "Microsoft.NET.ILLink.Tasks",
        "Avalonia.BuildServices",

        // Native files for Linux, macOS and the browser, which a win-x64 publish leaves out.
        "HarfBuzzSharp.NativeAssets.Linux",
        "HarfBuzzSharp.NativeAssets.macOS",
        "HarfBuzzSharp.NativeAssets.WebAssembly",
        "SkiaSharp.NativeAssets.Linux",
        "SkiaSharp.NativeAssets.macOS",
        "SkiaSharp.NativeAssets.WebAssembly",
    };

    private static readonly List<Component> Components = ReadComponents();

    // Only the components' package lines count: the licence texts and upstream notices name packages they do not describe.
    private static readonly List<ListedPackage> Listed =
    [
        .. Components.SelectMany(component => PackageLine().Matches(component.Text)
            .SelectMany(line => line.Groups["list"].Value.Split(", "))
            .Select(entry => ListedPackage.Of(component.Title, entry))),
    ];

    [Fact]
    public void Notices_name_every_package_that_an_app_ships()
    {
        var listed = Listed.Select(package => package.Name).ToHashSet(StringComparer.Ordinal);

        string.Join(", ", Shipped().Where(package => !listed.Contains(package.Name)).Select(package => package.Name).Distinct())
            .Should().BeEmpty(
                "every package that an app's packages.lock.json ships needs a component in THIRD-PARTY-NOTICES.md with its licence "
                + "and copyright, which names it as \"id version\" on its \"- NuGet package:\" or \"- NuGet packages:\" line; a "
                + "package that never reaches a release (a build tool or another platform's files) goes on NotShipped with the reason");
    }

    [Fact]
    public void Notices_give_the_versions_that_ship()
    {
        var outdated = Shipped()
            .Where(package => !Listed.Any(listed => listed.Name == package.Name && listed.Version == package.Version))
            .SelectMany(package => Listed.Where(listed => listed.Name == package.Name)
                .Select(listed => $"{package.Name} {package.Version}: update \"{listed.Component}\", which gives \"{listed.Version}\""));

        string.Join(Environment.NewLine, outdated).Should().BeEmpty("THIRD-PARTY-NOTICES.md must describe the versions that ship");
    }

    [Fact]
    public void Notices_list_each_package_once_and_only_while_an_app_ships_it()
    {
        var shipped = Shipped().Select(package => package.Name).ToHashSet(StringComparer.Ordinal);

        var extra = Listed.GroupBy(package => package.Name, StringComparer.Ordinal)
            .Where(listings => listings.Count() > 1 || !shipped.Contains(listings.Key))
            .Select(listings => $"{listings.Key} in \"{string.Join("\", \"", listings.Select(package => package.Component))}\"");

        string.Join(Environment.NewLine, extra).Should().BeEmpty(
            "one component describes each package that ships; a package that no app ships any more leaves the notices");
    }

    [Fact]
    public void Packages_said_not_to_ship_are_still_locked()
    {
        var locked = Locked().Select(package => package.Name).ToHashSet(StringComparer.Ordinal);

        string.Join(", ", NotShipped.Where(package => !locked.Contains(package)))
            .Should().BeEmpty("a package that no app uses any more leaves NotShipped too");
    }

    [Fact]
    public void Notices_give_the_versions_of_the_native_libraries_that_the_submodules_build()
    {
        var lzmaHeader = RepositoryFiles.PathOf("native", "xz", "src", "liblzma", "api", "lzma", "version.h");
        var xdeltaHeader = RepositoryFiles.PathOf("native", "xdelta", "xdelta3", "xdelta3-main.h");
        Assert.SkipUnless(File.Exists(lzmaHeader) && File.Exists(xdeltaHeader), "The native/xz and native/xdelta submodules are not checked out.");

        var lzma = LzmaVersion().Match(File.ReadAllText(lzmaHeader));
        var liblzma = $"{lzma.Groups["major"]}.{lzma.Groups["minor"]}.{lzma.Groups["patch"]}";
        var xdelta3 = XdeltaVersion().Match(File.ReadAllText(xdeltaHeader)).Groups[1].Value;

        VersionOf("liblzma (XZ Utils)").Should().Be(liblzma, "native/xz builds liblzma {0}", liblzma);
        VersionOf("xdelta3").Should().Be(xdelta3, "native/xdelta builds xdelta3 {0}", xdelta3);
    }

    private static string VersionOf(string title) =>
        VersionLine().Match(Components.Single(component => component.Title == title).Text).Groups["version"].Value;

    private static IEnumerable<LockedPackage> Shipped() => Locked().Where(package => !NotShipped.Contains(package.Name));

    private static List<LockedPackage> Locked() =>
    [
        .. RepositoryFiles.Apps().SelectMany(app => LockedBy(Path.Combine(app, "packages.lock.json")))
            .Distinct()
            .OrderBy(package => package.Name, StringComparer.Ordinal),
    ];

    // A win-x64 publish restores the packages of the framework plus the runtime-specific ones.
    private static IEnumerable<LockedPackage> LockedBy(string lockFile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockFile));
        var targets = document.RootElement.GetProperty("dependencies");
        var runtime = targets.EnumerateObject().Single(target => target.Name.EndsWith(Runtime, StringComparison.Ordinal));
        var framework = targets.GetProperty(runtime.Name[..^Runtime.Length]);

        return
        [
            .. framework.EnumerateObject().Concat(runtime.Value.EnumerateObject())
                .Where(package => package.Value.GetProperty("type").GetString() != "Project")
                .Select(package => new LockedPackage(package.Name, package.Value.GetProperty("resolved").GetString()!)),
        ];
    }

    private static List<Component> ReadComponents()
    {
        var notices = File.ReadAllText(RepositoryFiles.PathOf("THIRD-PARTY-NOTICES.md")).ReplaceLineEndings("\n");
        var start = notices.IndexOf("\n## Components\n", StringComparison.Ordinal);
        start.Should().BePositive("THIRD-PARTY-NOTICES.md lists its components under \"## Components\"");
        var end = notices.IndexOf("\n## ", start + 1, StringComparison.Ordinal);

        return
        [
            .. notices[start..end].Split("\n### ").Skip(1)
                .Select(section => section.Split('\n', 2))
                .Select(parts => new Component(parts[0].Trim(), parts.Length > 1 ? parts[1] : "")),
        ];
    }

    [GeneratedRegex(@"^- NuGet packages?: (?<list>.+)$", RegexOptions.Multiline)]
    private static partial Regex PackageLine();

    [GeneratedRegex(@"^- Version: (?<version>\S+)", RegexOptions.Multiline)]
    private static partial Regex VersionLine();

    [GeneratedRegex(@"#define LZMA_VERSION_MAJOR (?<major>\d+).*?#define LZMA_VERSION_MINOR (?<minor>\d+).*?#define LZMA_VERSION_PATCH (?<patch>\d+)",
        RegexOptions.Singleline)]
    private static partial Regex LzmaVersion();

    [GeneratedRegex(@"Xdelta version (\d+\.\d+\.\d+)")]
    private static partial Regex XdeltaVersion();

    private sealed record Component(string Title, string Text);

    private sealed record LockedPackage(string Name, string Version);

    private sealed record ListedPackage(string Component, string Name, string Version)
    {
        public static ListedPackage Of(string component, string entry) =>
            entry.Trim().Split(' ') is [var name, var version] ? new(component, name, version) : new(component, entry.Trim(), "");
    }
}
