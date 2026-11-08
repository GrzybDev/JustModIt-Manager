using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;

namespace JustModIt.Manager.Creator.Projects;

/// <summary>A project of the library.</summary>
/// <param name="Project">The project as its file holds it.</param>
/// <param name="Changed">When its file was last written.</param>
public sealed record LibraryProject(CreatorProject Project, DateTimeOffset Changed);

/// <summary>What the library holds.</summary>
/// <param name="Projects">The projects, the one changed last first.</param>
/// <param name="Unreadable">The folders of the projects that cannot be read, left as they are.</param>
public sealed record ProjectListing(IReadOnlyList<LibraryProject> Projects, IReadOnlyList<string> Unreadable);

/// <summary>What became of the project files on the list an earlier version kept.</summary>
/// <param name="Imported">The files now in the library, those brought over earlier included.</param>
/// <param name="Skipped">The files that are gone or damaged, left as they are.</param>
/// <param name="Waiting">The files that may yet be read, which stay on the list.</param>
public sealed record RecentImport(IReadOnlyList<string> Imported, IReadOnlyList<string> Skipped, IReadOnlyList<string> Waiting)
{
    /// <summary>The files the list no longer needs.</summary>
    public IReadOnlyList<string> Leaving => [.. Imported, .. Skipped];
}

/// <summary>
/// The author's projects, one folder each holding <c>project.json</c> and <c>builds.json</c>, which only the window's process
/// reads and writes.
/// </summary>
/// <param name="directory">The library's folder.</param>
/// <param name="time">Dates the imported projects that name no date of their own.</param>
public sealed class ProjectLibrary(string directory, TimeProvider time) : IDisposable
{
    private const string ProjectFileName = "project.json";
    private const string BuildsFileName = "builds.json";
    private const long MaxBuildsLength = 64 << 20;

    private readonly SemaphoreSlim _buildsGate = new(1, 1);

    /// <summary>Reads every project of the library.</summary>
    /// <param name="access">Reads the library's folder.</param>
    /// <param name="cancellationToken">Cancels the reading.</param>
    /// <exception cref="TimeoutException">The folder's drive did not answer in time.</exception>
    public Task<ProjectListing> ListAsync(LibraryAccess access, CancellationToken cancellationToken) =>
        access.ReadAsync(directory, async token =>
        {
            List<LibraryProject> projects = [];
            List<string> unreadable = [];
            foreach (var folder in Directory.Exists(directory) ? Directory.EnumerateDirectories(directory) : [])
            {
                var file = Path.Join(folder, ProjectFileName);
                if (!Guid.TryParse(Path.GetFileName(folder), out var id) || !File.Exists(file))
                {
                    continue;
                }

                if (await Store(id).LoadAsync(token).ConfigureAwait(false) is { } project)
                {
                    projects.Add(new LibraryProject(project, File.GetLastWriteTimeUtc(file)));
                }
                else
                {
                    unreadable.Add(folder);
                }
            }

            return new ProjectListing([.. projects.OrderByDescending(entry => entry.Changed)], unreadable);
        }, cancellationToken);

    /// <summary>Where a project of the library is kept.</summary>
    /// <param name="id">The project's id.</param>
    public IDocumentStore<CreatorProject> Store(Guid id) => new ProjectFile(Path.Join(directory, id.ToString(), ProjectFileName), id);

    /// <summary>Changes a project's record of its builds, one change at a time.</summary>
    /// <param name="id">The project's id.</param>
    /// <param name="update">Turns the record, null while there is none or it is damaged, into the new one.</param>
    /// <param name="cancellationToken">Cancels the change and keeps the record as it was.</param>
    public Task UpdateBuildAsync(Guid id, Func<BuildRecord?, BuildRecord> update, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        var path = Path.Join(directory, id.ToString(), BuildsFileName);
        await _buildsGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            BuildRecord? record = null;
            try
            {
                var content = await SharedFileRead.ReadAllBytesAsync(path, MaxBuildsLength, cancellationToken).ConfigureAwait(false);
                record = JsonSerializer.Deserialize(content, ProjectJsonContext.Default.BuildRecord);
            }
            // Nothing else reads the file, and it only tells what the next build can start from.
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException or JsonException or InvalidDataException)
            {
            }

            var updated = update(record);
            // Sorted, since a file's place in the listing says nothing and one record must always give the same bytes.
            var sorted = updated with
            {
                Parts = [.. updated.Parts.Select(part => part with
                {
                    Folders = [.. part.Folders.Select(folder => folder with
                    {
                        Files = folder.Files.OrderBy(file => file.Key, StringComparer.Ordinal).ToDictionary(),
                    })],
                })],
            };
            await AtomicFile.WriteJsonAsync(path, AtomicFile.ReadableJson with { NewLine = "\n" },
                writer => JsonSerializer.Serialize(writer, sorted, ProjectJsonContext.Default.BuildRecord), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _buildsGate.Release();
        }
    }, cancellationToken);

    /// <summary>
    /// Adds a project file to the library as a project of its own, so nothing can overwrite a project through an import, and
    /// a file handed over never chooses where a build writes.
    /// </summary>
    /// <param name="path">The project file, which stays as it is.</param>
    /// <param name="access">Reads the file.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    /// <returns>The project as the library holds it.</returns>
    /// <exception cref="ProjectLoadException">The file cannot be read, is damaged or was written by a newer app.</exception>
    /// <exception cref="TimeoutException">The file's drive did not answer in time.</exception>
    public async Task<CreatorProject> ImportAsync(string path, LibraryAccess access, CancellationToken cancellationToken)
    {
        var read = await access.ReadAsync(path, token => CreatorProjectStore.LoadAsync(path, token), cancellationToken).ConfigureAwait(false);
        var project = Imported(read, Guid.NewGuid(), path);
        await Store(project.Id).SaveAsync(project, cancellationToken).ConfigureAwait(false);
        return project;
    }

    /// <summary>Writes a project file to hand over or keep as a copy, without the folder its builds go to.</summary>
    /// <param name="project">The project.</param>
    /// <param name="path">The file, replaced when it exists.</param>
    /// <param name="access">Reaches the file's drive.</param>
    /// <param name="cancellationToken">Cancels the export.</param>
    /// <exception cref="TimeoutException">The file's drive did not answer in time.</exception>
    public static Task ExportAsync(CreatorProject project, string path, LibraryAccess access, CancellationToken cancellationToken) =>
        access.ReadAsync(path, async token =>
        {
            await CreatorProjectStore.SaveAsync(project with { OutputFolder = string.Empty }, path, token).ConfigureAwait(false);
            return true;
        }, cancellationToken);

    /// <summary>
    /// Brings over the project files on the list an earlier version kept, each once, whatever stops it halfway. The list is the
    /// only record of those paths, so a file leaves it only once it is in the library or surely gone or damaged.
    /// </summary>
    /// <param name="paths">The list.</param>
    /// <param name="access">Reads the files.</param>
    /// <param name="cancellationToken">Cancels the import.</param>
    public Task<RecentImport> ImportRecentAsync(IReadOnlyList<string> paths, LibraryAccess access, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        List<string> imported = [];
        List<string> skipped = [];
        List<string> waiting = [];
        foreach (var path in paths)
        {
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(path)).AsSpan(0, 16));
            var file = Path.Join(directory, id.ToString(), ProjectFileName);
            // Only the file proves an earlier import, since a stop may leave the folder that writing the file creates first.
            if (File.Exists(file))
            {
                imported.Add(path);
                continue;
            }

            try
            {
                var read = await access.ReadAsync<(CreatorProject Project, DateTime Changed)?>(path, async token =>
                {
                    try
                    {
                        return (await CreatorProjectStore.LoadAsync(path, token).ConfigureAwait(false), File.GetLastWriteTimeUtc(path));
                    }
                    // A folder that is not there may be on a drive that is not attached now, so only one that answers tells a file is gone.
                    catch (ProjectLoadException exception) when (exception.Error == ProjectLoadError.Damaged
                        || (exception.Error == ProjectLoadError.Unreadable && !File.Exists(path) && Directory.Exists(Path.GetDirectoryName(path))))
                    {
                        return null;
                    }
                }, cancellationToken).ConfigureAwait(false);
                if (read is not { } source)
                {
                    skipped.Add(path);
                    continue;
                }

                await Store(id).SaveAsync(Imported(source.Project, id, path), cancellationToken).ConfigureAwait(false);
                // Coming over changes nothing of the author's, so the library still tells when the author last did.
                File.SetLastWriteTimeUtc(file, source.Changed);
                imported.Add(path);
            }
            catch (Exception exception) when (exception is ProjectLoadException or TimeoutException or IOException or UnauthorizedAccessException)
            {
                waiting.Add(path);
            }
        }

        return new RecentImport(imported, skipped, waiting);
    }, cancellationToken);

    public void Dispose() => _buildsGate.Dispose();

    private CreatorProject Imported(CreatorProject read, Guid id, string path) => read with
    {
        Id = id,
        // Before projects had names of their own, the file's was the only one the author gave.
        Name = read.Name.Length > 0 ? read.Name : Path.GetFileNameWithoutExtension(path),
        CreatedAt = read.CreatedAt == default ? time.GetUtcNow() : read.CreatedAt,
        OutputFolder = string.Empty,
    };

    private sealed class ProjectFile(string path, Guid id) : IDocumentStore<CreatorProject>
    {
        public async Task<CreatorProject?> LoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                return await CreatorProjectStore.LoadAsync(path, cancellationToken).ConfigureAwait(false) with { Id = id };
            }
            catch (ProjectLoadException)
            {
                return null;
            }
        }

        public Task SaveAsync(CreatorProject document, CancellationToken cancellationToken) =>
            CreatorProjectStore.SaveAsync(document, path, cancellationToken);
    }
}
