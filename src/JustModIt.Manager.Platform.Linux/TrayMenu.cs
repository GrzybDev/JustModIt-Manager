using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.DBus;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// The tray icon's menu as the dbusmenu protocol serves it, one level under its root; an entry keeps its id while the menu
/// changes, so a click on a menu the desktop drew before a change still picks the entry the player saw.
/// </summary>
internal sealed class TrayMenu : DBusHandler, IdbusmenuHandler, IdbusmenuProperties
{
    /// <summary>Where the menu is served.</summary>
    public const string ObjectPath = "/MenuBar";

    private const int RootId = 0;

    private readonly Action<string> _invoked;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
    private (int Id, TrayMenuItem Item)[] _entries = [];
    private uint _revision;

    /// <summary>Creates the menu, empty until it is first given its entries.</summary>
    /// <param name="connection">The connection the menu is served on.</param>
    /// <param name="invoked">Hears the command of an entry the player picked, on the connection's reading thread.</param>
    public TrayMenu(DBusConnection connection, Action<string> invoked)
        : base(connection, ObjectPath, handlesChildPaths: false, handleOnCapturedContext: false) => _invoked = invoked;

    public uint Version => 3;

    public string TextDirection => "ltr";

    public string Status => "normal";

    public string[] IconThemePath => [];

    /// <summary>Takes the menu's entries.</summary>
    /// <param name="menu">The entries, in order.</param>
    /// <returns>The menu's new revision, by which the desktop tells to read it again; null when the entries are the ones before.</returns>
    public uint? Update(IReadOnlyList<TrayMenuItem> menu)
    {
        lock (_gate)
        {
            if (_entries.Select(entry => entry.Item).SequenceEqual(menu))
            {
                return null;
            }

            var separators = 0;
            _entries = [.. menu.Select(item => (IdOf(item.IsSeparator ? $"separator {separators++}" : item.Command), item))];
            return ++_revision;
        }
    }

    public ValueTask HandleGetPropertyAsync(IdbusmenuHandler.GetPropertyContext context) => context.Handle(this);

    public ValueTask HandleGetAllPropertiesAsync(IdbusmenuHandler.GetAllPropertiesContext context) => context.Handle(this);

    public ValueTask<(uint Revision, (int, Dictionary<string, VariantValue>, VariantValue[]) Layout)> GetLayoutAsync(int parentId,
        int recursionDepth, string[] propertyNames)
    {
        lock (_gate)
        {
            if (parentId != RootId)
            {
                return ValueTask.FromResult((_revision, (parentId, Properties(Find(parentId)), Array.Empty<VariantValue>())));
            }

            // A depth of 0 asks for the root alone.
            VariantValue[] children = recursionDepth == 0
                ? []
                : [.. _entries.Select(entry => VariantValue.Struct(entry.Id, new Dict<string, VariantValue>(Properties(entry.Item)),
                    VariantValue.ArrayOfVariant(Array.Empty<VariantValue>())))];
            return ValueTask.FromResult((_revision,
                (RootId, new Dictionary<string, VariantValue> { ["children-display"] = "submenu" }, children)));
        }
    }

    public ValueTask<(int, Dictionary<string, VariantValue>)[]> GetGroupPropertiesAsync(int[] ids, string[] propertyNames)
    {
        lock (_gate)
        {
            // No ids ask for every entry.
            return ValueTask.FromResult(_entries.Where(entry => ids.Length == 0 || ids.Contains(entry.Id))
                .Select(entry => (entry.Id, Properties(entry.Item))).ToArray());
        }
    }

    public ValueTask<VariantValue> GetPropertyAsync(int id, string name)
    {
        lock (_gate)
        {
            return Properties(Find(id)).TryGetValue(name, out var value)
                ? ValueTask.FromResult(value)
                : throw new DBusErrorReplyException("org.freedesktop.DBus.Error.InvalidArgs", $"The entry {id} has no {name}.");
        }
    }

    public ValueTask EventAsync(int id, string eventId, VariantValue data, uint timestamp)
    {
        Pick(id, eventId);
        return default;
    }

    public ValueTask<int[]> EventGroupAsync((int, string, VariantValue, uint)[] events)
    {
        List<int> unknown = [];
        foreach (var (id, eventId, _, _) in events)
        {
            if (!Pick(id, eventId))
            {
                unknown.Add(id);
            }
        }

        return ValueTask.FromResult(unknown.ToArray());
    }

    // The menu is the same whenever it opens, so the desktop never has to read it first.
    public ValueTask<bool> AboutToShowAsync(int id) => ValueTask.FromResult(false);

    public ValueTask<(int[] UpdatesNeeded, int[] IdErrors)> AboutToShowGroupAsync(int[] ids) =>
        ValueTask.FromResult((Array.Empty<int>(), Array.Empty<int>()));

    // The desktop shows an underscore as the shortcut's mark unless it is doubled.
    private static Dictionary<string, VariantValue> Properties(TrayMenuItem? item) => item switch
    {
        null => [],
        { IsSeparator: true } => new() { ["type"] = "separator" },
        _ => new() { ["label"] = item.Text.Replace("_", "__", StringComparison.Ordinal), ["enabled"] = item.IsEnabled },
    };

    private int IdOf(string key)
    {
        if (!_ids.TryGetValue(key, out var id))
        {
            _ids[key] = id = _ids.Count + 1;
        }

        return id;
    }

    private TrayMenuItem? Find(int id) => _entries.FirstOrDefault(entry => entry.Id == id).Item;

    // Tells whether the entry is in the menu; only a click on an enabled entry runs its command.
    private bool Pick(int id, string eventId)
    {
        TrayMenuItem? item;
        lock (_gate)
        {
            item = Find(id);
        }

        if (item is { IsSeparator: false, IsEnabled: true } && eventId == "clicked")
        {
            _invoked(item.Command);
        }

        return item is not null;
    }
}
