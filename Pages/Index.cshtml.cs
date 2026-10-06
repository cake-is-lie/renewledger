using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using RenewLedger.Models;
using RenewLedger.Services;

namespace RenewLedger.Pages;

public sealed class IndexModel(LedgerStore store) : PageModel
{
    public List<Subscription> All { get; private set; } = [];
    public List<Subscription> Shown { get; private set; } = [];
    public Dictionary<string, decimal> Totals { get; private set; } = [];
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Today);
    public int Soon => All.Count(item => Days(item) is >= 0 and <= 7);
    public int Overdue => All.Count(item => Days(item) < 0);
    public string Query { get; private set; } = "";
    public string Filter { get; private set; } = "all";
    public Subscription? Editing { get; private set; }
    public SubscriptionInput Input { get; private set; } = new();
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public int Days(Subscription item) => LedgerRules.DaysUntil(item.Due, Today);
    public string DateText(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public void OnGet(string? q, string? filter, Guid? edit)
    {
        Query = q?.Trim() ?? "";
        Filter = filter ?? "all";
        LoadLedger(edit);
        Input = Editing is null ? new SubscriptionInput { Due = Today } : new SubscriptionInput
        {
            Id = Editing.Id, Name = Editing.Name, Amount = Editing.Amount,
            Currency = Editing.Currency, Cycle = Editing.Cycle, Due = Editing.Due
        };
    }

    private void LoadLedger(Guid? edit)
    {
        try
        {
            All = store.Read();
            Totals = LedgerRules.MonthlyTotals(All);
            Shown = All.Where(item => item.Name.Contains(Query, StringComparison.OrdinalIgnoreCase))
                .Where(item => Filter switch
                {
                    "soon" => Days(item) is >= 0 and <= 7,
                    "overdue" => Days(item) < 0,
                    _ => true
                }).OrderBy(item => item.Due).ToList();
            Editing = All.FirstOrDefault(item => item.Id == edit);
        }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Error = error is InvalidDataException ? error.Message : "账本文件无法读取，请检查路径和文件权限。";
        }
    }

    public IActionResult OnPostSave([Bind(Prefix = "Input")] SubscriptionInput input)
    {
        Input = input;
        if (input.Id == Guid.Empty) ModelState.AddModelError("", "项目标识不正确，请刷新后重试。");
        if (!LedgerRules.Currencies.Contains(input.Currency))
            ModelState.AddModelError("Input.Currency", "请选择支持的币种。");
        if (input.Cycle is not ("monthly" or "yearly"))
            ModelState.AddModelError("Input.Cycle", "请选择月付或年付。");
        if (input.Due == default(DateOnly))
            ModelState.AddModelError("Input.Due", "请选择有效的到期日。");

        if (!ModelState.IsValid) return SaveFailure();
        try
        {
            store.Save(new Subscription(input.Id ?? Guid.NewGuid(), input.Name.Trim(),
                input.Amount!.Value, input.Currency, input.Cycle, input.Due!.Value), input.Id.HasValue);
            Notice = "已保存项目。";
            return RedirectToPage();
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ModelState.AddModelError("", FailureMessage(error));
            return SaveFailure();
        }
    }

    private IActionResult SaveFailure()
    {
        LoadLedger(Input.Id);
        return Page();
    }

    public IActionResult OnPostDelete(Guid id) => Change(() => store.Delete(id), "已删除项目。");
    public IActionResult OnPostRenew(Guid id) => Change(() => store.Renew(id), "到期日已推进一个周期。");

    public IActionResult OnGetExport()
    {
        try { return File(Encoding.UTF8.GetBytes(store.Export()), "application/json", $"renewledger-{DateText(Today)}.json"); }
        catch (Exception error) when (error is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Notice = error is InvalidDataException ? error.Message : "导出失败，请检查账本文件。";
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostImportAsync(IFormFile? file, bool replace)
    {
        if (!replace || file is null || file.Length is <= 0 or > 2 * 1024 * 1024)
        {
            Notice = "请选择 2 MB 以内的 JSON 文件，并确认替换当前账本。";
            return RedirectToPage();
        }
        try
        {
            using var reader = new StreamReader(file.OpenReadStream());
            var json = await reader.ReadToEndAsync();
            return Change(() => store.Import(json), "备份已导入。");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Notice = "备份文件无法读取，请重新选择文件。";
            return RedirectToPage();
        }
    }

    public IActionResult OnPostSample() => Change(() =>
    {
        var items = new List<Subscription>
        {
            new(Guid.NewGuid(), "示例 · 云服务器", 35m, "CNY", "monthly", Today.AddDays(3)),
            new(Guid.NewGuid(), "示例 · 开发工具", 12m, "USD", "monthly", Today.AddDays(15)),
            new(Guid.NewGuid(), "示例 · 域名", 88m, "CNY", "yearly", Today.AddDays(-2))
        };
        store.Seed(items);
    }, "已添加示例数据。");

    private IActionResult Change(Action action, string success)
    {
        try { action(); Notice = success; }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
        {
            Notice = FailureMessage(error);
        }
        return RedirectToPage();
    }

    private static string FailureMessage(Exception error) => error switch
    {
        JsonException => "JSON 文件格式不正确。",
        InvalidDataException => error.Message,
        IOException or UnauthorizedAccessException => "写入失败，请检查文件权限和磁盘空间。",
        ArgumentOutOfRangeException => "日期超出支持范围，未修改账本。",
        _ => error.Message
    };
}
