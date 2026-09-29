using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class TransactionJournalTests : IDisposable
{
    private static readonly TransactionStarted Header = new(TransactionStarted.CurrentFormatVersion, TransactionKind.Install,
        Guid.Parse("11111111-2222-3333-4444-555555555555"), @"D:\Games\Example", @"D:\Games\Example\.example-app\staging\1",
        @"D:\Games\Example\.example-app\backups\1", "VOLUME-1");

    private readonly TempDirectory _temp = new();

    private string Path => _temp.Combine("journal.jsonl");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Round_trips_every_entry_one_compact_line_each()
    {
        JournalEntry[] entries =
        [
            new DirectoryCreated("data/lang"),
            new FileBackedUp("bin/library.dll", "0", new FileSnapshot(16, new string('a', 64),
                new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc)), Moved: true, FileAttributes.ReadOnly | FileAttributes.Hidden),
            new FileBackedUp("readme.txt", "1", new FileSnapshot(7, new string('b', 64)), Moved: false),
            new FileWasAbsent("data/lang/text.txt"),
            new FileWritten("data/lang/text.txt", new FileSnapshot(4, new string('c', 64))),
            new BackupRelocated("0", Moved: true),
        ];
        using (var journal = TransactionJournal.Create(Path, Header with
               {
                   TargetBackup = new BackupFolder(BackupLocation.AppData, @"C:\Data\backups\steam-1\1"),
               }))
        {
            foreach (var entry in entries)
            {
                journal.Add(entry);
            }

            journal.Commit();
        }

        var read = TransactionJournal.Read(Path);

        read.Should().Equal([Header with { TargetBackup = new BackupFolder(BackupLocation.AppData, @"C:\Data\backups\steam-1\1") },
            .. entries]);
        File.ReadAllLines(Path).Should().HaveCount(entries.Length + 1).And.OnlyContain(line => line.StartsWith("{\"type\":\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Leaves_out_what_is_not_set_and_still_reads_it_back()
    {
        using (TransactionJournal.Create(Path, Header with { GameVolume = null }))
        {
        }

        File.ReadAllText(Path).Should().NotContain("gameVolume").And.NotContain("targetBackup");
        TransactionJournal.ReadHeader(Path).Should().Be(Header with { GameVolume = null });
    }

    [Fact]
    public void Ignores_a_line_cut_short()
    {
        using (var journal = TransactionJournal.Create(Path, Header))
        {
            journal.Add(new FileWasAbsent("a.txt"));
            journal.Commit();
        }

        File.AppendAllText(Path, """{"type":"absent","pa""");

        TransactionJournal.Read(Path).Should().Equal(Header, new FileWasAbsent("a.txt"));
    }

    [Theory]
    [InlineData("\0\0garbage")]
    [InlineData("""{"size":16,"sha256":"0123"}""")]
    [InlineData("""{"type":"unknown"}""")]
    public void Drops_a_damaged_last_line_but_refuses_damage_further_up(string damage)
    {
        using (var journal = TransactionJournal.Create(Path, Header))
        {
            journal.Add(new FileWasAbsent("a.txt"));
            journal.Commit();
        }

        File.AppendAllText(Path, damage + "\n");
        TransactionJournal.Read(Path).Should().Equal(Header, new FileWasAbsent("a.txt"));

        File.AppendAllText(Path, """{"type":"absent","path":"b.txt"}""" + "\n");
        var read = () => TransactionJournal.Read(Path);
        read.Should().Throw<InvalidDataException>();
        TransactionJournal.ReadHeader(Path).Should().Be(Header, "the header still tells whose journal it is");
    }

    [Fact]
    public void Refuses_a_header_written_by_a_newer_app()
    {
        using (TransactionJournal.Create(Path, Header with { FormatVersion = 2 }))
        {
        }

        var read = () => TransactionJournal.ReadHeader(Path);

        read.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Tells_that_an_empty_journal_never_described_a_change()
    {
        File.WriteAllText(Path, "");

        TransactionJournal.ReadHeader(Path).Should().BeNull();
    }
}
