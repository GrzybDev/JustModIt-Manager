namespace JustModIt.Manager.Core.Packages;

/// <summary>A third-party component a package carries, e.g. a mod loader, with its licence and the address of its source.</summary>
public sealed record ThirdPartyNotice
{
    public const int MaxTextLength = 200;

    public const int MaxSourceUrlLength = 2048;

    public const int MaxPerPackage = 32;

    public required string Name { get; init; }

    /// <summary>The licence's name or SPDX expression.</summary>
    public required string License { get; init; }

    public required string SourceUrl { get; init; }

    /// <summary>Game paths of the component's files in the package; null when its maker does not say.</summary>
    public IReadOnlyList<string>? Paths { get; init; }

    /// <summary>Whether a text fits a notice: not blank and at most <see cref="MaxTextLength"/> characters.</summary>
    /// <param name="text">A name or licence; null is invalid.</param>
    public static bool IsValidText(string? text) => !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;

    /// <summary>Whether an address can be a notice's source: an absolute http or https address, not too long.</summary>
    /// <param name="url">The address of the component's source; null is invalid.</param>
    public static bool IsValidSourceUrl(string? url) =>
        url is { Length: <= MaxSourceUrlLength }
        && Uri.TryCreate(url, UriKind.Absolute, out var source)
        && (source.Scheme == Uri.UriSchemeHttps || source.Scheme == Uri.UriSchemeHttp);

    /// <summary>Whether every field fits a notice.</summary>
    internal bool IsValid() =>
        IsValidText(Name) && IsValidText(License) && IsValidSourceUrl(SourceUrl) && (Paths?.All(PackagePath.IsValid) ?? true);
}
