using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Host;
using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace MenadzerSpolszczen;

internal static class Program
{
    internal static AppOptions Options => new()
    {
        Product = ProductConfiguration.Settings,
        Texts = TextCatalog.Load(ProductConfiguration.Language, typeof(Program).Assembly),
        Notices = new LegalNotices(typeof(Program).Assembly),
        ConfigureAgent = services => services.AddSingleton<IInstallPolicy, OneProjectPerGamePolicy>(),
    };

    [STAThread]
    private static int Main(string[] args) => AppHost.Run(args, Options);
}
