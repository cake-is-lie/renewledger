using System.Net;

namespace RenewLedger.Tests;

public sealed class CorruptionPageTests
{
    private const string Corrupted = "{broken ledger";

    [Theory]
    [InlineData("/")]
    [InlineData("/?handler=Export")]
    public async Task ReadsShowAnUnderstandableErrorWithoutChangingTheLedger(string url)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        File.WriteAllText(app.LedgerPath, Corrupted);

        using var response = await client.GetAsync(url);
        using var page = response.StatusCode == HttpStatusCode.Redirect
            ? await client.GetAsync(response.Headers.Location) : null;
        var document = await LedgerApplication.ParseAsync(page ?? response);

        Assert.Equal(HttpStatusCode.OK, (page ?? response).StatusCode);
        Assert.Contains("账本文件损坏", document.Body!.TextContent);
        Assert.Equal(Corrupted, File.ReadAllText(app.LedgerPath));
    }

    [Theory]
    [InlineData("Save")]
    [InlineData("Delete")]
    [InlineData("Renew")]
    [InlineData("Import")]
    [InlineData("Sample")]
    public async Task MutationsShowAnErrorAndNeverOverwriteCorruptedData(string handler)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var token = await LedgerApplication.TokenAsync(client);
        File.WriteAllText(app.LedgerPath, Corrupted);
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["id"] = Guid.NewGuid().ToString(),
            ["name"] = "Server", ["amount"] = "50", ["currency"] = "CNY",
            ["cycle"] = "monthly", ["due"] = "2026-12-31"
        });

        using var response = handler == "Import"
            ? await LedgerApplication.ImportAsync(client, "{\"version\":1,\"items\":[]}", token)
            : await client.PostAsync("/?handler=" + handler, content);
        using var page = response.StatusCode == HttpStatusCode.Redirect
            ? await client.GetAsync(response.Headers.Location) : null;
        var document = await LedgerApplication.ParseAsync(page ?? response);

        Assert.Equal(HttpStatusCode.OK, (page ?? response).StatusCode);
        Assert.Contains("账本文件损坏", document.Body!.TextContent);
        Assert.Equal(Corrupted, File.ReadAllText(app.LedgerPath));
    }
}
