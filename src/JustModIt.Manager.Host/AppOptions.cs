using JustModIt.Manager.Localization;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Host;

/// <summary>What an app hands the shared startup about itself.</summary>
public sealed record AppOptions
{
    public required ProductSettings Product { get; init; }

    /// <summary>The app's texts, as its build merged them: <see cref="TextCatalog.Load"/> of the app's assembly.</summary>
    public required TextCatalog Texts { get; init; }

    /// <summary>The app's own agent services, registered after the shared ones, so they add to or replace them.</summary>
    public Action<IServiceCollection>? ConfigureAgent { get; init; }

    /// <summary>The window's process; the shared one when null.</summary>
    public IUiHost? Ui { get; init; }
}
