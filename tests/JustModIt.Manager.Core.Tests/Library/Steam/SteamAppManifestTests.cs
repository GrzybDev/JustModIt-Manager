using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamAppManifestTests
{
    private const string Complete = """
        "AppState"
        {
        	"appid"		"1000"
        	"Universe"		"1"
        	"LauncherPath"		"C:\\Program Files (x86)\\Steam\\steam.exe"
        	"name"		"Example Detective"
        	"StateFlags"		"4"
        	"installdir"		"Example.Detective"
        	"LastUpdated"		"1790000000"
        	"SizeOnDisk"		"12884901888"
        	"buildid"		"1234567"
        	"UpdateResult"		"0"
        	"BytesToDownload"		"12000000000"
        	"BytesDownloaded"		"12000000000"
        	"TargetBuildID"		"1234567"
        	"ScheduledAutoUpdate"		"0"
        	"InstalledDepots"
        	{
        		"1001"
        		{
        			"manifest"		"1234567890123456789"
        			"size"		"12500000000"
        		}
        	}
        	"UserConfig"
        	{
        		"language"		"polish"
        	}
        	"MountedConfig"
        	{
        		"language"		"english"
        	}
        }
        """;

    [Fact]
    public void A_complete_manifest_parses_to_its_values()
    {
        var manifest = SteamAppManifest.Parse(VdfParser.Parse(Complete));

        manifest.Should().BeEquivalentTo(new SteamAppManifest
        {
            AppId = 1000,
            Name = "Example Detective",
            InstallDir = "Example.Detective",
            StateFlags = SteamAppStates.FullyInstalled,
            BuildId = "1234567",
            TargetBuildId = "1234567",
            SizeOnDisk = 12884901888,
            UpdateResult = SteamUpdateResult.NoError,
            BytesDownloaded = 12000000000,
            ScheduledAutoUpdate = null,
            Language = "polish",
            InstalledDepots = ["1001@1234567890123456789"],
        });
        manifest.HasPendingTargetBuild.Should().BeFalse();
    }

    [Fact]
    public void Installed_depots_are_read_with_their_manifests()
    {
        var manifest = Parse("""
            "appid" "1000" "installdir" "Game"
            "InstalledDepots"
            {
                "11" { "manifest" "5550001" "size" "100" }
                "12" { "manifest" "5550002" "size" "20" "dlcappid" "13" }
                "14" { "manifest" "0" }
                "15" { "size" "5" }
                "sixteen" { "manifest" "5550003" }
            }
            """);

        manifest.InstalledDepots.Should().Equal("11@5550001", "12@5550002");
        Parse("\"appid\" \"1000\" \"installdir\" \"Game\"").InstalledDepots.Should().BeEmpty();
    }

    [Fact]
    public void Optional_fields_are_read()
    {
        var manifest = Parse("""
            "appid" "1000" "installdir" "Game" "StateFlags" "1542" "buildid" "0" "TargetBuildID" "77"
            "UpdateResult" "4" "BytesDownloaded" "5" "ScheduledAutoUpdate" "1790000000"
            "MountedConfig" { "language" "german" }
            """);

        manifest.StateFlags.Should().Be(SteamAppStates.UpdateRequired | SteamAppStates.FullyInstalled
            | SteamAppStates.UpdatePaused | SteamAppStates.UpdateStarted);
        manifest.BuildId.Should().BeNull();
        manifest.TargetBuildId.Should().Be("77");
        manifest.HasPendingTargetBuild.Should().BeTrue();
        manifest.UpdateResult.Should().Be(SteamUpdateResult.Suspended);
        manifest.BytesDownloaded.Should().Be(5);
        manifest.ScheduledAutoUpdate.Should().Be(DateTimeOffset.FromUnixTimeSeconds(1790000000));
        manifest.Language.Should().Be("german");
    }

    [Fact]
    public void The_name_falls_back_to_the_user_config_and_may_be_missing()
    {
        Parse("""
            "appid" "1000" "installdir" "Game" "UserConfig" { "name" "Configured Name" }
            """).Name.Should().Be("Configured Name");
        Parse("""
            "appid" "1000" "installdir" "Game" "name" "  "
            """).Name.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("....")]
    [InlineData(". .")]
    [InlineData("../x")]
    [InlineData("sub/dir")]
    [InlineData(@"sub\\dir")]
    [InlineData("/etc")]
    [InlineData(@"C:\\Windows")]
    public void An_unsafe_install_folder_is_rejected(string installDir) =>
        FluentActions.Invoking(() => Parse($"\"appid\" \"1000\" \"installdir\" \"{installDir}\""))
            .Should().Throw<FormatException>();

    [Theory]
    [InlineData("\"installdir\" \"Game\"")]
    [InlineData("\"appid\" \"0\" \"installdir\" \"Game\"")]
    [InlineData("\"appid\" \"-5\" \"installdir\" \"Game\"")]
    [InlineData("\"appid\" \"ten\" \"installdir\" \"Game\"")]
    public void A_missing_or_invalid_app_id_is_rejected(string body) =>
        FluentActions.Invoking(() => Parse(body)).Should().Throw<FormatException>();

    [Fact]
    public void A_document_without_app_state_is_rejected() =>
        FluentActions.Invoking(() => SteamAppManifest.Parse(VdfParser.Parse("\"Other\" { \"appid\" \"1000\" }")))
            .Should().Throw<FormatException>();

    private static SteamAppManifest Parse(string body) => SteamAppManifest.Parse(VdfParser.Parse($"\"AppState\" {{ {body} }}"));
}
