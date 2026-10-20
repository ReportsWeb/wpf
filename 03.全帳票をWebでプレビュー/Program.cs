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
// 4 つのサーバー（Web・Java・Python・.NET）で同じ 64MiB（2026-10-13 決定。以前の .NET は 8MiB）。
const int MaxPrintDataBytes = 64 * 1024 * 1024;
const string PrintDataMediaType = "application/vnd.pao.reports-printdata+json";
var generationSlots = new SemaphoreSlim(2, 2);
// 受け取った印刷データ（/api/print-data）は 1 本ずつ展開する（W と同じ）。上限 64MiB の要求を 2 本同時に展開すると、
// 最大のワーキングセットが 2.2GiB になった（B18、2026-10-13 コーディネーターの決定 (a)：メモリーを使い切る危険）。
var uploadSlots = new SemaphoreSlim(1, 1);

var builder = WebApplication.CreateBuilder(args);
// 4 つのサーバーで同じ動き（2026-10-13 決定）：本文が届くまで 30 秒（超えたら 408）、生成は 60 秒（超えたら 503）、混雑の待ちは 30 秒（503）。
var bodyTimeout = TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("REPORTS_DOTNET_BODY_TIMEOUT_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var bodySeconds) && bodySeconds > 0 ? bodySeconds : 30);
var generationTimeout = TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("REPORTS_DOTNET_GENERATION_TIMEOUT_SECONDS"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var generationSeconds) && generationSeconds > 0 ? generationSeconds : 60);
builder.WebHost.ConfigureKestrel(options =>
{
    // Kestrel の既定（約 28.6MiB で断る、本文が遅いと 5 秒で中身の無い 408）では、上の上限と日本語の誤りを返せない。
    // Kestrel は届いた量で数えるので、chunked では上限ちょうどでも断ることがある。Kestrel の上限は 2 倍にし、64MiB は下の読み込みで数える。
    options.Limits.MaxRequestBodySize = 2L * MaxPrintDataBytes;
    options.Limits.MinRequestBodyDataRate = null;
});
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
        return JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。");

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
            return JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。");

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
    var rejected = PrecheckUpload(context);
    if (rejected is not null)
        return rejected;

    // 本文の受け取り・解析・展開も描画の枠の中で行う（同時に解析するのは枠の数まで。T-H1・BUG-28）。
    return await WithGenerationSlotAsync(context, uploadSlots, async token =>
    {
        var upload = await ReadUploadedPrintData(context, token);
        if (upload.Error is not null)
            return upload.Error;

        var pageIndex = page ?? 0;
        if (pageIndex < 0 || pageIndex >= upload.Pages!.Count)
            return Results.BadRequest(new { error = "ページ番号が範囲外です。" });

        token.ThrowIfCancellationRequested();
        var svg = new SvgReportRenderer().Render(upload.Pages[pageIndex].Document);
        token.ThrowIfCancellationRequested();
        if (Encoding.UTF8.GetByteCount(svg) > MaxSvgBytes)
            return JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。");

        context.Response.Headers["X-Reports-Engine"] = EngineHeader;
        if (upload.DiagnosticsHeader is { } svgDiagnostics) context.Response.Headers[RenderDiagnosticsHeader.Name] = svgDiagnostics;
        return Results.Content(svg, "image/svg+xml; charset=utf-8", Encoding.UTF8);
    });
});

app.MapPost("/api/print-data/pdf", async (HttpContext context) =>
{
    var rejected = PrecheckUpload(context);
    if (rejected is not null)
        return rejected;

    return await WithGenerationSlotAsync(context, uploadSlots, async token =>
    {
        var upload = await ReadUploadedPrintData(context, token);
        if (upload.Error is not null)
            return upload.Error;
        return RenderResolvedPdf(upload.Pages!, context, token, upload.DiagnosticsHeader);
    });
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
        return Results.BadRequest(new { error = "customerName を指定してください。" });
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

IResult RenderResolvedPdf(IReadOnlyList<ResolvedReportPage> pages, HttpContext context, CancellationToken token, string? diagnosticsHeader = null)
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
        return JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。");

    context.Response.Headers["X-Reports-Engine"] = EngineHeader;
    if (diagnosticsHeader is not null) context.Response.Headers[RenderDiagnosticsHeader.Name] = diagnosticsHeader;
    return Results.File(bytes, "application/pdf");
}

// 本文を読まずに断れるもの（種類が違う 415、Content-Length が上限を超える 413）は、描画の枠を待たずにすぐ断る。
IResult? PrecheckUpload(HttpContext context)
{
    if (!IsSupportedPrintDataContentType(context.Request.ContentType))
        return JsonError(StatusCodes.Status415UnsupportedMediaType, "印刷データは JSON（application/json）で送ってください。");
    if (context.Request.ContentLength > MaxPrintDataBytes)
        return JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。");
    return null;
}

// 描画の枠の中で呼ぶ。chunked で上限を超えたら、その時点で読むのをやめて 413。
async Task<UploadedPrintData> ReadUploadedPrintData(HttpContext context, CancellationToken token)
{
    var rejected = PrecheckUpload(context);
    if (rejected is not null)
        return UploadedPrintData.Failure(rejected);

    try
    {
        using var input = new MemoryStream();
        var buffer = new byte[64 * 1024];
        using var bodyDeadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        bodyDeadline.CancelAfter(bodyTimeout);
        while (true)
        {
            int read;
            try { read = await context.Request.Body.ReadAsync(buffer.AsMemory(), bodyDeadline.Token); }
            catch (OperationCanceledException) when (bodyDeadline.IsCancellationRequested && !token.IsCancellationRequested)
            {
                // 本文が時間内に届かない（4 つのサーバーで同じ 408）。
                return UploadedPrintData.Failure(JsonError(StatusCodes.Status408RequestTimeout, "要求の本文が時間内に届きませんでした。"));
            }
            if (read == 0) break;
            if (input.Length + read > MaxPrintDataBytes)
                return UploadedPrintData.Failure(JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。"));
            input.Write(buffer, 0, read);
        }
        if (input.Length == 0)
            return UploadedPrintData.Failure(Results.BadRequest(new { error = "PREPEJ印刷データが空です。" }));

        var data = PrintDataStore.LoadJson(input.ToArray(), preserveArtText: true);
        NativeDemoPrintDataPolicy.Validate(data, requireSelfContained: true);
        var pages = NativeDemoPrintDataPolicy.ResolveUploaded(data);
        // 項目 8（2026-10-14 決定 A）：その物体だけ描かなかった・既定値で読んだ などの知らせは断らずに、応答の見出しに添える（RenderDiagnosticsHeader）。
        // それ以外の診断（値の書き先が無い など）は今までどおり要求の誤りとして断る。
        var (unclassified, diagnosticsHeader) = RenderDiagnosticsHeader.Build(pages);
        if (unclassified.Count != 0)
            throw new InvalidDataException(string.Join(" | ", unclassified.Take(10)));
        // 描くと何倍にも膨らむ入力（巨大な画素数の画像、繰返しで広がるオブジェクト）は描く前に断る（T-H1・BUG-28）。
        NativeDemoPrintDataPolicy.CheckRenderBudget(pages);
        return UploadedPrintData.Success(pages, diagnosticsHeader);
    }
    catch (OperationCanceledException) { throw; }
    catch (Microsoft.AspNetCore.Http.BadHttpRequestException exception) when (exception.StatusCode == StatusCodes.Status413PayloadTooLarge)
    {
        return UploadedPrintData.Failure(JsonError(StatusCodes.Status413PayloadTooLarge, "印刷データのサイズが上限を超えています。"));
    }
    catch (RenderBudgetExceededException exception)
    {
        return UploadedPrintData.Failure(Results.Json(new { error = exception.Message }, statusCode: StatusCodes.Status413PayloadTooLarge));
    }
    catch (InvalidDataException exception) when (exception.Message == UntrustedImageSourcePolicy.RejectionMessage)
    {
        // 送られたパスや URL は応答に入れない（固定の文言だけを返す）。
        return UploadedPrintData.Failure(Results.BadRequest(new { error = UntrustedImageSourcePolicy.RejectionMessage }));
    }
    catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException or FormatException or NotSupportedException or ArgumentException)
    {
        return UploadedPrintData.Failure(Results.BadRequest(new { error = "自己完結したPREPEJ印刷データを読み込めません。" }));
    }
}

// 誤りの本文は 4 つのサーバーで同じ形 {"error":"日本語の理由"}（2026-10-13 決定）。
static IResult JsonError(int status, string message) => Results.Json(new { error = message }, statusCode: status);

static bool IsSupportedPrintDataContentType(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return false;
    var mediaType = value.Split(';', 2)[0].Trim();
    return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
        || mediaType.Equals(PrintDataMediaType, StringComparison.OrdinalIgnoreCase);
}

Task<IResult> WithGenerationSlot(HttpContext context, Func<CancellationToken, IResult> generate) =>
    WithGenerationSlotAsync(context, generationSlots, token => Task.FromResult(generate(token)));

async Task<IResult> WithGenerationSlotAsync(HttpContext context, SemaphoreSlim slots, Func<CancellationToken, Task<IResult>> generate)
{
    using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
    deadline.CancelAfter(generationTimeout);
    var entered = false;
    try
    {
        entered = await slots.WaitAsync(TimeSpan.FromSeconds(30), deadline.Token);
        if (!entered)
            return JsonError(StatusCodes.Status503ServiceUnavailable, "帳票を生成中です。少し待って再度お試しください。");
        return await generate(deadline.Token);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
    {
        return Results.StatusCode(499);
    }
    catch (OperationCanceledException)
    {
        // 生成の時間切れは 4 つのサーバーで同じ 503（以前の .NET は 504）。
        return JsonError(StatusCodes.Status503ServiceUnavailable, "帳票の生成が時間内に終わりませんでした。");
    }
    catch
    {
        return JsonError(StatusCodes.Status500InternalServerError, "帳票を生成できませんでした。");
    }
    finally
    {
        if (entered) slots.Release();
    }
}

public partial class Program;
public sealed record ReportRequest(string CustomerName);

internal sealed record UploadedPrintData(IReadOnlyList<ResolvedReportPage>? Pages, IResult? Error, string? DiagnosticsHeader = null)
{
    public static UploadedPrintData Success(IReadOnlyList<ResolvedReportPage> pages, string? diagnosticsHeader) => new(pages, null, diagnosticsHeader);
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
