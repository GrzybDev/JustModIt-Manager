namespace JustModIt.Manager.Core.Platform;

public enum AutostartState
{
    Disabled,
    Enabled,

    /// <summary>Registered, but the user turned it off among the system's startup apps, which wins over the setting.</summary>
    DisabledBySystem,
}

/// <summary>Starts the app when the user signs in.</summary>
public interface IAutostart
{
    /// <summary>Tells whether the app is registered, and whether the system lets it start.</summary>
    AutostartState GetState();

    /// <summary>Registers the app, overriding an earlier "off" among the system's startup apps.</summary>
    void Enable();

    /// <summary>Removes the registration.</summary>
    void Disable();

    /// <summary>
    /// Takes the registration away for good, as uninstalling the app does; where removing the registration leaves nothing
    /// behind, that is all it does.
    /// </summary>
    void Remove() => Disable();
}

/// <summary>The system's service manager refused a request, such as one to stop starting the app with the system.</summary>
/// <param name="message">The service manager's words.</param>
/// <param name="innerException">The refusal as it arrived.</param>
public sealed class ServiceRefusedException(string message, Exception innerException) : IOException(message, innerException);
