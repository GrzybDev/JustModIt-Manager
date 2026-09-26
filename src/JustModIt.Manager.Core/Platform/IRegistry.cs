namespace JustModIt.Manager.Core.Platform;

public enum RegistryHive
{
    CurrentUser,
    LocalMachine,
}

/// <summary>Which half of the registry to read; 32-bit installers write their keys under <c>WOW6432Node</c>.</summary>
public enum RegistryView
{
    /// <summary>The view of the running process.</summary>
    Default,
    Registry32,
    Registry64,
}

/// <summary>
/// Read access to the Windows registry, so the code that inspects stores can be tested without it, whose reads may block,
/// so they stay off the UI thread.
/// </summary>
public interface IRegistry
{
    /// <summary>The key's values and subkey names; null when the key does not exist or cannot be read.</summary>
    /// <param name="hive">The root the key is under.</param>
    /// <param name="view">Which half of the registry to read.</param>
    /// <param name="path">The key's path below the hive, with backslashes.</param>
    RegistryKeySnapshot? ReadKey(RegistryHive hive, RegistryView view, string path);
}
