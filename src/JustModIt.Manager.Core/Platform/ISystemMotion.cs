namespace JustModIt.Manager.Core.Platform;

/// <summary>The system's setting for motion on screen, such as Windows' "Animation effects".</summary>
public interface ISystemMotion
{
    /// <summary>Tells whether the system asks apps to keep motion down; a cached setting, cheap to read on the UI thread.</summary>
    /// <returns>True when motion should be reduced; false also where the system cannot tell.</returns>
    bool IsReduced();
}
