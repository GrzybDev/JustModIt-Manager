using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamStateResolverTests
{
    [Theory]
    [InlineData(0x4UL, SteamAppStatus.Installed, GameInstallState.Installed, GameStateReason.None)]
    [InlineData(0x14UL, SteamAppStatus.Installed, GameInstallState.Installed, GameStateReason.None)]
    [InlineData(0x80004UL, SteamAppStatus.Installed, GameInstallState.Installed, GameStateReason.None)]
    [InlineData(0x6UL, SteamAppStatus.UpdatePending, GameInstallState.UpdatePending, GameStateReason.UpdateRequired)]
    [InlineData(1030UL, SteamAppStatus.Updating, GameInstallState.Updating, GameStateReason.StoreBusy)]
    [InlineData(0x606UL, SteamAppStatus.Updating, GameInstallState.Updating, GameStateReason.UpdatePaused)]
    [InlineData(0x2UL, SteamAppStatus.Queued, null, GameStateReason.None)]
    [InlineData(18UL, SteamAppStatus.Queued, null, GameStateReason.None)]
    [InlineData(1026UL, SteamAppStatus.Installing, GameInstallState.Installing, GameStateReason.None)]
    [InlineData(1042UL, SteamAppStatus.Installing, GameInstallState.Installing, GameStateReason.None)]
    [InlineData(0x1UL, SteamAppStatus.NotInstalled, null, GameStateReason.None)]
    [InlineData(0x40UL, SteamAppStatus.NotInstalled, null, GameStateReason.None)]
    [InlineData(0x0UL, SteamAppStatus.Unknown, GameInstallState.Unknown, GameStateReason.UnknownStoreState)]
    [InlineData(0x24UL, SteamAppStatus.FilesMissing, GameInstallState.Broken, GameStateReason.FilesMissing)]
    [InlineData(0x84UL, SteamAppStatus.FilesCorrupt, GameInstallState.Broken, GameStateReason.FilesCorrupt)]
    [InlineData(0x200004UL, SteamAppStatus.Validating, GameInstallState.Validating, GameStateReason.None)]
    [InlineData(0x4000004UL, SteamAppStatus.Validating, GameInstallState.Validating, GameStateReason.None)]
    [InlineData(0xA00004UL, SteamAppStatus.Updating, GameInstallState.Updating, GameStateReason.StoreBusy)]
    [InlineData(0x804UL, SteamAppStatus.Uninstalling, GameInstallState.Updating, GameStateReason.Uninstalling)]
    [InlineData(0x8004UL, SteamAppStatus.MovingFolder, GameInstallState.Updating, GameStateReason.MovingFolder)]
    [InlineData(0x2004UL, SteamAppStatus.InUse, GameInstallState.Running, GameStateReason.GameRunning)]
    [InlineData(0x40004UL, SteamAppStatus.InUse, GameInstallState.Running, GameStateReason.GameRunning)]
    [InlineData(0x44UL, SteamAppStatus.Unknown, GameInstallState.Unknown, GameStateReason.UnknownStoreState)]
    public void Flags_decide_the_status_and_the_game_state(ulong flags, SteamAppStatus status, GameInstallState? state,
        GameStateReason reason)
    {
        var resolved = SteamStateResolver.Resolve(Manifest((SteamAppStates)flags), installDirectoryExists: true);

        resolved.Status.Should().Be(status);
        resolved.GameState.Should().Be(state is { } expected ? (expected, reason) : null);
    }
    [Fact]
    public void Downloaded_bytes_or_any_download_data_turn_a_queued_install_into_an_install()
    {
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired) with { BytesDownloaded = 1 }, false).Status
            .Should().Be(SteamAppStatus.Installing);
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired), false, new SteamLiveSignals(SteamDownload.Leftover)).Status
            .Should().Be(SteamAppStatus.Installing);
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired), false, new SteamLiveSignals(IsRegistryUpdating: true)).Status
            .Should().Be(SteamAppStatus.Installing);
    }

    [Fact]
    public void A_pending_target_build_means_an_update_is_pending()
    {
        var resolved = SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled) with { TargetBuildId = "200" }, true);

        resolved.GameState.Should().Be((GameInstallState.UpdatePending, GameStateReason.UpdateRequired));
    }

    [Theory]
    [InlineData(0x4UL, null, SteamDownload.Leftover, GameInstallState.Installed, GameStateReason.UnfinishedDownload)]
    [InlineData(0x14UL, null, SteamDownload.Leftover, GameInstallState.Installed, GameStateReason.UnfinishedDownload)]
    [InlineData(0x4UL, null, SteamDownload.Active, GameInstallState.Updating, GameStateReason.DownloadInProgress)]
    [InlineData(0x6UL, null, SteamDownload.Leftover, GameInstallState.UpdatePending, GameStateReason.DownloadInProgress)]
    [InlineData(0x4UL, "200", SteamDownload.Leftover, GameInstallState.UpdatePending, GameStateReason.DownloadInProgress)]
    [InlineData(0x606UL, null, SteamDownload.Leftover, GameInstallState.Updating, GameStateReason.UpdatePaused)]
    [InlineData(0x2004UL, null, SteamDownload.Leftover, GameInstallState.Running, GameStateReason.GameRunning)]
    public void Download_data_blocks_only_when_a_job_may_use_it(ulong flags, string? targetBuild, SteamDownload download,
        GameInstallState state, GameStateReason reason) =>
        SteamStateResolver.Resolve(Manifest((SteamAppStates)flags) with { TargetBuildId = targetBuild }, true, new SteamLiveSignals(download))
            .GameState.Should().Be((state, reason));

    [Fact]
    public void The_registry_updating_value_means_steam_is_updating_even_with_left_over_data() =>
        SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled), true,
                new SteamLiveSignals(SteamDownload.Leftover, IsRegistryUpdating: true))
            .GameState.Should().Be((GameInstallState.Updating, GameStateReason.StoreBusy));

    private static SteamAppManifest Manifest(SteamAppStates flags) => new()
    {
        AppId = 10,
        Name = "Example",
        InstallDir = "Example",
        StateFlags = flags,
        BuildId = "100",
    };
}
