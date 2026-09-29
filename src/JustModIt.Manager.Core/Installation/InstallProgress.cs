namespace JustModIt.Manager.Core.Installation;

public enum InstallStage
{
    Checking,
    Installing,
    RollingBack,
    Uninstalling,
    Verifying,
    MovingBackups,
}

/// <summary>How far an operation of the install engine has got.</summary>
/// <param name="Stage">What the engine does.</param>
/// <param name="CurrentFile">The game-relative path being worked on.</param>
/// <param name="FilesDone">Files of the stage done so far.</param>
/// <param name="FileCount">Files the stage works on.</param>
/// <param name="BytesDone">Bytes of the stage done so far.</param>
/// <param name="BytesTotal">Bytes the stage works on; 0 when it counts files only.</param>
public readonly record struct InstallProgress(
    InstallStage Stage,
    string? CurrentFile,
    int FilesDone,
    int FileCount,
    long BytesDone,
    long BytesTotal);

/// <summary>Counts files and bytes of one stage, reported on the worker; the caller decides how often to show them.</summary>
/// <param name="sink">Receives every report; null to report nothing.</param>
/// <param name="stage">The stage counted.</param>
/// <param name="fileCount">The files the stage works on.</param>
/// <param name="bytesTotal">The bytes the stage works on.</param>
internal sealed class ProgressTracker(IProgress<InstallProgress>? sink, InstallStage stage, int fileCount, long bytesTotal)
{
    private string? _file;
    private int _filesDone;
    private long _bytesDone;

    /// <summary>Counts the bytes reported to it as done.</summary>
    public IProgress<long> Bytes => field ??= new InlineProgress<long>(AddBytes);

    /// <summary>Reports that work on a file starts.</summary>
    /// <param name="path">The game-relative path.</param>
    public void StartFile(string path)
    {
        _file = path;
        Report();
    }

    /// <summary>Reports that work on the current file is done.</summary>
    public void FinishFile()
    {
        _filesDone++;
        Report();
    }

    /// <summary>Counts bytes as done.</summary>
    /// <param name="bytes">The bytes done since the last report.</param>
    public void AddBytes(long bytes)
    {
        _bytesDone += bytes;
        Report();
    }

    private void Report() =>
        sink?.Report(new InstallProgress(stage, _file, Math.Min(_filesDone, fileCount), fileCount, Math.Min(_bytesDone, bytesTotal),
            bytesTotal));
}
