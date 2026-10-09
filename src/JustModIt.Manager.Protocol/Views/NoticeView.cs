using JustModIt.Manager.Core.Automation;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>Something the automation did or found, for the window to show in its own words.</summary>
/// <param name="Kind">What happened, which decides the properties that apply.</param>
public sealed record NoticeView(NoticeKind Kind)
{
    public GameKey? Game { get; init; }

    public string? GameName { get; init; }

    public string? TeamName { get; init; }

    public string? ReleaseLabel { get; init; }

    public int? ReleaseVersion { get; init; }

    public JobKind? Job { get; init; }

    public JobFailureView? Failure { get; init; }

    /// <summary>The store already offers the game in the app's target language, which is why nothing was installed.</summary>
    public bool OffersTargetLanguage { get; init; }

    /// <summary>The originals a forgotten install kept outside the game folder stay, listed among the leftover backups.</summary>
    public bool KeptBackups { get; init; }

    /// <summary>The games a summary is about.</summary>
    public IReadOnlyList<GameKey> Games { get; init; } = [];

    /// <summary>Those of <see cref="Games"/> with an update.</summary>
    public IReadOnlyList<GameKey> Updates { get; init; } = [];

    /// <summary>Those of <see cref="Games"/> whose install needs a repair.</summary>
    public IReadOnlyList<GameKey> Repairs { get; init; } = [];

    /// <summary>Maps a notice of the automation.</summary>
    /// <param name="notice">The typed notice the engine produced.</param>
    public static NoticeView From(AutomationNotice notice)
    {
        ArgumentNullException.ThrowIfNull(notice);
        return new NoticeView(notice.Kind)
        {
            Game = notice.Game,
            GameName = notice.GameName,
            TeamName = notice.TeamName,
            ReleaseLabel = notice.ReleaseLabel,
            ReleaseVersion = notice.ReleaseVersion,
            Job = notice.Job,
            Failure = notice.Failure is { } failure ? JobFailureView.From(failure) : null,
            OffersTargetLanguage = notice.OffersTargetLanguage,
            KeptBackups = notice.KeptBackups,
            Games = [.. notice.Games],
            Updates = [.. notice.Updates],
            Repairs = [.. notice.Repairs],
        };
    }
}
