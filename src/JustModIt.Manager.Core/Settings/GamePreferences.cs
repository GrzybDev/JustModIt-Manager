namespace JustModIt.Manager.Core.Settings;

/// <summary>The player's choices for one game, stored only for games whose choices differ from these defaults.</summary>
public sealed record GamePreferences
{
    public static GamePreferences Default { get; } = new();

    /// <summary>Null follows <see cref="AppSettings.AutoInstallProjects"/>.</summary>
    public bool? AutoInstall { get; init; }

    /// <summary>Null follows <see cref="AppSettings.AutoUpdateProjects"/>.</summary>
    public bool? AutoUpdate { get; init; }

    /// <summary>The project to use when several teams made one for the game.</summary>
    public Guid? ProjectId { get; init; }

    /// <summary>The name of the release file that identifies the chosen variant, remembered for automatic installs.</summary>
    public string? VariantName { get; init; }

    /// <summary>Hidden from the player's list of games and left alone by the automation.</summary>
    public bool IsHidden { get; init; }
}
