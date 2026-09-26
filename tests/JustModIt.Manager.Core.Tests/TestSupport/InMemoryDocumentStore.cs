using JustModIt.Manager.Core.IO;

namespace JustModIt.Manager.Core.Tests.TestSupport;

internal sealed class InMemoryDocumentStore<T>(T? stored = null) : IDocumentStore<T>
    where T : class
{
    private readonly Lock _gate = new();
    private readonly List<T> _saves = [];
    private int _failuresLeft;

    public T? Stored { get; private set; } = stored;

    public IReadOnlyList<T> Saves
    {
        get
        {
            lock (_gate)
            {
                return [.. _saves];
            }
        }
    }

    /// <summary>Makes the next saves fail as if the disk were full.</summary>
    /// <param name="count">How many saves fail.</param>
    public void FailNextSaves(int count) => Interlocked.Exchange(ref _failuresLeft, count);

    public Task<T?> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(Stored);

    public Task SaveAsync(T document, CancellationToken cancellationToken)
    {
        if (Interlocked.Decrement(ref _failuresLeft) >= 0)
        {
            return Task.FromException(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)));
        }

        lock (_gate)
        {
            _saves.Add(document);
            Stored = document;
        }

        return Task.CompletedTask;
    }
}
