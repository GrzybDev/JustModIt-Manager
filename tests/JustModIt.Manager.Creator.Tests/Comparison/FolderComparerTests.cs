using System.Diagnostics;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Comparison;

public sealed class FolderComparerTests : IDisposable
{
    private readonly Workspace _workspace = new();

    public void Dispose() => _workspace.Dispose();

    [Fact]
    public async Task Tells_new_changed_and_identical_files_apart()
    {
        _workspace.Game("data/lang/en.txt", "hello");
        _workspace.Game("data/strings.bin", "0123456789");
        _workspace.Game("data/fonts.bin", "abcdefghij");
        _workspace.Game("readme.txt", "same");
        _workspace.Author("data/lang/xx.txt", "new text");
        _workspace.Author("data/lang/en.txt", "hello, longer");
        _workspace.Author("data/strings.bin", "9876543210");
        _workspace.Author("readme.txt", "same");

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Select(file => (file.Path, file.Difference)).Should().Equal(
            ("data/lang/en.txt", FileDifference.Changed),
            ("data/lang/xx.txt", FileDifference.New),
            ("data/strings.bin", FileDifference.Changed),
            ("readme.txt", FileDifference.Identical));
        comparison.Files[1].Game.Should().BeNull();
        comparison.Files[3].CopyOf.Should().Be("readme.txt");
        comparison.Game.FileCount.Should().Be(4);
    }

    [Fact]
    public async Task Spells_paths_like_the_game()
    {
        _workspace.Game("Data/Lang/Strings.txt", "original");
        _workspace.Author("data/lang/strings.txt", "customized");
        _workspace.Author("data/lang/extra/notes.txt", "notes");

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Select(file => (file.Path, file.AuthorPath)).Should().Equal(
            ("Data/Lang/extra/notes.txt", "data/lang/extra/notes.txt"),
            ("Data/Lang/Strings.txt", "data/lang/strings.txt"));
        comparison.FindAuthorFile("DATA/LANG/STRINGS.TXT")!.Difference.Should().Be(FileDifference.Changed);
    }

    [Fact]
    public async Task A_file_where_the_game_has_a_folder_is_new()
    {
        _workspace.Game("movies/intro.bik", "video");
        _workspace.Author("movies", "not a folder");

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Should().ContainSingle().Which.Difference.Should().Be(FileDifference.New);
    }

    [Fact]
    public async Task Leaves_out_hidden_files_of_the_author_and_the_apps_and_stores_folders_in_the_game()
    {
        var original = Workspace.Bytes(4096, 1);
        _workspace.Game($"{Workspace.HiddenFolderName}/backups/data.bin", original);
        _workspace.Game(".egstore/Pending/data.bin", original);
        _workspace.Author("data.bin", original);
        var hidden = _workspace.Author(".notes/todo.txt", "todo");
        File.SetAttributes(Path.GetDirectoryName(hidden)!, FileAttributes.Directory | FileAttributes.Hidden);

        var comparison = await _workspace.CompareAsync();

        var file = comparison.Files.Should().ContainSingle().Subject;
        file.Path.Should().Be("data.bin");
        file.CopyOf.Should().BeNull();
        comparison.Game.FileCount.Should().Be(0);
    }

    [Fact]
    public async Task Leaves_out_links_in_the_game_and_the_authors_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The links are junctions.");
        var outside = Directory.CreateDirectory(Path.Combine(_workspace.Root, "outside")).FullName;
        File.WriteAllText(Path.Combine(outside, "data.bin"), "outside");
        File.WriteAllText(Path.Combine(outside, "new.txt"), "outside");
        _workspace.Game("data/real.bin", "original");
        _workspace.Author("data/real.bin", "changed");
        CreateJunction(_workspace.GamePath("linked"), outside);
        CreateJunction(_workspace.GamePath("data/linked"), outside);
        CreateJunction(_workspace.AuthorFile("linked"), outside);

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Select(file => file.Path).Should().Equal("data/real.bin");
        comparison.Game.FileCount.Should().Be(1);
        File.WriteAllText(Path.Combine(outside, "data.bin"), "changed outside");
        File.WriteAllText(Path.Combine(outside, "new.txt"), "changed outside");
        (await new FolderComparer(Workspace.Options).HasChangedAsync(comparison, TestContext.Current.CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task Prefers_a_source_with_the_same_name_then_in_the_same_folder()
    {
        var video = Workspace.Bytes(4096, 1);
        _workspace.Game("movies/intro.bik", video);
        _workspace.Game("backup/intro.bik", video);
        _workspace.Game("extras/credits.bik", video);
        _workspace.Author("movies/intro_xx.bik", video);
        _workspace.Author("videos/credits.bik", video);

        var comparison = await _workspace.CompareAsync();

        comparison.FindAuthorFile("movies/intro_xx.bik")!.RenameFrom.Should().Be("movies/intro.bik");
        comparison.FindAuthorFile("videos/credits.bik")!.RenameFrom.Should().Be("extras/credits.bik");
    }

    [Fact]
    public async Task Proposes_each_game_file_for_one_rename_only()
    {
        var content = Workspace.Bytes(4096, 1);
        _workspace.Game("data/pack.bin", content);
        _workspace.Author("data/pack_old.bin", content);
        _workspace.Author("other/pack.bin", content);

        var comparison = await _workspace.CompareAsync();

        comparison.FindAuthorFile("other/pack.bin")!.RenameFrom.Should().Be("data/pack.bin");
        comparison.FindAuthorFile("data/pack_old.bin")!.RenameFrom.Should().BeNull();
        comparison.FindAuthorFile("data/pack_old.bin")!.CopyOf.Should().Be("data/pack.bin");
    }

    [Fact]
    public async Task A_copy_of_a_file_the_author_keeps_unchanged_is_no_rename()
    {
        var content = Workspace.Bytes(4096, 1);
        _workspace.Game("data/pack.bin", content);
        _workspace.Author("data/pack.bin", content);
        _workspace.Author("data/pack_copy.bin", content);

        var comparison = await _workspace.CompareAsync();

        var copy = comparison.FindAuthorFile("data/pack_copy.bin")!;
        copy.CopyOf.Should().Be("data/pack.bin");
        copy.RenameFrom.Should().BeNull();
    }

    [Fact]
    public async Task Ignores_small_files_that_match_by_chance()
    {
        _workspace.Game("config/default.ini", "[x]");
        _workspace.Author("config/mine.ini", "[x]");

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Should().ContainSingle().Which.CopyOf.Should().BeNull();
    }

    [Fact]
    public async Task Lists_files_removed_from_a_folder_the_author_copied_whole()
    {
        _workspace.Game("movies/a.bik", "a");
        _workspace.Game("movies/b.bik", "b");
        _workspace.Game("movies/c.bik", "c");
        _workspace.Game("movies/d.bik", "d");
        _workspace.Game("data/one.bin", "1");
        _workspace.Game("data/two.bin", "2");
        _workspace.Author("movies/a.bik", "a");
        _workspace.Author("movies/b.bik", "b, customized");
        _workspace.Author("movies/c.bik", "c");
        _workspace.Author("data/one.bin", "1, customized");

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Where(file => file.Difference == FileDifference.Missing).Select(file => file.Path)
            .Should().Equal("movies/d.bik");
    }

    [Fact]
    public async Task Points_out_a_file_the_author_put_at_the_top_instead_of_its_game_folder()
    {
        var video = Workspace.Bytes(4096, 1);
        _workspace.Game("data/lang/strings.txt", "original");
        _workspace.Game("movies/intro.bik", video);
        _workspace.Game("a/config.ini", "a");
        _workspace.Game("b/config.ini", "b");
        _workspace.Author("strings.txt", "customized");
        _workspace.Author("intro.bik", video);
        _workspace.Author("config.ini", "mine");
        _workspace.Author("extra/strings.txt", "nested");

        var comparison = await _workspace.CompareAsync();

        comparison.FindAuthorFile("strings.txt")!.ExpectedFolder.Should().Be("data/lang");
        comparison.FindAuthorFile("intro.bik")!.Should().Match<ComparedFile>(file =>
            file.ExpectedFolder == "movies" && file.RenameFrom == null && file.CopyOf == "movies/intro.bik");
        comparison.FindAuthorFile("config.ini")!.ExpectedFolder.Should().BeNull();
        comparison.FindAuthorFile("extra/strings.txt")!.ExpectedFolder.Should().BeNull();
    }

    [Fact]
    public async Task Tells_when_the_files_changed_since_the_comparison()
    {
        _workspace.Game("data/strings.bin", "original");
        _workspace.Author("data/strings.bin", "customized");
        _workspace.Author("data/new.txt", "new");
        var comparer = new FolderComparer(Workspace.Options);
        var token = TestContext.Current.CancellationToken;
        var comparison = await _workspace.CompareAsync();

        (await comparer.HasChangedAsync(comparison, token)).Should().BeFalse();
        _workspace.Author("data/other.txt", "added");
        (await comparer.HasChangedAsync(comparison, token)).Should().BeTrue();
        File.Delete(_workspace.AuthorFile("data/other.txt"));
        File.Delete(_workspace.AuthorFile("data/new.txt"));
        (await comparer.HasChangedAsync(comparison, token)).Should().BeTrue();
        _workspace.Author("data/new.txt", "new, edited");
        (await comparer.HasChangedAsync(comparison, token)).Should().BeTrue();

        comparison = await _workspace.CompareAsync();
        _workspace.Game("data/strings.bin", "updated by the store");
        (await comparer.HasChangedAsync(comparison, token)).Should().BeTrue();
    }

    [Fact]
    public async Task Refuses_missing_and_nested_folders()
    {
        Directory.Delete(_workspace.AuthorDirectory);
        var missing = () => _workspace.CompareAsync();
        (await missing.Should().ThrowAsync<CreatorBlockedException>()).Which.Blocks.Should()
            .ContainSingle().Which.Reason.Should().Be(CreatorBlockReason.AuthorFolderMissing);

        var nested = () => new FolderComparer(Workspace.Options).CompareAsync(
            new ComparisonRequest(_workspace.GameDirectory, Directory.CreateDirectory(_workspace.GamePath("mine")).FullName, []),
            progress: null, TestContext.Current.CancellationToken);
        (await nested.Should().ThrowAsync<CreatorBlockedException>()).Which.Blocks.Should()
            .ContainSingle().Which.Reason.Should().Be(CreatorBlockReason.FoldersOverlap);
    }

    [Fact]
    public async Task Refuses_a_game_that_holds_an_install()
    {
        _workspace.Author("data.txt", "text");
        var install = new ExistingInstall(Guid.NewGuid(), _workspace.GameDirectory);
        var elsewhere = new ExistingInstall(Guid.NewGuid(), Path.Combine(_workspace.Root, "other-game"));

        var compare = () => _workspace.CompareAsync([install, elsewhere]);

        var block = (await compare.Should().ThrowAsync<CreatorBlockedException>()).Which.Blocks.Should().ContainSingle().Subject;
        block.Reason.Should().Be(CreatorBlockReason.GameHasInstall);
        block.InstallId.Should().Be(install.RecordId);
    }

    [Fact]
    public async Task Refuses_a_game_another_app_manages_but_not_one_this_app_marked()
    {
        _workspace.Author("data.txt", "text");
        _workspace.MarkGameFolder(".other-app", "OtherApp");

        var compare = () => _workspace.CompareAsync();

        var block = (await compare.Should().ThrowAsync<CreatorBlockedException>()).Which.Blocks.Should().ContainSingle().Subject;
        block.Reason.Should().Be(CreatorBlockReason.GameManagedByOtherApp);
        block.App.Should().Be("OtherApp");
        Path.GetFileName(block.Path).Should().Be(".other-app");

        Directory.Delete(_workspace.GamePath(".other-app"), recursive: true);
        _workspace.MarkGameFolder(Workspace.HiddenFolderName, Workspace.InstanceName);
        (await _workspace.CompareAsync()).Game.FileCount.Should().Be(0, "the app's own folder holds no game files");
    }

    [Fact]
    public async Task Reports_progress_up_to_every_byte_hashed()
    {
        var content = Workspace.Bytes(4096, 1);
        _workspace.Game("same.bin", content);
        _workspace.Game("source.bin", Workspace.Bytes(2048, 2));
        _workspace.Author("same.bin", content);
        _workspace.Author("moved.bin", Workspace.Bytes(2048, 2));
        var reports = new List<CompareProgress>();

        await _workspace.CompareAsync(progress: new Core.InlineProgress<CompareProgress>(reports.Add));

        reports.Select(report => report.BytesDone).Should().BeInAscendingOrder();
        reports[^1].BytesDone.Should().Be(reports[^1].BytesTotal);
        reports[^1].BytesTotal.Should().Be((2 * 4096) + (2 * 2048));
        reports[^1].FilesDone.Should().Be(2);
    }

    [Fact]
    public async Task Skips_a_game_file_it_cannot_read_while_searching_for_copies()
    {
        var content = Workspace.Bytes(4096, 1);
        var locked = _workspace.Game("data/locked.bin", content);
        _workspace.Author("data/copy.bin", content);
        await using var holder = new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var comparison = await _workspace.CompareAsync();

        comparison.Files.Should().ContainSingle().Which.CopyOf.Should().BeNull();
    }

    [Fact]
    public async Task Stops_hashing_when_the_author_cancels()
    {
        _workspace.Game("data/pack.bin", Workspace.Bytes(3 * 1024 * 1024, 1));
        _workspace.Author("data/pack.bin", Workspace.Bytes(3 * 1024 * 1024, 2));
        using var cancellation = new CancellationTokenSource();
        var reports = new List<CompareProgress>();
        var progress = new Core.InlineProgress<CompareProgress>(report =>
        {
            reports.Add(report);
            if (report.BytesDone > 0)
            {
                cancellation.Cancel();
            }
        });

        var compare = () => _workspace.CompareAsync(progress: progress, cancellationToken: cancellation.Token);

        await compare.Should().ThrowAsync<OperationCanceledException>();
        reports[^1].BytesDone.Should().BeLessThan(reports[^1].BytesTotal);
    }

    private static void CreateJunction(string link, string target)
    {
        using var process = Process.Start(new ProcessStartInfo("cmd.exe") { ArgumentList = { "/c", "mklink", "/J", Path.GetFullPath(link), target },
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true })!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.SkipWhen(process.ExitCode != 0, "Junctions cannot be created on this machine.");
    }
}
