using System.Reflection;
using System.Resources;

namespace JustModIt.Manager.Ui;

/// <summary>The legal texts that an app's build embeds in it (<c>LegalNotices.targets</c>), for its licences page.</summary>
/// <param name="app">The app's own assembly.</param>
public sealed class LegalNotices(Assembly app)
{
    /// <summary>The licence of the app's own code.</summary>
    public string ReadLicense() => Read("JustModIt.Manager.License.md");

    /// <summary>The components of others that ship with the app, their licences and notices; several hundred kilobytes.</summary>
    public string ReadThirdPartyNotices() => Read("JustModIt.Manager.ThirdPartyNotices.md");

    /// <summary>What the app's name, logo and other brand assets may be used for.</summary>
    public string ReadBrandingNotice() => Read("JustModIt.Manager.BrandingNotice.md");

    private string Read(string resourceName)
    {
        using var stream = app.GetManifestResourceStream(resourceName)
            ?? throw new MissingManifestResourceException($"{app.FullName} has no {resourceName}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
