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
        var expectedRevision = item.Revision;
        item = LedgerRules.WithAnchors(item) with { Revision = Guid.NewGuid() };
        if (!LedgerRules.IsValid(item)) throw new ArgumentException("请检查名称、金额、币种和到期日。");
        lock (gate)
        {
            var items = Load();
            var index = items.FindIndex(value => value.Id == item.Id);
            if (editing && index < 0) throw new ArgumentException("项目已被删除，请刷新页面。");
            if (editing && (expectedRevision == Guid.Empty || items[index].Revision != expectedRevision))
                throw new ArgumentException("项目已变更，请重新打开编辑页核对；未覆盖新数据，当前输入已保留。");
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

    public void Renew(Guid id, Guid expectedRevision)
    {
        lock (gate)
        {
            var items = Load();
            var index = items.FindIndex(item => item.Id == id);
            if (index < 0) throw new ArgumentException("项目不存在，请刷新页面。");
            if (!items[index].IsActive) throw new ArgumentException("项目已停用，请先启用后再续费。");
            if (expectedRevision == Guid.Empty || items[index].Revision != expectedRevision)
                throw new ArgumentException("确认信息已失效：项目可能已续费、编辑或重新导入。请刷新核对，未重复推进日期。");
            items[index] = items[index] with { Due = LedgerRules.NextDate(items[index]), Revision = Guid.NewGuid() };
            Write(items);
        }
    }

    public void Import(string json)
    {
        var items = ParseBackup(json);
        // Restoring a backup must invalidate any confirmation opened before the import.
        items = items.Select(item => item with { Revision = Guid.NewGuid() }).ToList();
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
            return ParseBackup(File.ReadAllText(path));
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            throw new InvalidDataException("账本文件损坏；原文件未修改，请从备份恢复。", error);
        }
    }

    private static List<Subscription> ParseBackup(string json)
    {
        var backup = JsonSerializer.Deserialize<LedgerBackup>(json, JsonOptions);
        var items = LedgerRules.ValidateBackup(backup);
        if (backup!.Version == 2)
        {
            // These flags cannot default silently: false/true would change renewal intent or status.
            using var document = JsonDocument.Parse(json);
            var array = document.RootElement.EnumerateObject()
                .Last(property => property.Name.Equals("items", StringComparison.OrdinalIgnoreCase)).Value;
            foreach (var element in array.EnumerateArray())
            {
                var fields = element.EnumerateObject().Select(property => property.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (!fields.IsSupersetOf(["anchorDay", "anchorMonth", "endOfMonth", "isActive", "revision"]))
                    throw new ArgumentException("版本 2 备份缺少续费规则、启用状态或记录版本，未修改账本。");
            }
        }
        return items;
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
