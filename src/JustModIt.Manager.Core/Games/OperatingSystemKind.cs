namespace JustModIt.Manager.Core.Games;

/// <summary>
/// The system a game build is made for, which can differ from the system the app runs on, e.g. when Steam runs a game's
/// Windows build on Linux under Proton.
/// </summary>
public enum OperatingSystemKind
{
    /// <summary>A system the app does not know; only packages meant for every system serve it.</summary>
    Unknown,
    Windows,
    MacOS,
    Linux,
}
