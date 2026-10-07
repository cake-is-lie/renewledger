using System.Text.Json;
using System.Text.Json.Nodes;
using RenewLedger.Models;
using RenewLedger.Services;

namespace RenewLedger.Tests;

public sealed class AnchorTests
{
    [Theory]
    [InlineData(31, false, 31)]
    [InlineData(30, false, 30)]
    [InlineData(28, false, 28)]
    [InlineData(31, true, 31)]
    public void MonthlyAnchorSurvivesFebruary(int day, bool endOfMonth, int marchDay)
    {
        var item = new Subscription(Guid.NewGuid(), "Server", 10m, "CNY", "monthly",
            new(2026, 1, day), day, 1, endOfMonth);
        item = item with { Due = LedgerRules.NextDate(item) };
        Assert.Equal(new(2026, 2, 28), item.Due);
        Assert.Equal(new(2026, 3, marchDay), LedgerRules.NextDate(item));
    }

    [Fact]
    public void FixedThirtiethAndLastDayAreDifferentRules()
    {
        var fixedDay = new Subscription(Guid.NewGuid(), "Server", 1m, "CNY", "monthly",
            new(2026, 4, 30), 30, 4);
        Assert.Equal(new(2026, 5, 30), LedgerRules.NextDate(fixedDay));
        Assert.Equal(new(2026, 5, 31), LedgerRules.NextDate(fixedDay with { EndOfMonth = true }));
    }

    [Fact]
    public void AnnualLeapDayReturnsInTheNextLeapYear()
    {
        var item = LedgerRules.WithAnchors(new(Guid.NewGuid(), "Domain", 10m, "USD", "yearly", new(2028, 2, 29)));
        for (var year = 2029; year <= 2032; year++)
        {
            item = item with { Due = LedgerRules.NextDate(item) };
            Assert.Equal(new(year, 2, year == 2032 ? 29 : 28), item.Due);
        }
    }

    [Theory]
    [InlineData("monthly", "9999-12-31")]
    [InlineData("yearly", "9999-02-28")]
    public void UnsupportedNextDateFailsWithoutWrapping(string cycle, string due) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => LedgerRules.NextDate(
            new(Guid.NewGuid(), "Limit", 1m, "CNY", cycle, DateOnly.Parse(due))));

    [Fact]
    public void ReadingV1DoesNotRewriteItAndTheFirstSaveWritesV2()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "ledger.json");
        const string oldJson = """
            {"version":1,"items":[{"id":"45164008-9060-4549-8944-735f205c67c2",
            "name":"Old server","amount":12.34,"currency":"USD","cycle":"monthly","due":"2026-02-28"}]}
            """;
        File.WriteAllText(path, oldJson);
        var store = new LedgerStore(path);
        var item = Assert.Single(store.Read());
        Assert.Equal(28, item.AnchorDay);
        Assert.False(item.EndOfMonth);
        Assert.True(item.IsActive);
        Assert.Equal(item, Assert.Single(store.Read())); // Stable token until a mutation.
        Assert.Equal(oldJson, File.ReadAllText(path));
        Assert.Equal(2, JsonNode.Parse(store.Export())!["version"]!.GetValue<int>());
        Assert.Equal(oldJson, File.ReadAllText(path)); // Export does not migrate the file either.
        store.Save(item with { Amount = 15m }, true);
        Assert.Equal(2, JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<int>());
        var migrated = Assert.Single(store.Read());
        Assert.Equal(item.Due, migrated.Due);
        Assert.Equal(item.Id, migrated.Id);
        Assert.Equal(15m, migrated.Amount);
        Assert.Equal("USD", migrated.Currency);
        Assert.NotEqual(item.Revision, migrated.Revision);
    }

    [Theory]
    [InlineData("anchorDay", "0")]
    [InlineData("anchorDay", "32")]
    [InlineData("anchorMonth", "13")]
    [InlineData("anchorMonth", "2")]
    [InlineData("revision", "\"00000000-0000-0000-0000-000000000000\"")]
    public void InvalidV2RulesCannotOverwriteTheLedger(string field, string value)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "ledger.json");
        var store = new LedgerStore(path);
        store.Save(new(Guid.NewGuid(), "Server", 10m, "CNY", "yearly", new(2026, 12, 31)), false);
        var original = File.ReadAllText(path);
        var backup = JsonNode.Parse(original)!;
        backup["items"]![0]![field] = JsonNode.Parse(value);
        Assert.Throws<ArgumentException>(() => store.Import(backup.ToJsonString()));
        Assert.Equal(original, File.ReadAllText(path));
    }
}
