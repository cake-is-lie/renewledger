namespace RenewLedger.Models;

public static class LedgerRules
{
    public static readonly string[] Currencies = ["CNY", "USD", "EUR", "JPY", "GBP"];

    public static bool IsValid(Subscription? item) => item is not null &&
        item.Id != Guid.Empty && !string.IsNullOrWhiteSpace(item.Name) &&
        item.Name.Length <= 100 && item.Amount is >= 0 and <= 1_000_000_000_000m &&
        Currencies.Contains(item.Currency) && item.Cycle is "monthly" or "yearly" &&
        item.Due != default;

    public static int DaysUntil(DateOnly due, DateOnly today) => due.DayNumber - today.DayNumber;

    public static DateOnly NextDate(Subscription item) => item.Cycle switch
    {
        "monthly" => item.Due.AddMonths(1),
        "yearly" => item.Due.AddYears(1),
        _ => throw new ArgumentException("不支持的付费周期。")
    };

    public static Dictionary<string, decimal> MonthlyTotals(IEnumerable<Subscription> items) =>
        items.GroupBy(item => item.Currency).ToDictionary(
            group => group.Key,
            group => group.Sum(item => item.Amount / (item.Cycle == "yearly" ? 12m : 1m)));

    public static List<Subscription> ValidateBackup(LedgerBackup? backup)
    {
        if (backup is null || backup.Version != 1 || backup.Items is null ||
            backup.Items.Count > 1000 || !backup.Items.All(IsValid) ||
            backup.Items.Select(item => item.Id).Distinct().Count() != backup.Items.Count)
            throw new ArgumentException("备份格式不正确，或包含无效、重复的记录。");
        return backup.Items.Select(item => item with { Name = item.Name.Trim() }).ToList();
    }
}
