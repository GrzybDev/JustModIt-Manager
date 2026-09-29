using JustModIt.Manager.Core.Library.Steam;

namespace JustModIt.Manager.Core.Tests.Library.Steam;

public sealed class SteamAppManifestTests
{
    private const string Complete = """
        "AppState"
        {
        	"appid"		"110800"
        	"Universe"		"1"
        	"LauncherPath"		"C:\\Program Files (x86)\\Steam\\steam.exe"
        	"name"		"Example Detective"
        	"StateFlags"		"4"
        	"installdir"		"Example.Detective"
        	"LastUpdated"		"1790092126"
        	"SizeOnDisk"		"14730944938"
        	"buildid"		"13185632"
        	"UpdateResult"		"0"
        	"BytesToDownload"		"14006601632"
        	"BytesDownloaded"		"14006601632"
        	"TargetBuildID"		"13185632"
        	"ScheduledAutoUpdate"		"0"
        	"InstalledDepots"
        	{
        		"110801"
        		{
        			"manifest"		"7802845176688782381"
        			"size"		"14466771789"
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
            AppId = 110800,
            Name = "Example Detective",
            InstallDir = "Example.Detective",
            StateFlags = SteamAppStates.FullyInstalled,
            BuildId = "13185632",
            TargetBuildId = "13185632",
            SizeOnDisk = 14730944938,
            UpdateResult = SteamUpdateResult.NoError,
            BytesDownloaded = 14006601632,
            ScheduledAutoUpdate = null,
            Language = "polish",
        });
        manifest.HasPendingTargetBuild.Should().BeFalse();
    }

    [Fact]
    public void Optional_fields_are_read()
    {
        var manifest = Parse("""
            "appid" "10" "installdir" "Game" "StateFlags" "1542" "buildid" "0" "TargetBuildID" "77"
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
            "appid" "10" "installdir" "Game" "UserConfig" { "name" "Configured Name" }
            """).Name.Should().Be("Configured Name");
        Parse("""
            "appid" "10" "installdir" "Game" "name" "  "
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
        FluentActions.Invoking(() => Parse($"\"appid\" \"10\" \"installdir\" \"{installDir}\""))
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
        FluentActions.Invoking(() => SteamAppManifest.Parse(VdfParser.Parse("\"Other\" { \"appid\" \"10\" }")))
            .Should().Throw<FormatException>();

    private static SteamAppManifest Parse(string body) => SteamAppManifest.Parse(VdfParser.Parse($"\"AppState\" {{ {body} }}"));
}
