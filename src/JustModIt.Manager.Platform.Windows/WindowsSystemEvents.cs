using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using JustModIt.Manager.Core.Library.Watching;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Resume from sleep and volume arrival, through callback registrations that need no window, so the agent can hear of
/// them without a message loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe partial class WindowsSystemEvents : ISystemEvents, IDisposable
{
    private const uint DeviceNotifyCallback = 2;
    private const uint ResumeSuspend = 0x7;
    private const uint ResumeAutomatic = 0x12;
    private const int DeviceInterfaceFilter = 0;
    private const int DeviceInterfaceArrival = 0;
    private const int ConfigurationSuccess = 0;

    private static readonly Guid VolumeInterface = new("53f5630d-b6bf-11d0-94f2-00a0c91efb8b");

    private readonly Lock _gate = new();
    private GCHandle _self;
    private DeviceNotifySubscribeParameters* _parameters;
    private nint _power;
    private nint _volumes;

    /// <summary>Registers for both events; <see cref="IsListening"/> tells whether the system accepted them.</summary>
    public WindowsSystemEvents()
    {
        _self = GCHandle.Alloc(this);
        _parameters = (DeviceNotifySubscribeParameters*)NativeMemory.Alloc((nuint)sizeof(DeviceNotifySubscribeParameters));
        _parameters->Callback = &OnPowerEvent;
        _parameters->Context = GCHandle.ToIntPtr(_self);
        if (PowerRegisterSuspendResumeNotification(DeviceNotifyCallback, (nint)_parameters, out _power) != 0)
        {
            _power = 0;
        }

        var filter = new CmNotifyFilter
        {
            Size = (uint)sizeof(CmNotifyFilter),
            FilterType = DeviceInterfaceFilter,
            ClassGuid = VolumeInterface,
        };
        if (CM_Register_Notification(&filter, GCHandle.ToIntPtr(_self), &OnDeviceEvent, out _volumes) != ConfigurationSuccess)
        {
            _volumes = 0;
        }
    }

    public event EventHandler? Resumed;

    public event EventHandler? VolumeArrived;

    /// <summary>Whether the system accepted both registrations.</summary>
    public bool IsListening => _power != 0 && _volumes != 0;

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_self.IsAllocated)
            {
                return;
            }

            // Unregistering waits for callbacks in progress, so the handle below outlives them.
            if (_volumes != 0)
            {
                _ = CM_Unregister_Notification(_volumes);
                _volumes = 0;
            }

            if (_power != 0)
            {
                _ = PowerUnregisterSuspendResumeNotification(_power);
                _power = 0;
            }

            NativeMemory.Free(_parameters);
            _parameters = null;
            _self.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnPowerEvent(nint context, uint type, nint setting)
    {
        if (type is ResumeAutomatic or ResumeSuspend)
        {
            Raise(context, events => events.Resumed);
        }

        return 0;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint OnDeviceEvent(nint notification, nint context, int action, nint eventData, uint eventDataSize)
    {
        if (action == DeviceInterfaceArrival)
        {
            Raise(context, events => events.VolumeArrived);
        }

        return 0;
    }

    private static void Raise(nint context, Func<WindowsSystemEvents, EventHandler?> pick)
    {
        try
        {
            if (GCHandle.FromIntPtr(context).Target is WindowsSystemEvents events)
            {
                pick(events)?.Invoke(events, EventArgs.Empty);
            }
        }
        // An exception unwinding into the system's callback would end the process; a missed event only delays a rescan
        // until the next trigger.
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceNotifySubscribeParameters
    {
        public delegate* unmanaged[Stdcall]<nint, uint, nint, uint> Callback;
        public nint Context;
    }

    [StructLayout(LayoutKind.Explicit, Size = 416)]
    private struct CmNotifyFilter
    {
        [FieldOffset(0)]
        public uint Size;

        [FieldOffset(4)]
        public uint Flags;

        [FieldOffset(8)]
        public int FilterType;

        [FieldOffset(12)]
        public uint Reserved;

        [FieldOffset(16)]
        public Guid ClassGuid;
    }

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerRegisterSuspendResumeNotification(uint flags, nint recipient, out nint registrationHandle);

    [LibraryImport("powrprof.dll")]
    private static partial uint PowerUnregisterSuspendResumeNotification(nint registrationHandle);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Register_Notification(CmNotifyFilter* filter, nint context,
        delegate* unmanaged[Stdcall]<nint, nint, int, nint, uint, uint> callback, out nint notificationContext);

    [LibraryImport("cfgmgr32.dll")]
    private static partial int CM_Unregister_Notification(nint notificationContext);
}
