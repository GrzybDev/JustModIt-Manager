using System.Security.Cryptography;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Packages;

namespace JustModIt.Manager.LiveTests;

/// <summary>What a package leaves in the game, read the way the engine reads it.</summary>
internal static class PackageContent
{
    /// <summary>The SHA-256 of every file the package writes, by game path.</summary>
    /// <param name="packagePath">A plain zip or a manifest package.</param>
    /// <param name="cancellationToken">Stops the hashing.</param>
    /// <returns>The hashes, without a game file the package renames, whose content the package does not know.</returns>
    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(string packagePath, CancellationToken cancellationToken)
    {
        var written = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var package = await new PackageReader().OpenAsync(packagePath, cancellationToken);
        await using (package)
        {
            foreach (var step in package.Steps)
            {
                switch (step)
                {
                    case CopyStep copy:
                        var entry = await package.OpenEntryAsync(copy.Entry, cancellationToken);
                        await using (entry)
                        {
                            written[copy.Path] = await FileHash.ComputeAsync(entry, HashAlgorithmName.SHA256, progress: null,
                                cancellationToken);
                        }

                        break;
                    case PatchStep patch:
                        written[patch.Path] = patch.TargetSha256;
                        break;
                    case RenameStep rename when written.Remove(rename.From, out var moved):
                        written[rename.To] = moved;
                        break;
                    case RenameStep rename:
                        written.Remove(rename.To);
                        break;
                    case DeleteStep delete:
                        written.Remove(delete.Path);
                        break;
                }
            }
        }

        return written;
    }
}
