using System.IO.Compression;

namespace JustModIt.Manager.Creator.Building;

/// <summary>What a site that keeps a part's versions as one package stores of a run's packages.</summary>
public static class SiteStorage
{
    /// <summary>
    /// Estimates it from the packages' central directories alone: a part's first package whole, and of its others only the
    /// entries whose content the part has not had yet.
    /// </summary>
    /// <param name="packages">The packages, each with the name of its part.</param>
    /// <returns>The estimate and the packages' sizes together.</returns>
    public static (long Stored, long Total) Estimate(IEnumerable<(string Part, string Path)> packages)
    {
        long stored = 0, total = 0;
        foreach (var part in packages.GroupBy(package => package.Part, StringComparer.Ordinal))
        {
            HashSet<(uint Crc32, long Length)> seen = [];
            foreach (var (index, (_, path)) in part.Index())
            {
                var size = new FileInfo(path).Length;
                using var zip = ZipFile.OpenRead(path);
                var added = zip.Entries.Where(entry => seen.Add((entry.Crc32, entry.Length))).Sum(entry => entry.CompressedLength);
                (stored, total) = (stored + (index == 0 ? size : added), total + size);
            }
        }

        return (stored, total);
    }
}
