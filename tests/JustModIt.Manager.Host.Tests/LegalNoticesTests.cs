using JustModIt.Manager.Host.Tests.TestSupport;
using JustModIt.Manager.Ui;

namespace JustModIt.Manager.Host.Tests;

public sealed class LegalNoticesTests
{
    private static readonly LegalNotices Notices = new(typeof(LegalNoticesTests).Assembly);

    [Fact]
    public void An_app_embeds_the_licence_of_the_code() =>
        Notices.ReadLicense().Should().Be(File.ReadAllText(RepositoryFiles.PathOf("LICENSE.md")));

    [Fact]
    public void An_app_embeds_the_third_party_notices() =>
        Notices.ReadThirdPartyNotices().Should().Be(File.ReadAllText(RepositoryFiles.PathOf("THIRD-PARTY-NOTICES.md")));

    [Fact]
    public void An_app_embeds_the_branding_notice_of_its_own_folder() =>
        Notices.ReadBrandingNotice().Should().Be(
            File.ReadAllText(RepositoryFiles.PathOf("tests", "JustModIt.Manager.Host.Tests", "Branding", "LICENSE.md")));
}
