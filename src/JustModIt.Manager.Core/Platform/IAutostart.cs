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
}
