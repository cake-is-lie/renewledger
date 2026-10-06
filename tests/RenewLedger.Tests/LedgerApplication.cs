using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RenewLedger.Services;

namespace RenewLedger.Tests;

internal sealed class LedgerApplication : WebApplicationFactory<Program>
{
    private readonly TemporaryDirectory directory = new();
    public string LedgerPath => Path.Combine(directory.Path, "ledger.json");
    public LedgerStore Store => new(LedgerPath);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LedgerStore>();
            services.AddSingleton(new LedgerStore(LedgerPath));
        });
    }

    public HttpClient CreateBrowser() => CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    public static async Task<IDocument> ParseAsync(HttpResponseMessage response) =>
        await new HtmlParser().ParseDocumentAsync(await response.Content.ReadAsStringAsync());

    public static async Task<string> TokenAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/");
        var document = await ParseAsync(response);
        return document.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
    }

    public static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string handler, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = await TokenAsync(client);
        using var content = new FormUrlEncodedContent(fields);
        return await client.PostAsync("/?handler=" + handler, content);
    }

    public static async Task<HttpResponseMessage> ImportAsync(
        HttpClient client, string json, string token, bool replace = true,
        Dictionary<string, string>? viewState = null)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(token), "__RequestVerificationToken");
        content.Add(new StringContent(replace ? "true" : "false"), "replace");
        content.Add(new StringContent(json), "file", "backup.json");
        if (viewState is not null)
            foreach (var field in viewState) content.Add(new StringContent(field.Value), field.Key);
        return await client.PostAsync("/?handler=Import", content);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) directory.Dispose();
    }
}
