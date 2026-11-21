using JustModIt.Manager.Protocol.Messages;

namespace JustModIt.Manager.Protocol.Activation;

/// <summary>A command line of the app: the role it selects and where it leads.</summary>
/// <param name="Role">The role the process runs.</param>
/// <param name="Background">Started with the system, into the tray.</param>
/// <param name="Target">Where an app link leads; null for a plain start and for links that only open the window.</param>
/// <param name="Exit">Asks the running app to exit once its jobs are done, as the tray's exit does, and starts nothing.</param>
public sealed record LaunchArguments(ProcessRole Role, bool Background, NavigationTarget? Target, bool Exit = false)
{
    public const string AgentSwitch = "--agent";
    public const string UiSwitch = "--ui";
    public const string BackgroundSwitch = "--background";
    public const string ExitSwitch = "--exit";

    /// <summary>
    /// Reads a command line, ignoring anything unknown such as Velopack's own switches; of several links the last wins.
    /// </summary>
    /// <param name="args">The arguments without the executable, or those a later launch handed over.</param>
    /// <param name="links">The app's links, to tell a link from other arguments.</param>
    public static LaunchArguments Parse(IReadOnlyList<string> args, AppLinks links)
    {
        var agent = false;
        var ui = false;
        var background = false;
        var exit = false;
        NavigationTarget? target = null;

        // Forwarded command lines arrive as JSON, whose arrays can hold nulls that the element type does not admit.
        foreach (var arg in args.Where(arg => arg is not null))
        {
            if (string.Equals(arg, AgentSwitch, StringComparison.OrdinalIgnoreCase))
            {
                agent = true;
            }
            else if (string.Equals(arg, UiSwitch, StringComparison.OrdinalIgnoreCase))
            {
                ui = true;
            }
            else if (string.Equals(arg, BackgroundSwitch, StringComparison.OrdinalIgnoreCase))
            {
                background = true;
            }
            else if (string.Equals(arg, ExitSwitch, StringComparison.OrdinalIgnoreCase))
            {
                exit = true;
            }
            else if (links.IsLink(arg))
            {
                target = links.Parse(arg);
            }
        }

        var role = ui ? ProcessRole.Ui : agent ? ProcessRole.Agent : ProcessRole.Launcher;
        return new LaunchArguments(role, background, target, exit);
    }

    /// <summary>The command line that <see cref="Parse"/> reads back as these arguments.</summary>
    /// <param name="links">The app's links, to write the target as one.</param>
    public IReadOnlyList<string> ToArgs(AppLinks links)
    {
        List<string> args = Role switch
        {
            ProcessRole.Agent => [AgentSwitch],
            ProcessRole.Ui => [UiSwitch],
            _ => [],
        };
        if (Background)
        {
            args.Add(BackgroundSwitch);
        }

        if (Exit)
        {
            args.Add(ExitSwitch);
        }

        if (Target is not null)
        {
            args.Add(links.For(Target));
        }

        return args;
    }
}
