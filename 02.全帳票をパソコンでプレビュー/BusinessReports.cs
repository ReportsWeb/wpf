using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using Core = Pao.Reports.Maui.Engine.Core;
using System.Text.Json;
using Pao.Reports.Maui.Engine.Compatibility;
using Pao.Reports.Maui.Engine.Data;

namespace ReportsWpf.Learning;

/// <summary>同梱DBの値を帳票へ流し込むサンプルの業務処理。明細・改ページ・色分けをここで編集できます。</summary>
// SDKの描画エンジンではなく、利用者が変更するためのサンプルソースです。
public static class BusinessReports
{
    public delegate byte[] AssetLoader(string assetName);

    public static PrintDataDocument CreateEstimate(AssetLoader load) => CreateEstimate(load, false);

    public static PrintDataDocument CreateEstimate(AssetLoader load, bool preserveArtText)
    {
        ArgumentNullException.ThrowIfNull(load);
        using var data = Parse(load("estimate.data.json"));
        var cover = ReadDefinition(load("表紙.prepd"), preserveArtText);
        var estimate = ReadDefinition(load("見積書.prepd"), preserveArtText);
        var headers = Sheet(data, "見積ヘッダ");
        var details = Sheet(data, "見積明細").ToArray();
        var result = new PrintDataDocument { Definition = cover };

        foreach (var header in headers)
        {
            var number = Text(header, "見積番号");
            var coverPage = Page(result, cover);
            Add(coverPage, "お客様名", Text(header, "お客様名"));
            Add(coverPage, "担当者名", Text(header, "担当者名"));

            var page = Page(result, estimate);
            Add(page, "見積番号", number);
            Add(page, "お客様名", Text(header, "お客様名"));
            Add(page, "担当者名", Text(header, "担当者名"));
            Add(page, "見積日", DateTime.FromOADate(Number(header, "見積日")).ToString("yyyy年M月d日", CultureInfo.GetCultureInfo("ja-JP")));
            Add(page, "ヘッダ合計", "\\ " + Number(header, "合計金額").ToString("N0"));
            Add(page, "消費税額", Number(header, "消費税額").ToString("N0"));
            Add(page, "フッタ合計", Number(header, "合計金額").ToString("N0"));

            // 繰返しの物は、そのページで一度も書かなければ定義どおり 1 行目に 1 回だけ描く（原本 Reports.net と同じ。D-37）。
            // 青の背景と同じ位置の四角 Square10・Square5 も、毎行に描くため値なし（null）で書く。
            string[] backgrounds = ["品番白", "品名白", "数量白", "単価白", "金額白", "品番青", "品名青", "数量青", "単価青", "金額青", "Square10", "Square5"];
            for (var row = 0; row < 7; row++)
                foreach (var name in backgrounds) AddRepeated(page, name, null, row);

            var line = 0;
            foreach (var detail in details.Where(item => Text(item, "見積番号") == number))
            {
                AddRepeated(page, "品番", Text(detail, "品番"), line);
                AddRepeated(page, "品名", Text(detail, "品名"), line);
                AddRepeated(page, "数量", Number(detail, "数量").ToString("0"), line);
                AddRepeated(page, "単価", Number(detail, "単価").ToString("N0"), line);
                AddRepeated(page, "金額", Number(detail, "金額").ToString("N0"), line++);
            }
        }
        return PrintDataStore.Validate(result, preserveArtText);
    }

    public static PrintDataDocument CreatePostalList(AssetLoader load, DateTime? generatedAt = null) => CreatePostalList(load, false, generatedAt);

    public static PrintDataDocument CreatePostalList(AssetLoader load, bool preserveArtText, DateTime? generatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(load);
        using var data = Parse(load("postal.data.json"));
        var firstDesign = ReadDefinition(load("zip1.prepd"), preserveArtText);
        var secondDesign = ReadDefinition(load("zip2.prepd"), preserveArtText);
        var rows = Sheet(data, "郵便番号テーブル").ToArray();
        var result = new PrintDataDocument { Definition = firstDesign };
        var timestamp = (generatedAt ?? DateTime.Now).ToString(CultureInfo.CurrentCulture);
        const int rowsPerPage = 32;
        for (var offset = 0; offset < rows.Length; offset += rowsPerPage)
        {
            var pageNumber = result.Pages.Count + 1;
            var page = Page(result, pageNumber < 6 ? firstDesign : secondDesign);
            Add(page, "日時", timestamp);
            Add(page, "ページ", "Page-" + pageNumber);
            var pageRows = rows.Skip(offset).Take(rowsPerPage).ToArray();
            if (pageNumber < 6 && pageRows.Length > 0)
                Add(page, "QR", $"{Text(pageRows[0], "郵便番号")} {Text(pageRows[0], "市区町村")}{Text(pageRows[0], "住所")}", "DynamicBarcode");
            for (var line = 0; line < pageRows.Length; line++)
            {
                var row = pageRows[line];
                AddRepeated(page, "郵便番号", Text(row, "郵便番号"), line);
                AddRepeated(page, "市区町村", Text(row, "市区町村"), line);
                AddRepeated(page, "住所", Text(row, "住所"), line);
                AddRepeated(page, "横罫線", null, line);
                if (pageNumber > 5 && (line + 1) % 2 == 0) AddRepeated(page, "網掛け", null, line / 2);
            }
        }
        return PrintDataStore.Validate(result, preserveArtText);
    }

    public static PrintDataDocument CreateInvoice(AssetLoader load) => CreateInvoice(load, false);

    public static PrintDataDocument CreateInvoice(AssetLoader load, bool preserveArtText)
    {
        ArgumentNullException.ThrowIfNull(load);
        using var data = Parse(load("invoice.data.json"));
        var definition = ReadDefinition(load("請求書.prepd"), preserveArtText);
        var columnX = new[] { 10d, 43d, 125d, 143d, 171d };
        var columnWidth = new[] { 33d, 82d, 18d, 28d, 29d };
        for (var column = 0; column < 5; column++)
        {
            var field = definition.Objects.Single(item => item.Name == "field" + (column + 1));
            field.X = columnX[column]; field.Width = columnWidth[column];
            field.HorizontalAlignment = column < 2 ? (column == 0 ? "Center" : "Left") : "Right";
        }
        var verticalTemplate = definition.Objects.Single(item => item.Name == "vLine");
        verticalTemplate.Visible = false;
        foreach (var (x, index) in new[] { 10d, 43d, 125d, 143d, 171d, 200d }.Select((x, index) => (x, index)))
        {
            var line = verticalTemplate.Clone(); line.Name = "vLineFixed" + index; line.X = x; line.Width = 0; line.RepeatCount = 1; line.Visible = true;
            definition.Objects.Add(line);
        }
        var result = new PrintDataDocument { Definition = definition };
        var details = Sheet(data, "請求明細").ToArray();
        foreach (var header in Sheet(data, "請求ヘッダ"))
        {
            var page = Page(result, definition); var number = Text(header, "請求番号");
            Add(page, "txtNo", number); Add(page, "txtCustomer", Text(header, "お客様名"));
            Add(page, "txtDate", DateTime.Now.ToString("yyyy年M月d日"));
            Add(page, "Image1", Convert.ToBase64String(load("角印.png")), "DynamicImage");
            string[] headings = ["品番", "品名", "数量", "単価", "金額"];
            for (var column = 0; column < headings.Length; column++) AddRepeated(page, "field" + (column + 1), headings[column], 0);
            long total = 0; var line = 1;
            foreach (var detail in details.Where(item => Text(item, "請求番号") == number))
            {
                var quantity = (long)Number(detail, "数量"); var unitPrice = (long)Number(detail, "単価"); var amount = quantity * unitPrice;
                AddRepeated(page, "field1", Text(detail, "品番"), line); AddRepeated(page, "field2", Text(detail, "品名"), line);
                AddRepeated(page, "field3", quantity.ToString(), line); AddRepeated(page, "field4", unitPrice.ToString("N0"), line);
                AddRepeated(page, "field5", amount.ToString("N0"), line++); total += amount;
            }
            const double taxRate = .05; var tax = (long)(total * taxRate);
            AddRepeated(page, "field4", "小計", 11); AddRepeated(page, "field5", total.ToString("N0"), 11);
            AddRepeated(page, "field4", "消費税", 12); AddRepeated(page, "field5", tax.ToString("N0"), 12);
            AddRepeated(page, "field4", "合計", 13); AddRepeated(page, "field5", (total + tax).ToString("N0"), 13);
            Add(page, "txtTotal", (total + tax).ToString("N0"));
            for (var row = 0; row < 15; row++) AddRepeated(page, "hLine", null, row);
            for (var row = 0; row < 14; row++) AddRepeated(page, "LineRect", null, row);
        }
        var materialized = new PrintDataDocument();
        foreach (var resolved in PrintDataStore.Resolve(result))
        {
            if (resolved.Diagnostics.Count != 0) throw new InvalidDataException(string.Join(" | ", resolved.Diagnostics));
            var document = resolved.Document;
            for (var column = 0; column < 5; column++)
            {
                var fields = document.Objects.Where(o => o.Name == "field" + (column + 1)).OrderBy(o => o.Y).ToArray();
                for (var row = 0; row < fields.Length; row++)
                {
                    fields[row].FontBold = row == 0;
                    fields[row].FontSizePt = row == 0 ? fields[row].FontSizePt : 12;
                    fields[row].HorizontalAlignment = row == 0 ? "Center" : column == 0 ? "Center" : column == 1 ? "Left" : "Right";
                }
                if (column == 3)
                    foreach (var field in fields.Where(field => field.Text is "小計" or "消費税" or "合計"))
                    { field.FontBold = true; field.FontSizePt = 16; field.HorizontalAlignment = "Center"; }
            }
            var fills = document.Objects.Where(o => o.Name == "LineRect").OrderBy(o => o.Y).ToArray();
            for (var row = 1; row < fills.Length; row++)
            {
                fills[row].FillColor = row < fills.Length - 3 ? (row % 2 == 1 ? "#FFFFFFFF" : "#FF87CEFA") : "#FFFFFFB4";
                fills[row].FillEnabled = true; fills[row].FillStyle = "Solid";
            }
            var lines = document.Objects.Where(o => o.Name == "hLine").OrderBy(o => o.Y).ToArray();
            if (lines.Length > 0) { lines[0].StrokeWidth = .5; lines[^1].StrokeWidth = .5; }
            if (lines.Length > 1) lines[1].LineType = "Double";
            if (lines.Length > 3) lines[^4].LineType = "Double";
            foreach (var line in document.Objects.Where(o => o.Name is "vLineFixed0" or "vLineFixed5")) line.StrokeWidth = .5;
            materialized.Pages.Add(new PrintDataPage { Index = resolved.Index, Definition = document });
        }
        materialized.Definition = materialized.Pages[0].Definition!.Clone();
        return PrintDataStore.Validate(materialized, preserveArtText);
    }

    public static PrintDataDocument CreateProductClassification(AssetLoader load) => CreateProductClassification(load, false);

    public static PrintDataDocument CreateProductClassification(AssetLoader load, bool preserveArtText)
    {
        ArgumentNullException.ThrowIfNull(load);
        using var data = Parse(load("products.data.json"));
        var definition = ReadDefinition(load("商品一覧.prepd"), preserveArtText);
        var largeNames = Sheet(data, "M_大分類").ToDictionary(row => Text(row, "大分類コード"), row => Text(row, "大分類名称"));
        var smallNames = Sheet(data, "M_小分類").ToDictionary(row => (Text(row, "大分類コード"), Text(row, "小分類コード")), row => Text(row, "小分類名称"));
        var products = Sheet(data, "M_商品").OrderBy(row => Text(row, "大分類コード")).ThenBy(row => Text(row, "小分類コード")).ToArray();
        var lines = new List<(string Large, string Small, string Number, string Name, string Kind)>();
        foreach (var largeGroup in products.GroupBy(row => Text(row, "大分類コード")))
        {
            var firstLarge = true;
            foreach (var smallGroup in largeGroup.GroupBy(row => Text(row, "小分類コード")))
            {
                var firstProduct = true;
                foreach (var product in smallGroup)
                {
                    lines.Add((firstLarge ? largeNames[largeGroup.Key] : "", firstProduct ? smallNames[(largeGroup.Key, smallGroup.Key)] : "",
                        Text(product, "品番"), Text(product, "品名"), "detail"));
                    firstLarge = false;
                    firstProduct = false;
                }
                lines.Add(("", $"小分類（{smallNames[(largeGroup.Key, smallGroup.Key)]}）小計", $"{smallGroup.Count()} 冊", "", "small"));
            }
            lines.Add(("", $"大分類（{largeNames[largeGroup.Key]}）小計", $"{largeGroup.Count()} 冊", "", "large"));
        }
        var result = new PrintDataDocument { Definition = definition }; const int rowsPerPage = 20;
        for (var offset = 0; offset < lines.Count; offset += rowsPerPage)
        {
            var page = Page(result, definition); var rows = lines.Skip(offset).Take(rowsPerPage).ToArray();
            for (var line = 0; line < rows.Length; line++)
            {
                AddRepeated(page, "大分類", rows[line].Large, line); AddRepeated(page, "小分類", rows[line].Small, line);
                AddRepeated(page, "品番", rows[line].Number, line); AddRepeated(page, "品名", rows[line].Name, line);
                foreach (var frame in new[] { "枠_大分類", "枠_小分類", "枠_品番", "枠_品名" }) AddRepeated(page, frame, null, line);
            }
        }
        var materialized = new PrintDataDocument();
        for (var pageIndex = 0; pageIndex < result.Pages.Count; pageIndex++)
        {
            var resolved = PrintDataStore.Resolve(new PrintDataDocument { Definition = definition, Pages = [result.Pages[pageIndex]] }).Single();
            if (resolved.Diagnostics.Count != 0) throw new InvalidDataException(string.Join(" | ", resolved.Diagnostics));
            var pageRows = lines.Skip(pageIndex * rowsPerPage).Take(rowsPerPage).ToArray();
            for (var row = 0; row < pageRows.Length; row++)
            {
                var color = pageRows[row].Kind == "small" ? "#FFFFFFE0" : pageRows[row].Kind == "large" ? "#FFFFB6C1" : null;
                if (color is null) continue;
                foreach (var name in new[] { "枠_大分類", "枠_小分類", "枠_品番", "枠_品名" })
                {
                    var frame = resolved.Document.Objects.Where(o => o.Name == name).OrderBy(o => o.Y).ElementAt(row);
                    frame.FillColor = color; frame.FillEnabled = true; frame.FillStyle = "Solid";
                }
            }
            materialized.Pages.Add(new PrintDataPage { Index = resolved.Index, Definition = resolved.Document });
        }
        materialized.Definition = materialized.Pages[0].Definition!.Clone();
        return PrintDataStore.Validate(materialized, preserveArtText);
    }

    private static JsonDocument Parse(byte[] json) => JsonDocument.Parse(json);
    private static IEnumerable<JsonElement> Sheet(JsonDocument document, string name) => document.RootElement.GetProperty("sheets").GetProperty(name).EnumerateArray();
    private static string Text(JsonElement row, string name) => row.GetProperty(name).ValueKind == JsonValueKind.String ? row.GetProperty(name).GetString() ?? "" : row.GetProperty(name).ToString();
    private static double Number(JsonElement row, string name)
    {
        var value = row.GetProperty(name);
        return value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : double.Parse(value.GetString() ?? "0", CultureInfo.InvariantCulture);
    }
    private static Core.ReportDocument ReadDefinition(byte[] xml, bool preserveArtText)
    {
        using var stream = new MemoryStream(xml, writable: false);
        return PrintDefineXmlReader.Read(ReportsXmlDocument.Load(stream).Document, preserveArtText);
    }
    private static PrintDataPage Page(PrintDataDocument document, Core.ReportDocument definition)
    {
        var page = new PrintDataPage { Index = document.Pages.Count + 1, Definition = definition };
        document.Pages.Add(page);
        return page;
    }
    private static void Add(PrintDataPage page, string name, string value, string type = "DynamicText") =>
        page.Values.Add(new PrintDataValue { Name = name, Type = type, Value = value });
    // 罫線・図形（横罫線・網掛け・背景の四角・枠など）は値なし（null）で描く。"" を書くと描かない（原本 Reports.net と同じ。D-33）。
    private static void AddRepeated(PrintDataPage page, string name, string? value, int row) =>
        page.Values.Add(new PrintDataValue { Name = name, Type = "DynamicText", IndexX = 0, IndexY = row, Value = value });
}
