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
    ChangeVariant,
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

/// <summary>A variant of the project the player can pick, such as text only next to text and voice.</summary>
/// <param name="Name">The variant's name, which commands choose it by.</param>
/// <param name="IsRecommended">The release recommends it.</param>
public sealed partial class VariantChoice(string Name, bool IsRecommended) : ObservableObject
{
    public string Name { get; } = Name;

    public bool IsRecommended { get; } = IsRecommended;

    [ObservableProperty]
    public partial bool IsChosen { get; set; }
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
