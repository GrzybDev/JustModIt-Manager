using System.Text;
using JustModIt.Manager.Platform.Linux.Tests.DBus;
using JustModIt.Manager.Platform.Linux.Tests.DBus.Login;
using JustModIt.Manager.Platform.Linux.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace JustModIt.Manager.Platform.Linux.Tests;

public sealed class LinuxSystemEventsTests
{
    private const string Logind = "/org/freedesktop/login1";
    private const string Card = "/org/freedesktop/UDisks2/block_devices/mmcblk0p1";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_wake_from_sleep_and_a_mounted_drive_are_reported_and_going_to_sleep_or_an_unmount_are_not()
    {
        await using var bus = await PrivateBus.StartAsync(Token);
        // One connection plays logind and the disks service, so their signals reach the app in the order they are sent.
        var system = await bus.ConnectAsync();
        await system.RequestNameAsync("org.freedesktop.login1");
        await system.RequestNameAsync("org.freedesktop.UDisks2");
        await PrivateBus.LeaveReaderAsync();
        using var events = new LinuxSystemEvents(bus.Shared(), NullLogger<LinuxSystemEvents>.Instance);
        var resumes = 0;
        var arrivals = 0;
        events.Resumed += (_, _) => Interlocked.Increment(ref resumes);
        events.VolumeArrived += (_, _) => Interlocked.Increment(ref arrivals);

        // Sent until the events listen to each; a wake after that tells that every signal sent before it was heard.
        await Eventually.HoldsAsync(() =>
        {
            system.EmitPrepareForSleep(Logind, start: false);
            return Volatile.Read(ref resumes) > 0;
        });
        await Eventually.HoldsAsync(() =>
        {
            Mount();
            return Volatile.Read(ref arrivals) > 0;
        });
        var woken = resumes + 1;
        system.EmitPrepareForSleep(Logind, start: false);
        await Eventually.HoldsAsync(() => Volatile.Read(ref resumes) == woken);
        var mounted = arrivals;

        system.EmitPrepareForSleep(Logind, start: true);
        system.EmitFilesystemPropertiesChanged(Card, mountPoints: []);
        Mount();
        await Eventually.HoldsAsync(() => Volatile.Read(ref arrivals) > mounted);
        (resumes, arrivals).Should().Be((woken, mounted + 1), "going to sleep is no wake and an unmount no new drive");

        system.EmitPrepareForSleep(Logind, start: false);
        Mount();
        await Eventually.HoldsAsync(() => Volatile.Read(ref arrivals) > mounted + 1);
        resumes.Should().Be(woken + 1);

        void Mount() => system.EmitFilesystemPropertiesChanged(Card, mountPoints: [Encoding.UTF8.GetBytes("/run/media/player/CARD\0")]);
    }
}
