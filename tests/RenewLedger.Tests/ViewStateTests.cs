using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using RenewLedger.Models;

namespace RenewLedger.Tests;

public sealed class ViewStateTests
{
    private const string Query = "云 & server";

    [Fact]
    public async Task EveryMutationFormAndEditLinkKeepsTheCurrentView()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var item = new Subscription(Guid.NewGuid(), Query, 10m, "CNY", "monthly",
            DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        app.Store.Save(item, false);

        using var response = await client.GetAsync(
            "/?q=" + Uri.EscapeDataString(Query) + "&filter=overdue&edit=" + item.Id);
        var document = await LedgerApplication.ParseAsync(response);

        foreach (var form in document.QuerySelectorAll("form[method=post]"))
        {
            Assert.Equal(Query, form.QuerySelector("input[name=q]")!.GetAttribute("value"));
            Assert.Equal("overdue", form.QuerySelector("input[name=filter]")!.GetAttribute("value"));
        }
        var edit = document.QuerySelector(".actions a")!.GetAttribute("href")!;
        Assert.Equal(Query, QueryHelpers.ParseQuery(new Uri("http://localhost" + edit).Query)["q"]);
        var cancel = document.QuerySelector("#editor .section-head a")!.GetAttribute("href")!;
        Assert.DoesNotContain("edit=", cancel);
        Assert.Contains("filter=overdue", cancel);
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Delete")]
    [InlineData("Renew")]
    [InlineData("Import")]
    [InlineData("Sample")]
    public async Task PostRedirectsKeepSearchAndFilter(string handler)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var item = new Subscription(Guid.NewGuid(), Query, 10m, "CNY", "monthly", new(2026, 12, 31));
        app.Store.Save(item, false);
        var fields = SavePageTests.ValidFields();
        fields["Input.Id"] = item.Id.ToString();
        fields["id"] = item.Id.ToString();
        fields["q"] = Query;
        fields["filter"] = "overdue";

        using var response = handler == "Import"
            ? await LedgerApplication.ImportAsync(client, app.Store.Export(),
                await LedgerApplication.TokenAsync(client), viewState: new() { ["q"] = Query, ["filter"] = "overdue" })
            : await LedgerApplication.PostAsync(client, handler, fields);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var redirect = new Uri("http://localhost" + response.Headers.Location!.OriginalString);
        var query = QueryHelpers.ParseQuery(redirect.Query);
        Assert.Equal(Query, query["q"]);
        Assert.Equal("overdue", query["filter"]);
    }

    [Fact]
    public async Task FailedSaveKeepsTheViewAndInvalidFilterIsNormalized()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var fields = SavePageTests.ValidFields();
        fields["Input.Amount"] = "-1";
        fields["q"] = Query;
        fields["filter"] = "unknown";

        using var response = await LedgerApplication.PostAsync(client, "Save", fields);
        var document = await LedgerApplication.ParseAsync(response);

        Assert.Equal(Query, document.QuerySelector(".tools input[name=q]")!.GetAttribute("value"));
        Assert.Equal("all", document.QuerySelector(".tools select[name=filter] option[selected]")!.GetAttribute("value"));
    }
}
