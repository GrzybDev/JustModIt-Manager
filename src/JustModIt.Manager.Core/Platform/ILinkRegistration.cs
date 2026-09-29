namespace JustModIt.Manager.Core.Platform;

/// <summary>The app's link scheme, through which links from websites and notifications reach it.</summary>
public interface ILinkRegistration
{
    /// <summary>Registers the scheme for the current user, replacing an earlier registration.</summary>
    /// <param name="scheme">The scheme without <c>://</c>.</param>
    /// <param name="displayName">How the system names the scheme, e.g. when a browser asks before opening a link.</param>
    /// <param name="executable">The app's executable, started with the link as its only argument.</param>
    void Register(string scheme, string displayName, string executable);

    /// <summary>Removes the scheme's registration for the current user.</summary>
    /// <param name="scheme">The scheme without <c>://</c>.</param>
    void Unregister(string scheme);
}
