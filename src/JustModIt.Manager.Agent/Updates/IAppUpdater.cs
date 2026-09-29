using Velopack;
using Velopack.Sources;

namespace JustModIt.Manager.Agent.Updates;

/// <summary>Where the app's own updates come from and how they are applied.</summary>
public interface IAppUpdater
{
    /// <summary>This copy can update itself: it was installed, and the app has an update feed.</summary>
    bool IsSupported { get; }

    /// <summary>A version downloaded earlier that was not applied yet.</summary>
    string? PendingVersion { get; }

    /// <summary>Looks for a newer version on the feed.</summary>
    /// <param name="cancellationToken">Stops waiting for the feed.</param>
    /// <returns>The newer version, or null when this is the newest.</returns>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the version <see cref="CheckAsync"/> found.</summary>
    /// <param name="progress">Receives the percentage done.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Starts the updater, which waits for this process to exit, applies the downloaded version and starts the app
    /// again with <paramref name="restartArgs"/>.
    /// </summary>
    /// <param name="restartArgs">The command line to restart with; empty opens the window.</param>
    void ApplyAfterExit(IReadOnlyList<string> restartArgs);
}

/// <summary>Velopack with the app's GitHub releases.</summary>
internal sealed class VelopackUpdater : IAppUpdater
{
    private readonly UpdateManager? _manager;
    private UpdateInfo? _found;

    /// <summary>Creates the updater; a copy that was not installed, or has no feed, cannot update itself.</summary>
    /// <param name="options">The app's update feed.</param>
    public VelopackUpdater(AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Updates is { } feed)
        {
            var manager = new UpdateManager(new GithubSource(feed.Url, accessToken: null, prerelease: feed.IncludesPrereleases),
                new UpdateOptions { ExplicitChannel = feed.Channel });
            _manager = manager.IsInstalled ? manager : null;
        }
    }

    public bool IsSupported => _manager is not null;

    public string? PendingVersion => _manager?.UpdatePendingRestart?.Version.ToString();

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        _found = await Manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return _found?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken) =>
        Manager.DownloadUpdatesAsync(_found ?? throw new InvalidOperationException("No update was found to download."), progress,
            cancellationToken);

    public void ApplyAfterExit(IReadOnlyList<string> restartArgs) =>
        Manager.WaitExitThenApplyUpdates(Manager.UpdatePendingRestart, silent: true, restart: true, [.. restartArgs]);

    private UpdateManager Manager => _manager ?? throw new InvalidOperationException("This copy of the app cannot update itself.");
}
