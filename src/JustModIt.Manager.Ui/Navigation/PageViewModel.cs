using CommunityToolkit.Mvvm.ComponentModel;

namespace JustModIt.Manager.Ui.Navigation;

/// <summary>
/// A page of the window. The navigator keeps it while it is shown or on the way back (a sidebar destination for as long
/// as the window lives) and disposes it, when it is <see cref="IDisposable"/>, once it is dropped.
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    /// <summary>The page's name, for the window's title and screen readers.</summary>
    public abstract string Title { get; }

    /// <summary>Called when the page becomes the one shown, also when it is gone back to.</summary>
    public virtual void OnShown()
    {
    }

    /// <summary>Called when another page takes its place.</summary>
    public virtual void OnHidden()
    {
    }

    /// <summary>Whether the window may close now; a page with the player's unsaved work asks first and may say no.</summary>
    public virtual Task<bool> CanCloseAsync() => Task.FromResult(true);
}
