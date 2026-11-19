using System.Text.Json;
using System.Text.Json.Nodes;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Installation;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Installation;

public sealed class LaunchOptionsRecordTests : IAsyncDisposable
{
    private static readonly InstalledLaunchOptions Options = new(["-examplelog", "-windowed"]) { Added = ["-examplelog"] };

    private readonly InstallHarness _harness = new();

    private static CancellationToken Token => InstallHarness.Token;

    public ValueTask DisposeAsync() => _harness.DisposeAsync();

    [Fact]
    public async Task An_install_keeps_the_launch_options_in_its_record()
    {
        var package = _harness.Zip(new TestPackage().Entry("readme.txt", "Translated read me"));

        await _harness.Engine.InstallAsync(_harness.Request(package) with { LaunchOptions = Options }, null, Token);

        (await _harness.Engine.LoadRecordsAsync(Token)).Should().ContainSingle().Which.LaunchOptions.Should().Be(Options);
    }

    [Fact]
    public async Task The_record_keeps_options_the_app_added_later_unless_the_install_is_gone_or_waits_for_recovery()
    {
        var package = _harness.Zip(new TestPackage().Entry("readme.txt", "Translated read me"));
        var needed = new InstalledLaunchOptions(Options.Required);
        var installed = (await _harness.Engine.InstallAsync(_harness.Request(package) with { LaunchOptions = needed }, null, Token)).Record;

        (await _harness.Engine.SetLaunchOptionsAsync(installed.Id, Options, Token))!.LaunchOptions.Should().Be(Options);

        (await _harness.Engine.LoadRecordsAsync(Token)).Should().ContainSingle().Which.LaunchOptions.Should().Be(Options);
        var journal = Path.Join(_harness.InstallsDirectory, installed.Id.ToString("N"), "journal.jsonl");
        await File.WriteAllTextAsync(journal, "", Token);
        await _harness.Engine.Invoking(engine => engine.SetLaunchOptionsAsync(installed.Id, needed, Token)).Should()
            .ThrowAsync<InstallBlockedException>();
        File.Delete(journal);
        await _harness.UninstallAsync(installed.Id);
        (await _harness.Engine.SetLaunchOptionsAsync(installed.Id, Options, Token)).Should().BeNull();
    }

    [Fact]
    public async Task Launch_options_round_trip_and_a_record_without_them_stays_as_it_was()
    {
        var saved = await File.ReadAllBytesAsync(Path.Join(AppContext.BaseDirectory, "Installation", "Fixtures", "v1", "AppData", "installs",
            "5c43ae62d789404da0523d46ec29bcf6", "record.json"), Token);
        var context = InstallationJsonContext.Default.InstallRecord;
        var record = JsonSerializer.Deserialize(saved, context)!;
        var refused = new InstalledLaunchOptions([]) { Refusal = LaunchOptionsRefusal.UnsafeOption };

        var again = JsonSerializer.SerializeToUtf8Bytes(record, context);

        JsonNode.DeepEquals(JsonNode.Parse(again), JsonNode.Parse(saved)).Should().BeTrue();
        record.LaunchOptions.Should().BeNull();
        foreach (var options in new[] { Options, refused })
        {
            JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(record with { LaunchOptions = options }, context), context)!
                .LaunchOptions.Should().Be(options);
        }
    }
}
