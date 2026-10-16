using System.Runtime.InteropServices;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Agent.Diagnostics;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Extensions.Logging;

namespace JustModIt.Manager.Host;

/// <summary>
/// The roles one executable plays: a launch that finds its role taken hands its command line to the agent and ends, and
/// a launcher that finds no agent becomes the agent itself, so no second process starts.
/// </summary>
internal static partial class Roles
{
    private const string AgentRole = "agent";
    private const string UiRole = "ui";

    // How long a launch keeps trying to reach an agent that is still starting or already exiting.
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Runs the agent, or hands the command line to the running one, and returns the exit code.</summary>
    /// <param name="environment">This process's environment.</param>
    /// <param name="options">What the app hands the startup about itself.</param>
    /// <param name="launch">The command line, parsed.</param>
    /// <param name="args">The command line as given, for a hand-over.</param>
    public static int RunAgent(HostEnvironment environment, AppOptions options, LaunchArguments launch, string[] args) =>
        RunClaimed(environment, environment.Names.AgentLock, AgentRole, args, (loggers, session, previous) =>
        {
            var agent = environment.AgentOptions() with
            {
                Session = session,
                PreviousExit = UntoldExit.Carry(environment.Locations.StateDirectory, previous),
            };
            AgentHost.RunAsync(agent, launch, loggers, options.ConfigureAgent).GetAwaiter().GetResult();
            return 0;
        });

    /// <summary>
    /// Runs the window's process, or hands the command line to the agent when a window runs already, and returns the
    /// exit code.
    /// </summary>
    /// <param name="environment">This process's environment.</param>
    /// <param name="options">What the app hands the startup about itself.</param>
    /// <param name="launch">The command line, parsed.</param>
    /// <param name="args">The command line as given, for a hand-over.</param>
    public static int RunUi(HostEnvironment environment, AppOptions options, LaunchArguments launch, string[] args) =>
        RunClaimed(environment, environment.Names.UiLock, UiRole, args,
            (loggers, _, previous) => (options.Ui ?? new WindowHost()).Run(environment.WindowContext(options, launch, loggers, previous)));

    /// <summary>
    /// Ends the session markers of the runs that the installer stopped to replace the app, so the next start does not take
    /// a stop the player agreed to for an unexpected close; a crash a marker recorded, and an exit no window has told yet,
    /// stay to be told.
    /// </summary>
    /// <param name="stateDirectory">The app's state folder.</param>
    public static void EndStoppedRuns(string stateDirectory)
    {
        SessionMarker.EndStopped(stateDirectory, AgentRole);
        SessionMarker.EndStopped(stateDirectory, UiRole);
    }

    // A launch whose role is taken hands its command line over and ends; a failed hand-over is in the launcher's log.
    private static int RunClaimed(HostEnvironment environment, string lockName, string role, string[] args,
        Func<ILoggerFactory, SessionMarker, UncleanExit?, int> run)
    {
        InstanceClaim claim;
        using (var log = new RoleLog(environment, "launcher"))
        {
            claim = SingleInstance.ClaimOrForwardAsync(lockName, environment.Names.PipeName,
                    Hello.ForCurrentProcess(ProcessRole.Launcher, environment.Version), args, HandOverTimeout, AllowForeground,
                    logger: log.Loggers.CreateLogger(typeof(Roles).FullName!))
                .GetAwaiter().GetResult();
        }

        if (claim is not Claimed { Lock: var instanceLock })
        {
            return claim is Forwarded ? 0 : 1;
        }

        using (instanceLock)
        {
            return RunRole(environment, role, run);
        }
    }

    // The launcher has the foreground because the player just started it; the agent needs it to bring the window up.
    private static void AllowForeground(Hello agent)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsForegroundHandoff.Instance.Allow(agent.ProcessId);
        }
    }

    private static int RunRole(HostEnvironment environment, string role, Func<ILoggerFactory, SessionMarker, UncleanExit?, int> run)
    {
        using var log = new RoleLog(environment, role);
        var logger = log.Loggers.CreateLogger(typeof(Roles).FullName!);
        LogStarting(logger, role, environment.Version, RuntimeInformation.OSDescription, RuntimeInformation.ProcessArchitecture,
            environment.Install.IsInstalled, Environment.IsPrivilegedProcess);
        var session = new SessionMarker(environment.Locations.StateDirectory, role, environment.Version, TimeProvider.System, log.Redactor);
        var previous = session.Begin();
        CrashHandlers.Report(logger, previous);
        int exitCode;
        using (CrashHandlers.Install(logger, session, log))
        {
            exitCode = run(log.Loggers, session, previous);
        }

        session.End();
        return exitCode;
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting the {Role} {Version} on {System} ({Architecture}); installed: {Installed}, elevated: {Elevated}")]
    private static partial void LogStarting(ILogger logger, string role, string version, string system, Architecture architecture,
        bool installed, bool elevated);

    private sealed class RoleLog : IDisposable
    {
        private readonly Logger _logger;

        public RoleLog(HostEnvironment environment, string role)
        {
            Redactor = PathRedactor.ForCurrentUser();
            _logger = AppLogging.Create(environment.Locations.LogsDirectory, role, environment.Product.IsDevelopmentBuild, Redactor);
            Loggers = new SerilogLoggerFactory(_logger);
        }

        public SerilogLoggerFactory Loggers { get; }

        public PathRedactor Redactor { get; }

        public void Dispose()
        {
            Loggers.Dispose();
            _logger.Dispose();
        }
    }
}
