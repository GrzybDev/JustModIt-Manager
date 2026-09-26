using System.Collections.Immutable;
using JustModIt.Manager.Core.Collections;

namespace JustModIt.Manager.Core.Tests.Collections;

public sealed class ValueCollectionsTests
{
    private sealed record Holder(ValueList<string> List, ValueDictionary<string, int> Map);

    [Fact]
    public void Records_holding_equal_collections_are_equal()
    {
        var first = new Holder(["a", "b"], new ValueDictionary<string, int>(ImmutableDictionary<string, int>.Empty.Add("x", 1).Add("y", 2)));
        var second = new Holder(["a", "b"], new ValueDictionary<string, int>(ImmutableDictionary<string, int>.Empty.Add("y", 2).Add("x", 1)));

        first.Should().Be(second);
        first.GetHashCode().Should().Be(second.GetHashCode());
    }

    [Fact]
    public void Order_matters_for_lists_only()
    {
        ValueList<string> list = ["a", "b"];

        (list == ["b", "a"]).Should().BeFalse();
        list.Add("c").Should().Equal("a", "b", "c");
        list.RemoveAll(item => item == "a").Should().Equal("b");
    }

    [Fact]
    public void Different_values_make_dictionaries_different()
    {
        var map = default(ValueDictionary<string, int>).SetItem("x", 1);

        (map == map.SetItem("x", 2)).Should().BeFalse();
        (map == map.SetItem("y", 1)).Should().BeFalse();
        (map.Remove("x") == default).Should().BeTrue();
    }

    [Fact]
    public void Collections_print_their_items()
    {
        ((ValueList<int>)[1, 2]).ToString().Should().Be("[1, 2]");
        default(ValueDictionary<string, int>).SetItem("x", 1).ToString().Should().Be("{x: 1}");
    }

    [Fact]
    public void Default_collections_are_empty()
    {
        default(ValueList<int>).Should().BeEmpty();
        default(ValueDictionary<string, int>).Should().BeEmpty();
        (default(ValueList<int>) == []).Should().BeTrue();
    }
}
