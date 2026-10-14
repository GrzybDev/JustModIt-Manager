using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class ReplacementPredictionTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Fits_a_package_made_for_the_originals_while_another_version_is_installed()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var installed = await InstallAsync(new TestPackage().Patch("data/strings.pak", patch).Copy("readme.txt", "Changed"));
        var installedFiles = _harness.Snapshot();

        var report = await PredictAsync(installed, new TestPackage().Patch("data/strings.pak", patch).Copy("readme.txt", "Newer"));

        report.CanProceed.Should().BeTrue();
        _harness.Snapshot().Should().BeEquivalentTo(installedFiles, "a prediction changes nothing");
    }

    [Fact]
    public async Task Does_not_fit_a_patch_for_another_version_of_an_untouched_file()
    {
        var installed = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));
        var other = await TestPatch.CreateAsync(TestPatch.RandomBytes(1000, seed: 3), TestPatch.RandomBytes(1000, seed: 4));

        var report = await PredictAsync(installed, new TestPackage().Patch("data/strings.pak", other));

        report.Issues.Should().Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: false));
    }

    [Fact]
    public async Task Takes_a_file_the_store_changed_as_it_is_now()
    {
        var patch = await TestPatch.CreateAsync(InstallHarness.OriginalPak, InstallHarness.ChangedPak);
        var installed = await InstallAsync(new TestPackage().Patch("data/strings.pak", patch));
        _harness.WriteGameFile("data/strings.pak", TestPatch.RandomBytes(1000, seed: 9));

        var report = await PredictAsync(installed, new TestPackage().Patch("data/strings.pak", patch));

        report.Issues.Should().Equal(new FileMismatchIssue("data/strings.pak", AlreadyPatched: false));
    }

    [Fact]
    public async Task Does_not_fit_a_rename_of_a_file_the_uninstall_removes()
    {
        var installed = await InstallAsync(new TestPackage().Copy("bin/added.dll", "added"));

        var report = await PredictAsync(installed, new TestPackage().Rename("bin/added.dll", "bin/renamed.dll"));

        report.Issues.Should().Equal(new MissingFileIssue("bin/added.dll"));
    }

    [Fact]
    public async Task Lets_a_file_the_uninstall_removes_make_way_for_a_folder()
    {
        var installed = await InstallAsync(new TestPackage().Copy("data/lang", "a file"));

        var report = await PredictAsync(installed, new TestPackage().Copy("data/lang/text.txt", "a folder now"));

        report.CanProceed.Should().BeTrue();
    }

    [Fact]
    public async Task A_replacement_sees_handed_over_paths_as_they_are()
    {
        RuntimeFileTests.WithList(_harness);
        var installed = await InstallAsync(RuntimeFileTests.Software());
        RuntimeFileTests.Play(_harness);

        var declaring = await PredictAsync(installed, new TestPackage().Rename("tool/list_original.dat", "tool/list_saved.dat")
            .Declare("tool/list_original.dat"));
        var notDeclaring = await PredictAsync(installed, new TestPackage().Rename("tool/list_original.dat", "tool/list_saved.dat"));

        declaring.CanProceed.Should().BeTrue("the copy the software made goes over to the new release as it is");
        notDeclaring.Issues.Should().Equal(new MissingFileIssue("tool/list_original.dat"));
    }

    [Fact]
    public async Task Refuses_a_broken_package_before_anything_else()
    {
        var installed = await InstallAsync(new TestPackage().Copy("readme.txt", "Changed"));

        var predict = () => _harness.Engine.PredictReplacementAsync(installed.Record.Id,
            _harness.Request(_harness.Zip(new TestPackage().Entry("../evil.dll", "evil"))), null, InstallHarness.Token);

        (await predict.Should().ThrowAsync<InvalidPackageException>()).Which.Error.Should().Be(PackageError.UnsafeEntryName);
    }

    private Task<InstallResult> InstallAsync(TestPackage package) => _harness.InstallAsync(_harness.ManifestPackage(package));

    private Task<PreflightReport> PredictAsync(InstallResult installed, TestPackage replacement) =>
        _harness.Engine.PredictReplacementAsync(installed.Record.Id, _harness.Request(_harness.ManifestPackage(replacement)), null,
            InstallHarness.Token);
}
