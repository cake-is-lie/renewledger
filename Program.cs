using RenewLedger.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddSingleton(_ => new LedgerStore(
    builder.Configuration["Ledger:Path"] ??
    Path.Combine(builder.Environment.ContentRootPath, "data", "ledger.json")));
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 3 * 1024 * 1024);
var app = builder.Build();
app.UseStaticFiles();
app.MapRazorPages();
app.Run();
