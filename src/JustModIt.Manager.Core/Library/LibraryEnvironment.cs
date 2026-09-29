namespace JustModIt.Manager.Core.Library;

/// <summary>The user's folders that stores and launchers are found relative to.</summary>
public sealed record LibraryEnvironment
{
    public required AppPlatform Platform { get; init; }

    public required string HomeDirectory { get; init; }

    /// <summary>Windows only.</summary>
    public string? ProgramFilesX86 { get; init; }

    /// <summary>The roaming application data folder on Windows.</summary>
    public string? ApplicationData { get; init; }

    /// <summary><c>XDG_DATA_HOME</c>, when it is set to an absolute path.</summary>
    public string? XdgDataHome { get; init; }

    /// <summary><c>XDG_CONFIG_HOME</c>, when it is set to an absolute path.</summary>
    public string? XdgConfigHome { get; init; }

    /// <summary><c>SNAP_USER_DATA</c>, when it is set to an absolute path.</summary>
    public string? SnapUserData { get; init; }

    /// <summary>Reads the current user's folders and environment variables.</summary>
    public static LibraryEnvironment ForCurrentUser() => new()
    {
        Platform = AppPlatforms.Current,
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify),
        ProgramFilesX86 = FullyQualifiedOrNull(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolderOption.DoNotVerify)),
        ApplicationData = FullyQualifiedOrNull(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.DoNotVerify)),
        XdgDataHome = FullyQualifiedOrNull(Environment.GetEnvironmentVariable("XDG_DATA_HOME")),
        XdgConfigHome = FullyQualifiedOrNull(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")),
        SnapUserData = FullyQualifiedOrNull(Environment.GetEnvironmentVariable("SNAP_USER_DATA")),
    };

    // The XDG specification says to ignore relative values, which would otherwise resolve against the current folder.
    private static string? FullyQualifiedOrNull(string? path) =>
        !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) ? path : null;
}
