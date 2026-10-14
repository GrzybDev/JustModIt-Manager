using System.Text.Json;
using System.Text.Json.Serialization;

namespace JustModIt.Manager.Creator.Rules;

/// <summary>A third-party tool that a package may carry only with its licence and the address of its source.</summary>
/// <param name="Id">Keys the author's licence and source notice for the tool.</param>
/// <param name="Name">The tool's own name, shown as it is.</param>
/// <param name="Folders">Folder names the tool keeps its files in, anywhere in the game.</param>
/// <param name="Files">File name patterns (<c>*</c> and <c>?</c>) of the tool's loaders.</param>
public sealed record ThirdPartyTool(string Id, string Name, IReadOnlyList<string> Folders, IReadOnlyList<string> Files);

/// <summary>
/// What a site built on the backend does not host, as data so the lists can grow without touching the checks; names
/// are file name patterns with <c>*</c> and <c>?</c>, matched in any letter case.
/// </summary>
public sealed record HostingRuleSet
{
    private const string ResourceName = "JustModIt.Manager.Creator.HostingRules.json";

    /// <summary>The rules embedded in the app.</summary>
    public static HostingRuleSet Default { get; } = Load();

    /// <summary>Libraries of stores and their copy protection, which a package may not touch at all.</summary>
    public required IReadOnlyList<string> StoreLibraries { get; init; }

    /// <summary>Files of cracks and store emulators.</summary>
    public required IReadOnlyList<string> CircumventionFiles { get; init; }

    public required IReadOnlyList<string> CircumventionFolders { get; init; }

    /// <summary>Extensions of game archives, whose whole copy carries a large share of the game's assets.</summary>
    public required IReadOnlyList<string> ArchiveExtensions { get; init; }

    /// <summary>Extensions of libraries of code, which a package adds either as the author's own or as another author's tool.</summary>
    public required IReadOnlyList<string> LibraryExtensions { get; init; }

    /// <summary>Names of the game's saves and their folders, which the package's software may not be said to change.</summary>
    public IReadOnlyList<string> SaveNames { get; init; } = [];

    /// <summary>Extensions of files that may hold the player's settings, which uninstalling would bring back as they were.</summary>
    public IReadOnlyList<string> SettingsExtensions { get; init; } = [];

    /// <summary>From this size on, a whole game file counts like an archive.</summary>
    public required long LargeFileBytes { get; init; }

    /// <summary>A patch past this share of its file is little smaller than the file, which it then mostly carries.</summary>
    public required double LargePatchRatio { get; init; }

    /// <summary>Authors often rewrite smaller files whole, so their patches say nothing about game assets.</summary>
    public required long LargePatchMinimumBytes { get; init; }

    public required IReadOnlyList<ThirdPartyTool> ThirdPartyTools { get; init; }

    private static HostingRuleSet Load()
    {
        using var stream = typeof(HostingRuleSet).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is missing.");
        return JsonSerializer.Deserialize(stream, HostingRulesJsonContext.Default.HostingRuleSet)
            ?? throw new InvalidOperationException($"The resource {ResourceName} is empty.");
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, RespectNullableAnnotations = true,
    RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(HostingRuleSet))]
internal sealed partial class HostingRulesJsonContext : JsonSerializerContext;
