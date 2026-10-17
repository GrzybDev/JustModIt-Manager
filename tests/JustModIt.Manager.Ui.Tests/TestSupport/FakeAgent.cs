using CommunityToolkit.Mvvm.ComponentModel;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Transport;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>
/// An agent the test drives: it pushes states and events as the real one would, and records the window's requests,
/// answering each with <see cref="Answer"/>. Its states, events, requests and answers go through the pipe's codec, so a
/// message the wire cannot carry fails the test.
/// </summary>
public sealed partial class FakeAgent : ObservableObject, IAgent
{
    private long _lastRequestId;

    public event EventHandler<IReadOnlyList<JobView>>? JobsProgressed;

    public event EventHandler<JobEnded>? JobEnded;

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

    /// <summary>The window closes for an exit it asked for and accepts the agent's request to close at once.</summary>
    public bool ShutdownAccepted { get; private set; }

    /// <summary>Answers a request; by default with an empty answer.</summary>
    public Func<Message, Message?> Answer { get; set; } = _ => null;

    public void Push(AppStateView state)
    {
        State = Sent(new StateChanged(state)).State;
        HasState = true;
    }

    public void Progress(params JobView[] jobs)
    {
        var progress = Sent(new JobProgress(jobs));
        JobsProgressed?.Invoke(this, progress.Jobs);
    }

    public void End(JobEnded ended)
    {
        var sent = Sent(ended);
        JobEnded?.Invoke(this, sent);
    }

    public void Notice(NoticeView notice)
    {
        var notify = Sent(new Notify(notice));
        Noticed?.Invoke(this, notify.Notice);
    }

    public void Navigate(NavigationTarget target)
    {
        var navigate = Sent(new Navigate(target));
        NavigationRequested?.Invoke(this, navigate.Target);
    }

    public void BringToFront()
    {
        Sent(new BringToFront());
        BringToFrontRequested?.Invoke(this, EventArgs.Empty);
    }

    public Task<Message?> RequestAsync(Message request, CancellationToken cancellationToken = default)
    {
        var sent = SentFrame(new RequestFrame(++_lastRequestId, request));
        Requests.Add(sent.Payload);
        ResponseFrame response;
        try
        {
            response = new ResponseFrame(sent.Id, Answer(sent.Payload));
        }
        catch (RequestFailedException ex)
        {
            response = new ResponseFrame(sent.Id, Error: new RemoteError(ex.Code, ex.Detail));
        }

        var answered = SentFrame(response);
        if (answered.Error is { } error)
        {
            throw new RequestFailedException(error.Code, error.Detail);
        }

        return Task.FromResult(answered.Payload);
    }

    public void ReportActivity(bool isActive) => Activity.Add(Sent(new WindowActivity(isActive)).IsActive);

    public void AcceptShutdown() => ShutdownAccepted = true;

    private static TMessage Sent<TMessage>(TMessage message) where TMessage : Message =>
        (TMessage)SentFrame(new EventFrame(message)).Payload;

    private static TFrame SentFrame<TFrame>(TFrame frame) where TFrame : Frame =>
        (TFrame)FrameCodec.Deserialize(FrameCodec.Serialize(frame, FrameCodec.DefaultMaxFrameLength));
}
