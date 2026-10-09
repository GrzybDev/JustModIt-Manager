using JustModIt.Manager.Core.Catalog;
using JustModIt.Manager.Protocol.Views;

namespace JustModIt.Manager.Protocol.Tests.Views;

public sealed class CatalogueViewsTests
{
    [Fact]
    public void Filter_options_keep_the_order_the_site_lists_them_in()
    {
        var reference = new CatalogueReference
        {
            Statuses =
            [
                new ProjectStatus { Uuid = Guid.NewGuid(), Name = "Completed", Position = 5 },
                new ProjectStatus { Uuid = Guid.NewGuid(), Name = "Announced", Position = 0 },
            ],
        };

        CatalogueFiltersView.From(reference).Statuses.Select(status => status.Name).Should().Equal("Completed", "Announced");
    }
}
