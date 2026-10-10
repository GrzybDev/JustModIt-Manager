using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace JustModIt.Manager.Platform.Windows.Interop;

/// <summary>
/// Wraps system COM objects that this code alone holds, so that each one is released when the code lets it go rather
/// than when the collector finds its wrapper, as the objects of .NET's COM wrappers otherwise are.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ComObjects
{
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>Wraps an object whose reference the caller hands over.</summary>
    /// <param name="instance">The object's interface, which the wrapper holds from now on.</param>
    public static T Own<T>(nint instance)
        where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    /// <summary>Releases an object wrapped by <see cref="Own{T}"/> at once; null is ignored.</summary>
    /// <param name="owned">The wrapper, which cannot be used afterwards.</param>
    public static void Release(object? owned) => (owned as ComObject)?.FinalRelease();
}
