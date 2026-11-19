namespace JustModIt.Manager.Agent.Steam;

/// <summary>The scripts the app runs in Steam's interface, and the Millennium plugin made of them, embedded in the agent.</summary>
internal static class SteamScripts
{
    public const string HookResource = "JustModIt.Manager.Agent.Steam.achievement-texts.js";
    public const string LaunchOptionsResource = "JustModIt.Manager.Agent.Steam.launch-options.js";
    public const string FrontendResource = "JustModIt.Manager.Agent.Steam.millennium.frontend.js";
    public const string BackendResource = "JustModIt.Manager.Agent.Steam.millennium.backend.main.lua";

    private const string PluginNameMarker = "__PLUGIN_NAME__";

    /// <summary>The expression that places the achievement texts hook and gives which of its hooks are in place.</summary>
    public static string Hook => Read(HookResource);

    /// <summary>The expression that gives the object which reads, changes and watches games' launch options.</summary>
    public static string LaunchOptions => Read(LaunchOptionsResource);

    /// <summary>The plugin's frontend, holding both scripts.</summary>
    /// <param name="pluginName">The plugin's name, made of letters, digits and dashes.</param>
    public static string PluginFrontend(string pluginName) => Read(FrontendResource)
        .Replace(PluginNameMarker, pluginName, StringComparison.Ordinal)
        .Replace("__HOOK__", Hook, StringComparison.Ordinal)
        .Replace("__LAUNCH_OPTIONS__", LaunchOptions, StringComparison.Ordinal);

    /// <summary>The plugin's backend.</summary>
    /// <param name="pluginName">The plugin's name, made of letters, digits and dashes.</param>
    public static string PluginBackend(string pluginName) =>
        Read(BackendResource).Replace(PluginNameMarker, pluginName, StringComparison.Ordinal);

    /// <summary>One of the embedded files as it is in the repository.</summary>
    /// <param name="resource">The file's resource name.</param>
    public static string Read(string resource)
    {
        using var stream = typeof(SteamScripts).Assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
