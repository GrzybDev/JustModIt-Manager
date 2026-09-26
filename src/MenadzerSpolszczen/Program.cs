using JustModIt.Manager.Host;

namespace MenadzerSpolszczen;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppHost.Run(args);
}
