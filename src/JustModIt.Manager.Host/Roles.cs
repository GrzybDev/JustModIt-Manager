using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using JustModIt.Manager.Agent;
using JustModIt.Manager.Agent.Diagnostics;
using JustModIt.Manager.Agent.Updates;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux;
using JustModIt.Manager.Platform.Windows;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.Logging;
using Serilog.Core;
using Serilog.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Host;

/// <summary>
/// The roles one executable plays: a launch that finds its role taken hands its command line to the agent and ends, and
/// a launcher that finds no agent becomes the agent itself, so no second process starts; where the agent runs as the
/// user's systemd unit, a launcher starts the unit and becomes the window instead, unless the session would not show it.
/// </summary>
internal static partial class Roles
{
    private const string AgentRole = "agent";
    private const string UiRole = "ui";
    private const string LauncherRole = "launcher";

    // How long a launch keeps trying to reach an agent that is still starting or already exiting.
    private static readonly TimeSpan HandOverTimeout = TimeSpan.FromSeconds(15);

    /// <summary>Runs the agent, or hands the command line to the running one, and returns the exit code.</summary>
    /// <param name="environment">This process's environment.</param>
    /// <param name="options">What the app hands the startup about itself.</param>
    /// <param name="launch">The command line, parsed.</param>
    /// <param name="args">The command line as given, for a hand-over.</param>
    public static int RunAgent(HostEnvironment environment, AppOptions options, LaunchArguments launch, string[] args)
    {
        if (!AgentPlatform.IsSupported)
        {
            return Refuse(environment);
        }

        return RunClaimed(environment, environment.Names.AgentLock, AgentRole, args, (loggers, session, previous) =>
        {
            var agent = environment.AgentOptions() with
            {
                Session = session,
                PreviousExit = UntoldExit.Carry(environment.Locations.StateDirectory, previous),
            };
            AgentHost.RunAsync(agent, launch, loggers,
                    services => AgentPlatform.Compose(services, agent, environment.Product.DesktopCategories, options.App, options.ConfigureAgent))
                .GetAwaiter().GetResult();
            return 0;
        });
    }

    /// <summary>
    /// Starts the agent's unit and runs the window, where the agent runs as the user's systemd unit; otherwise runs the
    /// agent as <see cref="RunAgent"/> does. A launch that asks the app to exit only hands that to a running agent. Returns
    /// the exit code.
    /// </summary>
    /// <param name="environment">This process's environment.</param>
    /// <param name="options">What the app hands the startup about itself.</param>
    /// <param name="launch">The command line, parsed.</param>
    /// <param name="args">The command line as given, for a hand-over.</param>
    public static int RunLauncher(HostEnvironment environment, AppOptions options, LaunchArguments launch, string[] args)
    {
        if (launch.Exit)
        {
            return Exit(environment, args);
        }

        if (!environment.RunsAgentAsUnit)
        {
            return RunAgent(environment, options, launch, args);
        }

        var unit = AgentPlatform.Unit(environment.AgentOptions());
        return RouteOf(launch.Background, environment.StartedBySteam, InGamescope) switch
        {
            LaunchRoute.Background => StartUnit(environment, unit) ? 0 : RunAgent(environment, options, launch, args),
            LaunchRoute.HandOver => !StartUnit(environment, unit) ? RunAgent(environment, options, launch, args)
                : Claim(environment, lockName: null, args) is Forwarded ? 0
                : 1,
            // The launch becomes the window itself, so the window takes the desktop's activation, and in a store's game session it
            // is the program the store started, which the session shows.
            _ => LaunchWindow(environment.StartedBySteam, () => InstanceLock.TryAcquire(environment.Names.UiLock),
                () => StartUnit(environment, unit), windowLock => RunHolding(environment, windowLock, UiRole, Window(environment, options, launch)),
                () => RunUi(environment, options, launch, args), () => RunAgent(environment, options, launch, args)),
        };
    }

    /// <summary>
    /// Tells the way of a launch where the agent runs as the user's systemd unit: one that Steam did not start in a gamescope
    /// session, such as a link from Steam's browser, hands its command line over, since a window it became would never show
    /// and would hold the window's lock against the one Steam starts.
    /// </summary>
    /// <param name="background">Whether the launch only starts the agent.</param>
    /// <param name="startedBySteam">Whether Steam started the launch from its library.</param>
    /// <param name="inGamescope">Tells whether the user's session is gamescope's, asked only when the way depends on it.</param>
    internal static LaunchRoute RouteOf(bool background, bool startedBySteam, Func<bool> inGamescope) =>
        background ? LaunchRoute.Background
        : !startedBySteam && inGamescope() ? LaunchRoute.HandOver
        : LaunchRoute.Window;

    /// <summary>
    /// Carries out a launch that becomes the window where the agent runs as the user's systemd unit, taking the window's lock
    /// before the unit starts, so an agent that starts meanwhile sees the window on its way.
    /// </summary>
    /// <param name="startedBySteam">Whether Steam started this launch from its library.</param>
    /// <param name="takeWindow">Takes the window's lock; null while another process holds it.</param>
    /// <param name="startUnit">Starts the agent's unit; false when systemd did not.</param>
    /// <param name="runWindow">Runs the window, holding the lock it is given until it ends.</param>
    /// <param name="handOver">Hands the command line over to the agent, for the window that runs.</param>
    /// <param name="runAgent">Runs the agent in this process.</param>
    /// <returns>The exit code.</returns>
    internal static int LaunchWindow(bool startedBySteam, Func<InstanceLock?> takeWindow, Func<bool> startUnit,
        Func<InstanceLock, int> runWindow, Func<int> handOver, Func<int> runAgent)
    {
        var windowLock = takeWindow();
        // Where systemd does not start the unit, the launch runs the agent itself, unless Steam started it, since Steam ends
        // every process of its launch.
        if (!startUnit() && !startedBySteam)
        {
            windowLock?.Dispose();
            return runAgent();
        }

        return windowLock is null ? handOver() : runWindow(windowLock);
    }

    /// <summary>
    /// Runs the window's process, or hands the command line to the agent when a window runs already, and returns the
    /// exit code.
    /// </summary>
    /// <param name="environment">This process's environment.</param>
    /// <param name="options">What the app hands the startup about itself.</param>
    /// <param name="launch">The command line, parsed.</param>
    /// <param name="args">The command line as given, for a hand-over.</param>
    public static int RunUi(HostEnvironment environment, AppOptions options, LaunchArguments launch, string[] args) =>
        RunClaimed(environment, environment.Names.UiLock, UiRole, args, Window(environment, options, launch));

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

    /// <summary>
    /// Starts the agent as a process of its own for a window that waited for it in vain; a running agent takes the launch
    /// over, and the window reconnects to whichever answers.
    /// </summary>
    /// <param name="executable">This copy's executable.</param>
    /// <param name="logger">Receives why the process could not be started.</param>
    /// <returns>False when the system could not start it.</returns>
    internal static bool StartAgentProcess(string executable, ILogger logger)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(executable, LaunchArguments.BackgroundSwitch) { UseShellExecute = false });
            return true;
        }
        catch (Win32Exception exception)
        {
            LogAgentNotStarted(logger, exception);
            return false;
        }
    }

    // A launch whose role is taken hands its command line over and ends; a failed hand-over is in the launcher's log.
    private static int RunClaimed(HostEnvironment environment, string lockName, string role, string[] args,
        Func<ILoggerFactory, SessionMarker, UncleanExit?, int> run)
    {
        var claim = Claim(environment, lockName, args);
        return claim is Claimed { Lock: var instanceLock } ? RunHolding(environment, instanceLock, role, run)
            : claim is Forwarded ? 0
            : 1;
    }

    private static int RunHolding(HostEnvironment environment, InstanceLock instanceLock, string role,
        Func<ILoggerFactory, SessionMarker, UncleanExit?, int> run)
    {
        using (instanceLock)
        {
            return RunRole(environment, role, run);
        }
    }

    private static Func<ILoggerFactory, SessionMarker, UncleanExit?, int> Window(HostEnvironment environment, AppOptions options,
        LaunchArguments launch) =>
        (loggers, _, previous) => WindowHost.Run(environment.WindowContext(options, launch, loggers, previous));

    // A launch from a terminal or a handler that started earlier may carry another session's variables, or none, so logind
    // tells the session the user sees.
    private static bool InGamescope() =>
        Gamescope.IsSession(Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP"))
        || DisplaySession.IsGamescope(DBusConnection.System, DisplaySession.TimeLimit);

    // The updater's launch runs the version its record names, and an agent of the file before the update, which a launch
    // started while the updater replaced the file, makes way for it. The record is read first, since an agent of this version
    // deletes it as it starts.
    private static bool StartUnit(HostEnvironment environment, AgentUnit unit)
    {
        var attempted = UpdateAttempt.Read(environment.Locations);
        if (!unit.Start())
        {
            using var log = new RoleLog(environment, LauncherRole);
            LogUnitNotStarted(log.Loggers.CreateLogger(typeof(Roles).FullName!));
            return false;
        }

        var agent = attempted == environment.Version
            ? SingleInstance.FindAgentAsync(environment.Names.PipeName, Hello.ForCurrentProcess(ProcessRole.Launcher, environment.Version),
                HandOverTimeout).GetAwaiter().GetResult()
            : null;
        if (agent is not null && agent.AppVersion != environment.Version && !unit.Restart())
        {
            using var log = new RoleLog(environment, LauncherRole);
            LogAgentNotRestarted(log.Loggers.CreateLogger(typeof(Roles).FullName!), agent.AppVersion);
        }

        return true;
    }

    // An exit is for the running agent alone: a launch that finds none has nothing to end, so it starts nothing.
    private static int Exit(HostEnvironment environment, string[] args)
    {
        var claim = Claim(environment, environment.Names.AgentLock, args);
        (claim as Claimed)?.Lock.Dispose();
        return claim is Unreachable ? 1 : 0;
    }

    private static InstanceClaim Claim(HostEnvironment environment, string? lockName, string[] args)
    {
        using var log = new RoleLog(environment, LauncherRole);
        return SingleInstance.ClaimOrForwardAsync(lockName, environment.Names.PipeName,
                Hello.ForCurrentProcess(ProcessRole.Launcher, environment.Version), args, HandOverTimeout, AllowForeground,
                logger: log.Loggers.CreateLogger(typeof(Roles).FullName!))
            .GetAwaiter().GetResult();
    }

    // Refused before the claim, so a run that never starts takes no lock, begins no session and logs no start.
    private static int Refuse(HostEnvironment environment)
    {
        using var log = new RoleLog(environment, LauncherRole);
        LogUnsupported(log.Loggers.CreateLogger(typeof(Roles).FullName!), environment.System);
        return 1;
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
        LogStarting(logger, role, environment.Version, environment.System, RuntimeInformation.ProcessArchitecture,
            environment.Install.IsInstalled, Environment.IsPrivilegedProcess);
        // Before the role starts any program, every one of which would otherwise keep what this process inherited open.
        if (OperatingSystem.IsLinux() && ProcessDescriptors.KeepFromChildren() is { } error)
        {
            LogDescriptorsInherited(logger, error);
        }

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The agent's process could not be started")]
    private static partial void LogAgentNotStarted(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The agent's systemd unit could not be started")]
    private static partial void LogUnitNotStarted(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The agent of version {Version} could not be restarted after the update")]
    private static partial void LogAgentNotRestarted(ILogger logger, string version);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The descriptors this process inherited could not be kept from the programs it starts (error {Error})")]
    private static partial void LogDescriptorsInherited(ILogger logger, int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "The agent does not run on {System} yet")]
    private static partial void LogUnsupported(ILogger logger, SystemRelease system);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Starting the {Role} {Version} on {System} ({Architecture}); installed: {Installed}, elevated: {Elevated}")]
    private static partial void LogStarting(ILogger logger, string role, string version, SystemRelease system, Architecture architecture,
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
