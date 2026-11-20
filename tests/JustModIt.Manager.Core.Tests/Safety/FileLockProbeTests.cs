using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Safety;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.Safety;

public sealed class FileLockProbeTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly RecordingHolders _holders = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void Free_and_missing_files_and_folders_are_not_reported()
    {
        var free = _directory.Write("free.pak", "data");
        var folder = _directory.CreateDirectory("folder");

        var report = Probe(free, _directory.Combine("missing.pak"), folder);
        Probe();

        (report.InUse.Count, report.AccessDenied.Count).Should().Be((0, 0));
        FileLockProbe.ProbeFile(folder).Should().Be(FileLockState.Missing);
        _holders.Calls.Should().HaveCount(OperatingSystem.IsWindows() ? 0 : 1,
            "only Windows tells files in use without asking for holders, and no files need no holders");
    }

    [Fact]
    public void A_file_held_open_is_in_use_until_it_is_closed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows enforces sharing modes.");
        var held = _directory.Write("held.pak", "data");

        FileLockReport report;
        using (Hold(held))
        {
            report = Probe(held, held.ToUpperInvariant());
        }

        report.InUse.Should().Equal(held);
        report.Holders.Should().Equal("Holder.exe");
        _holders.Calls.Should().ContainSingle().Which.Should().Equal(held);
        Probe(held).InUse.Should().BeEmpty();
    }

    [Fact]
    public void Every_file_in_use_is_named_in_one_holder_lookup()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows enforces sharing modes.");
        var first = _directory.Write("first.pak", "data");
        var second = _directory.Write("second.pak", "data");
        var free = _directory.Write("free.pak", "data");

        FileLockReport report;
        using (Hold(first))
        using (Hold(second))
        {
            report = Probe(first, free, second);
        }

        report.InUse.Should().Equal(first, second);
        _holders.Calls.Should().ContainSingle("the system allows only so many lookups at once").Which.Should().Equal(first, second);
    }

    [Fact]
    [SupportedOSPlatform("linux")]
    public void On_linux_every_file_is_asked_about_and_one_a_program_holds_is_in_use()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux finds the files in use by the programs holding them.");
        Assert.SkipWhen(Environment.IsPrivilegedProcess, "Root reads every file.");
        var held = _directory.Write("held.pak", "data");
        var free = _directory.Write("free.pak", "data");
        var unreadable = _directory.Write("unreadable.pak", "data");
        var missing = _directory.Combine("missing.pak");
        File.SetUnixFileMode(unreadable, UnixFileMode.None);

        FileLockReport report;
        using (Hold(held))
        {
            report = Probe(held, free, unreadable, missing);
        }

        report.InUse.Should().Equal(held);
        report.AccessDenied.Should().Equal(unreadable);
        report.Holders.Should().Equal("Holder.exe");
        _holders.Calls.Should().ContainSingle().Which.Should().Equal(held, free, unreadable, missing);
    }

    [Fact]
    public void A_read_only_file_can_still_be_written_unless_it_is_held()
    {
        var readOnly = _directory.Write("read-only.pak", "data");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            // Elsewhere a file's own permissions do not keep it from being replaced, only its folder's.
            FileLockProbe.ProbeFile(readOnly).Should().Be(OperatingSystem.IsWindows() ? FileLockState.ReadOnly : FileLockState.Free);
            (Probe(readOnly).InUse.Count, Probe(readOnly).AccessDenied.Count).Should().Be((0, 0));
            if (OperatingSystem.IsWindows())
            {
                using var reader = new FileStream(readOnly, FileMode.Open, FileAccess.Read, FileShare.Read);
                FileLockProbe.ProbeFile(readOnly).Should().Be(FileLockState.InUse);
            }
        }
        finally
        {
            File.SetAttributes(readOnly, FileAttributes.Normal);
        }
    }

    [Fact]
    public void A_file_the_user_may_not_write_is_access_denied_not_in_use()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("The test uses Windows access control lists.");
            return;
        }

        var denied = _directory.Write("denied.pak", "data");
        DenyWriting(denied, deny: true);
        try
        {
            var report = Probe(denied);

            report.AccessDenied.Should().Equal(denied);
            report.InUse.Should().BeEmpty();
            report.Holders.Should().BeEmpty();
        }
        finally
        {
            DenyWriting(denied, deny: false);
        }
    }

    public void Dispose() => _directory.Dispose();

    private FileLockReport Probe(params string[] paths) => new FileLockProbe(_holders).Probe(paths, Token);

    // A program holding a file without a lock, which only the system's list of holders tells outside Windows.
    private FileStream Hold(string path)
    {
        _holders.Held.Add(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    [SupportedOSPlatform("windows")]
    private static void DenyWriting(string path, bool deny)
    {
        var file = new FileInfo(path);
        var security = file.GetAccessControl();
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.WriteData, AccessControlType.Deny);
        if (deny)
        {
            security.AddAccessRule(rule);
        }
        else
        {
            security.RemoveAccessRule(rule);
        }

        file.SetAccessControl(security);
    }

    private sealed class RecordingHolders : ILockHolderSource
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public HashSet<string> Held { get; } = new(PathComparison.Comparer);

        // Two processes of one program may spell it differently.
        public IReadOnlyList<FileHolder> FindHolders(IReadOnlyList<string> paths)
        {
            Calls.Add(paths);
            return [.. paths.Where(Held.Contains).SelectMany(path => new[] { new FileHolder("Holder.exe", path), new FileHolder("holder.exe", path) })];
        }
    }
}
