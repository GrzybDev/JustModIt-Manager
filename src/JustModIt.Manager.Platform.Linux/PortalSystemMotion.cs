using System.Globalization;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.DBus;
using Microsoft.Extensions.Logging;
using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// Follows the reduced-motion setting of Linux desktops through the desktop portal's settings, which carry the portal's
/// own setting, GNOME's animations switch and KDE's animation speed, whichever the desktop gives.
/// </summary>
public sealed partial class PortalSystemMotion : ISystemMotion
{
    private const string Appearance = "org.freedesktop.appearance";
    private const string Gnome = "org.gnome.desktop.interface";
    private const string Kde = "org.kde.kdeglobals.KDE";

    private readonly Lock _lock = new();
    private readonly Dictionary<(string Namespace, string Key), VariantValue> _settings = [];
    private volatile bool _reduced;

    private PortalSystemMotion()
    {
    }

    public bool IsReduced() => _reduced;

    /// <summary>Starts reading the setting, and following its changes, off the calling thread; until the portal answers, motion is not reduced.</summary>
    /// <param name="logger">Hears that the desktop has no portal to ask.</param>
    [SupportedOSPlatform("linux")]
    public static PortalSystemMotion Start(ILogger<PortalSystemMotion> logger)
    {
        var motion = new PortalSystemMotion();
        _ = Task.Run(() => motion.FollowAsync(logger));
        return motion;
    }

    /// <summary>
    /// Tells whether the desktop asks for less motion: the portal's own setting asks to reduce it, GNOME's animations are
    /// off or KDE's animation speed is "Instant".
    /// </summary>
    /// <param name="settings">The settings the portal gave, by their namespace and key.</param>
    internal static bool Decide(IReadOnlyDictionary<(string Namespace, string Key), VariantValue> settings) =>
        settings.GetValueOrDefault((Appearance, "reduced-motion")) is { Type: VariantValueType.UInt32 } reduced && reduced.GetUInt32() == 1
        || settings.GetValueOrDefault((Gnome, "enable-animations")) is { Type: VariantValueType.Bool } animations && !animations.GetBool()
        || Factor(settings.GetValueOrDefault((Kde, "AnimationDurationFactor"))) == 0;

    // KDE's portal hands an entry of kdeglobals over as the text the file holds.
    private static double? Factor(VariantValue value) => value.Type switch
    {
        VariantValueType.Double => value.GetDouble(),
        VariantValueType.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
            => factor,
        _ => null,
    };

    private async Task FollowAsync(ILogger logger)
    {
        try
        {
            var portal = new Settings(DBusConnection.Session, "org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop");
            // Watched before the first read, so no change falls between the two.
            await portal.WatchSettingChangedAsync(change => Take([(change.Namespace, change.Key, change.Value)]), emitOnCapturedContext: false)
                .ConfigureAwait(false);
            var all = await portal.ReadAllAsync([Appearance, Gnome, Kde]).ConfigureAwait(false);
            Take(all.SelectMany(group => group.Value.Select(setting => (group.Key, setting.Key, setting.Value))));
        }
        // Any failure, such as a session without a bus, means no portal answered, so motion stays as it is.
        catch (Exception exception)
        {
            LogNoPortal(logger, exception);
        }
    }

    private void Take(IEnumerable<(string Namespace, string Key, VariantValue Value)> settings)
    {
        lock (_lock)
        {
            foreach (var (space, key, value) in settings)
            {
                _settings[(space, key)] = value;
            }

            _reduced = Decide(_settings);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "The desktop's portal gave no motion setting, so motion is not reduced")]
    private static partial void LogNoPortal(ILogger logger, Exception exception);
}
