using JustModIt.Manager.Host;
using JustModIt.Manager.Localization;

namespace JustModIt.Manager;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args) => AppHost.Run(args, new AppOptions
    {
        Texts = TextCatalog.Load(ProductConfiguration.Language, typeof(Program).Assembly),
    });
}
