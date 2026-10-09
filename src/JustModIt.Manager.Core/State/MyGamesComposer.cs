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

    public IReadOnlyDictionary<FailureKey, FailedAttempt> Failures { get; init; } = ImmutableDictionary<FailureKey, FailedAttempt>.Empty;

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

    private static MyGame Compose(MyGamesInput input, GameKey key, InstalledGame? game, GameOffer offer, List<InstallRecord> records)
    {
        var preferences = input.Settings.GetGame(key);
        var running = input.Running.TryGetValue(key, out var processes) && processes.Count > 0 ? processes : [];
        var current = game is { State: GameInstallState.Installed } && running.Count > 0
            ? game with { State = GameInstallState.Running, Reason = GameStateReason.GameRunning }
            : game;
        var job = input.Jobs.GetValueOrDefault(key);
        List<MyInstall> installs = [.. records
            .OrderByDescending(record => game is not null && PathComparison.AreSame(record.InstallDirectory, game.InstallDirectory))
            .ThenByDescending(record => record.InstalledAt)
            .Select(record => Install(input, record, key, game, current, offer, job is not null))];
        var project = offer.Choose(preferences.ProjectId, input.Settings.PreferredTeams);
        var first = installs.FirstOrDefault();
        return new MyGame
        {
            Key = key,
            Name = (first?.Project ?? project)?.Project.Game.Name ?? first?.Record.Source?.GameName ?? game?.Name ?? first?.Record.GameName
                ?? key.ToString(),
            Status = MyGameStatusRules.Resolve(current, job is not null, installs.Select(install => install.Status)),
            Game = game,
            Projects = offer.Projects,
            Project = project,
            Installs = [.. installs],
            Preferences = preferences,
            Job = job,
            NewInstall = game is null ? null : InstallAdmission.For(input.InstallPolicy, game, records),
            OffersTargetLanguage = offer.OffersTargetLanguage
                || (input.Language.Code is { } language && game?.HasLanguage(language) == true),
            LastFailure = input.Failures.GetValueOrDefault(new FailureKey(key, null)),
            RunningProcesses = [.. running.Select(process => process.Name).Distinct(StringComparer.OrdinalIgnoreCase)],
        };
    }

    // The game is what its store reports, and current the same, running when it is known to run.
    private static MyInstall Install(MyGamesInput input, InstallRecord record, GameKey key, InstalledGame? game, InstalledGame? current,
        GameOffer offer, bool hasJob)
    {
        var project = InstalledProject(input.Index, offer, record, key, game);
        RepairReason? repair = input.Repairs.TryGetValue(record.Id, out var found) ? found : null;
        return new MyInstall
        {
            Record = record,
            Status = MyGameStatusRules.Resolve(current, record, project, repair, hasJob, input.UnavailableBackups.Contains(record.Id)),
            Project = project,
            LastFailure = input.Failures.GetValueOrDefault(new FailureKey(key, record.Id)),
        };
    }

    // Also once the project is no longer on offer, since the player chose it and it keeps its updates; the index holds
    // installed projects even when they no longer link the game.
    private static ProjectOffer? InstalledProject(InstallableIndex index, GameOffer offer, InstallRecord record, GameKey key,
        InstalledGame? game)
    {
        if (record.Source?.ProjectUuid is not { } uuid || record.IsTest)
        {
            return null;
        }

        return offer.Projects.FirstOrDefault(project => project.Project.ProjectUuid == uuid)
            ?? (index.FindProject(uuid) is { } entry
                ? new ProjectOffer(entry, ReleaseSelector.Select(entry, key, game?.Platform ?? OperatingSystemKind.Unknown))
                : null);
    }
}
