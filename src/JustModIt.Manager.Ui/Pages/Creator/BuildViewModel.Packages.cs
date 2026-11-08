using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Planning;
using JustModIt.Manager.Creator.Projects;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Pages.Library;

namespace JustModIt.Manager.Ui.Pages.Creator;

/// <summary>A package the card lists, made by a run or held by the project's record.</summary>
/// <param name="Name">Its file's name.</param>
/// <param name="Details">Its part and the stores of the versions it serves.</param>
/// <param name="Size">Its size, as words.</param>
/// <param name="Path">Its file.</param>
/// <param name="Versions">The versions of the game it serves.</param>
/// <param name="Part">The name of its part; empty for the base.</param>
public sealed record BuiltRow(string Name, string Details, string Size, string Path, IReadOnlyList<GameKey> Versions, string Part)
{
    /// <summary>The package's file is no longer where it was built, so it is neither tried nor shown in its folder.</summary>
    public bool IsMissing { get; init; }
}

// The packages the card shows: those a run of this page made, else the last ones the project's record holds, each looked for on
// the disk, with the way to their folder and to publish them.
public sealed partial class BuildViewModel
{
    private DateTimeOffset _builtAt;
    private BuildRecord? _record;
    private Dictionary<string, bool> _onDisk = [];
    private bool _ran;

    /// <summary>The packages the last run made, each with the versions it serves.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSeveral), nameof(PublishTitle), nameof(BuiltOverline), nameof(IsReadyToUse), nameof(IsGone), nameof(ShowsTest))]
    public partial IReadOnlyList<BuiltRow> Packages { get; private set; } = [];

    public bool HasSeveral => Packages.Count > 1;

    /// <summary>The built packages are there to use; false when every file has gone since.</summary>
    public bool IsReadyToUse => IsBuilt && Packages.Any(row => !row.IsMissing);

    public bool IsGone => IsBuilt && !IsReadyToUse;

    /// <summary>A test of the packages can start, or a test package is in the game, whose undo has no other place.</summary>
    public bool ShowsTest => (IsBuilt && Packages.Any(row => row.Part.Length == 0 && !row.IsMissing)) || !_test.CanRebuild;

    public string PublishTitle => Text.Format(Strings.Creator_PublishTitle, ("several", HasSeveral ? "true" : "false"));

    /// <summary>That the packages of the last run are ready, over them.</summary>
    public string BuiltOverline => Text.Format(Strings.Creator_BuiltHeading, ("count", Math.Max(Packages.Count(row => !row.IsMissing), 1))).ToUpper(Text.Culture);

    /// <summary>The package built first; empty before a build.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEarlierPackage), nameof(ShowsPackage))]
    public partial string OutputPath { get; private set; } = "";

    /// <summary>A build failed or stopped after an earlier one, whose package is still there but is not this build's.</summary>
    public bool HasEarlierPackage => IsReady && OutputPath.Length > 0;

    public bool ShowsPackage => IsBuilt || HasEarlierPackage;

    /// <summary>That the package shown is an earlier build's, and when it was built.</summary>
    public string EarlierText => GameWording.Join(
        [Strings.Creator_EarlierPackage, HasEarlierPackage ? Formats.RelativeTime(_builtAt, _page.Services.Clock) : ""]).ToUpper(Text.Culture);

    [ObservableProperty]
    public partial string ResultName { get; private set; } = "";

    [ObservableProperty]
    public partial string ResultSize { get; private set; } = "";

    [ObservableProperty]
    public partial string ResultDetails { get; private set; } = "";

    [ObservableProperty]
    public partial string ResultFolder { get; private set; } = "";

    /// <summary>Why the site would not take the package as it is.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSiteWarnings))]
    public partial IReadOnlyList<string> SiteWarnings { get; private set; } = [];

    public bool HasSiteWarnings => SiteWarnings.Count > 0;

    [ObservableProperty]
    public partial string PublishText { get; private set; } = "";

    [RelayCommand]
    private void Reveal()
    {
        if (ResultFolder.Length > 0)
        {
            _page.Services.Links.OpenFolder(ResultFolder);
        }
    }

    [RelayCommand]
    private void OpenSite() => _page.Services.Links.Open(PageLinks.Dashboard(_page.Services.Context.Site.SiteAddress));

    /// <summary>
    /// Takes the project's record of its builds, whose last packages the card lists until a run of this page makes its own, each
    /// looked for on the disk off the UI thread, behind the page's gate.
    /// </summary>
    /// <param name="record">The record; null when the project has none.</param>
    /// <returns>Completes once the card shows what the record holds.</returns>
    internal async Task ShowRecordAsync(BuildRecord? record)
    {
        _record = record;
        if (!_ran && record is not null)
        {
            var files = _page.FileSystem ?? PhysicalFileSystem.Instance;
            Dictionary<string, bool> onDisk = new(PathComparison.Comparer);
            foreach (var folder in record.Parts.SelectMany(part => part.Packages).Select(package => package.Path).Distinct(PathComparison.Comparer)
                .GroupBy(path => Path.GetDirectoryName(path) ?? path, PathComparison.Comparer))
            {
                try
                {
                    foreach (var (path, exists) in await _page.Access.ReadAsync(folder.Key,
                        _ => Task.FromResult(folder.Select(path => (path, files.FileExists(path))).ToList()), CancellationToken.None).ConfigureAwait(true))
                    {
                        onDisk[path] = exists;
                    }
                }
                // A drive that does not answer proves no package gone, so its packages are left out rather than called missing.
                catch (TimeoutException)
                {
                }
            }

            _onDisk = onDisk;
        }

        ShowLast();
    }

    /// <summary>
    /// Lists the last build's packages of the release typed while nothing since says they are out of date: no comparison runs or
    /// found a change since them; a test package in the game keeps them, since their card holds its undo.
    /// </summary>
    internal void ShowLast()
    {
        if (_ran || IsBuilding)
        {
            return;
        }

        var project = _session.Project;
        var version = project.Metadata.Version.Trim();
        // The newest package of each part for each version, as the record keeps rows of earlier ones too.
        List<(string Part, BuiltPackage Package)> last = version.Length == 0 ? [] : [.. project.Parts.SelectMany(part => project.Versions
            .Select(each => _record?.Parts.FirstOrDefault(built => built.Part == part.Name)?.Packages
                .Where(package => package.Version == version && package.Versions.Contains(each.Game.Key) && _onDisk.ContainsKey(package.Path))
                .MaxBy(package => package.BuiltAt))
            .OfType<BuiltPackage>().Distinct().Select(package => (part.Name, package)))];
        var stale = _page.Cells.Any(cell => cell.HasChanges || cell.State is CellState.Comparing or CellState.Waiting);
        if (last.Count > 0 && (!stale || !_test.CanRebuild))
        {
            if (!IsBuilt || !Rows(last).SequenceEqual(Packages))
            {
                Show(last, []);
            }
        }
        else if (IsBuilt)
        {
            Clear();
        }
    }

    private void Clear()
    {
        (OutputPath, ResultName, ResultSize, ResultDetails, ResultFolder, SiteWarnings, Packages) = ("", "", "", "", "", [], []);
        State = PackageState.Ready;
    }

    private void Built(IReadOnlyList<FinishedPackage> finished) =>
        Show([.. finished.Select(built => (built.Part, built.Package))], [.. finished.SelectMany(built => built.Result.Findings)
            .Where(finding => finding.Kind is FindingKind.LargerThanSiteTakes or FindingKind.FormatNotOnSite)
            .Select(finding => CreatorWording.Finding(finding)).Distinct()]);

    // The packages of a run, or the last ones the record holds.
    private void Show(List<(string Part, BuiltPackage Package)> built, IReadOnlyList<string> siteWarnings)
    {
        Packages = Rows(built);
        var package = built[0].Package;
        OutputPath = package.Path;
        (ResultName, ResultSize) = (Packages[0].Name, Packages[0].Size);
        ResultDetails = GameWording.Join(
        [
            package.Kind == PackageKind.Zip ? Strings.Creator_ZipKind : Strings.Creator_ManifestKind,
            package.Version.Length > 0 ? GameWording.ShortVersion(package.Version) : "",
            package.Kind == PackageKind.Manifest ? string.Join(", ", _session.Project.Metadata.Authors) : "",
        ]);
        ResultFolder = Path.GetDirectoryName(package.Path) ?? "";
        _builtAt = package.BuiltAt;
        SiteWarnings = siteWarnings;
        State = PackageState.Built;
        _test.Built();
    }

    private static string Details(string part, IReadOnlyList<GameKey> versions) =>
        GameWording.Join([part.Length > 0 ? part : Strings.Creator_PartBase, Stores(versions)]);

    // Each package with its part and versions, and whether its file is still where it was built.
    private List<BuiltRow> Rows(List<(string Part, BuiltPackage Package)> built) =>
    [
        .. built.Select(row => new BuiltRow(Path.GetFileName(row.Package.Path),
            Details(row.Part, row.Package.Versions),
            _onDisk.GetValueOrDefault(row.Package.Path, true) ? Formats.FileSize(row.Package.Size) : Strings.Creator_PackageMissing,
            row.Package.Path, row.Package.Versions, row.Part)
        {
            IsMissing = !_onDisk.GetValueOrDefault(row.Package.Path, true),
        }),
    ];
}
