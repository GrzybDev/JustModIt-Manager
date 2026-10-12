using System.IO.Compression;

namespace JustModIt.Manager.Agent.Diagnostics;

/// <summary>A zip file of the app's logs, whose paths are already free of the user's profile, for a problem report.</summary>
public static class DiagnosticReport
{
    /// <summary>Writes every log file into a zip file, which takes the place of a file there only once it is complete.</summary>
    /// <param name="logsDirectory">The app's logs folder.</param>
    /// <param name="destination">The report's full path.</param>
    /// <param name="cancellationToken">Stops writing, leaving no report.</param>
    public static Task WriteAsync(string logsDirectory, string destination, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logsDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        return Task.Run(() =>
        {
            var partial = destination + ".partial";
            try
            {
                using (var zip = ZipFile.Open(partial, ZipArchiveMode.Create))
                {
                    foreach (var file in Directory.Exists(logsDirectory) ? Directory.EnumerateFiles(logsDirectory) : [])
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        // The running roles keep their logs open for writing, which a reader has to share.
                        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        using var entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Optimal).Open();
                        input.CopyTo(entry);
                    }
                }

                File.Move(partial, destination, overwrite: true);
            }
            catch
            {
                File.Delete(partial);
                throw;
            }
        }, cancellationToken);
    }
}
