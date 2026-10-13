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
    /// <param name="developmentName">
    /// What a development build adds to its names, letters and digits; a copy that runs beside the developer's own takes
    /// another one.
    /// </param>
    public AppIdentity(string appId, string vendorId, string slug, bool isDevelopmentBuild, string developmentName = "Dev")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        if (string.IsNullOrEmpty(developmentName) || !developmentName.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException("A development name is made of letters and digits.", nameof(developmentName));
        }

        AppId = appId;
        VendorId = vendorId;
        Slug = isDevelopmentBuild ? $"{slug}-{developmentName.ToLowerInvariant()}" : slug;
        InstanceName = isDevelopmentBuild ? $"{appId}.{developmentName}" : appId;
        IsDevelopmentBuild = isDevelopmentBuild;
    }

    public string AppId { get; }

    public string VendorId { get; }

    /// <summary>
    /// The link scheme and the name of the hidden folder in game folders, with "-dev" (or another development name) in a
    /// development build so it never registers or handles an installed copy's links and never touches its folder in a game.
    /// </summary>
    public string Slug { get; }

    public bool IsDevelopmentBuild { get; }

    /// <summary>The app's hidden folder at a game's root, for staging files and, by default, the originals.</summary>
    public string HiddenFolderName => "." + Slug;

    /// <summary>
    /// The name behind the data folder, the mutexes, the pipe and the owner file in games, with ".Dev" (or another
    /// development name) in a development build so it never shares data with an installed copy or hands its start over to one.
    /// </summary>
    public string InstanceName { get; }
}
