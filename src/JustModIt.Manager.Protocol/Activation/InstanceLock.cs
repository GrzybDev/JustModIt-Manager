namespace JustModIt.Manager.Protocol.Activation;

/// <summary>Held for the whole life of a role; while it exists, later launches know the role is taken.</summary>
public sealed class InstanceLock : IDisposable
{
    private readonly IDisposable _held;

    private InstanceLock(IDisposable held) => _held = held;

    /// <summary>Takes the named lock, or returns null when another holder has it.</summary>
    /// <param name="name">
    /// One of the locks in <see cref="InstanceNames"/>: a mutex's name on Windows, a lock file's path elsewhere.
    /// </param>
    public static InstanceLock? TryAcquire(string name) =>
        OperatingSystem.IsWindows() ? TryAcquireMutex(name) : TryAcquireFile(name);

    /// <summary>Tells whether another holder has the named lock, without keeping it.</summary>
    /// <param name="name">One of the locks in <see cref="InstanceNames"/>.</param>
    public static bool IsHeld(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (!Mutex.TryOpenExisting(name, out var mutex))
                {
                    return false;
                }

                mutex.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }

        if (!File.Exists(name))
        {
            return false;
        }

        using var probe = TryAcquireFile(name);
        return probe is null;
    }

    public void Dispose() => _held.Dispose();

    private static InstanceLock? TryAcquireMutex(string name)
    {
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

    private static InstanceLock? TryAcquireFile(string path)
    {
        try
        {
            // A NativeAOT build's named mutexes reach no other process off Windows, so the lock is the file's own lock,
            // which the system drops however its holder ends. The file stays: removing it would race the next holder.
            return new InstanceLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another process holds the file, or a copy run as another user (through sudo) made it.
            return null;
        }
    }
}
