namespace RenewLedger.Models;

public static class LedgerRules
{
    public static readonly string[] Currencies = ["CNY", "USD", "EUR", "JPY", "GBP"];

    public static bool IsValid(Subscription? item) => item is not null &&
        item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Name) &&
        item.Name.Length <= 100 && item.Amount is >= 0 and <= 1_000_000_000_000m &&
        Currencies.Contains(item.Currency) && item.Cycle is "monthly" or "yearly" &&
        item.Due != default &&
        ((item.AnchorDay == 0 && item.AnchorMonth == 0 && !item.EndOfMonth) ||
         (item.AnchorDay is >= 1 and <= 31 && item.AnchorMonth is >= 1 and <= 12 &&
          (item.Cycle == "monthly" || item.Due.Month == item.AnchorMonth) &&
          item.Due.Day == ScheduledDay(item.Due.Year, item.Due.Month, item.AnchorDay, item.EndOfMonth)));

    public static int DaysUntil(DateOnly due, DateOnly today) => due.DayNumber - today.DayNumber;

    // Missing anchors are accepted for new records; persisted v2 records always carry them.
    public static Subscription WithAnchors(Subscription item) => item with
    {
        AnchorDay = item.AnchorDay == 0 ? item.Due.Day : item.AnchorDay,
        AnchorMonth = item.AnchorMonth == 0 ? item.Due.Month : item.AnchorMonth
    };

    private static int ScheduledDay(int year, int month, int day, bool endOfMonth) =>
        endOfMonth ? DateTime.DaysInMonth(year, month) : Math.Min(day, DateTime.DaysInMonth(year, month));

    public static DateOnly NextDate(Subscription item)
    {
        if (!IsValid(item)) throw new ArgumentException("项目或续费规则不正确。");
        item = WithAnchors(item);
        var target = item.Cycle == "monthly" ? item.Due.AddMonths(1) : item.Due.AddYears(1);
        return new DateOnly(target.Year, target.Month,
            ScheduledDay(target.Year, target.Month, item.AnchorDay, item.EndOfMonth));
    }

    public static Dictionary<string, decimal> MonthlyTotals(IEnumerable<Subscription> items) =>
        items.Where(item => item.IsActive).GroupBy(item => item.Currency).ToDictionary(
            group => group.Key,
            group => group.Sum(item => item.Amount / (item.Cycle == "yearly" ? 12m : 1m)));

    public static List<Subscription> ValidateBackup(LedgerBackup? backup)
    {
        if (backup is null || backup.Version is not (1 or 2) || backup.Items is null ||
            backup.Items.Count > 1000 || !backup.Items.All(IsValid) ||
            (backup.Version == 2 && backup.Items.Any(item =>
                item.AnchorDay == 0 || item.AnchorMonth == 0 || item.Revision == Guid.Empty)) ||
            backup.Items.Select(item => item.Id).Distinct().Count() != backup.Items.Count)
            throw new ArgumentException("备份格式不正确，或包含无效、重复的记录。");
        // v1 never stored intent: use the current due date, not a guessed original anchor.
        return backup.Items.Select(item => backup.Version == 1
            ? WithAnchors(new Subscription(item.Id, item.Name.Trim(), item.Amount,
                item.Currency, item.Cycle, item.Due)) with { Revision = item.Id }
            : item with { Name = item.Name.Trim() }).ToList();
    }
}
