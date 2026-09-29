namespace JustModIt.Manager.Core.Packages;

public sealed record PackageLimits
{
    public static PackageLimits Default { get; } = new();

    public int MaxEntries { get; init; } = 100_000;

    /// <summary>Voice-over packs reach tens of gigabytes; the install's free-space check still guards the disk.</summary>
    public long MaxUncompressedBytes { get; init; } = 64L << 30;

    public long MaxManifestBytes { get; init; } = 16 << 20;
}
