namespace JustModIt.Manager.Core;

/// <summary>
/// Who the app is, as its build configured it. Its data folder, locks and pipe are named after
/// <see cref="InstanceName"/>, so two apps, or a development build and an installed copy, never share them.
/// </summary>
public sealed record AppIdentity
{
    /// <param name="appId">Names the exe, the data folder, the mutexes and the pipe.</param>
    /// <param name="vendorId">The folder above the data folder.</param>
    /// <param name="slug">The link scheme and, with a leading dot, the hidden backup folder in game folders.</param>
    /// <param name="isDevelopmentBuild">Whether this is a development build, which takes names of its own.</param>
    public AppIdentity(string appId, string vendorId, string slug, bool isDevelopmentBuild)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        AppId = appId;
        VendorId = vendorId;
        Slug = slug;
        IsDevelopmentBuild = isDevelopmentBuild;
    }

    public string AppId { get; }

    public string VendorId { get; }

    public string Slug { get; }

    public bool IsDevelopmentBuild { get; }

    /// <summary>
    /// The name behind the data folder, the mutexes and the pipe, with ".Dev" in a development build so it never shares
    /// data with an installed copy or hands its start over to one.
    /// </summary>
    public string InstanceName => IsDevelopmentBuild ? AppId + ".Dev" : AppId;
}
