using System.Runtime.Versioning;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library.Gog;
using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Platform.Windows.Tests;

[SupportedOSPlatform("windows")]
public sealed class SqliteTests : IDisposable
{
    // Galaxy's tables as GOG Galaxy 2.1 creates them, cut down to the columns the app reads and those they depend on.
    private const string GalaxySchema = """
        CREATE TABLE 'InstalledBaseProducts'('productId' INTEGER NOT NULL, 'generation' INTEGER NOT NULL,
            'languageId' INTEGER NOT NULL, 'installationPath' TEXT NOT NULL, 'installationId' INT64 NOT NULL,
            'buildId' INT64 NULL, 'branch' TEXT NULL, 'installationDate' TEXT NULL);
        INSERT INTO InstalledBaseProducts VALUES (1207658924, 2, 16, 'D:\GOG Galaxy\Example', 1, 55101909912871751, NULL, NULL);
        INSERT INTO InstalledBaseProducts VALUES (1001, 2, 16, 'D:\GOG Galaxy\Another', 2, NULL, NULL, NULL);
        """;

    private const string JobsSchema = """
        CREATE TABLE InstallProductJobs(jobId INT64 PRIMARY KEY, gameName VARCHAR, productId INT64, languageCode VARCHAR,
            desktopShortcut BOOLEAN, autoUpdateEnabled BOOLEAN, selectedBranch VARCHAR, installationPrefix VARCHAR,
            downloadSize INT64, downloadProgress INT64, overallDownloadProgress INT64);
        CREATE TABLE UpdateProductJobs(jobId INT64 PRIMARY KEY, gameName VARCHAR, productId INT64, languageCode VARCHAR,
            selectedBranch VARCHAR, buildId INT64, downloadSize INT64, patchingSize INT64, verificationSize INT64,
            downloadProgress INT64, patchingProgress INT64, verificationProgress INT64, overallDownloadProgress INT64,
            isVerifying BOOLEAN NOT NULL);
        INSERT INTO InstallProductJobs (jobId, gameName, productId) VALUES (1, 'New Game', 1002);
        INSERT INTO UpdateProductJobs (jobId, gameName, productId, isVerifying) VALUES (2, 'Example', 1207658924, 0);
        INSERT INTO UpdateProductJobs (jobId, gameName, productId, isVerifying) VALUES (3, 'Another', 1001, 1);
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    public SqliteTests()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The system's SQLite exists only on Windows.");
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void A_query_reads_every_value_as_text_and_leaves_the_database_as_it_was()
    {
        var path = Path.Join(_directory, "launcher.db");
        using (TestDatabase.Create(path, """
            PRAGMA journal_mode = WAL;
            CREATE TABLE games(id INTEGER, name TEXT, size INTEGER);
            INSERT INTO games VALUES (18446744073709, 'Ünïcødé ☃', NULL);
            """))
        {
        }

        var content = File.ReadAllBytes(path);

        var rows = WindowsSqliteReader.Instance.Query(path, "SELECT id, name, size FROM games");

        rows.Should().ContainSingle().Which.Should().Equal("18446744073709", "Ünïcødé ☃", null);
        File.ReadAllBytes(path).Should().Equal(content);
        new FileInfo(path + "-wal").Length.Should().Be(0, "a reader writes nothing into the log");
    }

    [Fact]
    public void A_missing_database_is_no_database_and_a_locked_or_foreign_one_fails()
    {
        var path = Path.Join(_directory, "launcher.db");
        using var writer = TestDatabase.Create(path, "CREATE TABLE games(id INTEGER); BEGIN EXCLUSIVE; INSERT INTO games VALUES (1);");

        WindowsSqliteReader.Instance.Query(Path.Join(_directory, "missing.db"), "SELECT 1").Should().BeNull();
        FluentActions.Invoking(() => WindowsSqliteReader.Instance.Query(path, "SELECT id FROM games")).Should().Throw<IOException>();
        writer.Execute("COMMIT");
        FluentActions.Invoking(() => WindowsSqliteReader.Instance.Query(path, "SELECT id FROM other")).Should().Throw<IOException>();
        WindowsSqliteReader.Instance.Query(path, "SELECT id FROM games").Should().ContainSingle();
    }

    [Fact]
    public void Galaxy_reports_the_games_it_installed_and_what_it_does_with_them()
    {
        var galaxy = new GalaxyDatabase(PhysicalFileSystem.Instance, WindowsSqliteReader.Instance, _directory);

        var withoutDatabase = galaxy.Read();
        using (TestDatabase.Create(Path.Join(_directory, "galaxy-2.0.db"), GalaxySchema))
        {
        }

        var withoutJobs = galaxy.Read();
        using (TestDatabase.Create(Path.Join(_directory, "jobs.db"), JobsSchema))
        {
        }

        var games = galaxy.Read();

        withoutDatabase.Should().Be(GalaxyGames.None);
        withoutJobs.Jobs.Should().BeEmpty();
        games.Installed.Should().Equal(("1207658924", @"D:\GOG Galaxy\Example"), ("1001", @"D:\GOG Galaxy\Another"));
        games.Jobs.Should().BeEquivalentTo(new Dictionary<string, GameInstallState>
        {
            ["1002"] = GameInstallState.Installing,
            ["1207658924"] = GameInstallState.Updating,
            ["1001"] = GameInstallState.Validating,
        });
    }
}
