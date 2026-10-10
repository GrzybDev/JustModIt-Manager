using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>
/// Which page the window shows and the way back. Sidebar destinations clear the way back and keep their page, so the
/// player finds a page as they left it; a page opened from one goes on top of it.
/// </summary>
/// <param name="create">Creates the page of a route.</param>
public sealed partial class Navigator(Func<Route, PageViewModel> create) : ObservableObject
{
    private readonly Stack<Entry> _back = new();
    private readonly Dictionary<Route, PageViewModel> _roots = [];

    /// <summary>The page shown; null until the first navigation.</summary>
    [ObservableProperty]
    public partial PageViewModel? Current { get; private set; }

    [ObservableProperty]
    public partial Route? CurrentRoute { get; private set; }

    /// <summary>The last change went back, which reverses the page transition.</summary>
    [ObservableProperty]
    public partial bool WentBack { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GoBackCommand))]
    public partial bool CanGoBack { get; private set; }

    /// <summary>Raised after <see cref="Current"/> changed.</summary>
    public event EventHandler? Navigated;

    /// <summary>Every page alive: the one shown, those on the way back and the kept sidebar destinations.</summary>
    public IEnumerable<PageViewModel> Pages =>
        new[] { Current }.Concat(_back.Select(entry => entry.Page)).Concat(_roots.Values).OfType<PageViewModel>().Distinct();

    /// <summary>Shows a route, on top of the page shown unless it is a sidebar destination.</summary>
    /// <param name="route">Where to go; the route shown already changes nothing.</param>
    public void Navigate(Route route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route == CurrentRoute)
        {
            return;
        }

        if (route.IsRoot)
        {
            List<Entry> dropped = [.. _back];
            _back.Clear();
            if (CurrentRoute is { IsRoot: false } left && Current is { } page)
            {
                dropped.Add(new Entry(left, page));
            }

            if (!_roots.TryGetValue(route, out var root))
            {
                root = create(route);
                _roots[route] = root;
            }

            Show(new Entry(route, root), wentBack: false);
            dropped.ForEach(Drop);
            return;
        }

        if (CurrentRoute is { } current)
        {
            _back.Push(new Entry(current, Current!));
        }

        Show(new Entry(route, create(route)), wentBack: false);
    }

    /// <summary>Opens a place a link names: each route on top of the one before, the first a sidebar destination.</summary>
    /// <param name="routes">The routes in order, from <see cref="AgentLinks.RoutesFor"/>.</param>
    public void Open(IEnumerable<Route> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        foreach (var route in routes)
        {
            Navigate(route);
        }
    }

    /// <summary>Asks every page alive whether the window may close.</summary>
    /// <returns>False when a page keeps the player's unsaved work.</returns>
    public async Task<bool> CanCloseAsync()
    {
        foreach (var page in Pages.ToList())
        {
            if (!await page.CanCloseAsync().ConfigureAwait(true))
            {
                return false;
            }
        }

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void GoBack()
    {
        if (!_back.TryPop(out var previous))
        {
            return;
        }

        var left = CurrentRoute is { } route && Current is { } page ? new Entry(route, page) : null;
        Show(previous, wentBack: true);
        if (left is not null)
        {
            Drop(left);
        }
    }

    private void Show(Entry entry, bool wentBack)
    {
        Current?.OnHidden();
        WentBack = wentBack;
        CurrentRoute = entry.Route;
        Current = entry.Page;
        CanGoBack = _back.Count > 0;
        entry.Page.OnShown();
        Navigated?.Invoke(this, EventArgs.Empty);
    }

    private static void Drop(Entry entry)
    {
        if (!entry.Route.IsRoot && entry.Page is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private sealed record Entry(Route Route, PageViewModel Page);
}
