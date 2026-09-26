using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace JustModIt.Manager.Core.Collections;

/// <summary>An immutable list that equals another with the same items, so records holding one compare by value.</summary>
[CollectionBuilder(typeof(ValueList), nameof(ValueList.Create))]
public readonly struct ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly ImmutableArray<T> _items;

    /// <summary>Wraps the given items without copying them.</summary>
    /// <param name="items">The items; a default array counts as empty.</param>
    public ValueList(ImmutableArray<T> items) => _items = items;

    /// <summary>The items, never a default array.</summary>
    public ImmutableArray<T> Items => _items.IsDefault ? [] : _items;

    public int Count => Items.Length;

    public T this[int index] => Items[index];

    public static bool operator ==(ValueList<T> left, ValueList<T> right) => left.Equals(right);

    public static bool operator !=(ValueList<T> left, ValueList<T> right) => !left.Equals(right);

    /// <summary>Returns a copy with <paramref name="item"/> appended.</summary>
    /// <param name="item">The item to add at the end.</param>
    public ValueList<T> Add(T item) => new(Items.Add(item));

    /// <summary>Returns a copy without the items that <paramref name="match"/> picks.</summary>
    /// <param name="match">True for each item to drop.</param>
    public ValueList<T> RemoveAll(Predicate<T> match) => new(Items.RemoveAll(match));

    public bool Equals(ValueList<T> other) => Items.AsSpan().SequenceEqual(other.Items.AsSpan(), EqualityComparer<T>.Default);

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in Items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => $"[{string.Join(", ", Items)}]";

    public ImmutableArray<T>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => ((IEnumerable<T>)Items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)Items).GetEnumerator();
}

/// <summary>Builds a <see cref="ValueList{T}"/> from a collection expression.</summary>
public static class ValueList
{
    /// <summary>Creates a list holding a copy of <paramref name="items"/>.</summary>
    /// <param name="items">The items of the collection expression.</param>
    public static ValueList<T> Create<T>(ReadOnlySpan<T> items) => new([.. items]);
}
