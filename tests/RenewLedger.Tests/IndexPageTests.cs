using System.Net;

namespace RenewLedger.Tests;

public sealed class IndexPageTests
{
    [Fact]
    public async Task EmptyLedgerRendersWithoutCreatingData()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        using var response = await client.GetAsync("/");
        var document = await LedgerApplication.ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("让下一次续费有迹可循", document.Body!.TextContent);
        Assert.NotNull(document.QuerySelector("input[name=__RequestVerificationToken]"));
        Assert.False(File.Exists(app.LedgerPath));
    }
}
