using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using JustModIt.Manager.Ui.Controls;

namespace JustModIt.Manager.Ui.Theme;

/// <summary>
/// The accent family the theme draws the primary action, the app's own work and keyboard focus with, derived from the
/// app's accent colour. The theme's own family was tuned by hand for its default accent, so that accent keeps it.
/// </summary>
/// <param name="Accent">The primary button, progress and selection.</param>
/// <param name="Hover">The primary button under the pointer.</param>
/// <param name="Pressed">The primary button while pressed.</param>
/// <param name="Text">Accent text and icons, readable at 7:1 on the page.</param>
/// <param name="FocusRing">The ring of keyboard focus.</param>
public sealed record AccentPalette(Color Accent, Color Hover, Color Pressed, Color Text, Color FocusRing)
{
    private const double TextContrast = 7;

    private static readonly Color Page = Color.FromRgb(0x03, 0x07, 0x12);

    /// <summary>The theme's own family, which Theme/Tokens.axaml holds as well.</summary>
    public static AccentPalette Default { get; } = new(
        Color.FromRgb(0xD4, 0x21, 0x3D),
        Color.FromRgb(0xD8, 0x37, 0x50),
        Color.FromRgb(0xB9, 0x1C, 0x34),
        Color.FromRgb(0xFF, 0x64, 0x67),
        Color.FromRgb(0xEF, 0x44, 0x44));

    /// <summary>The family of an app's accent colour.</summary>
    /// <param name="hex">The accent as <c>#rrggbb</c>; empty or malformed keeps <see cref="Default"/>.</param>
    public static AccentPalette From(string? hex)
    {
        if (hex is not ['#', .. var digits] || digits.Length != 6
            || !uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
        {
            return Default;
        }

        var accent = Color.FromUInt32(0xFF000000 | rgb);
        if (accent == Default.Accent)
        {
            return Default;
        }

        var text = accent;
        for (var white = 0.0; StatusColors.Contrast(text, Page) < TextContrast && white < 1; white += 0.02)
        {
            text = StatusColors.Mix(accent, Colors.White, white);
        }

        return new AccentPalette(accent, StatusColors.Mix(accent, Colors.White, 0.08), StatusColors.Mix(accent, Colors.Black, 0.13),
            text, StatusColors.Mix(accent, text, 0.5));
    }

    /// <summary>Puts the family into the resources the theme reads it from.</summary>
    /// <param name="resources">The application's resources, which win over the theme's defaults.</param>
    public void Apply(IResourceDictionary resources)
    {
        ArgumentNullException.ThrowIfNull(resources);
        resources["AccentColor"] = Accent;
        resources["AccentHoverColor"] = Hover;
        resources["AccentPressedColor"] = Pressed;
        resources["AccentTextColor"] = Text;
        resources["AccentSoftColor"] = WithAlpha(Accent, 0.15);
        resources["AccentSoftStrongColor"] = WithAlpha(Accent, 0.22);
        resources["AccentFilterColor"] = WithAlpha(Accent, 0.13);
        resources["AccentFilterStrongColor"] = WithAlpha(Accent, 0.26);
        resources["AccentFilterEdgeColor"] = WithAlpha(Accent, 0.7);
        resources["AccentBorderColor"] = WithAlpha(FocusRing, 0.5);
        resources["FocusRingColor"] = FocusRing;
        resources["ElevPrimary"] = new BoxShadows(Shadow(0, 14, 30, -14, WithAlpha(Accent, 0.8)), [Inset(0.12)]);
        resources["ElevPrimaryHover"] = new BoxShadows(Shadow(0, 16, 32, -14, WithAlpha(Accent, 0.95)), [Inset(0.14)]);
        resources["ElevLift"] = new BoxShadows(Shadow(0, 22, 40, -18, WithAlpha(Accent, 0.35)),
            [Shadow(0, 12, 24, -12, Color.FromArgb(0xCC, 0, 0, 0))]);
    }

    private static Color WithAlpha(Color color, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), color.R, color.G, color.B);

    private static BoxShadow Shadow(double x, double y, double blur, double spread, Color color) =>
        new() { OffsetX = x, OffsetY = y, Blur = blur, Spread = spread, Color = color };

    private static BoxShadow Inset(double whiteAlpha) =>
        new() { OffsetY = 1, Color = WithAlpha(Colors.White, whiteAlpha), IsInset = true };
}
