using Velopack;

namespace JustModIt.Manager.Host;

/// <summary>The startup that every app runs from its <c>Main</c>.</summary>
public static class AppHost
{
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        VelopackApp.Build().Run();
        return 0;
    }
}
