using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Host;
using Microsoft.Extensions.DependencyInjection;

namespace MenadzerSpolszczen;

internal static class Program
{
    internal static AppOptions Options => new()
    {
        Product = ProductConfiguration.Settings,
        App = typeof(Program).Assembly,
        ConfigureAgent = services => services.AddSingleton<IInstallPolicy, OneProjectPerGamePolicy>(),
    };

    [STAThread]
    private static int Main(string[] args) => AppHost.Run(args, Options);
}
