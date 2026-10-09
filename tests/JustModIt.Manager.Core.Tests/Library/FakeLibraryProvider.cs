using System.Collections.Concurrent;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.Tests.Library;

/// <summary>A store whose scans return what the test sets, counting how often it is scanned.</summary>
/// <param name="store">The store its scans report.</param>
internal sealed class FakeLibraryProvider(StoreId store) : ILibraryProvider, IDisposable
{
    private readonly TaskCompletionSource _hang = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _scans;
    private int _inspections;

    public StoreId Store { get; } = store;

    public int Scans => Volatile.Read(ref _scans);

    public ConcurrentQueue<LibraryScanContext> Contexts { get; } = new();

    /// <summary>What the next scans find; a scan fails with <see cref="Failure"/> when that is set.</summary>
    public StoreScan Result { get; set; } = new() { Store = store, IsPresent = true };

    public Exception? Failure { get; set; }

    /// <summary>What a probe finds for a game; when null, the game as <see cref="Result"/> describes it.</summary>
    public Func<InstalledGame, InstalledGame?>? Probe { get; set; }

    public LibraryFolderEvidence Evidence { get; set; } = LibraryFolderEvidence.None;

    /// <summary>Whether looking into a folder hangs until disposal, as on a drive that does not answer.</summary>
    public bool InspectionHangs { get; set; }

    public int Inspections => Volatile.Read(ref _inspections);

    public Task<StoreScan> ScanAsync(LibraryScanContext context, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _scans);
        Contexts.Enqueue(context);
        return Failure is { } failure ? Task.FromException<StoreScan>(failure) : Task.FromResult(Result);
    }

    public Task<InstalledGame?> ProbeAsync(InstalledGame game, LibraryScanContext context, CancellationToken cancellationToken) =>
        Task.FromResult(Probe is { } probe ? probe(game) : Result.Describe(game));

    public async Task<LibraryFolderEvidence> InspectFolderAsync(string path, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _inspections);
        if (InspectionHangs)
        {
            await _hang.Task;
        }

        return Evidence;
    }

    public void Finds(params InstalledGame[] games) => Result = Result with { Games = [.. games] };

    public void Dispose() => _hang.TrySetResult();
}
