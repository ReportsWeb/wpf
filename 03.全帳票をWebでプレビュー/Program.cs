using System.Text;
using Pao.Reports.Maui.Engine.Core;
using Pao.Reports.Maui.Engine.Data;
using Pao.Reports.Maui.Engine.Rendering;
using Pao.Reports.Maui.Engine.Samples;
using Pao.Reports.Maui.Engine.Services;
using ReportsWpf.Learning;

const string EngineHeader = "DotNet";
const string EngineLabel = ".NET";
const int MaxSvgBytes = 16 * 1024 * 1024;
const int MaxPdfBytes = 64 * 1024 * 1024;
const int MaxPrintDataBytes = 8 * 1024 * 1024;
const string PrintDataMediaType = "application/vnd.pao.reports-printdata+json";
var generationSlots = new SemaphoreSlim(2, 2);

var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:19330");
var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});

app.MapGet("/", () => Results.File(
    Path.Combine(AppContext.BaseDirectory, "resources", "previewer", "index.html"),
    "text/html; charset=utf-8"));
app.MapGet("/{asset:regex(^native-previewer\\.(js|css)$)}", (string asset) =>
{
    var path = Path.Combine(AppContext.BaseDirectory, "resources", "previewer", asset);
    return Results.File(path, asset.EndsWith(".js", StringComparison.Ordinal) ? "text/javascript; charset=utf-8" : "text/css; charset=utf-8");
});

app.MapGet("/health", (HttpContext context) =>
{
    context.Response.Headers["Cache-Control"] = "no-store";
    context.Response.Headers["X-Reports-Engine"] = EngineHeader;
    return Results.Ok(new { status = "ok", engine = EngineHeader });
});

app.MapGet("/api/samples", () => Results.Ok(NativeDemoCatalog.All.Select(sample => new
{
    id = sample.Id,
    name = sample.Name,
    pages = sample.PageCount,
    engine = EngineLabel
})));

#if WINDOWS_SERVER
const string BackendName = "Windows Server / GDI+";
#else
const string BackendName = "Linux / SkiaSharp";
#endif
app.MapGet("/api/environment", () => Results.Ok(new
{
    backend = BackendName,
    os = Environment.OSVersion.Platform.ToString()
}));
app.MapGet("/api/reports", () => Results.Ok(NativeDemoCatalog.LegacyIds));

app.MapGet("/api/reports/{id}/data", (string id, HttpContext context) =>
{
    if (!NativeDemoCatalog.TryGet(id, out var sample))
        return Results.NotFound(new { error = "指定されたサンプル帳票はありません。" });

    var bytes = sample.SaveJson();
    if (bytes.Length > MaxPrintDataBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    context.Response.Headers["X-Reports-Engine"] = EngineHeader;
    return Results.File(bytes, PrintDataMediaType, $"reports-dotnet-{sample.Id.ToUpperInvariant()}.prepej");
});

app.MapGet("/api/reports/{id}/svg", async (string id, int? page, HttpContext context) =>
    await WithGenerationSlot(context, token =>
    {
        if (!NativeDemoCatalog.TryGet(id, out var sample))
            return Results.NotFound(new { error = "指定されたサンプル帳票はありません。" });

        var pageIndex = page ?? 0;
        var pages = sample.Resolve();
        if (pageIndex < 0 || pageIndex >= pages.Count)
            return Results.BadRequest(new { error = "ページ番号が範囲外です。" });

        token.ThrowIfCancellationRequested();
        var svg = new SvgReportRenderer().Render(pages[pageIndex].Document);
        token.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(svg) > MaxSvgBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        context.Response.Headers["X-Reports-Engine"] = EngineHeader;
        return Results.Content(svg, "image/svg+xml; charset=utf-8", Encoding.UTF8);
    }));

app.MapGet("/api/reports/{id}/pdf", async (string id, HttpContext context) =>
    await WithGenerationSlot(context, token =>
    {
        if (!NativeDemoCatalog.TryGet(id, out var sample))
            return Results.NotFound(new { error = "指定されたサンプル帳票はありません。" });
        return RenderPdf(sample, context, token);
    }));

app.MapPost("/api/print-data/svg", async (HttpContext context, int? page) =>
{
    var upload = await ReadUploadedPrintData(context, context.RequestAborted);
    if (upload.Error is not null)
        return upload.Error;

    return await WithGenerationSlot(context, token =>
    {
        var pageIndex = page ?? 0;
        if (pageIndex < 0 || pageIndex >= upload.Pages!.Count)
            return Results.BadRequest(new { error = "ページ番号が範囲外です。" });

        token.ThrowIfCancellationRequested();
        var svg = new SvgReportRenderer().Render(upload.Pages[pageIndex].Document);
        token.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(svg) > MaxSvgBytes)
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

        context.Response.Headers["X-Reports-Engine"] = EngineHeader;
        return Results.Content(svg, "image/svg+xml; charset=utf-8", Encoding.UTF8);
    });
});

app.MapPost("/api/print-data/pdf", async (HttpContext context) =>
{
    var upload = await ReadUploadedPrintData(context, context.RequestAborted);
    if (upload.Error is not null)
        return upload.Error;

    return await WithGenerationSlot(context, token => RenderResolvedPdf(upload.Pages!, context, token));
});

// Keep the original sample host routes for existing local users.  Unsupported
// decorative-text demonstrations are intentionally not exposed here either.
app.MapGet("/api/reports/{id}.pdf", async (string id, HttpContext context) =>
    await WithGenerationSlot(context, token =>
    {
        if (!NativeDemoCatalog.TryGetLegacy(id, out var sample))
            return Results.NotFound(new { error = "指定されたサンプル帳票はありません。" });
        return RenderPdf(sample, context, token);
    }));

app.MapPost("/api/reports/{id}.pdf", async (string id, ReportRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.CustomerName))
        return Results.BadRequest(new { error = "customerName is required." });
    context.Response.Headers["X-Request-ID"] = Guid.NewGuid().ToString("N");
    return await WithGenerationSlot(context, token =>
    {
        if (!NativeDemoCatalog.TryGetLegacy(id, out var sample))
            return Results.NotFound(new { error = "指定されたサンプル帳票はありません。" });
        return RenderPdf(sample, context, token);
    });
});

app.Run();

IResult RenderPdf(NativeDemoSample sample, HttpContext context, CancellationToken token)
    => RenderResolvedPdf(sample.Resolve(), context, token);

IResult RenderResolvedPdf(IReadOnlyList<ResolvedReportPage> pages, HttpContext context, CancellationToken token)
{
    var documents = pages.Select(page => page.Document).ToArray();
    token.ThrowIfCancellationRequested();
#if WINDOWS_SERVER
    var bytes = new Pao.Reports.WindowsServer.WindowsServerPdfExporter()
        .Render(documents, new PdfRenderOptions(), token);
#else
    using var output = new MemoryStream();
    new SkiaPdfRenderer().Render(documents, output, new PdfRenderOptions(), token);
    var bytes = output.ToArray();
#endif
    token.ThrowIfCancellationRequested();
    if (bytes.Length > MaxPdfBytes)
        return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

    context.Response.Headers["X-Reports-Engine"] = EngineHeader;
    return Results.File(bytes, "application/pdf");
}

async Task<UploadedPrintData> ReadUploadedPrintData(HttpContext context, CancellationToken token)
{
    if (!IsSupportedPrintDataContentType(context.Request.ContentType))
        return UploadedPrintData.Failure(Results.StatusCode(StatusCodes.Status415UnsupportedMediaType));
    if (context.Request.ContentLength > MaxPrintDataBytes)
        return UploadedPrintData.Failure(Results.StatusCode(StatusCodes.Status413PayloadTooLarge));

    try
    {
        using var input = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(buffer.AsMemory(), token);
            if (read == 0) break;
            if (input.Length + read > MaxPrintDataBytes)
                return UploadedPrintData.Failure(Results.StatusCode(StatusCodes.Status413PayloadTooLarge));
            input.Write(buffer, 0, read);
        }
        if (input.Length == 0)
            return UploadedPrintData.Failure(Results.BadRequest(new { error = "PREPEJ印刷データが空です。" }));

        var data = PrintDataStore.LoadJson(input.ToArray(), preserveArtText: true);
        NativeDemoPrintDataPolicy.Validate(data, requireSelfContained: true);
        var pages = PrintDataStore.Resolve(data);
        var diagnostics = pages.SelectMany(item => item.Diagnostics).Take(10).ToArray();
        if (diagnostics.Length != 0)
            throw new InvalidDataException(string.Join(" | ", diagnostics));
        return UploadedPrintData.Success(pages);
    }
    catch (OperationCanceledException) { throw; }
    catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or FormatException or NotSupportedException or ArgumentException)
    {
        return UploadedPrintData.Failure(Results.BadRequest(new { error = "自己完結したPREPEJ印刷データを読み込めません。" }));
    }
}

static bool IsSupportedPrintDataContentType(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;
    var mediaType = value.Split(';', 2)[0].Trim();
    return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals(PrintDataMediaType, StringComparison.OrdinalIgnoreCase);
}

async Task<IResult> WithGenerationSlot(HttpContext context, Func<CancellationToken, IResult> generate)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    deadline.CancelAfter(TimeSpan.FromSeconds(60));
    var entered = false;
    try
    {
        entered = await generationSlots.WaitAsync(TimeSpan.FromSeconds(30), deadline.Token);
        if (!entered)
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        return generate(deadline.Token);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch
    {
        return Results.Problem("帳票を生成できませんでした。", statusCode: StatusCodes.Status500InternalServerError);
    }
    finally
    {
        if (entered) generationSlots.Release();
    }
}

public partial class Program;
public sealed record ReportRequest(string CustomerName);

internal sealed record UploadedPrintData(IReadOnlyList<ResolvedReportPage>? Pages, IResult? Error)
{
    public static UploadedPrintData Success(IReadOnlyList<ResolvedReportPage> pages) => new(pages, null);
    public static UploadedPrintData Failure(IResult error) => new(null, error);
}

internal sealed class NativeDemoSample(string id, string name, Func<PrintDataDocument> create)
{
    private readonly Lazy<PrintDataDocument> data = new(create, LazyThreadSafetyMode.ExecutionAndPublication);

    public string Id { get; } = id;
    public string Name { get; } = name;
    public IReadOnlyList<ResolvedReportPage> Resolve()
    {
        var pages = PrintDataStore.Resolve(data.Value);
        var diagnostics = pages.SelectMany(page => page.Diagnostics).Take(10).ToArray();
        if (diagnostics.Length != 0)
            throw new InvalidDataException(string.Join(" | ", diagnostics));
        return pages;
    }
    public byte[] SaveJson()
    {
        NativeDemoPrintDataPolicy.Validate(data.Value, requireSelfContained: false);
        _ = Resolve();
        return PrintDataStore.SaveJson(data.Value);
    }
    public int PageCount => Resolve().Count;
}

internal static class NativeDemoPrintDataPolicy
{
    public static void Validate(PrintDataDocument data, bool requireSelfContained)
    {
        PrintDataStore.Validate(data, preserveArtText: true);
        if (ContainsArtText(data.Definition) || data.Pages.Any(page => ContainsArtText(page.Definition)))
            throw new InvalidDataException("ArtText is outside the Reports.Web product scope.");
        if (requireSelfContained && data.Pages.Any(page => page.Definition is null))
            throw new InvalidDataException("Every canonical PREPEJ page must contain its PrintDefine.");
        if (requireSelfContained)
        {
            foreach (var definition in Definitions(data))
            {
                if (!string.IsNullOrWhiteSpace(definition.BackgroundImagePath))
                    throw new InvalidDataException("External background image paths are not allowed.");
                ValidateBase64(definition.BackgroundImageDataBase64);
                foreach (var image in definition.Objects.Where(item => item.Kind == ReportObjectKind.Image))
                {
                    if (!string.IsNullOrWhiteSpace(image.ImagePath)
                        || (string.IsNullOrWhiteSpace(image.ImageDataBase64) && !string.IsNullOrWhiteSpace(image.Text)))
                        throw new InvalidDataException("External image paths are not allowed.");
                    ValidateBase64(image.ImageDataBase64);
                }
            }
        }
    }

    private static bool ContainsArtText(ReportDocument? definition) =>
        definition?.Objects.Any(item => item.Kind == ReportObjectKind.ArtText) == true;

    private static IEnumerable<ReportDocument> Definitions(PrintDataDocument data)
    {
        if (data.Definition is not null) yield return data.Definition;
        foreach (var page in data.Pages)
            if (page.Definition is not null) yield return page.Definition;
    }

    private static void ValidateBase64(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { _ = Convert.FromBase64String(value.Replace("\r", "").Replace("\n", "")); }
        catch (FormatException exception) { throw new InvalidDataException("An embedded image is not valid Base64.", exception); }
    }
}

internal static class NativeDemoCatalog
{
    private static readonly NativeSampleCatalog Catalog = new(
        Path.Combine(AppContext.BaseDirectory, "resources"),
        Path.Combine(AppContext.BaseDirectory, "database", "sample.db"));

    public static readonly IReadOnlyList<NativeDemoSample> All =
    [
        new("S01", "あっという間に帳票出力", () => Catalog.Create("quick-start")),
        new("S02", "10のサンプル", () => Catalog.Create("multiples-of-ten")),
        new("S04", "郵便番号一覧", () => Catalog.Create("postal")),
        new("S05", "見積書", () => Catalog.Create("estimate")),
        new("S06", "請求書", () => Catalog.Create("invoice")),
        new("S07", "商品一覧", () => Catalog.Create("products")),
        new("S03", "名刺", () => Catalog.Create("business-card")),
        new("S08", "デザイン機能見本", () => Catalog.Create("design-showcase"))
    ];

    private static readonly IReadOnlyDictionary<string, NativeDemoSample> ById =
        All.ToDictionary(sample => sample.Id, StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, NativeDemoSample> LegacyById =
        new Dictionary<string, NativeDemoSample>(StringComparer.OrdinalIgnoreCase)
        {
            ["quick-start"] = ById["S01"],
            ["multiples-of-ten"] = ById["S02"],
            ["business-card"] = ById["S03"],
            ["estimate"] = ById["S05"],
            ["postal"] = ById["S04"],
            ["invoice"] = ById["S06"],
            ["products"] = ById["S07"],
            ["design-showcase"] = ById["S08"]
        };

    public static IReadOnlyList<string> LegacyIds { get; } =
        ["quick-start", "multiples-of-ten", "postal", "estimate", "invoice", "products", "business-card", "design-showcase"];

    public static bool TryGet(string id, out NativeDemoSample sample) => ById.TryGetValue(id, out sample!);
    public static bool TryGetLegacy(string id, out NativeDemoSample sample) => LegacyById.TryGetValue(id, out sample!);
}
