using System.Collections.Immutable;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.State;

/// <summary>Everything the player's list is made from, read already, so making the list is a pure function.</summary>
public sealed record MyGamesInput
{
    public required LibrarySnapshot Library { get; init; }

    public required IReadOnlyCollection<InstallRecord> Records { get; init; }

    public required InstallableIndex Index { get; init; }

    public required AppSettings Settings { get; init; }

    public TargetLanguage Language { get; init; } = TargetLanguage.None;

    public IInstallPolicy InstallPolicy { get; init; } = SideBySideInstallPolicy.Instance;

    public IReadOnlyDictionary<GameKey, JobSnapshot> Jobs { get; init; } = ImmutableDictionary<GameKey, JobSnapshot>.Empty;

    /// <summary>What verification found wrong with installs, by record id.</summary>
    public IReadOnlyDictionary<Guid, RepairReason> Repairs { get; init; } = ImmutableDictionary<Guid, RepairReason>.Empty;

    /// <summary>Installs whose originals' folder is not there, by record id.</summary>
    public IReadOnlySet<Guid> UnavailableBackups { get; init; } = ImmutableHashSet<Guid>.Empty;

    public IReadOnlyDictionary<GameKey, FailedAttempt> Failures { get; init; } = ImmutableDictionary<GameKey, FailedAttempt>.Empty;

    /// <summary>Games known to run right now, with what runs from their folders.</summary>
    public IReadOnlyDictionary<GameKey, ValueList<RunningProcess>> Running { get; init; } =
        ImmutableDictionary<GameKey, ValueList<RunningProcess>>.Empty;
}

/// <summary>
/// Makes the player's list: the detected games with a project on offer plus every game with an install, so an install
/// keeps its updates and can be removed even offline or after its game is gone; the rest of the catalogue is only browsed.
/// </summary>
public static class MyGamesComposer
{
    /// <summary>Makes the player's list, sorted by name.</summary>
    /// <param name="input">Everything the list is made from.</param>
    public static ImmutableArray<MyGame> Compose(MyGamesInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var recordsByGame = input.Records.GroupBy(record => record.Game).ToDictionary(group => group.Key, group => group.ToList());
        var games = new List<MyGame>();
        foreach (var key in input.Library.Games.Keys.Union(recordsByGame.Keys))
        {
            var game = input.Library.Find(key);
            var offer = GameOffer.For(input.Index, key, game?.Platform ?? OperatingSystemKind.Unknown, input.Language);
            var records = recordsByGame.GetValueOrDefault(key) ?? [];
            if (records.Count == 0 && offer.Projects.Count == 0)
            {
                continue;
            }

            games.Add(Compose(input, key, game, offer, records));
        }

        return [.. games.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(game => game.Key.ToString(), StringComparer.Ordinal)];
    }

    /// <summary>The install the player's list shows for a game: the latest where its store reports the game, else the latest.</summary>
    private static InstallRecord RecordFor(List<InstallRecord> records, InstalledGame? game)
    {
        var latest = records.OrderByDescending(record => record.InstalledAt).ToList();
        return latest.Find(record => game is not null && PathComparison.AreSame(record.InstallDirectory, game.InstallDirectory))
            ?? latest[0];
    }

    private static MyGame Compose(MyGamesInput input, GameKey key, InstalledGame? game, GameOffer offer, List<InstallRecord> records)
    {
        var record = records.Count > 0 ? RecordFor(records, game) : null;
        var preferences = input.Settings.GetGame(key);
        var running = input.Running.TryGetValue(key, out var processes) && processes.Count > 0 ? processes : [];
        var current = game is { State: GameInstallState.Installed } && running.Count > 0
            ? game with { State = GameInstallState.Running, Reason = GameStateReason.GameRunning }
            : game;
        var project = record is not null
            ? InstalledProject(input.Index, offer, record, key, game)
            : offer.Choose(ParseProject(preferences), input.Settings.PreferredTeams);
        var job = input.Jobs.GetValueOrDefault(key);
        RepairReason? repair = record is not null && input.Repairs.TryGetValue(record.Id, out var found) ? found : null;
        var status = MyGameStatusRules.Resolve(current, record, project, repair, job is not null,
            record is not null && input.UnavailableBackups.Contains(record.Id));
        return new MyGame
        {
            Key = key,
            Name = project?.Project.Game.Name ?? record?.Source?.GameName ?? game?.Name ?? record?.GameName ?? key.ToString(),
            Status = status,
            Game = game,
            Projects = offer.Projects,
            Project = project,
            Installed = record,
            Preferences = preferences,
            Job = job,
            NewInstall = game is null ? null : InstallAdmission.For(input.InstallPolicy, game, records),
            OffersTargetLanguage = offer.OffersTargetLanguage
                || (input.Language.Code is { } language && game?.HasLanguage(language) == true),
            LastFailure = input.Failures.GetValueOrDefault(key),
            RunningProcesses = [.. running.Select(process => process.Name).Distinct(StringComparer.OrdinalIgnoreCase)],
        };
    }

    // Also once the project is no longer on offer, since the player chose it and it keeps its updates; the index holds
    // installed projects even when they no longer link the game.
    private static ProjectOffer? InstalledProject(InstallableIndex index, GameOffer offer, InstallRecord record, GameKey key,
        InstalledGame? game)
    {
        if (!Guid.TryParse(record.Source?.ProjectId, out var uuid) || record.IsTest)
        {
            return null;
        }

        return offer.Projects.FirstOrDefault(project => project.Project.ProjectUuid == uuid)
            ?? (index.FindProject(uuid) is { } entry
                ? new ProjectOffer(entry, ReleaseSelector.Select(entry, key, game?.Platform ?? OperatingSystemKind.Unknown))
                : null);
    }

    private static Guid? ParseProject(GamePreferences preferences) =>
        Guid.TryParse(preferences.ProjectId, out var project) ? project : null;
}
