using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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

        report.IsClear.Should().BeTrue();
        FileLockProbe.ProbeFile(folder).Should().Be(FileLockState.Missing);
        _holders.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_file_held_open_is_in_use_until_it_is_closed()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows enforces sharing modes.");
        var held = _directory.Write("held.pak", "data");

        FileLockReport report;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            report = Probe(held, held.ToUpperInvariant());
        }

        report.InUse.Should().Equal(held);
        report.Holders.Should().Equal("Holder.exe");
        _holders.Calls.Should().ContainSingle().Which.Should().Equal(held);
        Probe(held).IsClear.Should().BeTrue();
    }

    [Fact]
    public void A_read_only_file_can_still_be_written_unless_it_is_held()
    {
        var readOnly = _directory.Write("read-only.pak", "data");
        File.SetAttributes(readOnly, FileAttributes.ReadOnly);
        try
        {
            FileLockProbe.ProbeFile(readOnly).Should().Be(FileLockState.ReadOnly);
            Probe(readOnly).IsClear.Should().BeTrue();
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

        public IReadOnlyList<string> FindHolders(IReadOnlyList<string> paths)
        {
            Calls.Add(paths);
            return ["Holder.exe", "holder.exe"];
        }
    }
}
