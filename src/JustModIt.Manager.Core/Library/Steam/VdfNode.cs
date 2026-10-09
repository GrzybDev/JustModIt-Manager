using System.Diagnostics.CodeAnalysis;

namespace JustModIt.Manager.Core.Library.Steam;

/// <summary>
/// A node of Valve's text KeyValues, a string value or an object, whose keys are looked up ignoring letter case as
/// Valve's KeyValues does: the first of duplicate keys wins, while all are kept in <see cref="Children"/>.
/// </summary>
public sealed class VdfNode
{
    private readonly List<KeyValuePair<string, VdfNode>>? _children;

    private VdfNode(string? value, List<KeyValuePair<string, VdfNode>>? children)
    {
        Value = value;
        _children = children;
    }

    /// <summary>The text of a value node; null for an object.</summary>
    public string? Value { get; }

    [MemberNotNullWhen(true, nameof(_children))]
    public bool IsObject => _children is not null;

    public IReadOnlyList<KeyValuePair<string, VdfNode>> Children =>
        (IReadOnlyList<KeyValuePair<string, VdfNode>>?)_children ?? [];

    /// <summary>The first child with <paramref name="key"/>; null when there is none or this is a value node.</summary>
    /// <param name="key">The child's key, in any letter case.</param>
    public VdfNode? this[string key] => TryGet(key, out var node) ? node : null;

    /// <summary>Finds the first child with <paramref name="key"/>.</summary>
    /// <param name="key">The child's key, in any letter case.</param>
    /// <param name="node">The child; null when there is none or this is a value node.</param>
    public bool TryGet(string key, [NotNullWhen(true)] out VdfNode? node)
    {
        if (IsObject)
        {
            foreach (var (childKey, child) in _children)
            {
                if (string.Equals(childKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    node = child;
                    return true;
                }
            }
        }

        node = null;
        return false;
    }

    /// <summary>Gives the text of the child with <paramref name="key"/>; null when it is missing or an object.</summary>
    /// <param name="key">The child's key, in any letter case.</param>
    public string? GetString(string key) => this[key]?.Value;

    /// <summary>Creates a value node.</summary>
    /// <param name="value">Its text.</param>
    internal static VdfNode CreateValue(string value) => new(value, null);

    /// <summary>Creates an object node without children.</summary>
    internal static VdfNode CreateObject() => new(null, []);

    /// <summary>Adds a child to this object node.</summary>
    /// <param name="key">The child's key, kept even when another child has it.</param>
    /// <param name="node">The child.</param>
    internal void Add(string key, VdfNode node) =>
        (_children ?? throw new InvalidOperationException("A value node cannot have children.")).Add(new(key, node));
}

/// <summary>Text that is not well-formed KeyValues.</summary>
public sealed class VdfFormatException : FormatException
{
    /// <summary>Creates the exception for a problem on <paramref name="line"/>, which the message names too.</summary>
    /// <param name="message">What is wrong, without the line.</param>
    /// <param name="line">The 1-based line of the problem.</param>
    public VdfFormatException(string message, int line)
        : base($"{message} (line {line}).")
    {
        Line = line;
    }

    /// <summary>The 1-based line of the problem.</summary>
    public int Line { get; }
}
