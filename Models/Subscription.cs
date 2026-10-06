using System.Text.Json.Serialization;

namespace RenewLedger.Models;

public sealed record Subscription(
    [property: JsonRequired] Guid Id,
    [property: JsonRequired] string Name,
    [property: JsonRequired] decimal Amount,
    [property: JsonRequired] string Currency,
    [property: JsonRequired] string Cycle,
    [property: JsonRequired] DateOnly Due);

public sealed record LedgerBackup(
    [property: JsonRequired] int Version,
    [property: JsonRequired] List<Subscription>? Items);
