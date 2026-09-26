namespace JustModIt.Manager.Protocol.Activation;

/// <summary>Held for the whole life of a role; while it exists, later launches know the role is taken.</summary>
public sealed class InstanceLock : IDisposable
{
    private readonly Mutex _mutex;

    private InstanceLock(Mutex mutex) => _mutex = mutex;

    /// <summary>Takes the named lock, or returns null when another holder has it.</summary>
    /// <param name="name">One of the names in <see cref="InstanceNames"/>.</param>
    public static InstanceLock? TryAcquire(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        Mutex mutex;
        bool createdNew;
        try
        {
            // Existence is the lock: the mutex is kept open but never owned, so it can be released from any thread.
            mutex = new Mutex(initiallyOwned: false, name, out createdNew);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException)
        {
            // An elevated copy of the app, or another object squatting the name, holds it just the same.
            return null;
        }

        if (createdNew)
        {
            return new InstanceLock(mutex);
        }

        mutex.Dispose();
        return null;
    }

    public void Dispose() => _mutex.Dispose();
}
