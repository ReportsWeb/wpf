using System.Text.Json;
using System.Text.RegularExpressions;
using Pao.Reports.Maui.Engine.Data;

/// <summary>
/// 項目 8（2026-10-14 コーディネーターの決定 A、IMPL-BRIEF 02:00 の形。4 版のサーバーで同じ）：その物体だけ描かなかった・既定値で読んだ などの
/// 知らせは、要求を断らずに PDF・SVG を返し、200 の応答の見出し x-reports-image-diagnostics に添える。値は encodeURIComponent(JSON.stringify(配列))
/// と同じ符号化（ASCII だけ）。要素は objectName・pageNumber（定義の読み込みの知らせは省く）・reason・source・message（日本語）の順で、値の無いものは出さない。
/// reason：画像 unavailable・corrupt・unsupported、バーコード invalidBarcodeValue、unknownBarcodeKind・unknownEnumValue・unknownElement。
/// この形に当てはまらない診断（値の書き先が無い など）は、今までどおり要求の誤りとして断る（Classify が null）。
/// </summary>
internal static class RenderDiagnosticsHeader
{
    public const string Name = "x-reports-image-diagnostics";

    // JSON.stringify と同じく、日本語を \u で書かずにそのまま書く（見出しでは UTF-8 の % 符号になる）。
    private static readonly JsonSerializerOptions JsonOptions = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // 定義の読み込みの知らせ（種類・列挙値・要素）はページによらないので、ページ番号を付けない（同じ文は 1 回だけ）。
    private sealed record Item(string ObjectName, int? PageNumber, string Reason, string? Source, string Message);

    /// <summary>分けられない診断の一覧（断る理由）と、見出しの値（要素が無ければ null）。</summary>
    public static (IReadOnlyList<string> Unclassified, string? Header) Build(IReadOnlyList<ResolvedReportPage> pages)
    {
        var unclassified = new List<string>();
        var items = new List<Item>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
            foreach (var text in page.Diagnostics)
            {
                if (Classify(text, page.Index) is not { } item) { unclassified.Add(text); continue; }
                if (seen.Add($"{item.PageNumber}|{item.Message}")) items.Add(item);
            }
        return (unclassified, items.Count == 0 ? null : Uri.EscapeDataString(JsonSerializer.Serialize(items.Select(ToJson), JsonOptions)));
    }

    private static Dictionary<string, object> ToJson(Item item)
    {
        var json = new Dictionary<string, object> { ["objectName"] = item.ObjectName };
        if (item.PageNumber is { } page) json["pageNumber"] = page;
        json["reason"] = item.Reason;
        if (!string.IsNullOrEmpty(item.Source)) json["source"] = item.Source!;
        json["message"] = item.Message;
        return json;
    }

    private static readonly Regex Quoted = new("「([^」]*)」", RegexOptions.CultureInvariant);
    private static readonly Regex KindValue = new("Kind=\"([^\"]*)\"|BarcodeKind ([^）]+)）", RegexOptions.CultureInvariant);
    private static readonly Regex Attribute = new("([A-Za-z0-9_]+)=\"", RegexOptions.CultureInvariant);
    private static readonly Regex Element = new("要素 <([^>]+)>", RegexOptions.CultureInvariant);
    private static readonly Regex Image = new("^Image object '(.*)': (.*); the image was not drawn\\.$", RegexOptions.CultureInvariant);

    private static Item? Classify(string text, int? page)
    {
        var name = Quoted.Match(text) is { Success: true } quoted ? quoted.Groups[1].Value : "";
        if (text.Contains("[LEGACY_BARCODE_KIND]", StringComparison.Ordinal) || (text.StartsWith("バーコード「", StringComparison.Ordinal) && text.Contains("Code128 として描きます", StringComparison.Ordinal)))
        {
            var kind = KindValue.Match(text);
            return new(name, null, "unknownBarcodeKind", kind.Success ? (kind.Groups[1].Success ? kind.Groups[1].Value : kind.Groups[2].Value) : null, Clean(text));
        }
        if (text.Contains("既定値", StringComparison.Ordinal) && text.Contains("として読みます", StringComparison.Ordinal))
            return new(name, null, "unknownEnumValue", Attribute.Match(text) is { Success: true } attribute ? attribute.Groups[1].Value : null, Clean(text));
        if (Element.Match(text) is { Success: true } element)
            return new(name, null, "unknownElement", element.Groups[1].Value, Clean(text));
        if (text.StartsWith("バーコード「", StringComparison.Ordinal) && text.EndsWith("ため描きません。", StringComparison.Ordinal))
            return new(name, page, "invalidBarcodeValue", null, text);
        if (Image.Match(text) is { Success: true } image)
        {
            var cause = image.Groups[2].Value;
            var (reason, japanese) = cause.Contains("not found", StringComparison.Ordinal) || cause.Contains("could not be read", StringComparison.Ordinal) || cause.Contains("empty", StringComparison.Ordinal)
                ? ("unavailable", "読み込めない")
                : cause.Contains("not a supported", StringComparison.Ordinal) ? ("unsupported", "対応していない形式の")
                : ("corrupt", "壊れている");
            return new(image.Groups[1].Value, page, reason, null, $"画像「{image.Groups[1].Value}」は{japanese}ため描きません。");
        }
        return null;
    }

    // 版の中の目印（[LEGACY_ENUM_DEFAULT] など）は利用者向けの文から外す。
    private static string Clean(string text) => Regex.Replace(text, "^\\[[A-Z_]+\\] ", "");
}
