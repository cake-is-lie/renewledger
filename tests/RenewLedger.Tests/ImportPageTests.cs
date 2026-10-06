using System.Net;
using System.Text.Json;
using RenewLedger.Models;

namespace RenewLedger.Tests;

public sealed class ImportPageTests
{
    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("{\"version\":2,\"items\":[]}")]
    [InlineData("{\"version\":1,\"items\":null}")]
    [InlineData("{\"version\":1,\"items\":[null]}")]
    [InlineData("")]
    public async Task InvalidBackupDoesNotChangeTheLedger(string json)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "CNY", "monthly", new(2026, 12, 31)), false);
        var original = File.ReadAllText(app.LedgerPath);

        using var response = await LedgerApplication.ImportAsync(client, json, await LedgerApplication.TokenAsync(client));
        using var page = await client.GetAsync(response.Headers.Location);
        var document = await LedgerApplication.ParseAsync(page);

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.NotNull(document.QuerySelector(".message.error[role=alert]"));
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("too-many")]
    [InlineData("too-large")]
    [InlineData("not-confirmed")]
    public async Task UnsafeImportsAreRejected(string kind)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var item = new Subscription(Guid.NewGuid(), "Server", 10m, "CNY", "monthly", new(2026, 12, 31));
        app.Store.Save(item, false);
        var original = File.ReadAllText(app.LedgerPath);
        List<Subscription> items = kind switch
        {
            "duplicate" => [item, item],
            "invalid" => [item with { Currency = "INVALID" }],
            "too-many" => Enumerable.Range(0, 1001).Select(_ => item with { Id = Guid.NewGuid() }).ToList(),
            _ => []
        };
        var json = kind == "too-large" ? new string(' ', 2 * 1024 * 1024 + 1)
            : JsonSerializer.Serialize(new LedgerBackup(1, items), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var response = await LedgerApplication.ImportAsync(client, json,
            await LedgerApplication.TokenAsync(client), replace: kind != "not-confirmed");
        using var page = await client.GetAsync(response.Headers.Location);
        var document = await LedgerApplication.ParseAsync(page);

        Assert.NotNull(document.QuerySelector(".message.error[role=alert]"));
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }
}
