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

    /// <summary>Registers <paramref name="command"/>, overriding an earlier "off" among the system's startup apps.</summary>
    /// <param name="command">The whole command line, with the executable quoted.</param>
    void Enable(string command);

    /// <summary>Removes the registration.</summary>
    void Disable();
}
