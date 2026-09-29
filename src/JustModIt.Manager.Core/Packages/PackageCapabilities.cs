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

    private const string OperationPrefix = "op:";

    /// <summary>Everything this version of the app can install.</summary>
    public static FrozenSet<string> Supported { get; } =
        FrozenSet.Create(StringComparer.Ordinal, [.. new[] { Copy, Patch, Rename, Delete }.Select(ForOperation)]);

    /// <summary>The capability an operation needs.</summary>
    /// <param name="type">The operation's type as the manifest names it, e.g. <c>patch</c>.</param>
    public static string ForOperation(string type) => OperationPrefix + type;

    /// <summary>The capabilities a package with these operations needs, in a stable order, for its manifest.</summary>
    /// <param name="operations">The package's operations.</param>
    public static IReadOnlyList<string> Of(IEnumerable<PackageOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
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
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
    }
}
