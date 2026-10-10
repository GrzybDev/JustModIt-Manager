using System.Reflection;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace JustModIt.Manager.Ui;

/// <summary>The logo and icon that an app's build embeds in it (<c>App.targets</c>), read once the window needs them.</summary>
/// <param name="app">The app's own assembly; null for none, e.g. in tests.</param>
public sealed class AppBranding(Assembly? app)
{
    private const string LogoResource = "JustModIt.Manager.Logo.png";
    private const string IconResource = "JustModIt.Manager.Icon.ico";

    // The logo is drawn at 30 px in the title bar and 132 px in the first-run setup, twice that on high-DPI screens.
    private const int LogoWidth = 264;

    private Bitmap? _logo;
    private bool _logoRead;

    /// <summary>The app's logo; null when the app has none.</summary>
    public IImage? Logo
    {
        get
        {
            if (!_logoRead)
            {
                _logoRead = true;
                using var stream = Open(LogoResource);
                _logo = stream is null ? null : Bitmap.DecodeToWidth(stream, LogoWidth, BitmapInterpolationMode.HighQuality);
            }

            return _logo;
        }
    }

    /// <summary>The window's icon; null when the app has none.</summary>
    public WindowIcon? CreateIcon()
    {
        using var stream = Open(IconResource);
        return stream is null ? null : new WindowIcon(stream);
    }

    private Stream? Open(string resource) => app?.GetManifestResourceStream(resource);
}
