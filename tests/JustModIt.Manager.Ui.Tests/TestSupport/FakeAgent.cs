using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>
/// An agent the test drives: it pushes states and events as the real one would, and records the window's requests,
/// answering each with <see cref="Answer"/>.
/// </summary>
public sealed partial class FakeAgent : ObservableObject, IAgent
{
    public event EventHandler<IReadOnlyList<JobView>>? JobsProgressed;

    public event EventHandler<NoticeView>? Noticed;

    public event EventHandler<NavigationTarget>? NavigationRequested;

    public event EventHandler? BringToFrontRequested;

    [ObservableProperty]
    public partial AgentLink Link { get; set; } = AgentLink.Connected;

    [ObservableProperty]
    public partial AppStateView State { get; private set; } = new();

    [ObservableProperty]
    public partial bool HasState { get; private set; }

    public Func<ShutdownReason, Task<bool>>? ShutdownHandler { get; set; }

    /// <summary>The requests the window sent, in order.</summary>
    public List<Message> Requests { get; } = [];

    /// <summary>The window's reports of being in front or not, in order.</summary>
    public List<bool> Activity { get; } = [];

    /// <summary>Answers a request; by default with an empty answer.</summary>
    public Func<Message, Message?> Answer { get; set; } = _ => null;

    public void Push(AppStateView state)
    {
        State = state;
        HasState = true;
    }

    public void Progress(params JobView[] jobs) => JobsProgressed?.Invoke(this, jobs);

    public void Notice(NoticeView notice) => Noticed?.Invoke(this, notice);

    public void Navigate(NavigationTarget target) => NavigationRequested?.Invoke(this, target);

    public void BringToFront() => BringToFrontRequested?.Invoke(this, EventArgs.Empty);

    public Task<Message?> RequestAsync(Message request, CancellationToken cancellationToken = default)
    {
        Requests.Add(request);
        return Task.FromResult(Answer(request));
    }

    public void ReportActivity(bool isActive) => Activity.Add(isActive);
}
