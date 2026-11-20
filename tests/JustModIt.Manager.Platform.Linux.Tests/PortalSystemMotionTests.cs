using Tmds.DBus.Protocol;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class PortalSystemMotionTests
{
    [Fact]
    public void Motion_is_reduced_by_the_portals_own_setting_gnomes_animations_switch_or_kdes_instant_animations()
    {
        Reduced().Should().BeFalse("a desktop whose portal gives none of the settings keeps its motion");
        Reduced(("org.freedesktop.appearance", "reduced-motion", 1u)).Should().BeTrue();
        Reduced(("org.freedesktop.appearance", "reduced-motion", 0u)).Should().BeFalse();
        Reduced(("org.gnome.desktop.interface", "enable-animations", false)).Should().BeTrue();
        Reduced(("org.gnome.desktop.interface", "enable-animations", true)).Should().BeFalse();
        Reduced(("org.kde.kdeglobals.KDE", "AnimationDurationFactor", "0")).Should().BeTrue("KDE's portal gives the text kdeglobals holds");
        Reduced(("org.kde.kdeglobals.KDE", "AnimationDurationFactor", "0.5")).Should().BeFalse();
        Reduced(("org.kde.kdeglobals.KDE", "AnimationDurationFactor", 0.0)).Should().BeTrue();
        Reduced(("org.kde.kdeglobals.KDE", "AnimationDurationFactor", "0.5"), ("org.gnome.desktop.interface", "enable-animations", false))
            .Should().BeTrue("any of the desktop's settings that asks for less motion is followed");
    }

    private static bool Reduced(params (string Namespace, string Key, VariantValue Value)[] settings) =>
        PortalSystemMotion.Decide(settings.ToDictionary(setting => (setting.Namespace, setting.Key), setting => setting.Value));
}
