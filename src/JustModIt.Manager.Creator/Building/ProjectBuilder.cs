using System.Globalization;
using JustModIt.Manager.Core;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;
using JustModIt.Manager.Core.Platform;
using JustModIt.Manager.Creator.Comparison;
using JustModIt.Manager.Creator.Projects;

namespace JustModIt.Manager.Creator.Building;

public sealed record ProjectBuildRequest
{
    /// <summary>The project, whose record of builds the run reads and writes.</summary>
    public required Guid ProjectId { get; init; }

    /// <summary>Where the packages go; created when missing.</summary>
    public required string OutputFolder { get; init; }

    /// <summary>The parts to build by name, empty for the base, which comes first, each with its plan over each version of the game.</summary>
    public required IReadOnlyList<(string Part, IReadOnlyList<BuildVersion> Versions)> Parts { get; init; }

    /// <summary>Names a package by its part and the versions it serves; the run makes that fit for a file.</summary>
    public required Func<string, IReadOnlyList<BuildVersion>, string> PackageName { get; init; }

    /// <summary>A package's name when nothing of the given one is fit for a file.</summary>
    public required string FallbackName { get; init; }

    /// <summary>
    /// Asks, on the run's thread, whether to replace the named files at the packages' paths, which this project did not build;
    /// false ends the run with nothing written.
    /// </summary>
    public required Func<IReadOnlyList<string>, Task<bool>> ReplaceFiles { get; init; }

    /// <summary>Told on the run's thread after each package.</summary>
    public Action<FinishedPackage>? Built { get; init; }

    /// <summary>Every install the app keeps a record of; one on a game refuses that game's package.</summary>
    public IReadOnlyList<ExistingInstall> Installs { get; init; } = [];

    /// <summary>What the site takes as release files; what it does not say is not checked.</summary>
    public ReleaseFileRules Site { get; init; } = new();
}

/// <summary>A package a run finished.</summary>
/// <param name="Part">The part it belongs to.</param>
/// <param name="Package">Its row in the project's record.</param>
/// <param name="Result">What the build made.</param>
public sealed record FinishedPackage(string Part, BuiltPackage Package, PackageBuildResult Result);

/// <summary>A package of a run under way.</summary>
/// <param name="Part">The part it belongs to.</param>
/// <param name="Versions">The versions of the game it serves.</param>
/// <param name="Name">Its file's name.</param>
/// <param name="Size">Its size once it is finished; null until then.</param>
public sealed record RunPackage(string Part, IReadOnlyList<GameKey> Versions, string Name, long? Size = null);

/// <summary>How far a run has got.</summary>
/// <param name="Build">What is being done, with the bytes of the package being built; of the originals while versions are told apart.</param>
/// <param name="Package">The package being built, from 1; 0 while versions are told apart.</param>
/// <param name="PackageCount">The packages in all; 0 while versions are told apart.</param>
/// <param name="Packages">The run's packages in the order they are built; empty while versions are told apart.</param>
public readonly record struct ProjectBuildProgress(BuildProgress Build, int Package, int PackageCount, IReadOnlyList<RunPackage> Packages);

/// <summary>
/// Builds every part and version of a project in one run, one package at a time. A failure or a stop ends the run and leaves
/// the finished packages, which the project's record lists from the moment each is on the disk.
/// </summary>
/// <param name="encoder">Encodes the patches.</param>
/// <param name="options">The app's extensions, limits and hosting rules.</param>
/// <param name="library">Holds the project's record of its builds.</param>
/// <param name="access">Gives up on opening a folder whose drive does not answer; null waits for it.</param>
/// <param name="time">Dates the packages; null for the system clock.</param>
/// <param name="fileSystem">Checks the folders and the plans' files; null for the real file system.</param>
public sealed class ProjectBuilder(XDelta3Patcher encoder, CreatorOptions options, ProjectLibrary library, LibraryAccess? access = null,
    TimeProvider? time = null, IFileSystem? fileSystem = null)
{
    private readonly PackageBuilder _builder = new(encoder, options, time, fileSystem);
    private readonly LibraryAccess _access = access ?? LibraryAccess.Unlimited;
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly IFileSystem _fileSystem = fileSystem ?? PhysicalFileSystem.Instance;

    /// <summary>Builds the run's packages off the calling thread.</summary>
    /// <param name="request">The parts, where they go and what the window has to word or decide.</param>
    /// <param name="progress">Receives the run's progress; null for none.</param>
    /// <param name="cancellationToken">Stops the run, which is waited for at most <see cref="LibraryAccess.TimeLimit"/>; the finished packages stay.</param>
    /// <returns>The finished packages; none when the author kept the files that were in the way.</returns>
    /// <exception cref="PackageBuildException">A folder or a package's files do not allow the build; the failure names part and versions.</exception>
    /// <exception cref="CreatorBlockedException">A game holds an install, or another app manages files of it.</exception>
    public async Task<IReadOnlyList<FinishedPackage>> BuildAsync(ProjectBuildRequest request, IProgress<ProjectBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var run = Task.Run(() => BuildCoreAsync(request, progress, cancellationToken), cancellationToken);
        try
        {
            return await run.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        // A stopped run still removes the package it was writing, unless its drive stopped answering, which nobody waits for: a file
        // system call cannot be cancelled, so such a run is left to end by itself.
        catch (OperationCanceledException) when (!run.IsCompleted)
        {
            await Task.WhenAny(run, Task.Delay(LibraryAccess.TimeLimit, _time, CancellationToken.None)).ConfigureAwait(false);
            if (run.IsCompleted)
            {
                return await run.ConfigureAwait(false);
            }

            throw;
        }
    }

    private async Task<IReadOnlyList<FinishedPackage>> BuildCoreAsync(ProjectBuildRequest request, IProgress<ProjectBuildProgress>? progress,
        CancellationToken cancellationToken)
    {
        var output = await OpenAsync(request.OutputFolder, () => _fileSystem.GetCanonicalPath(request.OutputFolder), null, [], cancellationToken).ConfigureAwait(false);
        var plans = request.Parts.SelectMany(part => part.Versions).Select(version => version.Plan).ToList();
        var opened = new HashSet<string>(PathComparison.Comparer);
        foreach (var (part, versions) in request.Parts)
        {
            // A folder several versions read is named with all of them.
            foreach (var folder in versions.SelectMany(version => new[] { (Path: version.Plan.GameDirectory, version.Game.Key), (Path: version.Plan.AuthorDirectory, version.Game.Key) })
                .GroupBy(folder => folder.Path, PathComparison.Comparer).Where(folder => opened.Add(folder.Key)))
            {
                await OpenAsync(folder.Key, () => _fileSystem.DirectoryExists(folder.Key), part, [.. folder.Select(each => each.Key)], cancellationToken).ConfigureAwait(false);
            }
        }

        // Every plan's, not only its own package's: a package inside another version's game folder counts as a game file at
        // the next comparison, and one inside an add-on's folder would refuse only halfway through the run.
        plans.ForEach(plan => PackageBuilder.CheckOutputFolder(output, plan));

        var hashes = new ContentHashes(_fileSystem);
        var hashing = new InlineProgress<BuildProgress>(build => progress?.Report(new ProjectBuildProgress(build, 0, 0, [])));
        var taken = new HashSet<string>(PathComparison.Comparer);
        List<(string Part, BuildVersion[] Versions, string Path)> packages = [];
        foreach (var (part, versions) in request.Parts)
        {
            foreach (var group in await PackageGroups.GroupAsync(versions, hashes, hashing, cancellationToken).ConfigureAwait(false))
            {
                var extension = options.ExtensionFor(group[0].Plan.Format.Kind);
                var stem = FileNames.Suggest(request.PackageName(part, group), request.FallbackName, string.Empty);
                var name = stem + extension;
                // Two names may clean to one file, and the later package would overwrite the earlier.
                for (var number = 2; !taken.Add(name); number++)
                {
                    name = $"{stem} {number.ToString(CultureInfo.InvariantCulture)}{extension}";
                }

                packages.Add((part, group, Path.Join(output, name)));
            }
        }

        var listed = (await library.ReadBuildAsync(request.ProjectId, cancellationToken).ConfigureAwait(false))?.Parts.SelectMany(part => part.Packages).ToList() ?? [];
        var foreign = await OpenAsync(output, () => packages
            .Where(package => _fileSystem.GetEntry(package.Path) is { IsDirectory: false } file
                && !listed.Exists(row => PathComparison.AreSame(row.Path, package.Path) && row.Size == file.Length))
            .Select(package => Path.GetFileName(package.Path)).ToList(), null, [], cancellationToken).ConfigureAwait(false);
        if (foreign.Count > 0 && !await request.ReplaceFiles(foreign).WaitAsync(cancellationToken).ConfigureAwait(false))
        {
            return [];
        }

        var release = plans[0].Metadata.Version.Trim();
        // Reports reach the window later, so each finished package makes a new list rather than changing one a report holds.
        IReadOnlyList<RunPackage> run = [.. packages.Select(package => new RunPackage(package.Part, [.. package.Versions.Select(version => version.Game.Key)], Path.GetFileName(package.Path)))];
        List<FinishedPackage> finished = [];
        foreach (var part in packages.GroupBy(package => package.Part))
        {
            List<(BuildVersion[] Versions, PackageBuildResult Result)> built = [];
            foreach (var (_, versions, path) in part)
            {
                var number = finished.Count + 1;
                var bytes = new InlineProgress<BuildProgress>(build => progress?.Report(new ProjectBuildProgress(build, number, packages.Count, run)));
                var keys = versions.Select(version => version.Game.Key).ToList();
                PackageBuildResult result;
                try
                {
                    result = await _builder.BuildAsync(versions[0].Plan, new BuildRequest
                    {
                        OutputPath = path,
                        Game = versions[0].Game,
                        AlsoFor = versions[1..],
                        Installs = request.Installs,
                        Site = request.Site,
                    }, bytes, cancellationToken).ConfigureAwait(false);
                }
                catch (PackageBuildException failed)
                {
                    throw new PackageBuildException(failed.Failure, failed.Paths, failed.Findings) { Part = part.Key, Versions = [.. keys] };
                }

                var row = new BuiltPackage(result.OutputPath, result.Kind, result.PackageSize, keys, release, _time.GetUtcNow());
                // Not to be stopped: from here on the package is on the disk, and the record has to say so.
                await library.UpdateBuildAsync(request.ProjectId, record => (record ?? new BuildRecord([])).WithPackage(part.Key, row), CancellationToken.None)
                    .ConfigureAwait(false);
                built.Add((versions, result));
                finished.Add(new FinishedPackage(part.Key, row, result));
                request.Built?.Invoke(finished[^1]);
                run = [.. run.Select((package, index) => index == number - 1 ? package with { Size = result.PackageSize } : package)];
            }

            // What the build computed anyway; a file it did not carry has no hash, so nothing is read for it.
            var carried = new Dictionary<string, string>(PathComparison.Comparer);
            foreach (var (source, written) in built.SelectMany(package => package.Versions[0].Plan.Operations.Select(operation => operation.SourceFile)
                .Zip(package.Result.Operations)))
            {
                if (source is not null && written switch { CopyOperation copy => copy.Sha256, PatchOperation patch => patch.TargetSha256, _ => null } is { } hash)
                {
                    carried[source.FullPath] = hash;
                }
            }

            List<FolderBuild> stamps =
            [
                .. built.SelectMany(package => package.Versions).Select(version => version.Plan)
                    .DistinctBy(plan => plan.AuthorDirectory, PathComparison.Comparer)
                    .Select(plan => new FolderBuild(plan.AuthorDirectory, plan.Compared.Where(file => file.Author is not null).ToDictionary(file => file.AuthorPath!,
                        file => new BuiltFile(file.Author!.Value.Size, file.Author.Value.LastWriteTime,
                            carried.GetValueOrDefault(PackagePath.ToFullPath(plan.AuthorDirectory, file.AuthorPath!)))))),
            ];
            await library.UpdateBuildAsync(request.ProjectId, record => (record ?? new BuildRecord([])).WithFolders(part.Key, release, stamps),
                CancellationToken.None).ConfigureAwait(false);
        }

        return finished;
    }

    // Only the first look at a folder has a time limit, since a build may well outlast it.
    private async Task<T> OpenAsync<T>(string folder, Func<T> read, string? part, IReadOnlyList<GameKey> versions, CancellationToken cancellationToken)
    {
        try
        {
            return await _access.ReadAsync(folder, _ => Task.FromResult(read()), cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            throw new PackageBuildException(BuildFailure.FolderUnavailable, [folder]) { Part = part, Versions = [.. versions] };
        }
    }
}
