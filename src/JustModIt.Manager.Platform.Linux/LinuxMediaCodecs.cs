using System.Runtime.Versioning;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Platform.Linux.Interop;

namespace JustModIt.Manager.Platform.Linux;

/// <summary>
/// Decodes AVIF pictures with the app's own libavif and dav1d, since no Linux system is sure to have a decoder for them;
/// clips do not play, so they show their still.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxMediaCodecs : IMediaCodecs
{
    public bool PlaysClips => false;

    public MediaPicture? DecodePicture(string path, Func<int, int, (int Width, int Height)> sizeFor)
    {
        var content = File.ReadAllBytes(path);
        if (AvifShim.Size(content, (nuint)content.Length, out var naturalWidth, out var naturalHeight) != 0)
        {
            return null;
        }

        var (width, height) = sizeFor(naturalWidth, naturalHeight);
        if (width <= 0 || height <= 0 || Math.Max((long)naturalWidth * naturalHeight, (long)width * height) > MediaPicture.MaxPixels)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];
        return AvifShim.Decode(content, (nuint)content.Length, width, height, pixels) == 0
            ? new MediaPicture(width, height, naturalWidth, naturalHeight, pixels)
            : null;
    }

    public IMediaClip? OpenClip(string path) => null;
}
