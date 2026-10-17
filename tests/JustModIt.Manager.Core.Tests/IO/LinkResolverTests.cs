using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class LinkResolverTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    [Fact]
    public void A_link_in_the_middle_of_a_path_is_followed()
    {
        var target = _directory.CreateDirectory(Path.Combine("Library", "Games"));
        var link = _directory.CreateDirectoryLink("Shortcut", target);

        LinkResolver.Resolve(Path.Combine(link, "Lantern", "game.exe"))
            .Should().Be(Path.Combine(target, "Lantern", "game.exe"));
    }

    [Fact]
    public void Links_to_links_are_followed()
    {
        var target = _directory.CreateDirectory("Games");
        var inner = _directory.CreateDirectoryLink("Inner", target);
        var outer = _directory.CreateDirectoryLink("Outer", inner);

        LinkResolver.Resolve(Path.Combine(outer, "Lantern")).Should().Be(Path.Combine(target, "Lantern"));
    }

    [Fact]
    public void A_link_cycle_ends()
    {
        var first = _directory.Combine("First");
        var second = _directory.Combine("Second");
        _directory.CreateDirectoryLink("First", second);
        _directory.CreateDirectoryLink("Second", first);

        var resolved = LinkResolver.Resolve(Path.Combine(first, "Lantern"));

        resolved.Should().EndWith("Lantern");
    }

    public void Dispose() => _directory.Dispose();
}
