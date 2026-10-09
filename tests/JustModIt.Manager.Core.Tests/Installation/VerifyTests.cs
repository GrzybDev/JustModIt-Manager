using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class VerifyTests : IAsyncDisposable
{
    private readonly InstallHarness _harness = new();

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task Tells_modified_missing_and_restored_files_apart()
    {
        var result = await InstallAsync();
        _harness.WriteGameFile("readme.txt", "Updated");
        File.Delete(_harness.GameFile("added.txt"));
        _harness.WriteGameFile("bin/library.dll", "original library");
        _harness.WriteGameFile("movies/intro.bik", "a new video");

        var verification = await VerifyAsync(result);

        verification.IsIntact.Should().BeFalse();
        verification.Files.Select(file => (file.Path, file.Integrity)).Should().BeEquivalentTo([
            ("readme.txt", FileIntegrity.Modified),
            ("added.txt", FileIntegrity.Missing),
            ("bin/library.dll", FileIntegrity.OriginalRestored),
            ("movies/intro.bik", FileIntegrity.Modified)]);
    }

    [Fact]
    public async Task Finds_a_file_rewritten_with_the_same_content_intact()
    {
        var result = await InstallAsync();
        _harness.WriteGameFile("readme.txt", "Changed");

        (await VerifyAsync(result)).IsIntact.Should().BeTrue();
    }

    [Fact]
    public async Task Verifies_untouched_files_without_reading_them()
    {
        var result = await InstallAsync();
        var reports = new List<InstallProgress>();

        await _harness.Engine.VerifyAsync(result.Record.Id, new InlineProgress<InstallProgress>(reports.Add), InstallHarness.Token);

        reports.Should().OnlyContain(report => report.Stage == InstallStage.Verifying && report.BytesDone == 0);
    }

    private Task<InstallResult> InstallAsync() => _harness.InstallAsync(_harness.ManifestPackage(new TestPackage()
        .Copy("readme.txt", "Changed")
        .Copy("added.txt", "Added")
        .Copy("bin/library.dll", "proxy")
        .Delete("movies/intro.bik")));

    private Task<VerificationResult> VerifyAsync(InstallResult result) =>
        _harness.Engine.VerifyAsync(result.Record.Id, null, InstallHarness.Token);
}
