using System.Net;
using System.Text.Json;
using RenewLedger.Models;

namespace RenewLedger.Tests;

public sealed class LifecyclePageTests
{
    [Theory]
    [InlineData("monthly", "2026-02-28", "31", false, "2026-03-31")]
    [InlineData("monthly", "2026-04-30", "30", true, "2026-05-31")]
    [InlineData("yearly", "2029-02-28", "29", false, "2030-02-28")]
    public async Task EditorRoundTripsTheChosenAnchorWithoutResettingItOnPriceChange(
        string cycle, string due, string anchor, bool endOfMonth, string next)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var fields = SavePageTests.ValidFields();
        fields["Input.Cycle"] = cycle;
        fields["Input.Due"] = due;
        fields["Input.AnchorDay"] = anchor;
        fields["Input.EndOfMonth"] = endOfMonth.ToString();
        using var saved = await LedgerApplication.PostAsync(client, "Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var item = Assert.Single(app.Store.Read());
        Assert.Equal(DateOnly.Parse(next), LedgerRules.NextDate(item));
        using var page = await client.GetAsync("/?edit=" + item.Id);
        var document = await LedgerApplication.ParseAsync(page);
        var form = document.QuerySelector("#editor form")!;
        Assert.Equal(anchor, form.QuerySelector("input[name='Input.AnchorDay']")!.GetAttribute("value"));
        Assert.Equal(endOfMonth, form.QuerySelector("input[name='Input.EndOfMonth']")!.HasAttribute("checked"));
        Assert.True(form.QuerySelector("input[name='Input.IsActive']")!.HasAttribute("checked"));
        fields["Input.Id"] = item.Id.ToString();
        fields["Input.Amount"] = "99.50";
        using var edited = await LedgerApplication.PostAsync(client, "Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, edited.StatusCode);
        item = Assert.Single(app.Store.Read());
        Assert.Equal(DateOnly.Parse(next), LedgerRules.NextDate(item));
        Assert.Equal(99.50m, item.Amount);
    }

    [Theory]
    [InlineData("2026-03-28", "31", false, "due-error")]
    [InlineData("2026-05-30", "30", true, "due-error")]
    [InlineData("2026-01-31", "32", false, "anchor-error")]
    [InlineData("2026-01-31", "0", false, "anchor-error")]
    public async Task InconsistentRulesPreserveInputAndShowFieldErrors(
        string due, string anchor, bool endOfMonth, string error)
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var fields = SavePageTests.ValidFields();
        fields["Input.Due"] = due;
        fields["Input.AnchorDay"] = anchor;
        fields["Input.EndOfMonth"] = endOfMonth.ToString();
        using var response = await LedgerApplication.PostAsync(client, "Save", fields);
        var document = await LedgerApplication.ParseAsync(response);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(document.QuerySelector("#" + error)!.TextContent);
        Assert.Equal(anchor, document.QuerySelector("#editor input[name='Input.AnchorDay']")!.GetAttribute("value"));
        Assert.Equal(endOfMonth, document.QuerySelector("#editor input[name='Input.EndOfMonth']")!.HasAttribute("checked"));
        Assert.False(File.Exists(app.LedgerPath));
    }

    [Fact]
    public async Task DisablingKeepsTheRecordAndBackupButExcludesItFromActiveViewsAndTotals()
    {
        using var app = new LedgerApplication();
        using var client = app.CreateBrowser();
        var due = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        app.Store.Save(new(Guid.NewGuid(), "Disabled server", 500m, "USD", "monthly", due), false);
        var item = Assert.Single(app.Store.Read());
        var fields = SavePageTests.ValidFields();
        fields["Input.Id"] = item.Id.ToString();
        fields["Input.Name"] = item.Name;
        fields["Input.Amount"] = "500";
        fields["Input.Cycle"] = "monthly";
        fields["Input.Due"] = due.ToString("yyyy-MM-dd");
        fields["Input.IsActive"] = "false";
        fields["q"] = "Disabled";
        fields["filter"] = "disabled";
        using var saved = await LedgerApplication.PostAsync(client, "Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Contains("filter=disabled", saved.Headers.Location!.OriginalString);
        item = Assert.Single(app.Store.Read());
        Assert.False(item.IsActive);
        Assert.Empty(LedgerRules.MonthlyTotals([item]));
        var original = File.ReadAllText(app.LedgerPath);
        Assert.Throws<ArgumentException>(() => app.Store.Renew(item.Id, item.Revision));
        Assert.Equal(original, File.ReadAllText(app.LedgerPath));

        foreach (var filter in new[] { "all", "disabled", "active", "soon", "overdue" })
        {
            using var page = await client.GetAsync("/?filter=" + filter);
            var document = await LedgerApplication.ParseAsync(page);
            Assert.Equal(filter is "all" or "disabled" ? 1 : 0, document.QuerySelectorAll(".row").Length);
            Assert.Empty(document.QuerySelectorAll(".money em"));
            Assert.All(document.QuerySelectorAll(".summary strong"), node => Assert.Equal("0", node.TextContent));
            Assert.Empty(document.QuerySelectorAll(".row form[action*='Renew']"));
        }
        var backup = JsonSerializer.Deserialize<LedgerBackup>(app.Store.Export(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(Assert.Single(LedgerRules.ValidateBackup(backup)).IsActive);
        fields["Input.IsActive"] = "true";
        using var enabled = await LedgerApplication.PostAsync(client, "Save", fields);
        Assert.Equal(HttpStatusCode.Redirect, enabled.StatusCode);
        item = Assert.Single(app.Store.Read());
        Assert.True(item.IsActive);
        Assert.Equal(500m, LedgerRules.MonthlyTotals([item])["USD"]);
        Assert.Equal(due, item.Due); // Re-enabling never silently advances an overdue date.
    }
}
