using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Core.Games;
using JustModIt.Manager.Core.Library;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Tests.Views;

public sealed class DetectionReportViewTests
{
    [Fact]
    public void Each_store_takes_the_folder_tip_of_the_sites_first_store_of_its_kind_and_its_launcher_only_on_Windows()
    {
        var report = new DetectionReport(
        [
            new StoreDetection(StoreId.Steam, IsFound: true, ScanFailed: false, IsComplete: true, [], GameCount: 0),
            new StoreDetection(StoreId.Gog, IsFound: false, ScanFailed: false, IsComplete: true, [], GameCount: 0),
            new StoreDetection(new StoreId("example-store"), IsFound: false, ScanFailed: false, IsComplete: true, [], GameCount: 0),
        ], ScannedAt: null);
        StorefrontPlatform[] storefronts =
        [
            new() { Uuid = Guid.NewGuid(), Name = "Other Store" },
            new() { Uuid = Guid.NewGuid(), Name = "GOG", Provider = "gog", LauncherName = "GOG Galaxy" },
            new() { Uuid = Guid.NewGuid(), Name = "Steam", Provider = "steam", InstallHint = "Open the game's **Properties**." },
            new() { Uuid = Guid.NewGuid(), Name = "Steam Early Access", Provider = "steam", InstallHint = "Another tip." },
        ];

        var stores = DetectionReportView.From(report, storefronts).Stores;

        // The site names the programs that install its stores' games on Windows, which other systems do not have.
        var windows = OperatingSystem.IsWindows();
        stores.Select(store => (store.Name, store.Launcher, store.InstallHint)).Should().Equal(
            ("Steam", windows ? "Steam" : "", "Open the game's **Properties**."),
            ("GOG", windows ? "GOG Galaxy" : "", ""),
            ("", "", ""));
    }
}
