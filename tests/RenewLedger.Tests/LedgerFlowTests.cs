using System.Net;
using System.Text.Json;
using RenewLedger.Models;

namespace RenewLedger.Tests;

public sealed class LedgerFlowTests
{
    [Fact]
    public async Task CreateEditRenewExportDeleteAndImportRoundTripThroughHttp()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var fields = SavePageTests.ValidFields();
        using var created = await LedgerApplication.PostAsync(client, "Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var item = Assert.Single(app.Store.Read());
        Assert.Equal("Cloud server", item.Name);
        Assert.Equal(12.50m, item.Amount);

        using var editPage = await client.GetAsync("/?edit=" + item.Id);
        var document = await LedgerApplication.ParseAsync(editPage);
        Assert.Equal(item.Id.ToString(), document.QuerySelector("input[name='Input.Id']")!.GetAttribute("value"));
        Assert.Equal("yearly", document.QuerySelector("select[name='Input.Cycle'] option[selected]")!.GetAttribute("value"));

        fields = SavePageTests.ValidFields();
        fields["Input.Id"] = item.Id.ToString();
        fields["Input.Name"] = "服务器 <script>alert(1)</script>";
        fields["Input.Amount"] = "23.45";
        fields["Input.Cycle"] = "monthly";
        using var edited = await LedgerApplication.PostAsync(client, "Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        item = Assert.Single(app.Store.Read());
        Assert.Equal(23.45m, item.Amount);
        using var listPage = await client.GetAsync("/");
        document = await LedgerApplication.ParseAsync(listPage);
        Assert.Equal(item.Name, document.QuerySelector(".row h3")!.TextContent);
        Assert.Empty(document.QuerySelectorAll("script"));

        using var renewed = await LedgerApplication.PostAsync(client, "Renew", new()
        {
            ["id"] = item.Id.ToString(), ["revision"] = item.Revision.ToString(), ["confirm"] = "true"
        });
        Assert.Equal(HttpStatusCode.Redirect, renewed.StatusCode);
        Assert.Equal(new DateOnly(2027, 1, 31), Assert.Single(app.Store.Read()).Due);

        using var exported = await client.GetAsync("/?handler=Export");
        Assert.Equal(HttpStatusCode.OK, exported.StatusCode);
        Assert.Equal("application/json", exported.Content.Headers.ContentType!.MediaType);
        Assert.Contains("renewledger-", exported.Content.Headers.ContentDisposition!.FileNameStar!);
        var json = await exported.Content.ReadAsStringAsync();
        var backup = JsonSerializer.Deserialize<LedgerBackup>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var expected = Assert.Single(LedgerRules.ValidateBackup(backup));

        using var deleted = await LedgerApplication.PostAsync(client, "Delete", new() { ["id"] = item.Id.ToString() });
        Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
        Assert.Empty(app.Store.Read());
        using var imported = await LedgerApplication.ImportAsync(client, json, await LedgerApplication.TokenAsync(client));
        Assert.Equal(HttpStatusCode.Redirect, imported.StatusCode);
        var restored = Assert.Single(app.Store.Read());
        Assert.Equal(expected with { Revision = restored.Revision }, restored);
    }

    [Fact]
    public async Task PostWithoutAntiforgeryTokenIsRejected()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        using var content = new FormUrlEncodedContent(SavePageTests.ValidFields());

        using var response = await client.PostAsync("/?handler=Save", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(File.Exists(app.LedgerPath));
    }

    [Theory]
    [InlineData("Delete", "not-a-guid")]
    [InlineData("Renew", "not-a-guid")]
    [InlineData("Delete", "00000000-0000-0000-0000-000000000000")]
    [InlineData("Renew", "00000000-0000-0000-0000-000000000000")]
    public async Task InvalidRecordIdsAreRejectedWithoutChangingData(string handler, string id)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "CNY", "monthly", new(2026, 12, 31)), false);
        var original = File.ReadAllText(app.LedgerPath);

        using var response = await LedgerApplication.PostAsync(client, handler, new() { ["id"] = id });
        using var page = await client.GetAsync(response.Headers.Location);
        var document = await LedgerApplication.ParseAsync(page);

        Assert.Contains("请求参数不正确", document.QuerySelector(".message.error[role=alert]")!.TextContent);
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }

    [Fact]
    public async Task LockedLedgerShowsReadAndWriteErrorsWithoutLosingInputOrData()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        app.Store.Save(new(Guid.NewGuid(), "Server", 10m, "CNY", "monthly", new(2026, 12, 31)), false);
        var original = File.ReadAllText(app.LedgerPath);
        var fields = SavePageTests.ValidFields();
        fields["__RequestVerificationToken"] = await LedgerApplication.TokenAsync(client);

        using (var locked = File.Open(app.LedgerPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var page = await client.GetAsync("/");
            var document = await LedgerApplication.ParseAsync(page);
            Assert.Contains("账本文件无法读取", document.QuerySelector(".message.error[role=alert]")!.TextContent);

            using var content = new FormUrlEncodedContent(fields);
            using var response = await client.PostAsync("/?handler=Save", content);
            document = await LedgerApplication.ParseAsync(response);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("写入失败", document.QuerySelector(".validation-summary-errors")!.TextContent);
            Assert.Equal("Cloud server", document.QuerySelector("input[name='Input.Name']")!.GetAttribute("value"));
        }
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }
}
