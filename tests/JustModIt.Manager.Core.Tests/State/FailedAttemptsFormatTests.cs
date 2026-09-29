using System.Collections.Immutable;
using System.Text.Json;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Collections;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;
using JustModIt.Manager.Core.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using static JustModIt.Manager.Core.Tests.TestSupport.TestProjects;

namespace JustModIt.Manager.Core.Tests.State;

public sealed class FailedAttemptsFormatTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task Failures_survive_a_round_trip_with_every_detail()
    {
        var failures = new FailedAttempts(new ValueDictionary<GameKey, FailedAttempt>(ImmutableDictionary<GameKey, FailedAttempt>.Empty
            .Add(Game, new FailedAttempt(JobKind.Update, JobOrigin.Automatic, Now, new JobFailure(JobFailureReason.FilesLocked)
            {
                Files = ["data/strings.pak"],
                Processes = ["Viewer"],
                GameState = GameInstallState.Installed,
                StateReason = GameStateReason.None,
                Unavailability = SelectionUnavailability.OtherSystems,
                CatalogueError = CatalogueErrorReason.Timeout,
                RequiredBytes = 2048,
                AvailableBytes = 1024,
                Location = Path.Join("D:", "Games"),
                OtherApp = "OtherApp",
            })
            {
                LostPrevious = true,
                ReleaseId = ReleaseTwo,
                PackageFileId = FileTwo,
            })
            .Add(OtherGame, new FailedAttempt(JobKind.Install, JobOrigin.User, Now.AddHours(-2), null))));

        await Store().SaveAsync(failures, TestContext.Current.CancellationToken);

        (await Store().LoadAsync(TestContext.Current.CancellationToken)).Should().Be(failures);
    }

    [Fact]
    public void Entries_that_cannot_be_read_are_left_out()
    {
        using var json = JsonDocument.Parse("""
            {
              "games": [
                { "game": "steam:1000", "kind": "Install", "at": "2026-09-23T12:00:00+00:00", "failure": { "reason": "Offline" } },
                { "game": "not a game", "kind": "Install", "at": "2026-09-23T12:00:00+00:00" },
                { "game": "steam:2000", "kind": "Explode", "at": "2026-09-23T12:00:00+00:00" },
                { "game": "steam:3000", "kind": "Update" },
                "text"
              ]
            }
            """);

        var failures = FailedAttemptsFormat.Instance.Read(json.RootElement, 1);

        failures.Games.Should().ContainSingle().Which.Value.Failure!.Reason.Should().Be(JobFailureReason.Offline);
    }

    private JsonDocumentStore<FailedAttempts> Store() =>
        new(_directory.Combine("failures.json"), FailedAttemptsFormat.Instance, TimeProvider.System, NullLogger.Instance);
}
