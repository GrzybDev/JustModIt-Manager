using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Creator;
using JustModIt.Manager.Protocol.Activation;
using JustModIt.Manager.Protocol.Messages;
using JustModIt.Manager.Protocol.Views;
using JustModIt.Manager.Ui.Connection;
using JustModIt.Manager.Ui.Navigation;
using JustModIt.Manager.Ui.Overlays;
using JustModIt.Manager.Ui.Shell;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace JustModIt.Manager.Ui.Tests.TestSupport;

/// <summary>
/// The real window over a <see cref="FakeAgent"/>, put together the way the window's process does it, at the mockups'
/// size of 1280x800. Renders go to the folder in <c>UI_RENDERS</c>, or to <c>renders</c> beside the tests.
/// </summary>
public sealed class UiHarness : IDisposable
{
    public UiHarness(AppStateView? state = null, Action<UiComposition>? compose = null, double width = 1280, double height = 800,
        NavigationTarget? launch = null, Func<UiContext, UiContext>? context = null, Func<Message, Message?>? answer = null)
    {
        if (state is not null)
        {
            Agent.Push(state);
        }

        if (answer is not null)
        {
            Agent.Answer = answer;
        }

        var built = Context(compose, launch);
        Session = UiSession.Create(Application.Current!, context is null ? built : context(built), Agent, Images, Links, Clock, Pickers);
        Window = Session.CreateWindow();
        // A click here moves, presses and releases, each after running the dispatcher and a frame; under load that takes
        // longer than a tooltip's delay, and the tooltip, which opens inside the window here, can slide over the pointer
        // and take the press.
        ToolTip.SetServiceEnabled(Window, false);
        Window.Width = width;
        Window.Height = height;
        Window.Show();
        Settle();
    }

    public FakeAgent Agent { get; } = new();

    public FakeImages Images { get; } = new();

    /// <summary>The addresses the window asked to open outside itself.</summary>
    public RecordingLinks Links { get; } = new();

    /// <summary>Answers the window's pickers with what the test chose.</summary>
    public FakePickers Pickers { get; } = new();

    /// <summary>A clock standing at <see cref="TestStates.Now"/>, in UTC.</summary>
    public FakeTimeProvider Clock { get; } = new(TestStates.Now);

    public UiSession Session { get; }

    public MainWindow Window { get; }

    public ShellViewModel Shell => Session.Shell;

    public static string RendersDirectory =>
        Environment.GetEnvironmentVariable("UI_RENDERS") is { Length: > 0 } folder ? folder : Path.Combine(AppContext.BaseDirectory, "renders");

    /// <summary>How long a test waits for the window, or for work it started off the UI thread, before it fails.</summary>
    public static TimeSpan TimeLimit { get; } = TimeSpan.FromSeconds(30);

    /// <summary>A context of an invented app whose logo is the generic app's.</summary>
    public static UiContext Context(Action<UiComposition>? compose = null, NavigationTarget? launch = null)
    {
        var links = new AppLinks("example-app");
        return new UiContext
        {
            ProductName = "Example App",
            Version = "0.1.0",
            PipeName = "ExampleApp.Tests",
            Links = links,
            Creator = CreatorOptions.For(new Core.AppIdentity("ExampleApp", "ExampleVendor", "example-app", true), "Example App 0.1.0",
                ".examplemod", ".exampleproj"),
            Launch = new LaunchArguments(ProcessRole.Ui, Background: false, launch),
            Loggers = NullLoggerFactory.Instance,
            Notices = new LegalNotices(typeof(UiHarness).Assembly),
            Branding = new AppBranding(typeof(UiHarness).Assembly),
            Site = new CatalogueApiOptions(new Uri("https://api.example.com/"), new Uri("https://example.com/"), "ExampleApp", "0.1.0"),
            ImageCacheDirectory = Path.Combine(AppContext.BaseDirectory, "image-cache"),
            ImageHosts = ["images.example.com"],
            Compose = compose,
        };
    }

    /// <summary>Lets the dispatcher and the renderer catch up with what the test did.</summary>
    public static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Lets animations and timers that run on real time finish.</summary>
    /// <param name="duration">How long to let them run.</param>
    public static async Task WaitAsync(TimeSpan duration)
    {
        var until = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < until)
        {
            await Task.Delay(16);
            Settle();
        }
    }

    /// <summary>Waits until a condition holds, keeping the dispatcher going, and fails after <see cref="TimeLimit"/>.</summary>
    /// <param name="condition">What the window, or the work the test started off the UI thread, has to get to.</param>
    /// <param name="state">What the window shows instead, for the timeout's message.</param>
    public static async Task WaitUntilAsync(Func<bool> condition, Func<string>? state = null)
    {
        // Most waits are for work off the UI thread (plans, comparisons, builds, the disk), which a busy machine stretches to
        // seconds, while a passing wait ends as soon as its condition holds.
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            if (clock.Elapsed > TimeLimit)
            {
                var shown = state is null ? "" : $": {state()}";
                throw new TimeoutException($"The window did not get there in time{shown}.");
            }

            await Task.Delay(10);
            Settle();
        }
    }

    public void Push(AppStateView state)
    {
        Agent.Push(state);
        Settle();
    }

    public IEnumerable<T> Find<T>() => Window.GetVisualDescendants().OfType<T>();

    public T Find<T>(string name)
        where T : Control =>
        Window.GetVisualDescendants().OfType<T>().Single(control => control.Name == name);

    /// <summary>The controls a player can reach that a screen reader would read out without a name.</summary>
    public IEnumerable<Control> Unnamed() =>
        Find<Control>().Where(control => control is Button or ToggleButton or ToggleSwitch or TextBox && control.IsEffectivelyVisible
            && string.IsNullOrWhiteSpace(Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(control).GetName()));

    /// <summary>A real click in the middle of a control, scrolled into view first, so hit testing and click handlers run.</summary>
    /// <param name="control">The control to click.</param>
    public void Click(Control control)
    {
        control.BringIntoView();
        Settle();
        // A control that has just appeared still slides in from below, which scrolling into view does not count, so near the
        // bottom of a view it can stand past the edge, where the click would land on something else, until its entrance ends.
        var clock = Stopwatch.StartNew();
        while (!ClickLandsOn(control) && clock.Elapsed < TimeSpan.FromSeconds(1))
        {
            Thread.Sleep(16);
            Settle();
        }

        ClickAt(Middle(control));
    }

    private bool ClickLandsOn(Control control) =>
        Window.InputHitTest(Middle(control)) is Visual hit && (hit == control || control.IsVisualAncestorOf(hit));

    private Point Middle(Control control) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), Window)
            ?? throw new InvalidOperationException("The control is not in the window.");

    /// <summary>A real click at a point of the window.</summary>
    public void ClickAt(Point point)
    {
        Window.MouseMove(point);
        Window.MouseDown(point, MouseButton.Left);
        Window.MouseUp(point, MouseButton.Left);
        Settle();
    }

    public void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        Window.KeyPress(key, modifiers, PhysicalKey.None, null);
        Window.KeyRelease(key, modifiers, PhysicalKey.None, null);
        Settle();
    }

    /// <summary>Saves what the window shows as a PNG and returns its path.</summary>
    /// <param name="name">The file's name without its extension.</param>
    /// <param name="height">A height to render a taller window at, as the boards show a whole scrolled page; null keeps it.</param>
    public string Render(string name, double? height = null)
    {
        var kept = Window.Height;
        if (height is { } taller)
        {
            Window.Height = taller;
        }

        var path = Render(Window, name);
        Window.Height = kept;
        Settle();
        return path;
    }

    /// <summary>Saves what a window shows as a PNG and returns its path.</summary>
    /// <param name="window">A window of the test's own.</param>
    /// <param name="name">The file's name without its extension.</param>
    public static string Render(Window window, string name)
    {
        Settle();
        Settle();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Nothing was rendered.");
        Directory.CreateDirectory(RendersDirectory);
        var path = Path.Combine(RendersDirectory, name + ".png");
        frame.Save(path, new PngBitmapEncoderOptions());
        return path;
    }

    public void Dispose()
    {
        // Without an agent the window closes without asking what closing it means.
        Agent.Link = AgentLink.Connecting;
        Window.Close();
        Settle();
        // A page keeping unsaved work asks first. Left open, the window goes on animating, and the headless session runs
        // the frames left over while it sets the next test up, without the fonts; so the work is let go, and the question
        // has left before the test ends.
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (Shell.Dialogs.Current is { } question && DateTime.UtcNow < until)
        {
            LetGo(question);
            Thread.Sleep(16);
            Settle();
        }

        Session.Dispose();
        Settle();
    }

    private static void LetGo(DialogViewModel question)
    {
        switch (question)
        {
            case { IsClosing: true }:
                break;
            case ChoiceDialogViewModel choice when choice.Choices.FirstOrDefault(option => option.IsDanger) is { } letGo:
                choice.ChooseCommand.Execute(letGo);
                break;
            case ConfirmDialogViewModel confirm:
                confirm.ConfirmCommand.Execute(null);
                break;
            default:
                question.Cancel();
                break;
        }
    }
}

/// <summary>Records the addresses, clients and folders the window opens instead of starting programs.</summary>
public sealed class RecordingLinks : IExternalLinks
{
    public List<Uri> Opened { get; } = [];

    public List<ClientLink> Clients { get; } = [];

    public List<string> Folders { get; } = [];

    public void Open(Uri address) => Opened.Add(address);

    public void Open(ClientLink client) => Clients.Add(client);

    public void OpenFolder(string folder) => Folders.Add(folder);
}

/// <summary>Answers the window's pickers with what the test chose, as if the player picked it, and records what they asked.</summary>
public sealed class FakePickers : IFilePickers
{
    /// <summary>The folder the player picks; null cancels.</summary>
    public string? Folder { get; set; }

    public List<string> Asked { get; } = [];

    /// <summary>The file the player opens; null cancels.</summary>
    public string? File { get; set; }

    /// <summary>The file the player saves to; null cancels.</summary>
    public string? SaveFile { get; set; }

    /// <summary>The names the save pickers suggested, in order.</summary>
    public List<string> Suggested { get; } = [];

    public Task<string?> PickFolderAsync(string title, string? startIn = null)
    {
        Asked.Add(title);
        return Task.FromResult(Folder);
    }

    public Task<string?> PickFileAsync(string title, FileKind? kind = null, string? startIn = null)
    {
        Asked.Add(title);
        return Task.FromResult(File);
    }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, FileKind kind, string? startIn = null)
    {
        Asked.Add(title);
        Suggested.Add(suggestedName);
        return Task.FromResult(SaveFile);
    }
}
