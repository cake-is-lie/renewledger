using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using RenewLedger.Models;
using RenewLedger.Services;

namespace RenewLedger.Tests;

public sealed class RenewalTests
{
    private static async Task<Dictionary<string, string>> ConfirmationAsync(HttpClient client, Guid id)
    {
        using var response = await client.GetAsync("/?renew=" + id + "&q=Server&filter=overdue");
        var document = await LedgerApplication.ParseAsync(response);
        var form = document.QuerySelector("#renewal form")!;
        return new()
        {
            ["id"] = form.QuerySelector("input[name=id]")!.GetAttribute("value")!,
            ["revision"] = form.QuerySelector("input[name=revision]")!.GetAttribute("value")!,
            ["q"] = "Server", ["filter"] = "overdue", ["confirm"] = "true"
        };
    }

    [Fact]
    public async Task OpeningOrCancelingConfirmationNeverChangesDataAndRepeatingThePostAdvancesOnlyOnce()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 12.34m, "USD", "monthly", new(2026, 1, 31)), false);
        var item = Assert.Single(app.Store.Read());
        var original = File.ReadAllText(app.LedgerPath);
        using var page = await client.GetAsync("/?renew=" + item.Id + "&q=Server&filter=overdue");
        var document = await LedgerApplication.ParseAsync(page);
        Assert.Contains("12.34", document.QuerySelector("#renewal")!.TextContent);
        Assert.Contains("2026-01-31", document.QuerySelector("#renewal")!.TextContent);
        Assert.Contains("2026-02-28", document.QuerySelector("#renewal")!.TextContent);
        var cancel = document.QuerySelector("#renewal .section-head a")!.GetAttribute("href")!;
        Assert.DoesNotContain("renew=", cancel);
        using var canceled = await client.GetAsync(cancel);
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));

        var fields = await ConfirmationAsync(client, item.Id);
        using var first = await LedgerApplication.PostAsync(client, "Renew", fields);
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var query = QueryHelpers.ParseQuery(new Uri("http://localhost" + first.Headers.Location).Query);
        Assert.Equal("Server", query["q"]);
        Assert.Equal("overdue", query["filter"]);
        var once = File.ReadAllText(app.LedgerPath);
        Assert.Equal(new(2026, 2, 28), Assert.Single(app.Store.Read()).Due); // Even if still overdue.
        using var repeated = await LedgerApplication.PostAsync(client, "Renew", fields);
        using var result = await client.GetAsync(repeated.Headers.Location);
        document = await LedgerApplication.ParseAsync(result);
        Assert.Contains("未重复推进日期", document.QuerySelector(".message.error")!.TextContent);
        Assert.Equal(once, File.ReadAllText(app.LedgerPath));

        fields = await ConfirmationAsync(client, item.Id);
        using var secondPeriod = await LedgerApplication.PostAsync(client, "Renew", fields);
        Assert.Equal(new(2026, 3, 31), Assert.Single(app.Store.Read()).Due);
    }

    [Theory]
    [InlineData("price")]
    [InlineData("disable")]
    [InlineData("delete")]
    [InlineData("import")]
    public async Task ChangedOrRestoredRecordsInvalidateAnOpenConfirmation(string change)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "USD", "monthly", new(2026, 12, 31)), false);
        var item = Assert.Single(app.Store.Read());
        var fields = await ConfirmationAsync(client, item.Id);
        switch (change)
        {
            case "price": app.Store.Save(item with { Amount = 20m }, true); break;
            case "disable": app.Store.Save(item with { IsActive = false }, true); break;
            case "delete": app.Store.Delete(item.Id); break;
            case "import": app.Store.Import(app.Store.Export()); break;
        }
        var changed = File.ReadAllText(app.LedgerPath);
        using var response = await LedgerApplication.PostAsync(client, "Renew", fields);
        using var page = await client.GetAsync(response.Headers.Location);
        var document = await LedgerApplication.ParseAsync(page);
        Assert.NotNull(document.QuerySelector(".message.error[role=alert]"));
        Assert.Equal(changed, File.ReadAllText(app.LedgerPath));
    }

    [Theory]
    [InlineData("confirm", "false")]
    [InlineData("revision", "00000000-0000-0000-0000-000000000000")]
    [InlineData("revision", "not-a-guid")]
    public async Task MissingOrInvalidConfirmationDoesNotChangeData(string field, string value)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "CNY", "monthly", new(2026, 12, 31)), false);
        var fields = await ConfirmationAsync(client, Assert.Single(app.Store.Read()).Id);
        fields[field] = value;
        var original = File.ReadAllText(app.LedgerPath);
        using var response = await LedgerApplication.PostAsync(client, "Renew", fields);
        using var page = await client.GetAsync(response.Headers.Location);
        Assert.NotNull((await LedgerApplication.ParseAsync(page)).QuerySelector(".message.error"));
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }

    [Fact]
    public void ConcurrentSubmissionsWithTheSameRevisionAdvanceOnlyOnce()
    {
        using var directory = new TemporaryDirectory();
        var store = new LedgerStore(Path.Combine(directory.Path, "ledger.json"));
        store.Save(new(Guid.NewGuid(), "Server", 10m, "USD", "monthly", new(2026, 1, 31)), false);
        var item = Assert.Single(store.Read());
        var succeeded = 0;
        Parallel.For(0, 20, _ =>
        {
            try { store.Renew(item.Id, item.Revision); Interlocked.Increment(ref succeeded); }
            catch (ArgumentException) { }
        });
        Assert.Equal(1, succeeded);
        Assert.Equal(new(2026, 2, 28), Assert.Single(store.Read()).Due);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("disabled")]
    [InlineData("date-limit")]
    public async Task ImpossibleRenewalsShowErrorsInsteadOfRenderingAnAction(string kind)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "USD", "monthly",
            kind == "date-limit" ? new(9999, 12, 31) : new(2026, 1, 31), IsActive: kind != "disabled"), false);
        var item = Assert.Single(app.Store.Read());
        var original = File.ReadAllText(app.LedgerPath);
        using var page = await client.GetAsync("/?renew=" + (kind == "missing" ? Guid.NewGuid() : item.Id));
        var document = await LedgerApplication.ParseAsync(page);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.NotNull(document.QuerySelector(".message.error"));
        Assert.Null(document.QuerySelector("#renewal form"));
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }

    [Theory]
    [InlineData("renew")]
    [InlineData("import")]
    [InlineData("disable")]
    public async Task StaleEditorCannotUndoAChangeAndKeepsTheUserInput(string change)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "USD", "monthly", new(2026, 12, 31)), false);
        var item = Assert.Single(app.Store.Read());
        using var editPage = await client.GetAsync("/?edit=" + item.Id);
        var document = await LedgerApplication.ParseAsync(editPage);
        Assert.Equal(item.Revision.ToString(), document.QuerySelector("#editor input[name='Input.Revision']")!.GetAttribute("value"));
        switch (change)
        {
            case "renew": app.Store.Renew(item.Id, item.Revision); break;
            case "import": app.Store.Import(app.Store.Export()); break;
            case "disable": app.Store.Save(item with { IsActive = false }, true); break;
        }
        var changed = File.ReadAllText(app.LedgerPath);
        var fields = SavePageTests.ValidFields();
        fields["Input.Id"] = item.Id.ToString();
        fields["Input.Revision"] = item.Revision.ToString();
        fields["Input.Name"] = "My unsaved change";
        fields["Input.Amount"] = "42.50";
        fields["q"] = "Server";
        fields["filter"] = "active";
        using var response = await LedgerApplication.PostAsync(client, "Save", fields);
        document = await LedgerApplication.ParseAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("未覆盖新数据", document.QuerySelector(".validation-summary-errors")!.TextContent);
        Assert.Equal("My unsaved change", document.QuerySelector("#editor input[name='Input.Name']")!.GetAttribute("value"));
        Assert.Equal(item.Revision.ToString(), document.QuerySelector("#editor input[name='Input.Revision']")!.GetAttribute("value"));
        Assert.Equal(changed, File.ReadAllText(app.LedgerPath));
    }

    [Fact]
    public async Task OutOfRangeConfirmedRenewalDoesNotPartiallyChangeTheLedger()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "USD", "yearly", new(9999, 2, 28)), false);
        var item = Assert.Single(app.Store.Read());
        var original = File.ReadAllText(app.LedgerPath);
        using var response = await LedgerApplication.PostAsync(client, "Renew", new()
        {
            ["id"] = item.Id.ToString(), ["revision"] = item.Revision.ToString(), ["confirm"] = "true"
        });
        using var page = await client.GetAsync(response.Headers.Location);
        var document = await LedgerApplication.ParseAsync(page);
        Assert.Contains("日期超出支持范围", document.QuerySelector(".message.error")!.TextContent);
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }
}
