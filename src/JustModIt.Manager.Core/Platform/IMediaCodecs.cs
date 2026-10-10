namespace JustModIt.Manager.Core.Platform;

/// <summary>
/// The system's own decoders, for the pictures of a site's pages that the window's decoder does not read, such as AVIF
/// stills; every call reads a file and decodes it, so it stays off the UI thread.
/// </summary>
public interface IMediaCodecs
{
    /// <summary>Decodes the first frame of a picture to a size chosen from the picture's own.</summary>
    /// <param name="path">The picture's file.</param>
    /// <param name="sizeFor">Gives the size to decode to from the picture's own width and height.</param>
    /// <returns>The picture; null when the system has no decoder for it or it is damaged.</returns>
    MediaPicture? DecodePicture(string path, Func<int, int, (int Width, int Height)> sizeFor);
}

/// <summary>A decoded picture: rows of 32-bit pixels, blue, green, red and premultiplied alpha, one after another.</summary>
/// <param name="Width">The decoded width in pixels.</param>
/// <param name="Height">The decoded height in pixels.</param>
/// <param name="NaturalWidth">The picture's own width.</param>
/// <param name="NaturalHeight">The picture's own height.</param>
/// <param name="Pixels">Width × height × 4 bytes.</param>
public sealed record MediaPicture(int Width, int Height, int NaturalWidth, int NaturalHeight, byte[] Pixels);
