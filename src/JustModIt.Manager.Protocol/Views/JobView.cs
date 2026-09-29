using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.State;

namespace JustModIt.Manager.Protocol.Views;

/// <summary>A job as it is at one moment; <see cref="Sequence"/> tells a late snapshot apart.</summary>
public sealed record JobView
{
    public required Guid Id { get; init; }

    public required GameKey Game { get; init; }

    public required string GameName { get; init; }

    public required JobKind Kind { get; init; }

    public required JobOrigin Origin { get; init; }

    public JobStage Stage { get; init; }

    public JobWaitReason? WaitReason { get; init; }

    public IReadOnlyList<string> RunningProcesses { get; init; } = [];

    /// <summary>From 0 to 1; null while the stage cannot tell how far it is.</summary>
    public double? Fraction { get; init; }

    public long BytesDone { get; init; }

    public long? BytesTotal { get; init; }

    public long Sequence { get; init; }

    /// <summary>Maps a job as it is now.</summary>
    /// <param name="job">The job's newest snapshot.</param>
    public static JobView From(JobSnapshot job)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new JobView
        {
            Id = job.Id,
            Game = job.Game,
            GameName = job.GameName,
            Kind = job.Kind,
            Origin = job.Origin,
            Stage = job.Stage,
            WaitReason = job.WaitReason,
            RunningProcesses = [.. job.RunningProcesses],
            Fraction = job.Fraction,
            BytesDone = job.BytesDone,
            BytesTotal = job.BytesTotal,
            Sequence = job.Sequence,
        };
    }
}

/// <summary>What went wrong, with the details that apply to <see cref="Reason"/>.</summary>
/// <param name="Reason">The typed reason, for the app to say in its own words.</param>
public sealed record JobFailureView(JobFailureReason Reason)
{
    /// <summary>Game-relative paths of the files concerned.</summary>
    public IReadOnlyList<string> Files { get; init; } = [];

    public IReadOnlyList<string> Processes { get; init; } = [];

    public GameInstallState? GameState { get; init; }

    public GameStateReason? StateReason { get; init; }

    public SelectionUnavailability? Unavailability { get; init; }

    public CatalogueErrorReason? CatalogueError { get; init; }

    public long? RequiredBytes { get; init; }

    public long? AvailableBytes { get; init; }

    public string? Location { get; init; }

    public string? OtherApp { get; init; }

    /// <summary>Maps the failure a job ended with.</summary>
    /// <param name="failure">The failure with its details.</param>
    public static JobFailureView From(JobFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new JobFailureView(failure.Reason)
        {
            Files = [.. failure.Files],
            Processes = [.. failure.Processes],
            GameState = failure.GameState,
            StateReason = failure.StateReason,
            Unavailability = failure.Unavailability,
            CatalogueError = failure.CatalogueError,
            RequiredBytes = failure.RequiredBytes,
            AvailableBytes = failure.AvailableBytes,
            Location = failure.Location,
            OtherApp = failure.OtherApp,
        };
    }
}

/// <summary>A job on the game that did not end well, kept until a later job on it succeeds or the player dismisses it.</summary>
/// <param name="Kind">What the job did.</param>
/// <param name="Origin">Whether the player or the automation started it.</param>
/// <param name="At">When it ended.</param>
/// <param name="Failure">Null when the job was cancelled.</param>
public sealed record FailureView(JobKind Kind, JobOrigin Origin, DateTimeOffset At, JobFailureView? Failure = null)
{
    /// <summary>An update or repair removed the installed release and could not put it back.</summary>
    public bool LostPrevious { get; init; }

    /// <summary>Maps a failed attempt kept on a game or install.</summary>
    /// <param name="attempt">The attempt as the engine's state keeps it.</param>
    public static FailureView From(FailedAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        return new FailureView(attempt.Kind, attempt.Origin, attempt.At,
            attempt.Failure is { } failure ? JobFailureView.From(failure) : null)
        {
            LostPrevious = attempt.LostPrevious,
        };
    }
}
