using JustModIt.Manager.Agent.Lifetime;
using JustModIt.Manager.Agent.Shell;
using JustModIt.Manager.Agent.Updates;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Agent.Ipc;

/// <summary>
/// Carries out the window's commands; jobs are only started here, and how they go reaches the window with the state.
/// </summary>
/// <param name="coordinator">The engine's state, to find games and installs.</param>
/// <param name="jobs">Starts and cancels jobs.</param>
/// <param name="engine">Removes leftover backups.</param>
/// <param name="settings">Takes the player's settings and choices.</param>
/// <param name="library">The detected games and the library folders the player added.</param>
/// <param name="volumes">Tells the drives apart, to check a backup folder.</param>
/// <param name="fileSystem">Finds a game's program.</param>
/// <param name="checks">Runs a check the player asks for.</param>
/// <param name="updates">Restarts into a ready update.</param>
/// <param name="status">Tells whether an update is ready.</param>
/// <param name="lifetime">Exits the app.</param>
/// <param name="shell">Opens folders and links and starts programs.</param>
/// <param name="options">The app's folders and the folder it is installed in.</param>
/// <param name="time">Dates the player's confirmation of being an adult.</param>
public sealed class AgentCommands(
    StateCoordinator coordinator,
    JobRunner jobs,
    IInstallEngine engine,
    ISettingsService settings,
    ILibraryService library,
    IVolumes volumes,
    IFileSystem fileSystem,
    Checks checks,
    UpdateService updates,
    AgentStatus status,
    AgentLifetime lifetime,
    IDesktopShell shell,
    AgentOptions options,
    TimeProvider time)
{
    /// <summary>Carries out one of the window's commands.</summary>
    /// <param name="request">A command, as opposed to a query.</param>
    /// <param name="cancellationToken">Cancelled when the window's connection closes.</param>
    /// <returns>The answer's payload; null for an empty answer.</returns>
    public async ValueTask<Message?> HandleAsync(Message request, CancellationToken cancellationToken) => request switch
    {
        InstallProject install => Start(Install(install)),
        UpdateProject update => Start(new UpdateJobRequest(FindInstall(update.Install).Record)),
        RepairProject repair => Start(new RepairJobRequest(FindInstall(repair.Install).Record)),
        UninstallProject uninstall => Start(new UninstallJobRequest(FindInstall(uninstall.Install).Record)),
        TestInstallPackage test => Start(TestInstall(test)),
        MoveBackups => Done(() => coordinator.MoveBackups(BackupChoice.From(settings.Current))),
        RemoveLeftoverBackups remove => await RemoveLeftoverBackupsAsync(remove.RecordIds, cancellationToken).ConfigureAwait(false),
        CancelJob cancel => Done(() => jobs.Cancel(cancel.Job)),
        DismissFailure dismiss => Done(() => coordinator.DismissFailure(dismiss.Game, dismiss.Install)),
        OpenedGame opened => Done(() => coordinator.RequestVerification(opened.Game)),
        CheckNow => Done(() => _ = checks.CheckNowAsync()),
        ChangeSettings change => ChangeSettings(change.Settings),
        SetGameAutomation automation => Done(() => settings.Update(current => ChooseAutomation(current, automation))),
        ChooseProject choose => Done(() => settings.Update(current => current.WithGame(choose.Game, preferences => preferences with
        {
            ProjectId = choose.ProjectId,
            VariantName = preferences.ProjectId == choose.ProjectId ? preferences.VariantName : null,
        }))),
        ChooseVariant choose => Done(() => settings.Update(current =>
            current.WithGame(choose.Game, preferences => preferences with { VariantName = choose.VariantName }))),
        HideGame hide => Done(() => settings.Update(current =>
            current.WithGame(hide.Game, preferences => preferences with { IsHidden = hide.IsHidden }))),
        ConfirmAdultContent => Done(() => settings.Update(current => current.ConfirmAdultContent(time.GetUtcNow()))),
        LaunchGame launch => Launch(library.Snapshot.Find(launch.Game)),
        OpenFolder open => Open(FolderOf(open)),
        AddLibraryFolder add => await AddLibraryFolderAsync(add.Path, cancellationToken).ConfigureAwait(false),
        RemoveLibraryFolder remove => Done(() => library.RemoveLibraryFolder(new LibraryFolder(StoreOf(remove.Store), remove.Path))),
        ValidateBackupFolder validate => new BackupFolderChecked(CheckBackupFolder(validate.Folder)),
        ApplyAppUpdate => RestartToUpdate(),
        ExitApp exit => Done(() => _ = lifetime.RequestExitAsync(exit.Mode)),
        _ => throw new RequestFailedException(ErrorCode.Unsupported),
    };

    private static Message? Done(Action action)
    {
        action();
        return null;
    }

    private static RequestFailedException Invalid(string detail) => new(ErrorCode.InvalidRequest, detail);

    private static StoreId StoreOf(string store) =>
        StoreId.TryParse(store, out var id) ? id : throw Invalid("The store is not a store id.");

    private static AppSettings ChooseAutomation(AppSettings current, SetGameAutomation automation)
    {
        if (automation.AutoInstall is { } install)
        {
            current = current.ChooseAutoInstall(automation.Game, install);
        }

        return automation.AutoUpdate is { } update ? current.ChooseAutoUpdate(automation.Game, update) : current;
    }

    private MyGame Find(GameKey game) => coordinator.Current.Find(game) ?? throw Invalid("The game is not among the player's games.");

    private InstalledGame Detected(GameKey game) => Find(game).Game ?? throw Invalid("No store reports the game.");

    private MyInstall FindInstall(Guid install) =>
        coordinator.Current.Games.Select(game => game.FindInstall(install)).OfType<MyInstall>().FirstOrDefault()
        ?? throw Invalid("The install is not among the player's games.");

    private InstallJobRequest Install(InstallProject request)
    {
        var game = Detected(request.Game);
        var offer = Find(request.Game).Projects.FirstOrDefault(offer => offer.Project.ProjectUuid == request.ProjectId)
            ?? throw Invalid("The project is not on offer for the game.");
        return new InstallJobRequest(game.Key, game.Name, offer.Project.Locator, request.VariantName);
    }

    private TestInstallJobRequest TestInstall(TestInstallPackage request)
    {
        var game = Detected(request.Game);
        return Path.IsPathFullyQualified(request.PackagePath)
            ? new TestInstallJobRequest(game.Key, game.Name, request.PackagePath)
            : throw Invalid("The package path is not fully qualified.");
    }

    private JobStarted Start(JobRequest request)
    {
        var handle = jobs.Start(request);
        // A refused job comes back finished, without any event the window would learn it from.
        if (handle.Completion is { IsCompletedSuccessfully: true, Result: var refused })
        {
            if (refused.Failure?.Reason == JobFailureReason.AlreadyRunning)
            {
                throw new RequestFailedException(ErrorCode.Failed, "The game already has a job.");
            }

            if (refused.Outcome == JobOutcome.Cancelled)
            {
                throw new RequestFailedException(ErrorCode.ShuttingDown, "The app is exiting and starts no new jobs.");
            }
        }

        return new JobStarted(handle.Job.Id);
    }

    private async Task<Message?> RemoveLeftoverBackupsAsync(IReadOnlyList<Guid> recordIds, CancellationToken cancellationToken)
    {
        foreach (var record in recordIds)
        {
            await engine.RemoveLeftoverBackupsAsync(record, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private Message? ChangeSettings(SettingsView view)
    {
        var current = settings.Current;
        var choosesFolder = view.BackupLocation == BackupLocation.CustomFolder;
        if (choosesFolder && view.CustomBackupFolder is null)
        {
            throw Invalid("A custom backup location needs a folder.");
        }

        // A folder checked when it was picked may be gone by the time the player switches back to it.
        if (view.CustomBackupFolder is { } folder
            && (!string.Equals(folder, current.CustomBackupFolder, StringComparison.Ordinal)
                || (choosesFolder && current.BackupLocation != BackupLocation.CustomFolder)))
        {
            if (CheckBackupFolder(folder) is { } problem)
            {
                throw Invalid($"The backup folder is refused: {problem}.");
            }

            view = view with { CustomBackupFolder = PathCanonicalizer.Canonicalize(folder) };
        }

        var updated = settings.Update(view.ApplyTo);
        // The automation waited for the first-run setup; a check now fills the player's list and acts on their choices.
        if (!current.OnboardingCompleted && updated.OnboardingCompleted)
        {
            _ = checks.CheckNowAsync();
        }

        return null;
    }

    // Requests are answered on the thread pool, so the check's file system calls hold up no other request.
    private BackupFolderProblem? CheckBackupFolder(string folder)
    {
        var gameFolders = library.Snapshot.Games.Values.Select(game => game.InstallDirectory)
            .Concat(coordinator.Current.Games.SelectMany(game => game.Installs).Select(install => install.Record.InstallDirectory))
            .ToList();
        return BackupFolderPolicy.Check(folder, gameFolders, options.InstallDirectory, volumes);
    }

    private async Task<Message> AddLibraryFolderAsync(string path, CancellationToken cancellationToken)
    {
        var result = await library.AddLibraryFolderAsync(path, cancellationToken).ConfigureAwait(false);
        return new LibraryFolderAdded(result.Folder?.Store.Value, result.Folder?.Path, result.Rejection);
    }

    private string FolderOf(OpenFolder request)
    {
        if (request.Folder == AppFolder.Logs)
        {
            Directory.CreateDirectory(options.Locations.LogsDirectory);
            return options.Locations.LogsDirectory;
        }

        if (request.Folder == AppFolder.InstallBackups)
        {
            var install = request.Install ?? throw Invalid("The folder belongs to an install, which is missing.");
            return FindInstall(install).Record.Backup.Directory;
        }

        var game = Find(request.Game ?? throw Invalid("The folder belongs to a game, which is missing."));
        return game.Game?.InstallDirectory ?? (game.Installs is [var first, ..] ? first.Record.InstallDirectory : null)
            ?? throw Invalid("The game has no such folder.");
    }

    // Answered before the window is asked to close, since closing ends the connection the answer would take.
    private Message? RestartToUpdate() => status.Update.State == AppUpdateState.Ready
        ? Done(() => _ = updates.RestartToUpdateAsync())
        : throw Invalid("No update is ready.");

    private Message? Open(string target) =>
        shell.Open(target) ? null : throw new RequestFailedException(ErrorCode.Failed, "The system could not open it.");

    private Message? Launch(InstalledGame? game) => game?.Launch switch
    {
        LaunchLink link => Open(link.Uri),
        LaunchProgram program => StartProgram(program.Resolve(fileSystem, game.InstallDirectory)
            ?? throw new RequestFailedException(ErrorCode.Failed, "The game's program is missing or leads out of its folder.")),
        _ => throw new RequestFailedException(ErrorCode.Unsupported),
    };

    private Message? StartProgram(ProgramStart program) =>
        shell.Start(program) ? null : throw new RequestFailedException(ErrorCode.Failed, "The system could not start it.");
}
