using JustModIt.Manager.Core.IO;
using JustModIt.Manager.Core.Jobs;
using JustModIt.Manager.Core.Tests.TestSupport;

namespace JustModIt.Manager.Core.Tests.IO;

public sealed class IOErrorsTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    [Fact]
    public void A_file_another_handle_holds_exclusively_is_a_sharing_violation()
    {
        var path = _directory.Write("held.pak", "data");
        using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);

        var exception = Assert.Throws<IOException>(() => File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));

        IOErrors.IsSharingViolation(exception).Should().BeTrue();
        (IOErrors.IsAccessDenied(exception), IOErrors.IsDiskFull(exception)).Should().Be((false, false));
    }

    [Fact]
    public void Deleting_a_folder_that_is_not_empty_is_no_full_disk()
    {
        _directory.Write("folder/file.txt", "data");

        var exception = Assert.Throws<IOException>(() => Directory.Delete(_directory.Combine("folder")));

        JobFailures.From(exception).Reason.Should().Be(JobFailureReason.DiskError);
    }

    public void Dispose() => _directory.Dispose();
}
