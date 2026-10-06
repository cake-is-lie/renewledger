using RenewLedger.Models;

namespace RenewLedger.Tests;

public sealed class LedgerRulesTests
{
    private static Subscription Item => new(
        Guid.NewGuid(), "Server", 120m, "USD", "yearly", new DateOnly(2026, 1, 31));

    [Fact]
    public void MonthlyTotalsKeepCurrenciesSeparateAndDecimalPrecision()
    {
        var item = Item;
        var totals = LedgerRules.MonthlyTotals(
            [item, item with { Currency = "CNY", Amount = 35m, Cycle = "monthly" }]);

        Assert.Equal(10m, totals["USD"]);
        Assert.Equal(35m, totals["CNY"]);
        Assert.Equal(0.3m, LedgerRules.MonthlyTotals(
        [
            item with { Amount = 0.1m, Cycle = "monthly" },
            item with { Amount = 0.2m, Cycle = "monthly" }
        ])["USD"]);
    }

    [Theory]
    [InlineData("monthly", 2026, 1, 31, 2026, 2, 28)]
    [InlineData("yearly", 2028, 2, 29, 2029, 2, 28)]
    [InlineData("monthly", 2026, 12, 31, 2027, 1, 31)]
    public void NextDateHandlesMonthEndsAndLeapYears(
        string cycle, int year, int month, int day, int nextYear, int nextMonth, int nextDay)
    {
        var item = Item with { Cycle = cycle, Due = new DateOnly(year, month, day) };

        Assert.Equal(new DateOnly(nextYear, nextMonth, nextDay), LedgerRules.NextDate(item));
    }

    [Theory]
    [InlineData(6, 0)]
    [InlineData(13, 7)]
    [InlineData(5, -1)]
    public void DaysUntilIncludesTodayAndSevenDayBoundary(int dueDay, int expected) =>
        Assert.Equal(expected, LedgerRules.DaysUntil(new(2026, 10, dueDay), new(2026, 10, 6)));

    [Fact]
    public void InvalidBackupsAreRejected()
    {
        var item = Item;
        LedgerBackup?[] invalid =
        [
            null, new(1, null), new(1, [item, item]),
            new(1, [item with { Amount = -1m }]),
            new(1, [item with { Name = " " }]),
            new(2, [item]), new(1, [null!])
        ];

        foreach (var backup in invalid)
            Assert.Throws<ArgumentException>(() => LedgerRules.ValidateBackup(backup));
    }
}
