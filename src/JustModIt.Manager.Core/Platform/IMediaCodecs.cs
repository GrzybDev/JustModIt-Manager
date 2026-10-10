namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The system's own decoders, for the pictures and silent clips of a site's pages that the window's decoder does not
/// read, such as AVIF stills and the MP4 or WebM loops a store page's GIFs become; every call reads a file and decodes
/// it, so it stays off the UI thread.
/// </summary>
public interface IMediaCodecs
{
    /// <summary>Decodes the first frame of a picture to a size chosen from the picture's own.</summary>
    /// <param name="path">The picture's file.</param>
    /// <param name="sizeFor">Gives the size to decode to from the picture's own width and height.</param>
    /// <returns>The picture; null when the system has no decoder for it or it is damaged.</returns>
    MediaPicture? DecodePicture(string path, Func<int, int, (int Width, int Height)> sizeFor);

    /// <summary>Opens a clip to read its frames one after another, without its sound.</summary>
    /// <param name="path">The clip's file.</param>
    /// <returns>The clip; null when the system cannot play it.</returns>
    IMediaClip? OpenClip(string path);
}

/// <summary>A silent clip read frame by frame, from the first again after the last; one reader at a time.</summary>
public interface IMediaClip : IDisposable
{
    /// <summary>The frames' width in pixels.</summary>
    int Width { get; }

    /// <summary>The frames' height in pixels.</summary>
    int Height { get; }

    /// <summary>Reads the next frame, the first again after the last.</summary>
    /// <param name="pixels">Width × height × 4 bytes for rows of opaque pixels, blue, green, red and alpha, one after another.</param>
    /// <returns>How long the frame shows; null when the clip cannot be read further.</returns>
    TimeSpan? ReadFrame(Span<byte> pixels);
}

/// <summary>A decoded picture: rows of 32-bit pixels, blue, green, red and premultiplied alpha, one after another.</summary>
/// <param name="Width">The decoded width in pixels.</param>
/// <param name="Height">The decoded height in pixels.</param>
/// <param name="NaturalWidth">The picture's own width.</param>
/// <param name="NaturalHeight">The picture's own height.</param>
/// <param name="Pixels">Width × height × 4 bytes.</param>
public sealed record MediaPicture(int Width, int Height, int NaturalWidth, int NaturalHeight, byte[] Pixels);
