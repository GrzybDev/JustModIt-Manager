using System.Runtime.Versioning;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Core.Library.Watching;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Registry;
using Win32 = Microsoft.Win32;

namespace JustModIt.Manager.Platform.Windows;

/// <summary>
/// Watches registry keys with thread-agnostic change notifications waited on by the thread pool, so no thread is kept
/// per key and stopping never waits for one.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistryWatcher : IRegistryWatcher
{
    public static WindowsRegistryWatcher Instance { get; } = new();

    public IDisposable Watch(WatchedRegistryKey key, Action<WatchSignal> onSignal)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(onSignal);
        return new KeyWatch(key, onSignal);
    }

    /// <summary>
    /// Watches the key, or while it is missing its nearest existing ancestor for the next key on the way, moving down as
    /// keys appear, e.g. when a store is installed.
    /// </summary>
    private sealed class KeyWatch : IDisposable
    {
        private readonly WatchedRegistryKey _target;
        private readonly string _targetPath;
        private readonly Action<WatchSignal> _onSignal;
        private readonly Lock _gate = new();
        private readonly AutoResetEvent _changed = new(initialState: false);
        private readonly RegisteredWaitHandle _wait;
        private Win32.RegistryKey? _key;
        private string _keyPath = string.Empty;
        private bool _waitedOnAncestor;
        private bool _disposed;

        public KeyWatch(WatchedRegistryKey target, Action<WatchSignal> onSignal)
        {
            _target = target;
            _targetPath = target.Path.Trim('\\');
            _onSignal = onSignal;
            lock (_gate)
            {
                Arm();
            }

            _wait = ThreadPool.RegisterWaitForSingleObject(_changed, (_, _) => OnChanged(), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        private bool IsOnTarget => _key is not null && _keyPath.Length == _targetPath.Length;

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _key?.Dispose();
                _key = null;
            }

            _wait.Unregister(null);
            _changed.Dispose();
        }

        private void OnChanged()
        {
            bool report;
            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                var wasOnTarget = IsOnTarget;
                if (!wasOnTarget)
                {
                    // Something below the ancestor changed; the next key on the way may exist now.
                    CloseKey();
                }

                report = Arm() || wasOnTarget;
            }

            if (report)
            {
                _onSignal(WatchSignal.Changed);
            }
        }

        // Asks for the next notification, opening the nearest existing key first when none is open; true when the
        // target was reached after watching an ancestor.
        private bool Arm()
        {
            var reached = false;
            while (true)
            {
                if (_key is null)
                {
                    if (!OpenNearest())
                    {
                        return reached;
                    }

                    reached |= IsOnTarget && _waitedOnAncestor;
                    _waitedOnAncestor = !IsOnTarget;
                }

                var onTarget = IsOnTarget;
                var changes = onTarget
                    ? REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_LAST_SET
                    : REG_NOTIFY_FILTER.REG_NOTIFY_CHANGE_NAME;
                // Thread-agnostic, or the notification would end with the thread that asked for it, and pool threads come
                // and go.
                var result = PInvoke.RegNotifyChangeKeyValue(_key!.Handle, onTarget && _target.IncludeSubkeys,
                    changes | REG_NOTIFY_FILTER.REG_NOTIFY_THREAD_AGNOSTIC, _changed.SafeWaitHandle, fAsynchronous: true);
                if (result == WIN32_ERROR.ERROR_KEY_DELETED)
                {
                    CloseKey();
                    continue;
                }

                if (result != WIN32_ERROR.ERROR_SUCCESS)
                {
                    // Anything else would fail again at once; the periodic rescan covers the key.
                    CloseKey();
                    return reached;
                }

                // A key created after the ancestor was opened but before the notification was armed is never reported.
                if (!onTarget && NextKeyExists())
                {
                    CloseKey();
                    continue;
                }

                return reached;
            }
        }

        private bool OpenNearest()
        {
            Win32.RegistryKey root;
            try
            {
                root = Win32.RegistryKey.OpenBaseKey(WindowsRegistry.ToWin32(_target.Hive), WindowsRegistry.ToWin32(_target.View));
            }
            catch (Exception exception) when (WindowsRegistry.IsUnreadable(exception))
            {
                return false;
            }

            for (var path = _targetPath; path.Length > 0; path = ParentOf(path))
            {
                if (TryOpen(root, path) is { } key)
                {
                    root.Dispose();
                    _key = key;
                    _keyPath = path;
                    return true;
                }
            }

            _key = root;
            _keyPath = string.Empty;
            return true;
        }

        private bool NextKeyExists()
        {
            var rest = _keyPath.Length == 0 ? _targetPath : _targetPath[(_keyPath.Length + 1)..];
            using var next = TryOpen(_key!, rest.Split('\\', 2)[0]);
            return next is not null;
        }

        private void CloseKey()
        {
            _key?.Dispose();
            _key = null;
        }

        private static Win32.RegistryKey? TryOpen(Win32.RegistryKey parent, string path)
        {
            try
            {
                return parent.OpenSubKey(path);
            }
            catch (Exception exception) when (WindowsRegistry.IsUnreadable(exception))
            {
                return null;
            }
        }

        private static string ParentOf(string path) => path.LastIndexOf('\\') is var separator and >= 0 ? path[..separator] : string.Empty;
    }
}
