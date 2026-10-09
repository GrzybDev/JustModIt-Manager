using System.Runtime.Versioning;
using JustModIt.Manager.Core.Interop;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class PathCanonicalizerTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    [Fact]
    public void An_existing_folder_is_canonical_already()
    {
        var folder = _directory.CreateDirectory("Games");

        var canonical = PathCanonicalizer.Canonicalize(folder);

        PathCanonicalizer.Canonicalize(canonical).Should().Be(canonical);
        PathComparison.AreSame(canonical, folder).Should().BeTrue();
    }

    [Fact]
    public void A_junction_or_link_resolves_to_its_target()
    {
        var target = _directory.CreateDirectory(Path.Combine("Library", "Games"));
        Directory.CreateDirectory(Path.Combine(target, "Witcher"));
        var link = _directory.CreateDirectoryLink("Shortcut", target);

        var canonical = PathCanonicalizer.Canonicalize(Path.Combine(link, "Witcher"));

        canonical.Should().Be(Path.Join(PathCanonicalizer.Canonicalize(target), "Witcher"));
    }

    [Fact]
    public void A_missing_tail_is_kept_as_given()
    {
        var existing = _directory.CreateDirectory("Games");

        var canonical = PathCanonicalizer.Canonicalize(Path.Combine(existing, "Not Yet", "There"));

        canonical.Should().Be(Path.Join(PathCanonicalizer.Canonicalize(existing), "Not Yet", "There"));
    }

    [Fact]
    public void Letter_case_on_disk_wins_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows reports the on-disk casing.");
        var folder = _directory.CreateDirectory("MixedCase");

        var canonical = PathCanonicalizer.Canonicalize(folder.ToUpperInvariant());

        canonical.Should().Be(PathCanonicalizer.Canonicalize(folder));
        canonical.Should().EndWith(@"\MixedCase");
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Short_names_resolve_to_long_names_on_windows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "8.3 names exist only on Windows.");
        // Volumes often stop creating 8.3 names, while folders made at setup keep theirs.
        string[] candidates =
        [
            _directory.CreateDirectory("A Folder With A Long Name"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        ];
        var (folder, shortForm) = candidates
            .Select(candidate => (Folder: candidate, Short: WindowsFileApi.TryGetShortPath(candidate)))
            .FirstOrDefault(pair => pair.Short?.Contains('~', StringComparison.Ordinal) == true);
        Assert.SkipWhen(shortForm is null, "No folder with an 8.3 name was found.");

        PathCanonicalizer.Canonicalize(shortForm).Should().Be(PathCanonicalizer.Canonicalize(folder));
    }

    [Fact]
    public void Paths_longer_than_260_characters_are_resolved()
    {
        var segments = Enumerable.Range(0, 6).Select(index => $"Segment{index}".PadRight(50, 'x')).ToArray();
        var folder = _directory.CreateDirectory(Path.Combine(segments));

        var canonical = PathCanonicalizer.Canonicalize(OperatingSystem.IsWindows() ? folder.ToUpperInvariant() : folder);

        canonical.Length.Should().BeGreaterThan(300);
        canonical.Should().EndWith(segments[^1]);
    }

    [Fact]
    public void A_root_stays_a_root()
    {
        var root = Path.GetPathRoot(_directory.Path)!;

        PathCanonicalizer.Canonicalize(root).Should().Be(root);
    }

    [Fact]
    public async Task A_junction_cycle_ends()
    {
        var first = _directory.Combine("First");
        var second = _directory.Combine("Second");
        _directory.CreateDirectoryLink("First", second);
        _directory.CreateDirectoryLink("Second", first);

        var canonicalize = Task.Run(() =>
        {
            try
            {
                return PathCanonicalizer.Canonicalize(Path.Join(first, "game.exe"));
            }
            catch (IOException)
            {
                return null;
            }
        }, TestContext.Current.CancellationToken);

        await canonicalize.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
    }

    public void Dispose() => _directory.Dispose();
}
