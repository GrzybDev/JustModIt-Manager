using System.Collections;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace JustModIt.Manager.Core.Collections;

/// <summary>
/// An immutable dictionary that equals another with the same entries, so records holding one compare by value; the
/// default value is empty.
/// </summary>
public readonly struct ValueDictionary<TKey, TValue> : IReadOnlyDictionary<TKey, TValue>, IEquatable<ValueDictionary<TKey, TValue>>
    where TKey : notnull
{
    private readonly ImmutableDictionary<TKey, TValue>? _items;

    /// <summary>Wraps the given entries without copying them.</summary>
    /// <param name="items">The entries.</param>
    public ValueDictionary(ImmutableDictionary<TKey, TValue> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    public int Count => Items.Count;

    public IEnumerable<TKey> Keys => Items.Keys;

    public IEnumerable<TValue> Values => Items.Values;

    private ImmutableDictionary<TKey, TValue> Items => _items ?? ImmutableDictionary<TKey, TValue>.Empty;

    public TValue this[TKey key] => Items[key];

    public static bool operator ==(ValueDictionary<TKey, TValue> left, ValueDictionary<TKey, TValue> right) => left.Equals(right);

    public static bool operator !=(ValueDictionary<TKey, TValue> left, ValueDictionary<TKey, TValue> right) => !left.Equals(right);

    /// <summary>Returns a copy in which <paramref name="key"/> maps to <paramref name="value"/>.</summary>
    /// <param name="key">The key to add or replace.</param>
    /// <param name="value">Its new value.</param>
    public ValueDictionary<TKey, TValue> SetItem(TKey key, TValue value) => new(Items.SetItem(key, value));

    /// <summary>Returns a copy without <paramref name="key"/>.</summary>
    /// <param name="key">The key to drop; a missing key changes nothing.</param>
    public ValueDictionary<TKey, TValue> Remove(TKey key) => new(Items.Remove(key));

    public bool ContainsKey(TKey key) => Items.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => Items.TryGetValue(key, out value);

    public bool Equals(ValueDictionary<TKey, TValue> other)
    {
        var others = other.Items;
        return ReferenceEquals(Items, others)
            || (others.Count == Count
                && Items.All(pair => others.TryGetValue(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(pair.Value, value)));
    }

    public override bool Equals(object? obj) => obj is ValueDictionary<TKey, TValue> other && Equals(other);

    // Order-independent, because equal dictionaries may enumerate in different orders.
    public override int GetHashCode() => Items.Aggregate(Count, (hash, pair) => hash ^ HashCode.Combine(pair.Key, pair.Value));

    public override string ToString() => $"{{{string.Join(", ", Items.Select(pair => $"{pair.Key}: {pair.Value}"))}}}";

    public ImmutableDictionary<TKey, TValue>.Enumerator GetEnumerator() => Items.GetEnumerator();

    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
