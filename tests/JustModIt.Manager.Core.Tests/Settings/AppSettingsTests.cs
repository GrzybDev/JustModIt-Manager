using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Core.Tests.Settings;

public sealed class AppSettingsTests
{
    private static readonly GameKey Witcher = new(StoreId.Gog, "1207658924");
    private static readonly GameKey Portal = new(StoreId.Steam, "400");

    [Fact]
    public void Every_setting_starts_at_its_intended_default()
    {
        var settings = AppSettings.Default;

        settings.StartWithSystem.Should().BeTrue();
        settings.CloseBehavior.Should().Be(CloseBehavior.Ask);
        settings.AutoInstallProjects.Should().BeFalse();
        settings.AutoUpdateProjects.Should().BeTrue();
        settings.ReapplyAfterGameUpdate.Should().BeTrue();
        settings.AutoUpdateApp.Should().BeTrue();
        settings.ShowNotifications.Should().BeTrue();
        settings.BlurAdultContent.Should().BeTrue();
        settings.CreatorMode.Should().BeFalse();
        settings.OnboardingCompleted.Should().BeFalse();
        settings.TrayNoticeShown.Should().BeFalse();
        settings.BackupLocation.Should().Be(BackupLocation.GameFolder);
        settings.CustomBackupFolder.Should().BeNull();
        settings.Games.Should().BeEmpty();
        settings.LibraryFolders.Should().BeEmpty();
        settings.AdultContentConfirmedUntil.Should().BeNull();
    }

    [Fact]
    public void A_game_back_at_its_defaults_is_no_longer_stored_and_others_stay()
    {
        var settings = AppSettings.Default
            .WithGame(Witcher, preferences => preferences with { IsHidden = true })
            .WithGame(Portal, preferences => preferences with { ProjectId = Guid.Parse("0b2c4d6e-8f10-4a12-b314-c516d718e920") });

        var updated = settings.WithGame(Witcher, preferences => preferences with { IsHidden = false });

        updated.Games.Keys.Should().Equal(Portal);
        updated.GetGame(Witcher).Should().Be(GamePreferences.Default);
        updated.GetGame(Portal).ProjectId.Should().Be(Guid.Parse("0b2c4d6e-8f10-4a12-b314-c516d718e920"));
    }

    [Fact]
    public void A_game_without_its_own_choice_follows_the_global_settings()
    {
        var settings = AppSettings.Default with { AutoInstallProjects = true, AutoUpdateProjects = false };

        settings.ResolveAutoInstall(Witcher).Should().BeTrue();
        settings.ResolveAutoUpdate(Witcher).Should().BeFalse();
        settings.WithGame(Witcher, preferences => preferences with { AutoInstall = false }).ResolveAutoInstall(Witcher).Should().BeFalse();
    }

    [Fact]
    public void A_choice_equal_to_the_global_setting_is_stored_as_following_it()
    {
        var settings = AppSettings.Default.ChooseAutoInstall(Witcher, true).ChooseAutoUpdate(Witcher, false);

        settings.GetGame(Witcher).AutoInstall.Should().BeTrue();
        settings.GetGame(Witcher).AutoUpdate.Should().BeFalse();

        var back = settings.ChooseAutoInstall(Witcher, false).ChooseAutoUpdate(Witcher, true);

        back.GetGame(Witcher).Should().Be(GamePreferences.Default);
        back.Games.Should().BeEmpty();
    }

    [Fact]
    public void A_library_folder_is_added_once()
    {
        var folder = new LibraryFolder(StoreId.Steam, Rooted("SteamLibrary"));

        var settings = AppSettings.Default.WithLibraryFolder(folder).WithLibraryFolder(folder with { Path = folder.Path + Path.DirectorySeparatorChar });

        settings.LibraryFolders.Should().Equal(folder);
    }

    [Fact]
    public void The_same_folder_may_serve_two_stores_and_is_removed_per_store()
    {
        var steam = new LibraryFolder(StoreId.Steam, Rooted("Games"));
        var gog = new LibraryFolder(StoreId.Gog, Rooted("Games"));

        var settings = AppSettings.Default.WithLibraryFolder(steam).WithLibraryFolder(gog).WithoutLibraryFolder(steam);

        settings.LibraryFolders.Should().Equal(gog);
    }

    [Fact]
    public void Folders_differing_only_in_letter_case_are_the_same_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows paths ignore letter case.");

        var settings = AppSettings.Default
            .WithLibraryFolder(new LibraryFolder(StoreId.Steam, @"E:\SteamLibrary"))
            .WithLibraryFolder(new LibraryFolder(StoreId.Steam, @"e:\steamlibrary"));

        settings.LibraryFolders.Should().ContainSingle();
    }

    [Fact]
    public void A_confirmed_age_lasts_a_day()
    {
        var now = new DateTimeOffset(2026, 9, 1, 22, 0, 0, TimeSpan.FromHours(2));

        var settings = AppSettings.Default.ConfirmAdultContent(now);

        AppSettings.Default.IsAdultContentConfirmed(now).Should().BeFalse();
        settings.IsAdultContentConfirmed(now.AddHours(23).AddMinutes(59)).Should().BeTrue();
        settings.IsAdultContentConfirmed(now.AddDays(1)).Should().BeFalse();
    }

    private static string Rooted(string name) => Path.Combine(OperatingSystem.IsWindows() ? @"E:\" : "/mnt", name);
}
