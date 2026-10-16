using JustModIt.Manager.Core.Settings;
using Velopack;
using Velopack.Sources;

namespace JustModIt.Manager.Agent.Updates;

/// <summary>A version of the app on its update feed.</summary>
/// <param name="Version">The version.</param>
/// <param name="Notes">What it changes, in Markdown, as it was packed with it; null when it carries no notes.</param>
public sealed record AppRelease(string Version, string? Notes);

/// <summary>Where the app's own updates come from and how they are applied.</summary>
public interface IAppUpdater
{
    /// <summary>This copy can update itself: it was installed, and the app has an update feed.</summary>
    bool IsSupported { get; }

    /// <summary>A version downloaded earlier that was not applied yet.</summary>
    AppRelease? Pending { get; }

    /// <summary>Looks for a newer version on the feed.</summary>
    /// <param name="cancellationToken">Stops waiting for the feed.</param>
    /// <returns>The newer version, or null when this is the newest.</returns>
    Task<AppRelease?> CheckAsync(CancellationToken cancellationToken);

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
    private readonly UpdateFeed? _feed;
    private readonly ISettingsService _settings;
    private UpdateManager? _manager;
    private bool _includesPrereleases;
    private UpdateInfo? _found;

    /// <summary>Creates the updater; a copy that was not installed, or has no feed, cannot update itself.</summary>
    /// <param name="options">The app's update feed.</param>
    /// <param name="settings">Whether the player takes the app's test versions.</param>
    public VelopackUpdater(AgentOptions options, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(options);
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        if (options.Updates is { } feed)
        {
            _includesPrereleases = settings.Current.UpdatePrereleases;
            var manager = Create(feed, _includesPrereleases);
            if (manager.IsInstalled)
            {
                (_feed, _manager) = (feed, manager);
            }
        }
    }

    public bool IsSupported => _manager is not null;

    public AppRelease? Pending => _manager?.UpdatePendingRestart is { } pending ? Release(pending) : null;

    public async Task<AppRelease?> CheckAsync(CancellationToken cancellationToken)
    {
        // Velopack's GitHub source takes pre-releases or not when it is made, so a changed setting needs a new one.
        if (_feed is not null && _settings.Current.UpdatePrereleases != _includesPrereleases)
        {
            _includesPrereleases = !_includesPrereleases;
            _manager = Create(_feed, _includesPrereleases);
        }

        _found = await Manager.CheckForUpdatesAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return _found is null ? null : Release(_found.TargetFullRelease);
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken) =>
        Manager.DownloadUpdatesAsync(_found ?? throw new InvalidOperationException("No update was found to download."), progress,
            cancellationToken);

    public void ApplyAfterExit(IReadOnlyList<string> restartArgs) =>
        Manager.WaitExitThenApplyUpdates(Manager.UpdatePendingRestart, silent: true, restart: true, [.. restartArgs]);

    private UpdateManager Manager => _manager ?? throw new InvalidOperationException("This copy of the app cannot update itself.");

    private static UpdateManager Create(UpdateFeed feed, bool includesPrereleases) =>
        new(new GithubSource(feed.Url, accessToken: null, prerelease: includesPrereleases),
            new UpdateOptions { ExplicitChannel = feed.Channel });

    private static AppRelease Release(VelopackAsset asset) =>
        new(asset.Version.ToString(), string.IsNullOrWhiteSpace(asset.NotesMarkdown) ? null : asset.NotesMarkdown);
}
