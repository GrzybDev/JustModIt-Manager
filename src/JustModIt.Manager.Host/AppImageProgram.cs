using System.Security.Cryptography;
using System.Text;
using JustModIt.Manager.Agent.Shell;

namespace JustModIt.Manager.Host;

/// <summary>
/// The AppImage file the app runs from, with what its runs leave outside the app's data: the updater's folder and the
/// thumbnails the AppImage's start script makes of its icon.
/// </summary>
/// <param name="file">The AppImage file.</param>
/// <param name="updates">The updater's folder of downloads and of its own copy, which lasts across starts; null for none.</param>
/// <param name="home">The user's home folder, under which the start script keeps its thumbnails.</param>
internal sealed class AppImageProgram(string file, string? updates, string home) : IAppProgram
{
    public string File => file;

    public void Remove()
    {
        // The app's processes keep running, each from a mount that holds the file open.
        System.IO.File.Delete(file);
        Try(() =>
        {
            if (updates is not null && Directory.Exists(updates))
            {
                Directory.Delete(updates, recursive: true);
            }
        });
        foreach (var thumbnail in Thumbnails(home, file))
        {
            Try(() => System.IO.File.Delete(thumbnail));
        }
    }

    /// <summary>
    /// Where Velopack's start script copies the icon at every start: the thumbnails of the file's address, which it names
    /// after the address's MD5 as the thumbnail specification does, without escaping the address.
    /// </summary>
    /// <param name="home">The user's home folder.</param>
    /// <param name="file">The AppImage file.</param>
    internal static IEnumerable<string> Thumbnails(string home, string file)
    {
        var name = Convert.ToHexStringLower(CryptographicOperations.HashData(HashAlgorithmName.MD5, Encoding.UTF8.GetBytes("file://" + file)))
            + ".png";
        return [Path.Join(home, ".cache", "thumbnails", "normal", name), Path.Join(home, ".cache", "thumbnails", "large", name)];
    }

    // Once the program is gone the app is uninstalled, and what it left beside it goes as far as the files allow.
    private static void Try(Action remove)
    {
        try
        {
            remove();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
