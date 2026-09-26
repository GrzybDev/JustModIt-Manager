using System.Collections.Frozen;

namespace JustModIt.Manager.Core.Platform;

/// <summary>A registry key as it was when it was read, whose names ignore letter case as the registry's do.</summary>
public sealed class RegistryKeySnapshot
{
    /// <summary>Captures a key that was just read.</summary>
    /// <param name="path">The key's path below its hive.</param>
    /// <param name="subKeyNames">The names of the key's subkeys.</param>
    /// <param name="values">The values by name; the default value has the empty name.</param>
    public RegistryKeySnapshot(string path, IEnumerable<string> subKeyNames, IEnumerable<KeyValuePair<string, RegistryValue>> values)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(subKeyNames);
        ArgumentNullException.ThrowIfNull(values);
        Path = path;
        SubKeyNames = [.. subKeyNames];
        Values = values.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public string Path { get; }

    public IReadOnlyList<string> SubKeyNames { get; }

    /// <summary>Keyed by value name; the default value of a key has the empty name.</summary>
    public IReadOnlyDictionary<string, RegistryValue> Values { get; }

    /// <summary>The value with this name; null when the key has none.</summary>
    /// <param name="name">The value's name, in any letter case; empty for the default value.</param>
    public RegistryValue? GetValue(string name) => Values.GetValueOrDefault(name);

    /// <summary>A string value, or the decimal digits of a number (see <see cref="RegistryValue.AsText"/>).</summary>
    /// <param name="name">The value's name, in any letter case; empty for the default value.</param>
    public string? GetText(string name) => GetValue(name)?.AsText();
}
