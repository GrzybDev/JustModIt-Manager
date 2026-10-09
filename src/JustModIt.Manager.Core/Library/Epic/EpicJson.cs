using System.Text.Json.Serialization;

namespace JustModIt.Manager.Core.Library.Epic;

/// <summary>An Epic Games Launcher <c>.item</c> manifest: one installed game, DLC, engine or plugin.</summary>
internal sealed class EpicItem
{
    public string? AppName { get; init; }

    public string? CatalogNamespace { get; init; }

    public string? CatalogItemId { get; init; }

    public string? DisplayName { get; init; }

    public string? InstallLocation { get; init; }

    public string? InstallationGuid { get; init; }

    /// <summary>Set for content installed into another item's folder, such as an engine's plugin.</summary>
    public string? HostInstallationGuid { get; init; }

    public string? AppVersionString { get; init; }

    public string? TechnicalType { get; init; }

    public IReadOnlyList<string>? AppCategories { get; init; }

    public IReadOnlyList<string>? CompatibleApps { get; init; }

    [JsonPropertyName("bIsIncompleteInstall")]
    public bool IsIncompleteInstall { get; init; }

    [JsonPropertyName("bNeedsValidation")]
    public bool NeedsValidation { get; init; }
}

/// <summary><c>LauncherInstalled.dat</c>, which names each item's version only once an update is complete.</summary>
internal sealed class LauncherInstalledFile
{
    public IReadOnlyList<LauncherInstallation>? InstallationList { get; init; }
}

/// <summary>An entry of <c>LauncherInstalled.dat</c>.</summary>
internal sealed class LauncherInstallation
{
    public string? AppName { get; init; }

    public string? AppVersion { get; init; }
}

/// <summary>An entry of Legendary's <c>installed.json</c>.</summary>
internal sealed class LegendaryInstalledGame
{
    [JsonPropertyName("app_name")]
    public string? AppName { get; init; }

    public string? Title { get; init; }

    [JsonPropertyName("install_path")]
    public string? InstallPath { get; init; }

    /// <summary><c>Windows</c>, <c>Win32</c> or <c>Mac</c>: the build Legendary installed.</summary>
    public string? Platform { get; init; }

    public string? Version { get; init; }

    [JsonPropertyName("is_dlc")]
    public bool IsDlc { get; init; }

    [JsonPropertyName("needs_verification")]
    public bool NeedsVerification { get; init; }
}

/// <summary>The file Legendary keeps in its <c>metadata</c> folder for each installed app.</summary>
internal sealed class LegendaryMetadataFile
{
    public LegendaryMetadata? Metadata { get; init; }
}

internal sealed class LegendaryMetadata
{
    /// <summary>The catalog item id.</summary>
    public string? Id { get; init; }

    public string? Namespace { get; init; }
}

/// <summary>A <c>.mancpn</c> file in a game's <c>.egstore</c> folder: what the folder holds, as the launcher installed it.</summary>
internal sealed class EpicFolderManifest
{
    public string? CatalogNamespace { get; init; }

    public string? CatalogItemId { get; init; }

    public string? AppName { get; init; }
}
