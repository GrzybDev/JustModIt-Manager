using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Tests.Collections;

public sealed class ValueCollectionsTests
{
    private sealed record Holder(ValueList<string> List, ValueDictionary<string, int> Map);

    [Fact]
    public void Records_compare_their_collections_by_content()
    {
        var first = new Holder(["a", "b"], new ValueDictionary<string, int>(ImmutableDictionary<string, int>.Empty.Add("x", 1).Add("y", 2)));
        var second = new Holder(["a", "b"], new ValueDictionary<string, int>(ImmutableDictionary<string, int>.Empty.Add("y", 2).Add("x", 1)));

        first.Should().Be(second, "dictionaries ignore the order of their entries");
        first.GetHashCode().Should().Be(second.GetHashCode());
        first.Should().NotBe(first with { List = ["b", "a"] }, "lists keep their order");
        first.Should().NotBe(first with { Map = first.Map.SetItem("x", 2) });
        first.Should().NotBe(first with { Map = first.Map.SetItem("z", 1) });
        first.List.Add("c").Should().Equal("a", "b", "c");
        first.List.RemoveAll(item => item == "a").Should().Equal("b");
        (first.Map.Remove("x").Remove("y") == default).Should().BeTrue();
    }
    [Fact]
    public void Default_collections_are_empty()
    {
        default(ValueList<int>).Should().BeEmpty();
        default(ValueDictionary<string, int>).Should().BeEmpty();
        (default(ValueList<int>) == []).Should().BeTrue();
    }
}
