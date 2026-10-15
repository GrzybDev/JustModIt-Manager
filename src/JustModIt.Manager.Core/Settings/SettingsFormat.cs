using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Settings;

/// <summary>
/// The format of <c>settings.json</c>, which loads the prototype's files (version 0) with the few names that changed
/// since, and falls back to a value's own default when it cannot be read, so one bad value never resets the others.
/// </summary>
public sealed class SettingsFormat : IJsonDocumentFormat<AppSettings>
{
    private const string StartWithSystem = "startWithSystem";
    private const string CloseBehaviorName = "closeBehavior";
    private const string AutoInstallProjects = "autoInstallProjects";
    private const string AutoUpdateProjects = "autoUpdateProjects";
    private const string ReapplyAfterGameUpdate = "reapplyAfterGameUpdate";
    private const string AutoUpdateApp = "autoUpdateApp";
    private const string SendErrorReports = "sendErrorReports";
    private const string ShowNotifications = "showNotifications";
    private const string BlurAdultContent = "blurAdultContent";
    private const string CreatorMode = "creatorMode";
    private const string OnboardingCompleted = "onboardingCompleted";
    private const string OnboardingStep = "onboardingStep";
    private const string TrayNoticeShown = "trayNoticeShown";
    private const string BackupLocationName = "backupLocation";
    private const string CustomBackupFolder = "customBackupFolder";
    private const string PreferredTeams = "preferredTeams";
    private const string Games = "games";
    private const string LibraryFolders = "libraryFolders";
    private const string AdultContentConfirmedUntil = "adultContentConfirmedUntil";
    private const string RevealedAdultGames = "revealedAdultGames";
    private const string RecentCreatorProjects = "recentCreatorProjects";
    private const string InstallationId = "installationId";

    // Version 0 stored the inverse of BlurAdultContent and named the automation after the only kind of project it knew.
    private const string LegacyShowAdultContent = "showAdultContent";
    private const string LegacyAutoInstallProjects = "autoInstallTranslations";
    private const string LegacyAutoUpdateProjects = "autoUpdateTranslations";

    private const string AutoInstall = "autoInstall";
    private const string AutoUpdate = "autoUpdate";
    private const string ProjectId = "projectId";
    private const string VariantName = "variantName";
    private const string FeaturesName = "features";
    private const string IsHidden = "isHidden";
    private const string Store = "store";
    private const string FolderPath = "path";

    public static SettingsFormat Instance { get; } = new();

    public int CurrentVersion => 1;

    public IReadOnlySet<string> KnownProperties { get; } = FrozenSet.Create(StringComparer.Ordinal,
        StartWithSystem, CloseBehaviorName, AutoInstallProjects, AutoUpdateProjects, ReapplyAfterGameUpdate,
        AutoUpdateApp, SendErrorReports, ShowNotifications, BlurAdultContent, CreatorMode, OnboardingCompleted, OnboardingStep,
        TrayNoticeShown, BackupLocationName, CustomBackupFolder, PreferredTeams, Games, LibraryFolders,
        AdultContentConfirmedUntil, RevealedAdultGames, RecentCreatorProjects, InstallationId, LegacyShowAdultContent,
        LegacyAutoInstallProjects, LegacyAutoUpdateProjects);

    public AppSettings Read(JsonElement root, int version)
    {
        var defaults = AppSettings.Default;
        bool? Legacy(string name) => version == 0 ? root.GetBooleanOrNull(name) : null;
        var blurAdultContent = root.GetBooleanOrNull(BlurAdultContent)
            ?? !Legacy(LegacyShowAdultContent)
            ?? defaults.BlurAdultContent;
        var customBackupFolder = root.GetFullPathOrNull(CustomBackupFolder);
        var backupLocation = root.GetEnumOrNull<BackupLocation>(BackupLocationName) is { } location
            && (location != BackupLocation.CustomFolder || customBackupFolder is not null)
                ? location
                : defaults.BackupLocation;
        return new AppSettings
        {
            StartWithSystem = root.GetBooleanOrNull(StartWithSystem) ?? defaults.StartWithSystem,
            CloseBehavior = root.GetEnumOrNull<CloseBehavior>(CloseBehaviorName) ?? defaults.CloseBehavior,
            AutoInstallProjects = root.GetBooleanOrNull(AutoInstallProjects) ?? Legacy(LegacyAutoInstallProjects)
                ?? defaults.AutoInstallProjects,
            AutoUpdateProjects = root.GetBooleanOrNull(AutoUpdateProjects) ?? Legacy(LegacyAutoUpdateProjects)
                ?? defaults.AutoUpdateProjects,
            ReapplyAfterGameUpdate = root.GetBooleanOrNull(ReapplyAfterGameUpdate) ?? defaults.ReapplyAfterGameUpdate,
            AutoUpdateApp = root.GetBooleanOrNull(AutoUpdateApp) ?? defaults.AutoUpdateApp,
            SendErrorReports = root.GetBooleanOrNull(SendErrorReports) ?? defaults.SendErrorReports,
            ShowNotifications = root.GetBooleanOrNull(ShowNotifications) ?? defaults.ShowNotifications,
            BlurAdultContent = blurAdultContent,
            CreatorMode = root.GetBooleanOrNull(CreatorMode) ?? defaults.CreatorMode,
            OnboardingCompleted = root.GetBooleanOrNull(OnboardingCompleted) ?? defaults.OnboardingCompleted,
            OnboardingStep = root.GetInt32OrNull(OnboardingStep) is >= 0 and var step ? step : defaults.OnboardingStep,
            TrayNoticeShown = root.GetBooleanOrNull(TrayNoticeShown) ?? defaults.TrayNoticeShown,
            BackupLocation = backupLocation,
            CustomBackupFolder = customBackupFolder,
            PreferredTeams = ReadIds(root.GetOrNull(PreferredTeams, JsonValueKind.Array)),
            Games = ReadGames(root.GetOrNull(Games, JsonValueKind.Object)),
            LibraryFolders = ReadLibraryFolders(root.GetOrNull(LibraryFolders, JsonValueKind.Array)),
            AdultContentConfirmedUntil = root.GetDateTimeOffsetOrNull(AdultContentConfirmedUntil),
            RevealedAdultGames = ReadIds(root.GetOrNull(RevealedAdultGames, JsonValueKind.Array)),
            RecentCreatorProjects = ReadPaths(root.GetOrNull(RecentCreatorProjects, JsonValueKind.Array)),
            InstallationId = root.GetGuidOrNull(InstallationId),
        };
    }

    public void Write(Utf8JsonWriter writer, AppSettings document)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(document);
        writer.WriteBoolean(StartWithSystem, document.StartWithSystem);
        writer.WriteString(CloseBehaviorName, document.CloseBehavior.ToString());
        writer.WriteBoolean(AutoInstallProjects, document.AutoInstallProjects);
        writer.WriteBoolean(AutoUpdateProjects, document.AutoUpdateProjects);
        writer.WriteBoolean(ReapplyAfterGameUpdate, document.ReapplyAfterGameUpdate);
        writer.WriteBoolean(AutoUpdateApp, document.AutoUpdateApp);
        writer.WriteBoolean(SendErrorReports, document.SendErrorReports);
        writer.WriteBoolean(ShowNotifications, document.ShowNotifications);
        writer.WriteBoolean(BlurAdultContent, document.BlurAdultContent);
        writer.WriteBoolean(CreatorMode, document.CreatorMode);
        writer.WriteBoolean(OnboardingCompleted, document.OnboardingCompleted);
        writer.WriteNumber(OnboardingStep, document.OnboardingStep);
        writer.WriteBoolean(TrayNoticeShown, document.TrayNoticeShown);
        writer.WriteString(BackupLocationName, document.BackupLocation.ToString());
        writer.WriteIfSet(CustomBackupFolder, document.CustomBackupFolder);
        WriteIds(writer, PreferredTeams, document.PreferredTeams);

        writer.WriteStartObject(Games);
        // Sorted, so a file changes only where a game changed.
        foreach (var (key, preferences) in document.Games.OrderBy(game => game.Key.ToString(), StringComparer.Ordinal))
        {
            writer.WriteStartObject(key.ToString());
            writer.WriteIfSet(AutoInstall, preferences.AutoInstall);
            writer.WriteIfSet(AutoUpdate, preferences.AutoUpdate);
            writer.WriteIfSet(ProjectId, preferences.ProjectId);
            WriteFeatures(writer, preferences.Features);
            if (preferences.IsHidden)
            {
                writer.WriteBoolean(IsHidden, true);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();

        writer.WriteStartArray(LibraryFolders);
        foreach (var folder in document.LibraryFolders)
        {
            writer.WriteStartObject();
            writer.WriteString(Store, folder.Store.Value);
            writer.WriteString(FolderPath, folder.Path);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteIfSet(AdultContentConfirmedUntil, document.AdultContentConfirmedUntil);
        WriteIds(writer, RevealedAdultGames, document.RevealedAdultGames);
        writer.WriteStartArray(RecentCreatorProjects);
        foreach (var project in document.RecentCreatorProjects)
        {
            writer.WriteStringValue(project);
        }

        writer.WriteEndArray();
        writer.WriteIfSet(InstallationId, document.InstallationId);
    }

    private static void WriteFeatures(Utf8JsonWriter writer, ValueDictionary<Guid, ValueDictionary<string, bool>> features)
    {
        if (features.Count == 0)
        {
            return;
        }

        writer.WriteStartObject(FeaturesName);
        foreach (var (project, chosen) in features.OrderBy(entry => entry.Key))
        {
            writer.WriteStartObject(project.ToString("D"));
            foreach (var (key, on) in chosen.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                writer.WriteBoolean(key, on);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
    }

    private static void WriteIds(Utf8JsonWriter writer, string name, ValueList<Guid> ids)
    {
        writer.WriteStartArray(name);
        foreach (var id in ids)
        {
            writer.WriteStringValue(id);
        }

        writer.WriteEndArray();
    }

    private static ValueDictionary<GameKey, GamePreferences> ReadGames(JsonElement? games)
    {
        if (games is not { } entries)
        {
            return default;
        }

        var result = ImmutableDictionary.CreateBuilder<GameKey, GamePreferences>();
        foreach (var game in entries.EnumerateObject())
        {
            if (!GameKey.TryParse(game.Name, out var key) || game.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var preferences = new GamePreferences
            {
                AutoInstall = game.Value.GetBooleanOrNull(AutoInstall),
                AutoUpdate = game.Value.GetBooleanOrNull(AutoUpdate),
                ProjectId = game.Value.GetGuidOrNull(ProjectId),
                VariantName = game.Value.GetTextOrNull(VariantName),
                Features = ReadFeatures(game.Value.GetOrNull(FeaturesName, JsonValueKind.Object)),
                IsHidden = game.Value.GetBooleanOrNull(IsHidden) ?? false,
            };
            if (preferences != GamePreferences.Default)
            {
                result[key] = preferences;
            }
        }

        return new ValueDictionary<GameKey, GamePreferences>(result.ToImmutable());
    }

    private static ValueDictionary<Guid, ValueDictionary<string, bool>> ReadFeatures(JsonElement? features)
    {
        if (features is not { } projects)
        {
            return default;
        }

        var result = ImmutableDictionary.CreateBuilder<Guid, ValueDictionary<string, bool>>();
        foreach (var project in projects.EnumerateObject())
        {
            if (!Guid.TryParse(project.Name, out var id) || project.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var chosen = ImmutableDictionary.CreateBuilder<string, bool>(StringComparer.Ordinal);
            foreach (var feature in project.Value.EnumerateObject())
            {
                if (feature.Name.Length > 0 && feature.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    chosen[feature.Name] = feature.Value.GetBoolean();
                }
            }

            if (chosen.Count > 0)
            {
                result[id] = new ValueDictionary<string, bool>(chosen.ToImmutable());
            }
        }

        return new ValueDictionary<Guid, ValueDictionary<string, bool>>(result.ToImmutable());
    }

    private static ValueList<Guid> ReadIds(JsonElement? ids) =>
        ids is { } entries
            ? [.. entries.EnumerateArray()
                .Select(id => id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var uuid) ? uuid : Guid.Empty)
                .Where(uuid => uuid != Guid.Empty)
                .Distinct()]
            : [];

    private static ValueList<string> ReadPaths(JsonElement? paths) =>
        AppSettings.RecentProjects(paths is { } entries
            ? entries.EnumerateArray().Where(path => path.ValueKind == JsonValueKind.String).Select(path => path.GetString()!)
            : []);

    private static ValueList<LibraryFolder> ReadLibraryFolders(JsonElement? folders)
    {
        var result = new ValueList<LibraryFolder>();
        if (folders is not { } entries)
        {
            return result;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && StoreId.TryParse(entry.GetTextOrNull(Store), out var store)
                && entry.GetFullPathOrNull(FolderPath) is { } path
                && new LibraryFolder(store, path) is var folder
                && !result.Any(existing => AppSettings.IsSameFolder(existing, folder)))
            {
                result = result.Add(folder);
            }
        }

        return result;
    }
}
