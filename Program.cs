using RenewLedger.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages().AddMvcOptions(options =>
{
    options.ModelBindingMessageProvider.SetAttemptedValueIsInvalidAccessor(
        (_, _) => "输入格式不正确，请检查金额、日期或项目标识。");
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(_ => "请输入有效的金额。");
});
builder.Services.AddSingleton(_ => new LedgerStore(
    builder.Configuration["Ledger:Path"] ??
    Path.Combine(builder.Environment.ContentRootPath, "data", "ledger.json")));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
var app = builder.Build();
app.UseStaticFiles();
app.MapRazorPages();
app.Run();

public partial class Program;
