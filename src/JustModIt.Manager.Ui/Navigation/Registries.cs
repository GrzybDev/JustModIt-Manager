using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>
/// The views of view models: an explicit list of factories, since a locator that finds views by reflection does not
/// survive trimming. A later registration of the same view model replaces the earlier one, which is how an app puts
/// its own view in place of a shared one.
/// </summary>
public sealed class ViewRegistry : IDataTemplate
{
    private readonly Dictionary<Type, Func<Control>> _views = [];

    /// <summary>Registers the view of a view model, or replaces the one registered.</summary>
    /// <param name="view">Creates the view; its data context is the view model.</param>
    /// <typeparam name="TViewModel">The view model's exact type.</typeparam>
    public void Register<TViewModel>(Func<Control> view)
        where TViewModel : class
    {
        ArgumentNullException.ThrowIfNull(view);
        _views[typeof(TViewModel)] = view;
    }

    public bool Match(object? data) => data is not null && _views.ContainsKey(data.GetType());

    public Control? Build(object? param) => param is not null && _views.TryGetValue(param.GetType(), out var view) ? view() : null;
}

/// <summary>
/// The pages of routes. Each screen registers the page of its route (see <c>Pages/*Screen.cs</c>); an app registers its
/// own afterwards to replace one.
/// </summary>
public sealed class PageRegistry
{
    private readonly Dictionary<Type, Func<Route, UiServices, PageViewModel>> _pages = [];

    /// <summary>Registers the page of a route, or replaces the one registered.</summary>
    /// <param name="page">Creates the page for a route of that type.</param>
    /// <typeparam name="TRoute">The route's type.</typeparam>
    public void Register<TRoute>(Func<TRoute, UiServices, PageViewModel> page)
        where TRoute : Route
    {
        ArgumentNullException.ThrowIfNull(page);
        _pages[typeof(TRoute)] = (route, services) => page((TRoute)route, services);
    }

    /// <summary>Creates the page of a route.</summary>
    /// <param name="route">The route to show.</param>
    /// <param name="services">What the page talks to.</param>
    /// <exception cref="InvalidOperationException">No screen registered a page for the route's type.</exception>
    public PageViewModel Create(Route route, UiServices services)
    {
        ArgumentNullException.ThrowIfNull(route);
        return _pages.TryGetValue(route.GetType(), out var page)
            ? page(route, services)
            : throw new InvalidOperationException($"No page is registered for {route.GetType().Name}.");
    }
}
