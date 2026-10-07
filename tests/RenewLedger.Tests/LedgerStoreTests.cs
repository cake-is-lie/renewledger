using RenewLedger.Models;
using RenewLedger.Services;

namespace RenewLedger.Tests;

public sealed class LedgerStoreTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private string Path => System.IO.Path.Combine(directory.Path, "ledger.json");
    private LedgerStore Store => new(Path);
    private static Subscription Item => new(
        Guid.NewGuid(), "Server", 120m, "USD", "yearly", new DateOnly(2026, 1, 31));

    [Fact]
    public void ChangesPersistAndBackupsRoundTrip()
    {
        var store = Store;
        var item = Item;
        store.Save(item, editing: false);
        Assert.Equal(LedgerRules.WithAnchors(item) with { Revision = Assert.Single(Store.Read()).Revision }, Assert.Single(Store.Read()));
        item = Assert.Single(store.Read());
        Assert.Throws<ArgumentException>(() => store.Seed([item with { Id = Guid.NewGuid() }]));

        store.Save(item with { Amount = 150m }, editing: true);
        Assert.Equal(150m, Assert.Single(store.Read()).Amount);
        store.Renew(item.Id, Assert.Single(store.Read()).Revision);
        Assert.Equal(new DateOnly(2027, 1, 31), Assert.Single(store.Read()).Due);

        var backup = store.Export();
        store.Delete(item.Id);
        Assert.Empty(store.Read());
        store.Import(backup);
        var restoredBackup = store.Export();
        Assert.NotEqual(backup, restoredBackup); // Import deliberately changes the revision.
        Assert.Throws<ArgumentException>(() => store.Import("{\"version\":99,\"items\":[]}"));
        Assert.Equal(restoredBackup, store.Export());
    }

    [Fact]
    public void ConcurrentWritesDoNotLoseRecords()
    {
        var store = Store;
        var item = Item;
        Parallel.For(0, 30, i => store.Save(item with { Id = Guid.NewGuid(), Name = $"Item {i}" }, false));

        Assert.Equal(30, store.Read().Count);
    }

    [Fact]
    public void CorruptedLedgerCannotBeReadOrReplacedByImport()
    {
        var store = Store;
        store.Save(Item, false);
        var backup = store.Export();
        File.WriteAllText(Path, "corrupted");

        Assert.Throws<InvalidDataException>(() => store.Read());
        Assert.Throws<InvalidDataException>(() => store.Import(backup));
        Assert.Equal("corrupted", File.ReadAllText(Path));
    }

    public void Dispose() => directory.Dispose();
}
