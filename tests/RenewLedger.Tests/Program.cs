using RenewLedger.Models;
using RenewLedger.Services;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected {expected}, got {actual}.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
var item = new Subscription(Guid.NewGuid(), "Server", 120m, "USD", "yearly", new(2026, 1, 31));
var totals = LedgerRules.MonthlyTotals([item, item with { Currency = "CNY", Amount = 35m, Cycle = "monthly" }]);
Equal(10m, totals["USD"]);
Equal(35m, totals["CNY"]);
Equal(0.3m, LedgerRules.MonthlyTotals([item with { Amount = 0.1m, Cycle = "monthly" }, item with { Amount = 0.2m, Cycle = "monthly" }])["USD"]);
Console.WriteLine("PASS: decimal totals and currency isolation");
Equal(new DateOnly(2026, 2, 28), LedgerRules.NextDate(item with { Cycle = "monthly" }));
Equal(new DateOnly(2029, 2, 28), LedgerRules.NextDate(item with { Due = new(2028, 2, 29) }));
Equal(new DateOnly(2027, 1, 31), LedgerRules.NextDate(item with { Due = new(2026, 12, 31), Cycle = "monthly" }));
Equal(0, LedgerRules.DaysUntil(new(2026, 10, 6), new(2026, 10, 6)));
Equal(7, LedgerRules.DaysUntil(new(2026, 10, 13), new(2026, 10, 6)));
Equal(-1, LedgerRules.DaysUntil(new(2026, 10, 5), new(2026, 10, 6)));
Console.WriteLine("PASS: month ends, leap years, due boundaries");
Throws<ArgumentException>(() => LedgerRules.ValidateBackup(new(1, [item, item])));
Throws<ArgumentException>(() => LedgerRules.ValidateBackup(new(1, [item with { Amount = -1m }])));
Throws<ArgumentException>(() => LedgerRules.ValidateBackup(new(1, [item with { Name = " " }])));
Throws<ArgumentException>(() => LedgerRules.ValidateBackup(new(2, [item])));
Throws<ArgumentException>(() => LedgerRules.ValidateBackup(new(1, [null!])));
Console.WriteLine("PASS: invalid imports are rejected");
var directory = Path.Combine(Path.GetTempPath(), "renewledger-tests-" + Guid.NewGuid());
var path = Path.Combine(directory, "ledger.json");
try
{
    var store = new LedgerStore(path);
    store.Save(item, editing: false);
    Throws<ArgumentException>(() => store.Seed([item with { Id = Guid.NewGuid() }]));
    Equal(item, new LedgerStore(path).Read().Single());
    store.Save(item with { Amount = 150m }, editing: true);
    Equal(150m, store.Read().Single().Amount);
    store.Renew(item.Id);
    Equal(new DateOnly(2027, 1, 31), store.Read().Single().Due);
    var backup = store.Export();
    store.Delete(item.Id);
    Equal(0, store.Read().Count);
    store.Import(backup);
    Equal(1, store.Read().Count);
    Throws<ArgumentException>(() => store.Import("{\"version\":2,\"items\":[]}"));
    Equal(backup, store.Export());
    Parallel.For(0, 30, i => store.Save(item with { Id = Guid.NewGuid(), Name = $"Item {i}" }, editing: false));
    Equal(31, store.Read().Count);
    File.WriteAllText(path, "corrupted");
    Throws<InvalidDataException>(() => store.Read());
    Throws<InvalidDataException>(() => store.Import(backup));
    Equal("corrupted", File.ReadAllText(path));
    Console.WriteLine("PASS: persistence, CRUD, concurrent writes and corruption protection");
}
finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
