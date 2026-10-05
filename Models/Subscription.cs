namespace RenewLedger.Models;

public sealed record Subscription(
    Guid Id, string Name, decimal Amount, string Currency, string Cycle, DateOnly Due);

public sealed record LedgerBackup(int Version, List<Subscription>? Items);
