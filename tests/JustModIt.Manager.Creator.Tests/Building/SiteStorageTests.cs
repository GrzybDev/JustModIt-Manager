using System.IO.Compression;
using JustModIt.Manager.Creator.Building;
using JustModIt.Manager.Creator.Tests.TestSupport;

namespace JustModIt.Manager.Creator.Tests.Building;

public sealed class SiteStorageTests : IDisposable
{
    private readonly Workspace _work = new();

    public void Dispose() => _work.Dispose();

    [Fact]
    public void Counts_an_entry_shared_by_two_packages_once()
    {
        var subtitles = Workspace.Bytes(8192, 41);
        var steam = Package("steam.zip", ("files/data/subtitles.bin", subtitles), ("patches/data/strings.bin", Workspace.Bytes(512, 42)));
        var gog = Package("gog.zip", ("files/data/subtitles.bin", subtitles), ("patches/data/strings.bin", Workspace.Bytes(640, 43)));
        // An add-on is a package of its own on the site, even with a file its base has.
        var voices = Package("voices.zip", ("files/data/subtitles.bin", subtitles));

        var (stored, total) = SiteStorage.Estimate([("", steam), ("", gog), ("Voices", voices)]);

        long Size(string path) => new FileInfo(path).Length;
        using var other = ZipFile.OpenRead(gog);
        total.Should().Be(Size(steam) + Size(gog) + Size(voices));
        stored.Should().Be(Size(steam) + other.GetEntry("patches/data/strings.bin")!.CompressedLength + Size(voices));
    }

    private string Package(string name, params (string Path, byte[] Content)[] entries)
    {
        var path = _work.Output(name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, content) in entries)
        {
            using var stream = zip.CreateEntry(entry).Open();
            stream.Write(content);
        }

        return path;
    }
}
