using JustModIt.Manager.Core.Platform;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>
/// Registry keys in memory, whose keys and values ignore letter case like the real registry, with
/// <see cref="RegistryView.Default"/> as the 64-bit view, as in the 64-bit process the app runs in.
/// </summary>
internal sealed class InMemoryRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, RegistryValue>> _keys = new(StringComparer.OrdinalIgnoreCase);

    public InMemoryRegistry SetValue(RegistryHive hive, RegistryView view, string path, string name, RegistryValue value)
    {
        AddKey(hive, view, path)[name] = value;
        return this;
    }

    public InMemoryRegistry SetText(RegistryHive hive, RegistryView view, string path, string name, string text) =>
        SetValue(hive, view, path, name, RegistryValue.FromText(text));

    public InMemoryRegistry CreateKey(RegistryHive hive, RegistryView view, string path)
    {
        AddKey(hive, view, path);
        return this;
    }

    public RegistryKeySnapshot? ReadKey(RegistryHive hive, RegistryView view, string path)
    {
        var id = Id(hive, view, path);
        if (!_keys.TryGetValue(id, out var values))
        {
            return null;
        }

        var prefix = id + '\\';
        var subKeys = _keys.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && !key.AsSpan(prefix.Length).Contains('\\'))
            .Select(key => key[prefix.Length..]);
        return new RegistryKeySnapshot(path, subKeys, values);
    }

    private Dictionary<string, RegistryValue> AddKey(RegistryHive hive, RegistryView view, string path)
    {
        var segments = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        Dictionary<string, RegistryValue>? values = null;
        for (var length = 1; length <= segments.Length; length++)
        {
            var id = Id(hive, view, string.Join('\\', segments[..length]));
            if (!_keys.TryGetValue(id, out values))
            {
                values = new Dictionary<string, RegistryValue>(StringComparer.OrdinalIgnoreCase);
                _keys[id] = values;
            }
        }

        return values ?? throw new ArgumentException("A key path needs at least one segment.", nameof(path));
    }

    private static string Id(RegistryHive hive, RegistryView view, string path) =>
        $"{hive}\\{(view == RegistryView.Default ? RegistryView.Registry64 : view)}\\{path.Trim('\\')}";
}
