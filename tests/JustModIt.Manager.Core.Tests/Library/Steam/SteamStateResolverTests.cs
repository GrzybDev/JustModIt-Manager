using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamStateResolverTests
{
    [Theory]
    [InlineData(0x4UL, SteamAppStatus.Installed, GameInstallState.Installed)]
    [InlineData(0x14UL, SteamAppStatus.Installed, GameInstallState.Installed)]
    [InlineData(0x80004UL, SteamAppStatus.Installed, GameInstallState.Installed)]
    [InlineData(0x6UL, SteamAppStatus.UpdatePending, GameInstallState.UpdatePending)]
    [InlineData(1030UL, SteamAppStatus.Updating, GameInstallState.Updating)]
    [InlineData(0x2UL, SteamAppStatus.Queued, null)]
    [InlineData(18UL, SteamAppStatus.Queued, null)]
    [InlineData(1026UL, SteamAppStatus.Installing, GameInstallState.Installing)]
    [InlineData(1042UL, SteamAppStatus.Installing, GameInstallState.Installing)]
    [InlineData(0x1UL, SteamAppStatus.NotInstalled, null)]
    [InlineData(0x40UL, SteamAppStatus.NotInstalled, null)]
    [InlineData(0x0UL, SteamAppStatus.Unknown, GameInstallState.Unknown)]
    [InlineData(0x24UL, SteamAppStatus.FilesMissing, GameInstallState.Broken)]
    [InlineData(0x84UL, SteamAppStatus.FilesCorrupt, GameInstallState.Broken)]
    [InlineData(0x200004UL, SteamAppStatus.Validating, GameInstallState.Validating)]
    [InlineData(0x4000004UL, SteamAppStatus.Validating, GameInstallState.Validating)]
    [InlineData(0xA00004UL, SteamAppStatus.Updating, GameInstallState.Updating)]
    [InlineData(0x804UL, SteamAppStatus.Uninstalling, GameInstallState.Updating)]
    [InlineData(0x8004UL, SteamAppStatus.MovingFolder, GameInstallState.Updating)]
    [InlineData(0x2004UL, SteamAppStatus.InUse, GameInstallState.Running)]
    [InlineData(0x40004UL, SteamAppStatus.InUse, GameInstallState.Running)]
    [InlineData(0x44UL, SteamAppStatus.Unknown, GameInstallState.Unknown)]
    public void Flags_decide_the_status_and_the_game_state(ulong flags, SteamAppStatus status, GameInstallState? state)
    {
        var resolved = SteamStateResolver.Resolve(Manifest((SteamAppStates)flags), installDirectoryExists: true);

        resolved.Status.Should().Be(status);
        resolved.GameState?.State.Should().Be(state);
        if (state is null)
        {
            resolved.GameState.Should().BeNull();
        }
    }

    [Fact]
    public void Only_an_installed_app_allows_writing_and_every_other_state_names_a_reason()
    {
        SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled), true).GameState
            .Should().Be((GameInstallState.Installed, GameStateReason.None));
        SteamStateResolver.Resolve(Manifest((SteamAppStates)0x44), true).GameState
            .Should().Be((GameInstallState.Unknown, GameStateReason.UnknownStoreState));
        SteamStateResolver.Resolve(Manifest((SteamAppStates)0x804), true).GameState
            .Should().Be((GameInstallState.Updating, GameStateReason.Uninstalling));
        SteamStateResolver.Resolve(Manifest((SteamAppStates)0x8004), true).GameState
            .Should().Be((GameInstallState.Updating, GameStateReason.MovingFolder));
        SteamStateResolver.Resolve(Manifest((SteamAppStates)0x606), true).GameState
            .Should().Be((GameInstallState.Updating, GameStateReason.UpdatePaused));
    }

    [Fact]
    public void A_queued_install_stays_queued_although_its_folder_exists() =>
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired), installDirectoryExists: true).Status
            .Should().Be(SteamAppStatus.Queued);

    [Fact]
    public void Downloaded_bytes_or_an_active_download_turn_a_queued_install_into_an_install()
    {
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired) with { BytesDownloaded = 1 }, false).Status
            .Should().Be(SteamAppStatus.Installing);
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired), false, new SteamLiveSignals(IsDownloadActive: true)).Status
            .Should().Be(SteamAppStatus.Installing);
        SteamStateResolver.Resolve(Manifest(SteamAppStates.UpdateRequired), false, new SteamLiveSignals(IsRegistryUpdating: true)).Status
            .Should().Be(SteamAppStatus.Installing);
    }

    [Fact]
    public void A_missing_folder_of_an_installed_app_is_broken()
    {
        var resolved = SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled), installDirectoryExists: false);

        resolved.GameState.Should().Be((GameInstallState.Broken, GameStateReason.InstallDirectoryMissing));
    }

    [Fact]
    public void A_pending_target_build_means_an_update_is_pending()
    {
        var resolved = SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled) with { TargetBuildId = "200" }, true);

        resolved.GameState.Should().Be((GameInstallState.UpdatePending, GameStateReason.UpdateRequired));
    }

    [Fact]
    public void A_leftover_download_means_a_partly_downloaded_update_is_pending()
    {
        var scheduled = DateTimeOffset.FromUnixTimeSeconds(1790000000);

        var resolved = SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled) with { ScheduledAutoUpdate = scheduled }, true,
            new SteamLiveSignals(IsDownloadActive: true));

        resolved.Status.Should().Be(SteamAppStatus.UpdatePending);
        resolved.IsPartiallyDownloaded.Should().BeTrue();
        resolved.ScheduledUpdate.Should().Be(scheduled);
        resolved.GameState.Should().Be((GameInstallState.UpdatePending, GameStateReason.DownloadInProgress));
    }

    [Fact]
    public void The_registry_updating_value_means_steam_is_updating() =>
        SteamStateResolver.Resolve(Manifest(SteamAppStates.FullyInstalled), true, new SteamLiveSignals(IsRegistryUpdating: true))
            .GameState.Should().Be((GameInstallState.Updating, GameStateReason.StoreBusy));

    [Fact]
    public void The_description_names_the_status_its_qualifiers_and_the_flags()
    {
        var resolved = SteamStateResolver.Resolve(
            Manifest((SteamAppStates)1026) with { UpdateResult = SteamUpdateResult.Suspended }, installDirectoryExists: false);

        resolved.Describe().Should().Be(
            "Installing (paused, last update result Suspended); StateFlags 1026 [UpdateRequired, UpdateStarted]");
        SteamStateResolver.Resolve(Manifest((SteamAppStates)0x14), true).Describe()
            .Should().Be("Installed (optional update available); StateFlags 20 [FullyInstalled, UpdateOptional]");
    }

    private static SteamAppManifest Manifest(SteamAppStates flags) => new()
    {
        AppId = 10,
        Name = "Example",
        InstallDir = "Example",
        StateFlags = flags,
        BuildId = "100",
    };
}
