namespace JustModIt.Manager.Host.Tests;

public sealed class AppImageProgramTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "ExampleApp.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void The_thumbnails_are_those_velopacks_start_script_names_after_the_appimage()
    {
        // The name is what `echo -n "file://$APPIMAGE" | md5sum` prints in the AppImage's start script.
        AppImageProgram.Thumbnails("/home/player", "/home/player/Applications/ExampleApp.AppImage").Should().Equal(
            Path.Join("/home/player", ".cache", "thumbnails", "normal", "1c39304ecf734464e85f2d6678e002dd.png"),
            Path.Join("/home/player", ".cache", "thumbnails", "large", "1c39304ecf734464e85f2d6678e002dd.png"));
    }

    [Fact]
    public void Removing_the_appimage_takes_its_updates_and_thumbnails_only_once_the_file_itself_went()
    {
        var applications = Path.Combine(_home, "Applications");
        var file = Path.Combine(applications, "ExampleApp.AppImage");
        var updates = Path.Combine(_home, "velopack", "ExampleApp");
        var other = Path.Combine(applications, "OtherApp.AppImage");
        var otherThumbnail = Path.Combine(_home, ".cache", "thumbnails", "normal", "0123456789abcdef0123456789abcdef.png");
        string[] leftovers = [Path.Combine(updates, "packages", "ExampleApp-1.0.0-full.nupkg"), Path.Combine(updates, "updater", "usr", "bin", "UpdateNix"),
            .. AppImageProgram.Thumbnails(_home, file)];
        foreach (var path in leftovers.Append(file).Append(other).Append(otherThumbnail))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "");
        }

        var program = new AppImageProgram(file, updates, _home);
        using (new Protection(applications, file))
        {
            program.Invoking(appImage => appImage.Remove()).Should().Throw<UnauthorizedAccessException>();
        }

        leftovers.Append(file).Should().OnlyContain(path => File.Exists(path), "a program that stays runs and updates as before");
        program.Remove();

        leftovers.Append(file).Should().NotContain(path => File.Exists(path));
        Directory.Exists(updates).Should().BeFalse();
        File.Exists(other).Should().BeTrue();
        File.Exists(otherThumbnail).Should().BeTrue();
    }

    public void Dispose()
    {
        if (Directory.Exists(_home))
        {
            Directory.Delete(_home, recursive: true);
        }
    }

    // Windows refuses to delete a read-only file; Linux, a file in a folder the user may not write to.
    private sealed class Protection : IDisposable
    {
        private readonly string _folder;
        private readonly string _file;
        private readonly UnixFileMode _mode;

        public Protection(string folder, string file)
        {
            (_folder, _file) = (folder, file);
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(file, FileAttributes.ReadOnly);
            }
            else
            {
                _mode = File.GetUnixFileMode(folder);
                File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                File.SetAttributes(_file, FileAttributes.Normal);
            }
            else
            {
                File.SetUnixFileMode(_folder, _mode);
            }
        }
    }
}
