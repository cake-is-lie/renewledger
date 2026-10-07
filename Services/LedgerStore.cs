using System.Text.Json;
using RenewLedger.Models;

namespace RenewLedger.Services;

// One application process owns this file. Every mutation reloads it under the same lock.
public sealed class LedgerStore(string path)
{
    private readonly object gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public List<Subscription> Read()
    {
        lock (gate) return Load();
    }

    public void Save(Subscription item, bool editing)
    {
        item = LedgerRules.WithAnchors(item) with { Revision = Guid.NewGuid() };
        if (!LedgerRules.IsValid(item)) throw new ArgumentException("请检查名称、金额、币种和到期日。");
        lock (gate)
        {
            var items = Load();
            var index = items.FindIndex(value => value.Id == item.Id);
            if (editing && index < 0) throw new ArgumentException("项目已被删除，请刷新页面。");
            if (!editing && index >= 0) throw new ArgumentException("项目已存在。");
            if (index >= 0) items[index] = item;
            else
            {
                if (items.Count >= 1000) throw new ArgumentException("Demo 最多支持 1000 项记录。");
                items.Add(item);
            }
            Write(items);
        }
    }

    public void Delete(Guid id)
    {
        lock (gate)
        {
            var items = Load();
            if (items.RemoveAll(item => item.Id == id) > 0) Write(items);
        }
    }

    public void Renew(Guid id)
    {
        lock (gate)
        {
            var items = Load();
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0) throw new ArgumentException("项目不存在，请刷新页面。");
            items[index] = items[index] with { Due = LedgerRules.NextDate(items[index]), Revision = Guid.NewGuid() };
            Write(items);
        }
    }

    public void Import(string json)
    {
        var items = LedgerRules.ValidateBackup(JsonSerializer.Deserialize<LedgerBackup>(json, JsonOptions));
        lock (gate)
        {
            Load(); // Refuse to overwrite a corrupted existing ledger.
            Write(items);
        }
    }

    public void Seed(List<Subscription> items)
    {
        var validated = LedgerRules.ValidateBackup(new LedgerBackup(2,
            items.Select(item => LedgerRules.WithAnchors(item) with { Revision = Guid.NewGuid() }).ToList()));
        lock (gate)
        {
            if (Load().Count != 0) throw new ArgumentException("账本非空，未添加示例。");
            Write(validated);
        }
    }

    public string Export()
    {
        lock (gate) return JsonSerializer.Serialize(new LedgerBackup(2, Load()), JsonOptions);
    }

    private List<Subscription> Load()
    {
        if (!File.Exists(path)) return [];
        try
        {
            return LedgerRules.ValidateBackup(JsonSerializer.Deserialize<LedgerBackup>(File.ReadAllText(path), JsonOptions));
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("账本文件损坏；原文件未修改，请从备份恢复。", error);
        }
    }

    private void Write(List<Subscription> items)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(new LedgerBackup(2, items), JsonOptions));
            File.Move(temporaryPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }
}
