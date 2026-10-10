using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Library.Watching;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Devices.DeviceAndDriverInstallation;
using Windows.Win32.Foundation;
using Windows.Win32.System.Power;
using Windows.Win32.UI.WindowsAndMessaging;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Resume from sleep and volume arrival, through callback registrations that need no window, so the agent can hear of
/// them without a message loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WindowsSystemEvents : ISystemEvents, IDisposable
{
    private readonly Lock _gate = new();
    private readonly ILogger _logger;
    private readonly CM_Unregister_NotificationSafeHandle _volumes;
    private GCHandle _self;

    // Kept for as long as the registration lasts, since the system is not documented to copy it.
    private nint _parameters;
    private HPOWERNOTIFY _power;

    /// <summary>Registers for both events; <see cref="IsListening"/> tells whether the system accepted them.</summary>
    /// <param name="logger">Logs a registration the system refused and a listener that failed.</param>
    public WindowsSystemEvents(ILogger<WindowsSystemEvents> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _self = GCHandle.Alloc(this);
        var context = GCHandle.ToIntPtr(_self);
        _parameters = Marshal.AllocHGlobal(Unsafe.SizeOf<DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS>());
        if (RegisterForResume(_parameters, context, out _power) is var powerError and not WIN32_ERROR.ERROR_SUCCESS)
        {
            _power = default;
            LogNotRegistered("resume from sleep", (uint)powerError);
        }

        if (RegisterForVolumes(context, out _volumes) is var volumeError and not CONFIGRET.CR_SUCCESS)
        {
            LogNotRegistered("volume arrival", (uint)volumeError);
        }
    }

    public event EventHandler? Resumed;

    public event EventHandler? VolumeArrived;

    /// <summary>Whether the system accepted both registrations.</summary>
    public bool IsListening => !_power.IsNull && !_volumes.IsInvalid;

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_self.IsAllocated)
            {
                return;
            }

            // Unregistering waits for callbacks in progress, so the handle below outlives them.
            _volumes.Dispose();
            if (!_power.IsNull)
            {
                _ = PInvoke.PowerUnregisterSuspendResumeNotification(_power);
                _power = default;
            }

            Marshal.FreeHGlobal(_parameters);
            _parameters = 0;
            _self.Free();
        }
    }

    // Windows calls back through the parameters, and only unsafe code can take a function's address and fill them.
    private static unsafe WIN32_ERROR RegisterForResume(nint parameters, nint context, out HPOWERNOTIFY registration)
    {
        *(DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS*)parameters = new() { Callback = &OnPowerEvent, Context = (void*)context };
        void* handle = null;
        var result = PInvoke.PowerRegisterSuspendResumeNotification(REGISTER_NOTIFICATION_FLAGS.DEVICE_NOTIFY_CALLBACK,
            (HANDLE)parameters, &handle);
        registration = (HPOWERNOTIFY)(nint)handle;
        return result;
    }

    // Windows calls back through a function pointer, whose address only unsafe code can take.
    private static unsafe CONFIGRET RegisterForVolumes(nint context, out CM_Unregister_NotificationSafeHandle registration)
    {
        var filter = new CM_NOTIFY_FILTER
        {
            cbSize = (uint)sizeof(CM_NOTIFY_FILTER),
            FilterType = CM_NOTIFY_FILTER_TYPE.CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
        };
        filter.u.DeviceInterface.ClassGuid = PInvoke.GUID_DEVINTERFACE_VOLUME;
        return PInvoke.CM_Register_Notification(filter, (void*)context, &OnDeviceEvent, out registration);
    }

    // Windows passes the context and the power setting as pointers, which only unsafe code can receive.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe uint OnPowerEvent(void* context, uint type, void* setting)
    {
        if (type is PInvoke.PBT_APMRESUMEAUTOMATIC or PInvoke.PBT_APMRESUMESUSPEND)
        {
            Raise((nint)context, events => events.Resumed);
        }

        return 0;
    }

    // Windows passes the context and the event's data as pointers, which only unsafe code can receive.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe uint OnDeviceEvent(HCMNOTIFICATION notification, void* context, CM_NOTIFY_ACTION action,
        CM_NOTIFY_EVENT_DATA* eventData, uint eventDataSize)
    {
        if (action == CM_NOTIFY_ACTION.CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL)
        {
            Raise((nint)context, events => events.VolumeArrived);
        }

        return 0;
    }

    private static void Raise(nint context, Func<WindowsSystemEvents, EventHandler?> pick)
    {
        if (GCHandle.FromIntPtr(context).Target is not WindowsSystemEvents events)
        {
            return;
        }

        try
        {
            pick(events)?.Invoke(events, EventArgs.Empty);
        }
        // An exception unwinding into the system's callback would end the process; a missed event only delays a rescan
        // until the next trigger.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            events.LogListenerFailed(exception);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The system refused to report {SystemEvent} (error {Error}); only other triggers rescan the library meanwhile")]
    private partial void LogNotRegistered(string systemEvent, uint error);

    [LoggerMessage(Level = LogLevel.Error, Message = "A listener of a system event failed")]
    private partial void LogListenerFailed(Exception exception);
}
