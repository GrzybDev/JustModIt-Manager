using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Pages.Game;

/// <summary>What the next step of a game's page is about, which also picks its look.</summary>
public enum StepKind
{
    Install,
    Update,
    Repair,
    Choose,
    Refused,
    Unavailable,
    NotReady,
    Queued,
    Waiting,
    Job,
    Done,
    Current,
    Failed,
    Cancelled,
    Offline,
    Gone,
    Moved,
    NotInCatalogue,
    Backups,
    Test,
    Removed,
}

/// <summary>
/// The band at the foot of a game's panel that says what comes next, with at most one red button: what to install or
/// update, a job under way, or what is in the way and the one way out of it.
/// </summary>
/// <param name="Kind">What the step is about.</param>
/// <param name="Tone">Its tint and its icon's colour.</param>
/// <param name="Icon">The icon in the timeline.</param>
/// <param name="Title">The step in a few words.</param>
/// <param name="Text">What happens, or why not.</param>
public sealed record NextStep(StepKind Kind, Tone Tone, Geometry Icon, string Title, string Text)
{
    /// <summary>A warning under the text, e.g. that an update goes without an add-on; empty for none.</summary>
    public string Warning { get; init; } = "";

    /// <summary>A line under the text, e.g. when the release came out; empty for none.</summary>
    public string Meta { get; init; } = "";

    /// <summary>A warning at the end of the meta line; empty for none.</summary>
    public string MetaWarning { get; init; } = "";

    /// <summary>The meta line offers the release's changes, which unfold in place.</summary>
    public bool OffersChanges { get; init; }

    /// <summary>The red button's label; null for none.</summary>
    public string? Primary { get; init; }

    /// <summary>The red button's second line, e.g. the version and size.</summary>
    public string PrimaryCaption { get; init; } = "";

    public Geometry? PrimaryIcon { get; init; }

    /// <summary>What the red button sends.</summary>
    public Message? PrimaryRequest { get; init; }

    /// <summary>An outlined button's label, e.g. to cancel or to uninstall; null for none.</summary>
    public string? Secondary { get; init; }

    public Geometry? SecondaryIcon { get; init; }

    /// <summary>The outlined button removes the install, so it asks first.</summary>
    public bool SecondaryUninstalls { get; init; }

    /// <summary>The outlined button cancels the job.</summary>
    public bool SecondaryCancels { get; init; }

    /// <summary>The outlined button leads back to the player's games.</summary>
    public bool SecondaryGoesBack { get; init; }

    /// <summary>The store's check of the game's files, which a button starts, where the step advises it; null for none.</summary>
    public Uri? CheckFiles { get; init; }

    /// <summary>The label of the button that starts <see cref="CheckFiles"/>, naming the store.</summary>
    public string CheckFilesLabel { get; init; } = "";

    /// <summary>The step can be closed, e.g. a result; what closing sends, if anything, is <see cref="DismissRequest"/>.</summary>
    public bool CanDismiss { get; init; }

    public Message? DismissRequest { get; init; }

    /// <summary>The step links to the site's help.</summary>
    public bool OffersHelp { get; init; }

    /// <summary>The step points to the team's notes further down.</summary>
    public bool PointsToNotes { get; init; }

    public bool HasWarning => Warning.Length > 0;

    public bool HasPrimary => Primary is not null;

    public bool HasSecondary => Secondary is not null;

    public bool HasCheckFiles => CheckFiles is not null;

    /// <summary>The step leads a panel in a short window, so a button it adds goes under its text.</summary>
    public bool IsCompact { get; init; }

    /// <summary>The check's button stands where the step's buttons go, since the step has none of its own.</summary>
    public bool ChecksBeside => HasCheckFiles && !HasPrimary && !HasSecondary && !IsCompact;

    /// <summary>The check's button stands under the text, beside the step's own buttons or in a short window.</summary>
    public bool ChecksUnder => HasCheckFiles && !ChecksBeside;

    public bool HasMeta => Meta.Length > 0 || MetaWarning.Length > 0 || OffersChanges || OffersHelp;

    // A separator stands only between two parts of the meta line, never before the first.
    public bool SeparatesWarning => Meta.Length > 0 && MetaWarning.Length > 0;

    public bool SeparatesChanges => OffersChanges && (Meta.Length > 0 || MetaWarning.Length > 0);

    public bool SeparatesHelp => OffersHelp && (Meta.Length > 0 || MetaWarning.Length > 0 || OffersChanges);

    public bool IsJob => Kind == StepKind.Job;

    public bool IsAccent => Tone == Tone.Accent;

    public bool IsInfo => Tone == Tone.Info;

    public bool IsSuccess => Tone == Tone.Success;

    public bool IsWarning => Tone == Tone.Warning;

    public bool IsDanger => Tone == Tone.Danger;

    public bool IsNeutral => Tone == Tone.Neutral;
}

/// <summary>Where a step of a job stands.</summary>
public enum StepState
{
    Done,
    Current,
    Pending,
}

/// <summary>One numbered step of a job under way, such as downloading.</summary>
/// <param name="Number">Its place among the job's steps, from 1.</param>
/// <param name="Label">What it does.</param>
/// <param name="State">Whether it is done, running or still to come.</param>
public sealed record JobStepView(int Number, string Label, StepState State)
{
    public bool IsDone => State == StepState.Done;

    public bool IsCurrent => State == StepState.Current;

    public bool IsPending => State == StepState.Pending;
}

/// <summary>An install on the game as its row in the panel shows it.</summary>
/// <param name="Id">The install's record id.</param>
/// <param name="Title">Its version, e.g. "Version 2".</param>
/// <param name="When">When it was installed.</param>
/// <param name="Detail">The line under it: its files and where the originals wait, or what changed them.</param>
public sealed record InstallRow(Guid Id, string Title, string When, string Detail)
{
    /// <summary>The project and team, shown when the game holds several installs.</summary>
    public string Project { get; init; } = "";

    /// <summary>The detail is a warning, e.g. that the store replaced files.</summary>
    public bool IsWarning { get; init; }

    public bool CanUninstall { get; init; }

    /// <summary>The uninstall action named with its game and project, for screen readers.</summary>
    public string UninstallName { get; init; } = "";

    /// <summary>An action of this install beside the page's own next step, e.g. its update; null for none.</summary>
    public string? Action { get; init; }

    public Geometry? ActionIcon { get; init; }

    public Message? ActionRequest { get; init; }

    /// <summary>The files the install's software changes while the game runs; null when it changes none.</summary>
    public RuntimeFilesView? RuntimeFiles { get; init; }

    /// <summary>The software the install's packages carry, with its licences; null when they carry none.</summary>
    public LicensesView? Licenses { get; init; }

    /// <summary>The install's add-ons, its line and its pills; null when its project offers none to choose.</summary>
    public FeaturePanel? Features { get; init; }

    public bool HasProject => Project.Length > 0;

    public bool HasAction => Action is not null;
}

/// <summary>
/// The game files an install's software changes while the game runs, listed under the line that tells of them once the
/// player unfolds them; kept across the page's states, so unfolding them keeps the row and the toggle's focus.
/// </summary>
/// <param name="paths">The files, game-relative.</param>
public sealed partial class RuntimeFilesView(IReadOnlyList<string> paths) : ObservableObject
{
    public IReadOnlyList<string> Paths { get; } = paths;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Toggle), nameof(ToggleIcon))]
    public partial bool AreShown { get; set; }

    public string Toggle => AreShown ? Strings.GamePage_RuntimeHide : Strings.GamePage_RuntimeShow;

    public Geometry ToggleIcon => AreShown ? Theme.Icons.ChevronUp : Theme.Icons.ChevronDown;

    [RelayCommand]
    private void ToggleShown() => AreShown = !AreShown;
}

/// <summary>
/// The software an install's packages carry, named on the line that tells of it and listed with its licence and source
/// once the player unfolds it; kept across the page's states, so unfolding it keeps the row and the toggle's focus.
/// </summary>
/// <param name="notices">The software as the packages name it.</param>
public sealed partial class LicensesView(IReadOnlyList<ThirdPartyNoticeView> notices) : ObservableObject
{
    public IReadOnlyList<ThirdPartyNoticeView> Notices { get; } = notices;

    public IReadOnlyList<LicenseItem> Items { get; } = [.. notices.Select(LicenseItem.From)];

    public string Line => Text.Format(Strings.GamePage_SoftwareLine, ("names", string.Join(", ", Notices.Select(notice => notice.Name))));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Toggle), nameof(ToggleIcon))]
    public partial bool AreShown { get; set; }

    public string Toggle => Text.Format(AreShown ? Strings.GamePage_LicensesHide : Strings.GamePage_LicensesShow, ("count", Notices.Count));

    public Geometry ToggleIcon => AreShown ? Theme.Icons.ChevronUp : Theme.Icons.ChevronDown;

    [RelayCommand]
    private void ToggleShown() => AreShown = !AreShown;
}

/// <summary>A piece of software an install's package carries, as its unfolded list shows it.</summary>
/// <param name="Name">What the package calls it.</param>
/// <param name="License">Its licence, as the package names it.</param>
/// <param name="Address">Where its source code is.</param>
/// <param name="Link">The address to open; null for one the window does not open, which shows as text.</param>
public sealed record LicenseItem(string Name, string License, string Address, Uri? Link)
{
    /// <summary>The link to the source, named with the software for screen readers.</summary>
    public string SourceName => Text.Format(Strings.GamePage_LicenseSourceName, ("name", Name));

    /// <summary>Makes the item of a notice of an installed package.</summary>
    /// <param name="notice">The notice as the install's view carries it.</param>
    public static LicenseItem From(ThirdPartyNoticeView notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var link = Uri.TryCreate(notice.SourceUrl, UriKind.Absolute, out var address) && address.Scheme == Uri.UriSchemeHttps ? address : null;
        return new LicenseItem(notice.Name, notice.License, notice.SourceUrl, link);
    }
}

/// <summary>An add-on as a pill under its row's line: turned on or off on its own, or chosen among its group.</summary>
public sealed partial class FeaturePill : ObservableObject
{
    private readonly Action<FeaturePill> _turned;

    /// <summary>Creates the pill of an add-on, on as the plan takes it.</summary>
    /// <param name="feature">The add-on as the agent offers it.</param>
    /// <param name="turned">Told when the player turns or chooses the pill.</param>
    public FeaturePill(FeatureView feature, Action<FeaturePill> turned)
    {
        ArgumentNullException.ThrowIfNull(feature);
        _turned = turned;
        Key = feature.Key;
        Name = feature.Name;
        Description = feature.Description;
        IsRecommended = feature.IsDefault;
        Baseline = feature.IsOn;
        IsOn = feature.IsOn;
        Size = feature.Package.Size;
        IsPresent = feature.IsPresent;
    }

    public string Key { get; }

    public string Name { get; }

    /// <summary>Plain text, shown beside the pill under the pointer or the keyboard's focus; empty for none.</summary>
    public string Description { get; }

    public bool HasDescription => Description.Length > 0;

    /// <summary>The member a group recommends, marked as such.</summary>
    public bool IsRecommended { get; }

    /// <summary>What the agent's plan takes, which the pill returns to when a change is dropped.</summary>
    public bool Baseline { get; set; }

    /// <summary>The bytes of the add-on's package; null when the site does not say.</summary>
    public long? Size { get; set; }

    /// <summary>The install holds the package already, so taking it downloads nothing.</summary>
    public bool IsPresent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Icon))]
    public partial bool IsOn { get; set; }

    /// <summary>False while the install went without the add-on on this game build, so it cannot be turned on here.</summary>
    [ObservableProperty]
    public partial bool CanTurn { get; set; } = true;

    /// <summary>A plus while the add-on is off, a check while it is on.</summary>
    public Geometry Icon => IsOn ? Theme.Icons.Check : Theme.Icons.Plus;

    public bool IsChanged => IsOn != Baseline;

    [RelayCommand]
    private void Turn() => _turned(this);
}

/// <summary>Add-ons of which exactly one is on, under the caption their author gave the choice.</summary>
/// <param name="Caption">The choice's caption.</param>
/// <param name="Members">The add-ons, in the release's order.</param>
/// <param name="Name">Ties the group's pills together, unique on the page.</param>
public sealed record FeatureGroupView(string Caption, IReadOnlyList<FeaturePill> Members, string Name);

/// <summary>
/// The add-ons of an install, or of the project a new install takes: the quiet line under its row that names them, and
/// the pills it unfolds into; kept across the page's states, so what the player turned stays until applied or dropped.
/// </summary>
/// <param name="install">The install the add-ons are of; null for the project a new install takes.</param>
/// <param name="project">The project whose add-ons they are.</param>
public sealed partial class FeaturePanel(Guid? install, Guid project) : ObservableObject
{
    public Guid? Install { get; } = install;

    public Guid Project { get; } = project;

    public ObservableCollection<FeaturePill> Toggles { get; } = [];

    public ObservableCollection<FeatureGroupView> Groups { get; } = [];

    /// <summary>Every pill, those on their own first.</summary>
    public IEnumerable<FeaturePill> Pills => Toggles.Concat(Groups.SelectMany(group => group.Members));

    /// <summary>The line: what the install has, or what installing takes.</summary>
    [ObservableProperty]
    public partial string Line { get; set; } = "";

    /// <summary>The names the line quotes, drawn brighter than the rest of it.</summary>
    [ObservableProperty]
    public partial string LineEmphasis { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Toggle), nameof(ToggleIcon))]
    public partial bool IsOpen { get; set; }

    /// <summary>
    /// Turns of a project not installed stay in the window, as a link from the site set them, rather than being remembered;
    /// installing takes them.
    /// </summary>
    public bool KeepsTurns { get; set; }

    /// <summary>No job runs on the game, so the add-ons can change.</summary>
    [ObservableProperty]
    public partial bool CanChange { get; set; }

    /// <summary>"Apply changes" stands under the pills, for an install that is up to date whose pills differ from it.</summary>
    [ObservableProperty]
    public partial bool ShowsApply { get; set; }

    [ObservableProperty]
    public partial string SummaryLead { get; set; } = "";

    /// <summary>What the change adds and removes, drawn brighter than its lead.</summary>
    [ObservableProperty]
    public partial string SummaryChanges { get; set; } = "";

    /// <summary>The version and the size the change downloads.</summary>
    [ObservableProperty]
    public partial string SummaryCaption { get; set; } = "";

    /// <summary>Says that the pending update or repair takes the change; empty for none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsApplyWith))]
    public partial string ApplyWith { get; set; } = "";

    public bool ShowsApplyWith => ApplyWith.Length > 0;

    public bool HasToggles => Toggles.Count > 0;

    public bool HasGroups => Groups.Count > 0;

    public string Toggle => IsOpen ? Strings.GamePage_CollapseChoice : Strings.GamePage_ChangeChoice;

    public Geometry ToggleIcon => IsOpen ? Theme.Icons.ChevronUp : Theme.Icons.ChevronDown;

    /// <summary>The player turned a pill away from what the plan takes.</summary>
    public bool HasChanges => Pills.Any(pill => pill.IsChanged);

    /// <summary>What the player turned, as the agent takes it: an add-on on its own by its state, a group by its chosen member.</summary>
    public IReadOnlyDictionary<string, bool?> Changes() =>
        Pills.Where(pill => pill.IsChanged && (pill.IsOn || Toggles.Contains(pill))).ToDictionary(pill => pill.Key, pill => (bool?)pill.IsOn);

    /// <summary>Puts every pill back where the plan has it, dropping what the player turned.</summary>
    public void Reset()
    {
        foreach (var pill in Pills)
        {
            pill.IsOn = pill.Baseline;
        }
    }

    /// <summary>
    /// Follows the agent's plan: a new set of add-ons makes new pills; otherwise a pill whose plan changed takes it, and the
    /// others keep what the player turned.
    /// </summary>
    /// <param name="features">The add-ons as the agent offers them.</param>
    /// <param name="variantCaption">The caption of the variants of a release from before add-ons; null for any other release.</param>
    /// <param name="leftOut">The add-ons the install went without on this game build, by key.</param>
    /// <param name="turned">Told when the player turns or chooses a pill.</param>
    public void Follow(IReadOnlyList<FeatureView> features, string? variantCaption, IReadOnlySet<string> leftOut, Action<FeaturePill> turned)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(leftOut);
        if (!Pills.Select(pill => pill.Key).SequenceEqual(Ordered(features).Select(feature => feature.Key)))
        {
            Toggles.Clear();
            Groups.Clear();
            foreach (var feature in features.Where(feature => feature.Group is null))
            {
                Toggles.Add(new FeaturePill(feature, turned));
            }

            foreach (var group in features.Where(feature => feature.Group is not null).GroupBy(feature => feature.Group!, StringComparer.Ordinal))
            {
                Groups.Add(new FeatureGroupView(variantCaption ?? group.Key, [.. group.Select(feature => new FeaturePill(feature, turned))],
                    $"{Install ?? Project:N}-{Groups.Count}"));
            }

            OnPropertyChanged(nameof(HasToggles));
            OnPropertyChanged(nameof(HasGroups));
        }
        else
        {
            foreach (var (pill, feature) in Pills.Zip(Ordered(features)))
            {
                if (pill.Baseline != feature.IsOn)
                {
                    pill.Baseline = feature.IsOn;
                    pill.IsOn = feature.IsOn;
                }

                (pill.Size, pill.IsPresent) = (feature.Package.Size, feature.IsPresent);
            }
        }

        foreach (var pill in Pills)
        {
            pill.CanTurn = !leftOut.Contains(pill.Key);
        }
    }

    // The pills of add-ons on their own come first, then each group's.
    private static IEnumerable<FeatureView> Ordered(IReadOnlyList<FeatureView> features) =>
        features.Where(feature => feature.Group is null)
            .Concat(features.Where(feature => feature.Group is not null).GroupBy(feature => feature.Group!, StringComparer.Ordinal)
                .SelectMany(group => group));

    [RelayCommand]
    private void ToggleOpen() => IsOpen = !IsOpen;
}

/// <summary>A project on offer for the game, as a column of the comparison shows it.</summary>
/// <param name="ProjectId">The project, which choosing it names.</param>
/// <param name="Team">The team's name.</param>
/// <param name="Avatar">The team's avatar; empty for none.</param>
/// <param name="Status">The site's status of the project; empty for none.</param>
/// <param name="StatusColor">The site's colour for it.</param>
/// <param name="Version">The latest release, e.g. "Version 2".</param>
/// <param name="Released">When it came out.</param>
public sealed record ProjectChoice(Guid ProjectId, string Team, string Avatar, string Status, string StatusColor, string Version, string Released)
{
    /// <summary>The project the game takes, as the player chose it or the preferred teams pick it.</summary>
    public bool IsChosen { get; init; }

    /// <summary>The game holds an install of the project already.</summary>
    public bool IsInstalled { get; init; }

    /// <summary>The column read out as one sentence.</summary>
    public string AccessibleName { get; init; } = "";

    /// <summary>What the project's progress measures, e.g. its proofreading; empty while it shows none.</summary>
    public string ProgressKind { get; init; } = "";

    /// <summary>From 0 to 100.</summary>
    public double Progress { get; init; }

    public string ProgressText { get; init; } = "";

    public bool HasStatus => Status.Length > 0;

    public bool HasProgress => ProgressText.Length > 0;
}

/// <summary>A page that sets a game's projects side by side, of which the player picks one.</summary>
public interface IProjectComparison : INotifyPropertyChanged
{
    /// <summary>The projects, one column each.</summary>
    ObservableCollection<ProjectChoice> Choices { get; }

    /// <summary>What the projects share, said once above them.</summary>
    string ChoicesText { get; }

    /// <summary>Takes a column's project, which it is given.</summary>
    ICommand ChooseProjectCommand { get; }

    /// <summary>Opens the site's explanation of the statuses.</summary>
    ICommand OpenStatusesCommand { get; }
}
