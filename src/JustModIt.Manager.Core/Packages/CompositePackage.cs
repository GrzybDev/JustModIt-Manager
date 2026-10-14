using System.Globalization;

namespace JustModIt.Manager.Core.Packages;

/// <summary>One package of a composite, as opening it went.</summary>
/// <param name="Index">Its place among the composite's packages, counted from 0.</param>
/// <param name="IsOptional">The install may go on without it.</param>
/// <param name="Package">The opened package; null for an optional one that only a newer app can open.</param>
/// <param name="Refusal">Why only a newer app can open it; null when it opened.</param>
public sealed record CompositePart(int Index, bool IsOptional, IPackage? Package, PackageError? Refusal = null);

/// <summary>
/// The packages of one install, each opened and validated on its own, whose steps run in order as one package, so a later
/// package's file replaces an earlier one's at the same path; a single package is a composite of one.
/// </summary>
public sealed class CompositePackage : IPackage
{
    private const char EntrySeparator = '/';

    private readonly IReadOnlyList<CompositePart> _opened;
    private readonly bool _owns;

    private CompositePackage(IReadOnlyList<CompositePart> opened, IReadOnlyList<CompositePart> parts, bool owns)
    {
        _opened = opened;
        _owns = owns;
        Parts = parts;
        Steps = [.. parts.Where(part => part.Package is not null).SelectMany(part => part.Package!.Steps.Select(step => Prefixed(step, part.Index)))];
        RuntimeFiles = [.. parts.SelectMany(part => part.Package?.RuntimeFiles ?? [])];
    }

    /// <summary>The packages it installs, in order.</summary>
    public IReadOnlyList<CompositePart> Parts { get; }

    /// <summary>A manifest package when any of its packages is one.</summary>
    public PackageKind Kind => Parts.Any(part => part.Package?.Kind == PackageKind.Manifest) ? PackageKind.Manifest : PackageKind.Zip;

    /// <summary>The first package's, which is the base of a release.</summary>
    public string FileName => Parts[0].Package?.FileName ?? "";

    /// <summary>Always null: each package has its own, see <see cref="Parts"/>.</summary>
    public PackageManifest? Manifest => null;

    /// <summary>The packages' steps in order, each payload entry named <c>&lt;part&gt;/&lt;entry&gt;</c>.</summary>
    public IReadOnlyList<PackageStep> Steps { get; }

    public IReadOnlyList<RuntimeFile> RuntimeFiles { get; }

    /// <summary>Opens every package in order, each validated in full.</summary>
    /// <param name="reader">Opens and validates one package.</param>
    /// <param name="files">Each package's full path, and whether the install may go on without it.</param>
    /// <param name="cancellationToken">Stops opening them.</param>
    /// <exception cref="InvalidPackageException">
    /// A package is malformed or unsafe, or a package the install needs was made for a newer app; an optional one made for
    /// a newer app is kept with its <see cref="CompositePart.Refusal"/> instead.
    /// </exception>
    public static async Task<CompositePackage> OpenAsync(PackageReader reader, IReadOnlyList<(string Path, bool IsOptional)> files,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0)
        {
            throw new ArgumentException("A composite needs at least one package.", nameof(files));
        }

        var parts = new List<CompositePart>(files.Count);
        try
        {
            foreach (var (path, isOptional) in files)
            {
                try
                {
                    parts.Add(new CompositePart(parts.Count, isOptional, await reader.OpenAsync(path, cancellationToken).ConfigureAwait(false)));
                }
                catch (InvalidPackageException refused) when (isOptional && refused.Error.NeedsNewerApp())
                {
                    parts.Add(new CompositePart(parts.Count, isOptional, null, refused.Error));
                }
            }
        }
        catch
        {
            await DisposeAllAsync(parts).ConfigureAwait(false);
            throw;
        }

        return new CompositePackage(parts, parts, owns: true);
    }

    /// <summary>The same packages without some of them, which stay open as long as this composite.</summary>
    /// <param name="leftOut">The indexes of the packages to go without.</param>
    public CompositePackage Without(IReadOnlySet<int> leftOut)
    {
        ArgumentNullException.ThrowIfNull(leftOut);
        return new CompositePackage(_opened, [.. Parts.Where(part => !leftOut.Contains(part.Index))], owns: false);
    }

    public Task<Stream> OpenEntryAsync(string entry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var separator = entry.IndexOf(EntrySeparator, StringComparison.Ordinal);
        return separator > 0 && int.TryParse(entry.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && Parts.FirstOrDefault(part => part.Index == index)?.Package is { } package
                ? package.OpenEntryAsync(entry[(separator + 1)..], cancellationToken)
                : throw new InvalidPackageException(PackageError.MissingPayload, entry);
    }

    public ValueTask DisposeAsync() => _owns ? DisposeAllAsync(_opened) : ValueTask.CompletedTask;

    private static PackageStep Prefixed(PackageStep step, int index) => step switch
    {
        CopyStep copy => copy with { Entry = EntryOf(index, copy.Entry) },
        PatchStep patch => patch with { Entry = EntryOf(index, patch.Entry) },
        _ => step,
    };

    private static string EntryOf(int index, string entry) =>
        index.ToString(CultureInfo.InvariantCulture) + EntrySeparator + entry;

    private static async ValueTask DisposeAllAsync(IEnumerable<CompositePart> parts)
    {
        foreach (var part in parts)
        {
            if (part.Package is { } package)
            {
                await package.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
