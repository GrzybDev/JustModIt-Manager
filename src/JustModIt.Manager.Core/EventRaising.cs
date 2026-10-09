namespace JustModIt.Manager.Core;

/// <summary>Raises events so that every listener hears them even when one of them fails.</summary>
public static class EventRaising
{
    /// <summary>Calls each handler in turn, handing a failure to <paramref name="onFailure"/> instead of letting it through.</summary>
    /// <param name="handlers">The event's handlers; null when it has none.</param>
    /// <param name="sender">The object that raises the event.</param>
    /// <param name="args">What the event carries.</param>
    /// <param name="onFailure">Logs a handler's failure.</param>
    public static void RaiseEach<T>(EventHandler<T>? handlers, object sender, T args, Action<Exception> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onFailure);
        foreach (var handler in handlers?.GetInvocationList() ?? [])
        {
            try
            {
                ((EventHandler<T>)handler)(sender, args);
            }
            // One broken listener must neither keep the others from hearing of the event nor break the work that raised it.
            catch (Exception exception)
            {
                onFailure(exception);
            }
        }
    }
}
