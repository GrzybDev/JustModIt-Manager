using System.Reflection;
using JustModIt.Manager.Ui;
using Microsoft.Extensions.DependencyInjection;

namespace JustModIt.Manager.Host;

/// <summary>What an app hands the shared startup about itself.</summary>
public sealed record AppOptions
{
    public required ProductSettings Product { get; init; }

    /// <summary>The app's own assembly, into which its build merged its texts and embedded its licence and notices.</summary>
    public required Assembly App { get; init; }

    /// <summary>The app's own agent services, registered after the shared ones, so they add to or replace them.</summary>
    public Action<IServiceCollection>? ConfigureAgent { get; init; }

    /// <summary>The app's own views, pages, styles and resources of the window, added after the shared ones so they win.</summary>
    public Action<UiComposition>? ConfigureUi { get; init; }

    /// <summary>The window's process; the shared one when null.</summary>
    public IUiHost? Ui { get; init; }
}
