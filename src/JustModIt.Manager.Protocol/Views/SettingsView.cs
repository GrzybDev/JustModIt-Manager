using JustModIt.Manager.Core.Settings;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>
/// The settings the player changes on the settings page and in the first-run setup; per-game choices and added library
/// folders have commands of their own.
/// </summary>
public sealed record SettingsView
{
    public bool StartWithSystem { get; init; }

    public CloseBehavior CloseBehavior { get; init; }

    public bool AutoInstallProjects { get; init; }

    public bool AutoUpdateProjects { get; init; }

    public bool ReapplyAfterGameUpdate { get; init; }

    public bool AutoUpdateApp { get; init; }

    public bool ShowNotifications { get; init; }

    public bool BlurAdultContent { get; init; }

    public bool CreatorMode { get; init; }

    public bool OnboardingCompleted { get; init; }

    public BackupLocation BackupLocation { get; init; }

    /// <summary>The folder the player picked; kept while another location is chosen, so the player can go back to it.</summary>
    public string? CustomBackupFolder { get; init; }

    /// <summary>Takes the values the window shows from the settings.</summary>
    /// <param name="settings">The current settings.</param>
    public static SettingsView From(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new SettingsView
        {
            StartWithSystem = settings.StartWithSystem,
            CloseBehavior = settings.CloseBehavior,
            AutoInstallProjects = settings.AutoInstallProjects,
            AutoUpdateProjects = settings.AutoUpdateProjects,
            ReapplyAfterGameUpdate = settings.ReapplyAfterGameUpdate,
            AutoUpdateApp = settings.AutoUpdateApp,
            ShowNotifications = settings.ShowNotifications,
            BlurAdultContent = settings.BlurAdultContent,
            CreatorMode = settings.CreatorMode,
            OnboardingCompleted = settings.OnboardingCompleted,
            BackupLocation = settings.BackupLocation,
            CustomBackupFolder = settings.CustomBackupFolder,
        };
    }

    /// <summary>Returns <paramref name="settings"/> with these values; what this view does not carry stays.</summary>
    /// <param name="settings">The current settings.</param>
    public AppSettings ApplyTo(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings with
        {
            StartWithSystem = StartWithSystem,
            CloseBehavior = CloseBehavior,
            AutoInstallProjects = AutoInstallProjects,
            AutoUpdateProjects = AutoUpdateProjects,
            ReapplyAfterGameUpdate = ReapplyAfterGameUpdate,
            AutoUpdateApp = AutoUpdateApp,
            ShowNotifications = ShowNotifications,
            BlurAdultContent = BlurAdultContent,
            CreatorMode = CreatorMode,
            OnboardingCompleted = OnboardingCompleted,
            BackupLocation = BackupLocation,
            CustomBackupFolder = CustomBackupFolder,
        };
    }
}
