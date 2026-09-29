using System.Collections.Concurrent;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Core.Tests.Library;

/// <summary>A store whose scans return what the test sets, counting how often it is scanned.</summary>
/// <param name="store">The store its scans report.</param>
internal sealed class FakeLibraryProvider(StoreId store) : ILibraryProvider
{
    private int _scans;

    public StoreId Store { get; } = store;

    public int Scans => Volatile.Read(ref _scans);

    public ConcurrentQueue<LibraryScanContext> Contexts { get; } = new();

    /// <summary>What the next scans find; a scan fails with <see cref="Failure"/> when that is set.</summary>
    public StoreScan Result { get; set; } = new() { Store = store, IsPresent = true };

    public Exception? Failure { get; set; }

    /// <summary>What a probe finds for a game; when null, the game as <see cref="Result"/> describes it.</summary>
    public Func<InstalledGame, InstalledGame?>? Probe { get; set; }

    public LibraryFolderEvidence Evidence { get; set; } = LibraryFolderEvidence.None;

    public TimeSpan InspectionDelay { get; set; }

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
        if (InspectionDelay > TimeSpan.Zero)
        {
            await Task.Delay(InspectionDelay, cancellationToken);
        }

        return Evidence;
    }

    public void Finds(params InstalledGame[] games) => Result = Result with { Games = [.. games] };
}
