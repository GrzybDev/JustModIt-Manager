using System.Text.Json;
using System.Text.RegularExpressions;
using JustModIt.Manager.Tests;

namespace JustModIt.Manager.Host.Tests;

public sealed partial class ThirdPartyNoticesTests
{
    // Build tools: the trimmer and Avalonia's MSBuild tasks.
    private static readonly string[] BuildTools = ["Microsoft.NET.ILLink.Tasks", "Avalonia.BuildServices"];

    // Native files for macOS and the browser, which no release ships.
    private static readonly string[] OtherNatives =
    [
        "HarfBuzzSharp.NativeAssets.macOS",
        "HarfBuzzSharp.NativeAssets.WebAssembly",
        "SkiaSharp.NativeAssets.macOS",
        "SkiaSharp.NativeAssets.WebAssembly",
    ];

    // The systems the apps are released for, each with the packages in the apps' lock files that never reach its release;
    // every other package needs a notice. NativeAOT trims away the code that only another system runs, as the strings of
    // the published executables show.
    private static readonly Release[] Releases =
    [
        new("Windows", "win-x64",
        [
            .. BuildTools, .. OtherNatives,
            "HarfBuzzSharp.NativeAssets.Linux",
            "SkiaSharp.NativeAssets.Linux",
            "Tmds.DBus.Generator",
            "Tmds.DBus.Protocol",
        ]),
        new("Linux", "linux-x64",
        [
            .. BuildTools, .. OtherNatives,
            "Avalonia.Angle.Windows.Natives",
            "HarfBuzzSharp.NativeAssets.Win32",
            "SkiaSharp.NativeAssets.Win32",
            "MicroCom.Runtime",
            "Microsoft.Windows.CsWin32",
        ]),
    ];

    // Code generators that only a library references, whose generated code is compiled into the apps.
    private static readonly HashSet<string> GeneratedIntoApps = new(StringComparer.Ordinal)
    {
        "Microsoft.Windows.CsWin32",
        "Tmds.DBus.Generator",
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
                + "package that never reaches a release (a build tool or another platform's files) goes on that release's NotShipped "
                + "with the reason");
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
        var gone = Releases.SelectMany(release =>
        {
            var locked = Locked(release).Select(package => package.Name).ToHashSet(StringComparer.Ordinal);
            return release.NotShipped.Where(package => !locked.Contains(package)).Select(package => $"{package} ({release.System})");
        });

        string.Join(", ", gone).Should().BeEmpty("a package that no app uses any more leaves NotShipped too");
    }

    [Fact]
    public void Components_of_one_system_ship_nothing_with_another()
    {
        var hidden = Components.Select(component => (component.Title, Systems: SystemsLine().Match(component.Text)))
            .Where(component => component.Systems.Success)
            .SelectMany(component =>
            {
                var systems = component.Systems.Groups["systems"].Value.Split(", ");
                var packages = Listed.Where(package => package.Component == component.Title).Select(package => package.Name)
                    .ToHashSet(StringComparer.Ordinal);
                return Releases.Where(release => !systems.Contains(release.System))
                    .SelectMany(release => Shipped(release).Where(package => packages.Contains(package.Name))
                        .Select(package => $"{component.Title}: {package.Name} ships with {release.System}"));
            });

        string.Join(Environment.NewLine, hidden).Should().BeEmpty(
            "the licences page leaves out a component whose \"- Systems:\" line does not name the system it runs on");
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

    private static IEnumerable<LockedPackage> Shipped() => Releases.SelectMany(Shipped).Distinct();

    private static IEnumerable<LockedPackage> Shipped(Release release) =>
        Locked(release).Where(package => !release.NotShipped.Contains(package.Name));

    private static List<LockedPackage> Locked(Release release) =>
    [
        .. RepositoryFiles.Apps().SelectMany(app => LockedBy(Path.Combine(app, "packages.lock.json"), release.Runtime))
            .Concat(Directory.EnumerateDirectories(RepositoryFiles.PathOf("src"))
                .Select(project => Path.Combine(project, "packages.lock.json"))
                .Where(File.Exists)
                .SelectMany(lockFile => LockedBy(lockFile, release.Runtime))
                .Where(package => GeneratedIntoApps.Contains(package.Name)))
            .Distinct()
            .OrderBy(package => package.Name, StringComparer.Ordinal),
    ];

    // A publish restores the packages of the framework plus those of its runtime.
    private static IEnumerable<LockedPackage> LockedBy(string lockFile, string runtime)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(lockFile));
        var targets = document.RootElement.GetProperty("dependencies");
        var graph = targets.EnumerateObject().Single(target => target.Name.EndsWith($"/{runtime}", StringComparison.Ordinal));
        var framework = targets.GetProperty(graph.Name[..^(runtime.Length + 1)]);

        return
        [
            .. framework.EnumerateObject().Concat(graph.Value.EnumerateObject())
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

    [GeneratedRegex(@"^- Systems: (?<systems>.+)$", RegexOptions.Multiline)]
    private static partial Regex SystemsLine();

    [GeneratedRegex(@"#define LZMA_VERSION_MAJOR (?<major>\d+).*?#define LZMA_VERSION_MINOR (?<minor>\d+).*?#define LZMA_VERSION_PATCH (?<patch>\d+)",
        RegexOptions.Singleline)]
    private static partial Regex LzmaVersion();

    [GeneratedRegex(@"Xdelta version (\d+\.\d+\.\d+)")]
    private static partial Regex XdeltaVersion();

    private sealed record Component(string Title, string Text);

    private sealed record Release(string System, string Runtime, HashSet<string> NotShipped);

    private sealed record LockedPackage(string Name, string Version);

    private sealed record ListedPackage(string Component, string Name, string Version)
    {
        public static ListedPackage Of(string component, string entry) =>
            entry.Trim().Split(' ') is [var name, var version] ? new(component, name, version) : new(component, entry.Trim(), "");
    }
}
