using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests.TestSupport;

/// <summary>Fails a fake peer's next read, as a read fails while the peer leaves the bus or its state changes.</summary>
internal sealed class ReadRefusal
{
    private TaskCompletionSource? _next;

    /// <summary>Fails the next read.</summary>
    /// <returns>Completes once a read was refused.</returns>
    public Task RefuseNextAsync()
    {
        var next = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _next = next;
        return next.Task;
    }

    /// <summary>Throws the error the peer answers with, when this read is the one to fail.</summary>
    public void ThrowIfRefused()
    {
        if (Interlocked.Exchange(ref _next, null) is { } next)
        {
            next.SetResult();
            throw new DBusErrorReplyException("org.freedesktop.DBus.Error.NoReply", "The peer did not answer.");
        }
    }
}
