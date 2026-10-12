using JustModIt.Manager.Localization;

namespace JustModIt.Manager.Ui.Pages.Licenses;

/// <summary>What a component of others is for, in a few words of the app's language.</summary>
/// <param name="Text">The words.</param>
/// <param name="IsArtwork">A typeface or icons, which the page lists apart from the libraries.</param>
public sealed record ComponentUse(string Text, bool IsArtwork = false);

/// <summary>
/// What each component of <c>THIRD-PARTY-NOTICES.md</c> is for, by its title there; the notices say it only in English,
/// for the record.
/// </summary>
internal static class ComponentUses
{
    /// <summary>What the component is for; null for one the notices gained after this list.</summary>
    /// <param name="component">The component's title in the notices.</param>
    public static ComponentUse? Of(string component) => component switch
    {
        ".NET" => new(Strings.Licenses_UseDotNet),
        "Microsoft.Extensions" => new(Strings.Licenses_UseExtensions),
        "CsWin32" => new(Strings.Licenses_UseCsWin32),
        "Serilog" => new(Strings.Licenses_UseSerilog),
        "MessageFormat" => new(Strings.Licenses_UseMessageFormat),
        "Velopack" => new(Strings.Licenses_UseVelopack),
        "Velopack installer and updater" => new(Strings.Licenses_UseVelopackInstaller),
        "Avalonia" => new(Strings.Licenses_UseAvalonia),
        "MicroCom.Runtime" => new(Strings.Licenses_UseMicroCom),
        "Tmds.DBus.Protocol" => new(Strings.Licenses_UseDBus),
        "SkiaSharp and HarfBuzzSharp" => new(Strings.Licenses_UseSkiaSharp),
        "Skia (libSkiaSharp.dll)" => new(Strings.Licenses_UseSkia),
        "Libraries built into libSkiaSharp.dll" => new(Strings.Licenses_UseSkiaLibraries),
        "Wuffs (in libSkiaSharp.dll)" => new(Strings.Licenses_UseWuffs),
        "SPIRV-Cross (in libSkiaSharp.dll)" => new(Strings.Licenses_UseSpirvCross),
        "Vulkan Memory Allocator (in libSkiaSharp.dll)" => new(Strings.Licenses_UseVulkanMemory),
        "D3D12 Memory Allocator (in libSkiaSharp.dll)" => new(Strings.Licenses_UseD3D12Memory),
        "HarfBuzz (libHarfBuzzSharp.dll)" => new(Strings.Licenses_UseHarfBuzz),
        "ANGLE (av_libglesv2.dll)" => new(Strings.Licenses_UseAngle),
        "Abseil (in av_libglesv2.dll)" => new(Strings.Licenses_UseAbseil),
        "xxHash (in av_libglesv2.dll)" => new(Strings.Licenses_UseXxHash),
        "astc-encoder (in av_libglesv2.dll)" => new(Strings.Licenses_UseAstc),
        "LLVM libc++ (in av_libglesv2.dll)" => new(Strings.Licenses_UseLibCxx),
        "CommunityToolkit.Mvvm" => new(Strings.Licenses_UseMvvm),
        "Markdig" => new(Strings.Licenses_UseMarkdig),
        "xdelta3" => new(Strings.Licenses_UseXdelta),
        "liblzma (XZ Utils)" => new(Strings.Licenses_UseLzma),
        "Inter" => new(Strings.Licenses_UseInter, IsArtwork: true),
        "Lucide" => new(Strings.Licenses_UseLucide, IsArtwork: true),
        "Simple Icons" => new(Strings.Licenses_UseSimpleIcons, IsArtwork: true),
        _ => null,
    };
}
