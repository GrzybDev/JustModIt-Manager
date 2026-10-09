using JustModIt.Manager.Core;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
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

        // The installer starts the app with hook arguments and expects it to end inside Run, so this comes first. Its own
        // applying of updates at start is off, since it would run in every process, including launches that only hand
        // over; the agent applies them itself.
        var product = options.Product;
        var restartedByUpdater = false;
        var velopack = VelopackApp.Build().SetAutoApplyOnStartup(false).OnRestarted(_ => restartedByUpdater = true);
        if (OperatingSystem.IsWindows())
        {
            velopack = velopack
                .OnAfterInstallFastCallback(_ => RegisterShell(product))
                .OnAfterUpdateFastCallback(_ => RegisterShell(product))
                .OnBeforeUninstallFastCallback(_ => UnregisterShell(product));
        }

        velopack.Run();
        Text.Use(TextCatalog.Load(product.Language, options.App));

        HostEnvironment environment;
        try
        {
            environment = HostEnvironment.Create(product, InstallLocation.Current()) with { RestartedByUpdater = restartedByUpdater };
        }
        // Without a data folder there is nowhere to write even a log; the app must not guess one.
        catch (AppDataUnavailableException)
        {
            return 1;
        }

        var launch = LaunchArguments.Parse(args, environment.Links);
        return launch.Role == ProcessRole.Ui
            ? Roles.RunUi(environment, options, launch, args)
            : Roles.RunAgent(environment, options, launch, args);
    }

    private static void RegisterShell(ProductSettings product)
    {
        if (OperatingSystem.IsWindows())
        {
            ShellHooks.Register(product);
        }
    }

    private static void UnregisterShell(ProductSettings product)
    {
        if (OperatingSystem.IsWindows())
        {
            ShellHooks.Unregister(product);
        }
    }
}
