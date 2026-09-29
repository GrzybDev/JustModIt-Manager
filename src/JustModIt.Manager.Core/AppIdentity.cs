namespace JustModIt.Manager.Core;

/// <summary>
/// Who the app is, as its build configured it, with names that keep two apps, or a development build and an installed
/// copy, from sharing data, locks, the pipe, links or folders in games.
/// </summary>
public sealed record AppIdentity
{
    /// <summary>Takes the app's names from its build.</summary>
    /// <param name="appId">Names the exe, the data folder, the mutexes and the pipe.</param>
    /// <param name="vendorId">The folder above the data folder.</param>
    /// <param name="slug">The link scheme and, with a leading dot, the hidden folder in game folders.</param>
    /// <param name="isDevelopmentBuild">Whether this is a development build, which takes names of its own.</param>
    public AppIdentity(string appId, string vendorId, string slug, bool isDevelopmentBuild)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        AppId = appId;
        VendorId = vendorId;
        Slug = isDevelopmentBuild ? slug + "-dev" : slug;
        IsDevelopmentBuild = isDevelopmentBuild;
    }

    public string AppId { get; }

    public string VendorId { get; }

    /// <summary>
    /// The link scheme and the name of the hidden folder in game folders, with "-dev" in a development build so it never
    /// registers or handles an installed copy's links and never touches its folder in a game.
    /// </summary>
    public string Slug { get; }

    public bool IsDevelopmentBuild { get; }

    /// <summary>The app's hidden folder at a game's root, for staging files and, by default, the originals.</summary>
    public string HiddenFolderName => "." + Slug;

    /// <summary>
    /// The name behind the data folder, the mutexes and the pipe, with ".Dev" in a development build so it never shares
    /// data with an installed copy or hands its start over to one.
    /// </summary>
    public string InstanceName => IsDevelopmentBuild ? AppId + ".Dev" : AppId;
}
