using System.Net;
using RenewLedger.Models;

namespace RenewLedger.Tests;

public sealed class SavePageTests
{
    internal static Dictionary<string, string> ValidFields() => new()
    {
        ["Input.Name"] = "Cloud server", ["Input.Amount"] = "12.50",
        ["Input.Currency"] = "USD", ["Input.Cycle"] = "yearly", ["Input.Due"] = "2026-12-31"
    };

    [Theory]
    [InlineData("Input.Name", " ", "name-error")]
    [InlineData("Input.Amount", "-1", "amount-error")]
    [InlineData("Input.Amount", "not-a-number", "amount-error")]
    [InlineData("Input.Amount", "1000000000001", "amount-error")]
    [InlineData("Input.Currency", "INVALID", "currency-error")]
    [InlineData("Input.Cycle", "weekly", "cycle-error")]
    [InlineData("Input.Due", "not-a-date", "due-error")]
    [InlineData("Input.Due", "0001-01-01", "due-error")]
    public async Task InvalidInputReturnsTheFormWithFieldErrorsAndOtherValuesIntact(
        string field, string value, string errorId)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var fields = ValidFields();
        fields[field] = value;

        using var response = await LedgerApplication.PostAsync(client, "Save", fields);
        var document = await LedgerApplication.ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(document.QuerySelector("#" + errorId)!.TextContent);
        if (field != "Input.Name")
            Assert.Equal("Cloud server", document.QuerySelector("input[name='Input.Name']")!.GetAttribute("value"));
        if (field != "Input.Amount")
            Assert.Equal(12.50m, decimal.Parse(document.QuerySelector("input[name='Input.Amount']")!.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture));
        if (field != "Input.Currency")
            Assert.Equal("USD", document.QuerySelector("select[name='Input.Currency'] option[selected]")!.GetAttribute("value"));
        Assert.False(File.Exists(app.LedgerPath));
    }

    [Fact]
    public async Task FailedEditKeepsTheRecordIdAndDoesNotChangeExistingData()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var item = new Subscription(Guid.NewGuid(), "Server", 35m, "CNY", "monthly", new(2026, 12, 31));
        app.Store.Save(item, false);
        var original = File.ReadAllText(app.LedgerPath);
        var fields = ValidFields();
        fields["Input.Id"] = item.Id.ToString();
        fields["Input.Amount"] = "-1";

        using var response = await LedgerApplication.PostAsync(client, "Save", fields);
        var document = await LedgerApplication.ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(item.Id.ToString(), document.QuerySelector("input[name='Input.Id']")!.GetAttribute("value"));
        Assert.Contains("编辑项目", document.QuerySelector("#editor h2")!.TextContent);
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));
    }

    [Fact]
    public async Task WriteFailureKeepsInputAndShowsAPageLevelError()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        Directory.CreateDirectory(app.LedgerPath);

        using var response = await LedgerApplication.PostAsync(client, "Save", ValidFields());
        var document = await LedgerApplication.ParseAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("写入失败", document.QuerySelector(".validation-summary-errors")!.TextContent);
        Assert.Equal("Cloud server", document.QuerySelector("input[name='Input.Name']")!.GetAttribute("value"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(app.LedgerPath)!, "*.tmp"));
    }
}
