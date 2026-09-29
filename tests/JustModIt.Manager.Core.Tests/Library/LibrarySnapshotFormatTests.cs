using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Core.Tests.Library;

public sealed class LibrarySnapshotFormatTests : IDisposable
{
    private static readonly InstalledGame Game = new()
    {
        Key = new GameKey(StoreId.Gog, "59534219748634025"),
        Name = "Café Racer",
        InstallDirectory = @"D:\GOG Games\Cafe Racer",
        LibraryId = "gog-registry",
        BuildId = "59534219748634025",
        State = GameInstallState.Updating,
        Reason = GameStateReason.BuildMismatch,
        StoreActivityAt = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero),
        Platform = OperatingSystemKind.Linux,
        Languages = ["pl-PL", "en"],
        IsStale = true,
    };

    private readonly TempDirectory _directory = new();

    [Fact]
    public async Task A_snapshot_survives_a_round_trip_and_comes_back_as_cached()
    {
        var snapshot = new LibrarySnapshot
        {
            Version = 7,
            Games = new ValueDictionary<GameKey, InstalledGame>(ImmutableDictionary<GameKey, InstalledGame>.Empty.Add(Game.Key, Game)),
            Stores =
            [
                new StoreStatus(StoreId.Gog, IsPresent: true, ScanFailed: true, IsComplete: false,
                [
                    new LibraryLocation("gog-registry", "gog-registry", null, LibraryState.Online),
                    new LibraryLocation(@"E:\Games", "gog-folder", @"E:\Games", LibraryState.Offline, IsManual: true, UnreadableEntries: 2),
                ]),
            ],
            ScannedAt = new DateTimeOffset(2026, 3, 2, 8, 0, 0, TimeSpan.Zero),
        };
        var store = Store();

        await store.SaveAsync(snapshot, TestContext.Current.CancellationToken);
        var loaded = await Store().LoadAsync(TestContext.Current.CancellationToken);

        loaded.Should().Be(snapshot with { Version = 0, IsCached = true });
    }

    [Fact]
    public void Entries_that_cannot_be_read_are_left_out_and_an_unknown_state_blocks_writing()
    {
        using var json = JsonDocument.Parse("""
            {
              "version": 1,
              "stores": [ { "store": "steam", "libraries": [ { "id": "x" }, { "id": "D:\\Library", "source": "steam-library", "state": "Sideways" } ] },
                          { "store": "" }, 42 ],
              "games": [
                { "key": "steam:10", "name": "Kept", "installDirectory": "D:\\Library\\Kept", "libraryId": "D:\\Library", "state": "Hovering" },
                { "key": "steam:11", "name": "Relative", "installDirectory": "Library\\Relative", "libraryId": "D:\\Library" },
                { "key": "no-separator", "name": "Broken", "installDirectory": "D:\\Broken", "libraryId": "D:\\Library" }
              ]
            }
            """);

        var snapshot = LibrarySnapshotFormat.Instance.Read(json.RootElement, 1);

        snapshot.IsCached.Should().BeTrue();
        snapshot.Stores.Should().Equal(new StoreStatus(StoreId.Steam, false, false, true,
            [new LibraryLocation(@"D:\Library", "steam-library", null, LibraryState.Online)]));
        snapshot.Games.Values.Should().ContainSingle().Which.State.Should().Be(GameInstallState.Unknown);
    }

    [Fact]
    public void The_file_lives_with_the_other_state()
    {
        var locations = new AppDataLocations(@"C:\Data", @"C:\Data\cache", @"C:\Data\logs");

        LibrarySnapshotFormat.FileIn(locations).Should().Be(Path.Combine(locations.StateDirectory, "library.json"));
    }

    public void Dispose() => _directory.Dispose();

    private JsonDocumentStore<LibrarySnapshot> Store() =>
        new(_directory.Combine("library.json"), LibrarySnapshotFormat.Instance, TimeProvider.System,
            NullLogger<JsonDocumentStore<LibrarySnapshot>>.Instance);
}
