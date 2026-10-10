using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Settings;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>Invented states of the agent for the window to show.</summary>
public static class TestStates
{
    public static readonly GameKey Ember = new(StoreId.Steam, "4100");

    /// <summary>The moment the tests' clock stands at, a day after the invented projects' newest releases.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 29, 14, 58, 0, TimeSpan.Zero);

    /// <summary>An agent that has read everything and has nothing to do, its setup done and its version current.</summary>
    public static AppStateView Idle(bool creatorMode = false) => new()
    {
        Version = 1,
        IsLoaded = true,
        Settings = SettingsView.From(AppSettings.Default) with { OnboardingCompleted = true, CreatorMode = creatorMode },
        Update = new AppUpdateView { State = AppUpdateState.UpToDate, CurrentVersion = "0.1.0" },
    };

    public static JobView Job(JobKind kind = JobKind.Install, JobStage stage = JobStage.Installing, double? fraction = 0.42,
        long sequence = 1) => new()
    {
        Id = new Guid("8b1d5a8e-0c3f-4f7a-9c2b-1a2b3c4d5e6f"),
        Game = Ember,
        GameName = "Ember Hollow",
        Kind = kind,
        Origin = JobOrigin.User,
        Stage = stage,
        Fraction = fraction,
        Sequence = sequence,
    };

    public static AppStateView With(this AppStateView state, params JobView[] jobs) => state with { Version = state.Version + 1, Jobs = jobs };

    public static AppStateView With(this AppStateView state, AppUpdateView update) => state with { Version = state.Version + 1, Update = update };
}
