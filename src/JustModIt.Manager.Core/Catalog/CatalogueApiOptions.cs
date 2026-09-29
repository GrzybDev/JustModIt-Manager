namespace JustModIt.Manager.Core.Catalog;

/// <summary>
/// How to reach the site's API, as the app's build configured it; a class rather than a record, so a generated ToString
/// never puts the key into a log.
/// </summary>
public sealed class CatalogueApiOptions
{
    /// <summary>Checks the build's API settings and normalizes the address and the key.</summary>
    /// <param name="apiAddress">The API root, e.g. <c>https://api.example.com/</c>.</param>
    /// <param name="siteAddress">The website, named in the User-Agent so the site can tell where requests come from.</param>
    /// <param name="productName">A token such as the app id; other characters are left out of the User-Agent.</param>
    /// <param name="productVersion">The app's version for the User-Agent; build metadata after a plus is left out.</param>
    /// <param name="apiKey">Sent with every API request when not empty; the API is used anonymously otherwise.</param>
    public CatalogueApiOptions(Uri apiAddress, Uri? siteAddress, string productName, string productVersion, string? apiKey = null)
    {
        ArgumentNullException.ThrowIfNull(apiAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);
        if (!apiAddress.IsAbsoluteUri || (apiAddress.Scheme != Uri.UriSchemeHttps && apiAddress.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The API address must be an absolute http or https address.", nameof(apiAddress));
        }

        // Relative paths would otherwise resolve against the parent of the address's last segment.
        ApiAddress = apiAddress.AbsolutePath.EndsWith('/')
            ? apiAddress
            : new UriBuilder(apiAddress) { Path = apiAddress.AbsolutePath + "/" }.Uri;
        SiteAddress = siteAddress;
        ProductName = productName;
        ProductVersion = productVersion;
        ApiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
    }

    public Uri ApiAddress { get; }

    public Uri? SiteAddress { get; }

    public string ProductName { get; }

    public string ProductVersion { get; }

    public string? ApiKey { get; }
}
