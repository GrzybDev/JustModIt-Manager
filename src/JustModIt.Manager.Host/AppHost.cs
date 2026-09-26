using JustModIt.Manager.Localization;
using Velopack;

namespace JustModIt.Manager.Host;

/// <summary>The startup that every app runs from its <c>Main</c>.</summary>
public static class AppHost
{
    /// <summary>Runs the process in the role its command line names and returns its exit code.</summary>
    /// <param name="args">The command line, as <c>Main</c> got it.</param>
    /// <param name="options">What the app hands the startup about itself.</param>
    public static int Run(string[] args, AppOptions options)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(options);

        VelopackApp.Build().Run();
        Text.Use(options.Texts);
        return 0;
    }
}
