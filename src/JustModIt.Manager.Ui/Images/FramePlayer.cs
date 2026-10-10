using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace JustModIt.Manager.Ui.Images;

/// <summary>
/// Plays the frames of a moving picture or a clip into a bitmap of its own: each frame decodes on the thread pool ahead of
/// its time, shows for its duration and tells the owner to draw again. It shows the first frame at once; paused, it keeps
/// the frame it shows and decodes nothing.
/// </summary>
internal sealed class FramePlayer : IDisposable
{
    // Browsers show a frame meant for less than this for a tenth of a second, which GIFs made for them rely on.
    private static readonly TimeSpan ShortestFrame = TimeSpan.FromMilliseconds(11);
    private static readonly TimeSpan DefaultFrame = TimeSpan.FromMilliseconds(100);

    // A player that falls this far behind, e.g. on a busy machine, starts counting again instead of racing to catch up.
    private static readonly TimeSpan MostBehind = TimeSpan.FromSeconds(1);

    private readonly IFrameSource _source;
    private readonly WriteableBitmap _bitmap;
    private readonly byte[] _pixels;
    private readonly Action _shown;
    private readonly Action _failed;
    private readonly Lock _gate = new();
    private TaskCompletionSource _wake = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource _pause = new();
    private bool _playing;
    private bool _disposed;
    private bool _finished;
    private int _frames;

    /// <summary>Starts decoding the first frame.</summary>
    /// <param name="source">The frames, disposed with the player.</param>
    /// <param name="shown">Runs on the thread pool each time a frame is shown.</param>
    /// <param name="failed">Runs on the thread pool when not even the first frame decodes, as with a clip cut short.</param>
    public FramePlayer(IFrameSource source, Action shown, Action failed)
    {
        _source = source;
        _shown = shown;
        _failed = failed;
        _bitmap = new WriteableBitmap(source.Size, new Vector(96, 96), PixelFormats.Bgra8888, AlphaFormat.Premul);
        _pixels = new byte[source.Size.Width * source.Size.Height * 4];
        _ = Task.Run(RunAsync);
    }

    /// <summary>The frame shown; empty until <see cref="Frames"/> is more than 0.</summary>
    public Bitmap Bitmap => _bitmap;

    /// <summary>The picture's or the clip's own size.</summary>
    public PixelSize NaturalSize => _source.NaturalSize;

    /// <summary>How many frames have been shown so far.</summary>
    public int Frames => Volatile.Read(ref _frames);

    /// <summary>Plays on from the frame shown.</summary>
    public void Play()
    {
        lock (_gate)
        {
            if (_playing || _disposed)
            {
                return;
            }

            _playing = true;
            _pause = new CancellationTokenSource();
            _wake.TrySetResult();
        }
    }

    /// <summary>Keeps the frame shown until <see cref="Play"/>.</summary>
    public void Pause()
    {
        lock (_gate)
        {
            if (!_playing)
            {
                return;
            }

            _playing = false;
            _wake = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _pause.Cancel();
        }
    }

    public void Dispose()
    {
        bool finished;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _playing = false;
            _pause.Cancel();
            _wake.TrySetResult();
            finished = _finished;
        }

        // The bitmap goes with whichever of the loop and this call comes last, as the loop may be writing to it.
        if (finished)
        {
            _bitmap.Dispose();
        }
    }

    private async Task RunAsync()
    {
        try
        {
            if (_source.DecodeNext(_pixels) is not { } duration)
            {
                return;
            }

            Show();
            var due = Stopwatch.GetTimestamp() + Ticks(duration);
            var decoded = false;
            while (true)
            {
                Task wake;
                CancellationToken pause;
                bool playing;
                lock (_gate)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    (wake, pause, playing) = (_wake.Task, _pause.Token, _playing);
                }

                if (!playing)
                {
                    await wake.ConfigureAwait(false);
                    // A frame decoded before the pause shows as soon as the player plays on.
                    due = Stopwatch.GetTimestamp();
                    continue;
                }

                if (!decoded)
                {
                    if (_source.DecodeNext(_pixels) is not { } next)
                    {
                        return;
                    }

                    (duration, decoded) = (next, true);
                }

                var wait = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), due);
                if (wait > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(wait, pause).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        continue;
                    }
                }

                Show();
                decoded = false;
                var now = Stopwatch.GetTimestamp();
                due = Stopwatch.GetElapsedTime(due, now) > MostBehind ? now + Ticks(duration) : due + Ticks(duration);
            }
        }
        // A file damaged past its first frames, or one the cache removed meanwhile, stops on the frame shown.
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or InvalidOperationException)
        {
        }
        finally
        {
            _source.Dispose();
            bool disposed;
            lock (_gate)
            {
                _finished = true;
                disposed = _disposed;
            }

            if (disposed)
            {
                _bitmap.Dispose();
            }
            else if (Frames == 0)
            {
                _failed();
            }
        }
    }

    // The bitmap's own lock keeps the renderer from drawing it half written.
    private void Show()
    {
        using (var buffer = _bitmap.Lock())
        {
            var row = _source.Size.Width * 4;
            for (var y = 0; y < _source.Size.Height; y++)
            {
                Marshal.Copy(_pixels, y * row, buffer.Address + (y * buffer.RowBytes), row);
            }
        }

        Interlocked.Increment(ref _frames);
        _shown();
    }

    private static long Ticks(TimeSpan duration) =>
        (long)((duration < ShortestFrame ? DefaultFrame : duration).TotalSeconds * Stopwatch.Frequency);
}
