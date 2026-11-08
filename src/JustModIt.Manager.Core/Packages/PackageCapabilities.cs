using System.Collections.Frozen;

namespace JustModIt.Manager.Core.Packages;

/// <summary>
/// What a package may need from the app, an operation (<c>op:patch</c>) and later archive handlers
/// (<c>handler:unity-bundle@1</c>); a manifest that omits <see cref="PackageManifest.Requires"/> needs those of the
/// operations it uses.
/// </summary>
public static class PackageCapabilities
{
    public const string Copy = "copy";
    public const string Patch = "patch";
    public const string Rename = "rename";
    public const string Delete = "delete";

    /// <summary>The package declares game files its software changes while the game runs (<see cref="PackageManifest.RuntimeFiles"/>).</summary>
    public const string RuntimeFiles = "runtime:files@1";

    /// <summary>The package declares folders its software fills while the game runs, which this version cannot undo yet.</summary>
    public const string RuntimeFolders = "runtime:folders@1";

    /// <summary>The package's targets name where its folders lie on each game (<see cref="PackageTarget.Folders"/>).</summary>
    public const string TargetFolders = "targets:folders@1";

    /// <summary>The package's operations name the stores they run on (<see cref="PackageOperation.Only"/>).</summary>
    public const string TargetsOnly = "targets:only@1";

    private const string OperationPrefix = "op:";

    /// <summary>Everything this version of the app can install.</summary>
    public static FrozenSet<string> Supported { get; } = FrozenSet.Create(StringComparer.Ordinal,
        [.. new[] { Copy, Patch, Rename, Delete }.Select(ForOperation), RuntimeFiles, TargetFolders, TargetsOnly]);

    /// <summary>The capability an operation needs.</summary>
    /// <param name="type">The operation's type as the manifest names it, e.g. <c>patch</c>.</param>
    public static string ForOperation(string type) => OperationPrefix + type;

    /// <summary>The capabilities a package with these operations needs, in a stable order, for its manifest.</summary>
    /// <param name="operations">The package's operations.</param>
    /// <param name="declaresFiles">The package declares files its software changes while the game runs.</param>
    public static IReadOnlyList<string> Of(IEnumerable<PackageOperation> operations, bool declaresFiles = false)
    {
        return
        [
            .. operations
                .Select(operation => ForOperation(operation switch
                {
                    CopyOperation => Copy,
                    PatchOperation => Patch,
                    RenameOperation => Rename,
                    DeleteOperation => Delete,
                    _ => throw new ArgumentException($"Unknown operation {operation.GetType().Name}.", nameof(operations)),
                }))
                .Concat(declaresFiles ? [RuntimeFiles] : [])
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }
}
