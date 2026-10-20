using Pao.Reports.Maui.Engine.Core;
using Pao.Reports.Maui.Engine.Data;

// 受け取った PREPEJ（POST /api/print-data/svg・pdf）と、このサンプルが返す PREPEJ の検査。
// tests/Pao.Reports.Wpf.Round4FixVerify がこのファイルをそのまま取り込んで試験する。
internal static class NativeDemoPrintDataPolicy
{
    public static void Validate(PrintDataDocument data, bool requireSelfContained)
    {
        PrintDataStore.Validate(data, preserveArtText: true);
        if (ContainsArtText(data.Definition) || data.Pages.Any(page => ContainsArtText(page.Definition)))
            throw new InvalidDataException("ArtText is outside the Reports.Web product scope.");
        // ENG-COMPAT-081: a page whose Definition is null inherits the root Definition.
        // Only a page with neither its own nor a root PrintDefine is incomplete.
        if (requireSelfContained && data.Pages.Any(page => page.Definition is null && data.Definition is null))
            throw new InvalidDataException("Every canonical PREPEJ page must contain its PrintDefine or inherit the root PrintDefine.");
        // 利用者から受け取った定義の画像は、埋め込みの PNG・JPEG・GIF だけを使う（Reports.Web の server/imagePolicy.ts と同じ）。
        // パス・URL・UNC は、開いたり接続したりする前に文字列の段階で断る。パスと埋め込みの両方があれば埋め込みを使う。
        if (requireSelfContained)
            foreach (var definition in Definitions(data))
                UntrustedImageSourcePolicy.Validate(definition);
    }

    // 受け取った PREPEJ の展開。動的画像の値（Values・XY 値）をファイルとして調べず、
    // 動的属性で変わったものも含め、展開後の全ページの画像をもう一度同じ規則で確かめる。
    public static IReadOnlyList<ResolvedReportPage> ResolveUploaded(PrintDataDocument data) =>
        UntrustedImageSourcePolicy.Resolve(data);

    // 描画の重さの上限（T-H1・BUG-28）。受け取りの大きさ（8 MiB）の中でも、描くと何倍にも膨らむ入力を、描く前に断る（413）。
    //   ・埋め込み画像の画素数：1 枚 4,000 万画素、1 回の要求の合計 1 億 2,000 万画素まで（展開すると 1 画素 4 バイト）。
    //     圧縮した PNG は数 MB でも、展開すると何 GB にもなる（30000×30000 の白い PNG は 2.5 MB で、展開すると 3.6 GB）。
    //   ・繰返しで広がるオブジェクトの数：1 ページ 100,000 個まで（エンジンの ReportRepeatLayout と同じ上限。超えると描画の途中で失敗する）。
    // 画像の大きさは、ファイルの先頭（PNG の IHDR、GIF の画面の大きさ、JPEG の SOF）だけを読んで調べる（展開しない）。
    public const long MaxImagePixels = 40_000_000;
    public const long MaxTotalImagePixels = 120_000_000;
    public const int MaxObjectsPerPage = 100_000;

    public static void CheckRenderBudget(IReadOnlyList<ResolvedReportPage> pages)
    {
        long total = 0;
        foreach (var page in pages)
        {
            long objects = 0;
            foreach (var item in page.Document.Objects)
            {
                objects += (long)Math.Max(1, item.RepeatCount) * (item.RepeatBothDirections ? Math.Max(1, item.RepeatCountX) : 1);
                if (objects > MaxObjectsPerPage) throw new RenderBudgetExceededException($"1 ページのオブジェクトが {MaxObjectsPerPage} 個を超えます。");
            }
            foreach (var (image, copies) in Images(page.Document))
            {
                var pixels = ImagePixels(image);
                if (pixels > MaxImagePixels) throw new RenderBudgetExceededException($"埋め込み画像が大きすぎます（1 枚 {MaxImagePixels / 1_000_000} 百万画素まで）。");
                total += pixels * copies;
                if (total > MaxTotalImagePixels) throw new RenderBudgetExceededException($"埋め込み画像の合計が大きすぎます（{MaxTotalImagePixels / 1_000_000} 百万画素まで）。");
            }
        }
    }

    // 画像と、繰返しで描かれる枚数。
    private static IEnumerable<(string Image, long Copies)> Images(ReportDocument document)
    {
        if (!string.IsNullOrWhiteSpace(document.BackgroundImageDataBase64)) yield return (document.BackgroundImageDataBase64, 1);
        foreach (var item in document.Objects)
            if (item.Kind == ReportObjectKind.Image && !string.IsNullOrWhiteSpace(item.ImageDataBase64))
                yield return (item.ImageDataBase64, (long)Math.Max(1, item.RepeatCount) * (item.RepeatBothDirections ? Math.Max(1, item.RepeatCountX) : 1));
    }

    // 先頭の 192 KB だけを復号して、幅×高さを返す。分からなければ 0（描画側が読めずに代わりの表示にする）。
    internal static long ImagePixels(string base64)
    {
        var compact = new string(base64.Where(c => !char.IsWhiteSpace(c)).Take(262_144).ToArray());
        compact = compact[..(compact.Length / 4 * 4)];
        byte[] head;
        try { head = Convert.FromBase64String(compact); } catch (FormatException) { return 0; }
        static int Big16(byte[] b, int i) => (b[i] << 8) | b[i + 1];
        static long Big32(byte[] b, int i) => ((long)b[i] << 24) | ((long)b[i + 1] << 16) | ((long)b[i + 2] << 8) | b[i + 3];
        if (head.Length >= 24 && head[0] == 0x89 && head[1] == 0x50 && head[2] == 0x4E && head[3] == 0x47)
            return Big32(head, 16) * Big32(head, 20);
        if (head.Length >= 10 && head[0] == (byte)'G' && head[1] == (byte)'I' && head[2] == (byte)'F')
            return (long)(head[6] | (head[7] << 8)) * (head[8] | (head[9] << 8));
        if (head.Length >= 4 && head[0] == 0xFF && head[1] == 0xD8)
        {
            var position = 2;
            while (position + 9 < head.Length)
            {
                if (head[position] != 0xFF) { position++; continue; }
                var marker = head[position + 1];
                if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7) or 0xFF) { position += marker == 0xFF ? 1 : 2; continue; }
                // SOF0〜SOF15（DHT=C4・JPG=C8・DAC=CC を除く）に高さと幅がある
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                    return (long)Big16(head, position + 5) * Big16(head, position + 7);
                position += 2 + Big16(head, position + 2);
            }
        }
        return 0;
    }

    private static bool ContainsArtText(ReportDocument? definition) =>
        definition?.Objects.Any(item => item.Kind == ReportObjectKind.ArtText) == true;

    private static IEnumerable<ReportDocument> Definitions(PrintDataDocument data)
    {
        if (data.Definition is not null) yield return data.Definition;
        foreach (var page in data.Pages)
            if (page.Definition is not null) yield return page.Definition;
    }
}

// 描画の重さの上限を超えた（応答は 413）。
internal sealed class RenderBudgetExceededException(string message) : Exception(message);
