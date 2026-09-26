using JustModIt.Manager.Host;

namespace JustModIt.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppHost.Run(args);
}
