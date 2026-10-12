using Avalonia.Controls;
using Avalonia.Styling;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Images;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;

namespace JustModIt.Manager.Ui;

/// <summary>What the window's pages talk to; one set for the window's life.</summary>
public sealed class UiServices
{
    /// <summary>The agent: the state to show, and the commands and queries that do everything else.</summary>
    public required IAgent Agent { get; init; }

    public required Navigator Navigator { get; init; }

    /// <summary>Asks the player questions inside the window.</summary>
    public required DialogHost Dialogs { get; init; }

    /// <summary>Shows notifications in the window's corner.</summary>
    public required ToastHost Toasts { get; init; }

    /// <summary>Loads the site's images, which <see cref="Controls.RemoteImage"/> shows.</summary>
    public required IImageLoader Images { get; init; }

    /// <summary>The app's names, links and options.</summary>
    public required UiContext Context { get; init; }

    /// <summary>Opens the site's pages and the stores' links outside the window.</summary>
    public required IExternalLinks Links { get; init; }

    /// <summary>The system's pickers of folders and files.</summary>
    public required IFilePickers Pickers { get; init; }

    /// <summary>The time now and the local time zone, which dates on the pages are told against.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;
}

/// <summary>
/// Where the window is put together: the shared screens register here first, then the app adds its own or replaces
/// them, before the window opens.
/// </summary>
/// <param name="resources">The application's resources, which win over the theme's.</param>
/// <param name="styles">The application's styles, applied after the theme's.</param>
public sealed class UiComposition(IResourceDictionary resources, Styles styles)
{
    /// <summary>The views of view models, the pages' among them.</summary>
    public ViewRegistry Views { get; } = new();

    /// <summary>The pages of routes.</summary>
    public PageRegistry Pages { get; } = new();

    public IResourceDictionary Resources { get; } = resources;

    public Styles Styles { get; } = styles;

    /// <summary>The first-run setup's page, shown over the whole window until the setup is done; null shows none.</summary>
    public Func<UiServices, PageViewModel>? FirstRun { get; set; }
}
