using JustModIt.Manager.Core.Packages;
using JustModIt.Manager.Core.Patching;

namespace JustModIt.Manager.Core.Tests.TestSupport;

/// <summary>An xdelta3 patch made in memory, with what the manifest says about it.</summary>
/// <param name="Content">The patch's bytes.</param>
/// <param name="Info">The sizes and hashes of its source and target, as the manifest lists them.</param>
internal sealed record TestPatch(byte[] Content, PatchInfo Info)
{
    public static async Task<TestPatch> CreateAsync(byte[] original, byte[] modified)
    {
        using var temp = new TempDirectory();
        var source = temp.Write("original", original);
        var target = temp.Write("modified", modified);
        using var patch = new MemoryStream();
        var info = await new XDelta3Patcher(memoryBudget: 64 << 20).EncodeAsync(source, target, patch, progress: null,
            TestContext.Current.CancellationToken);
        return new TestPatch(patch.ToArray(), info);
    }

    public static byte[] RandomBytes(int size, int seed)
    {
        var bytes = new byte[size];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }
}

internal sealed partial class TestPackage
{
    public TestPackage Patch(string path, TestPatch patch)
    {
        var entry = UniqueEntry(PackageLayout.PatchesFolder + path + PackageLayout.PatchExtension);
        _entries.Add((entry, patch.Content, 0));
        _operations.Add(new PatchOperation(path, entry, patch.Info.SourceSize, patch.Info.SourceSha256, patch.Info.TargetSize,
            patch.Info.TargetSha256));
        return this;
    }
}
