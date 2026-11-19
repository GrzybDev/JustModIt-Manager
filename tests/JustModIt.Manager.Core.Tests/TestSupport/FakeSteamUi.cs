using System.Collections.Concurrent;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Steam;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>A way into Steam's interface whose status the test sets and whose launch options change as asked.</summary>
internal sealed class FakeSteamUi : ISteamUi
{
    private readonly Lock _gate = new();
    private SteamUiStatus _status = SteamUiStatus.None;

    public event EventHandler<SteamUiStatus>? Changed;

    public SteamUiStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    /// <summary>The changes of launch options asked for, in order.</summary>
    public ConcurrentQueue<(GameKey Game, IReadOnlyList<string> Add, IReadOnlyList<string> Remove)> Changes { get; } = new();

    /// <summary>Why the next changes fail; null lets them through.</summary>
    public LaunchOptionsFailure? Failure { get; set; }

    public void Publish(SteamUiStatus status)
    {
        lock (_gate)
        {
            _status = status;
        }

        Changed?.Invoke(this, status);
    }

    public SteamRestartResult RestartSteam() => SteamRestartResult.Restarting;

    public Task<LaunchOptionsResult> ChangeLaunchOptionsAsync(GameKey game, IReadOnlyList<string> add, IReadOnlyList<string> remove,
        CancellationToken cancellationToken)
    {
        Changes.Enqueue((game, add, remove));
        if (Failure is { } failure)
        {
            return Task.FromResult(new LaunchOptionsResult(null, failure));
        }

        SteamUiStatus changed;
        lock (_gate)
        {
            List<string> options = [.. _status.LaunchOptions.GetValueOrDefault(game, "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(option => !remove.Contains(option))];
            options.AddRange(add.Where(option => !options.Contains(option)));
            changed = _status = _status with { LaunchOptions = _status.LaunchOptions.SetItem(game, string.Join(' ', options)) };
        }

        Changed?.Invoke(this, changed);
        return Task.FromResult(new LaunchOptionsResult(changed.LaunchOptions[game]));
    }
}
