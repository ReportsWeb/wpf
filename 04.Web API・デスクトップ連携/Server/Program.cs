using System.Text;
using ReportsWeb.Sample;
using ReportsWpf.Learning;
using Pao.Reports.Maui.Engine.Data;

const string MediaType = "application/vnd.pao.reports-printdata+json";
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://localhost:19340");
var app = builder.Build();
var catalog = new NativeSampleCatalog(Path.Combine(AppContext.BaseDirectory, "resources"), Path.Combine(AppContext.BaseDirectory, "database", "sample.db"));
app.MapGet("/", () => Results.Ok(new { message = "Reports.NET PREPEJ API", client = "04/Client を起動してください。" }));
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/samples", () => SampleCatalog.Samples.Select(x => new { id = x.Key, name = x.Value }));
app.MapGet("/api/reports/{id}/data", (string id) =>
{
    if (!SampleCatalog.Samples.ContainsKey(id)) return Results.NotFound(new { error = "指定された帳票はありません。" });
    var bytes = PrintDataStore.SaveJson(catalog.Create(id));
    return Results.File(bytes, MediaType, $"{id}.prepej");
});
app.Run();
public partial class Program { }
