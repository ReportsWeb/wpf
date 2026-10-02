using System.Text.Json.Nodes;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Pao.Reports.Web;
namespace ReportsWeb.Sample;
/// <summary>Original PHP/Java report operations, not static pre-rendered documents.</summary>
public sealed class SampleCatalog(string resources, string databasePath, Func<DateTime>? clock = null)
{
    public static readonly Dictionary<string, string> Samples = new()
    {
        ["quick-start"] = "あっという間に帳票出力",
        ["multiples-of-ten"] = "10のサンプル",
        ["postal"] = "郵便番号一覧（定義切替）",
        ["estimate"] = "見積書（表紙＋明細）",
        ["invoice"] = "請求書",
        ["products"] = "商品大小分類（途中で小計）",
        ["business-card"] = "名刺",
        ["design-showcase"] = "デザイン機能見本"
    };
    public JsonObject Definition(string sample)
    {
        Check(sample);
        return Load(sample == "postal" ? "postal-1" : sample);
    }
    JsonObject Load(string file) => JsonNode.Parse(File.ReadAllText(Path.Combine(resources, "definitions", file + ".prepdj")))!.AsObject();
    public PrintData Create(string sample)
    {
        Check(sample);
        return sample switch
        {
            "multiples-of-ten" => Multiples(),
            "postal" => Postal(),
            "estimate" => Estimate(),
            "invoice" => Invoice(),
            "products" => Products(),
            _ => Simple(sample)
        };
    }
    PrintData Simple(string sample)
    {
        var p = new PrintData().SetDefinition(Definition(sample))
            .PageStart();
        if (sample == "quick-start")
            p
                .SetValue("Title", "あっという間に帳票出力")
                .SetValue("Customer", "株式会社パオ・アット・オフィス 御中")
                .SetValue("Message", "Reports Web Quick Start — 同じ帳票定義から、プレビュー・PREPEJ・SVG・PDFを作成しました。")
                .SetValue("Amount", "¥123,456");
        return p
            .PageEnd();
    }
    PrintData Multiples()
    {
        var p = new PrintData().SetDefinition(Definition("multiples-of-ten"));
        for (var page = 1; page <= 4; page++)
        {
            p
                .PageStart()
                .SetValue("日付", Now("yyyy/MM/dd HH:mm:ss"))
                .SetValue("頁数", "Page - " + page)
                .SetValue("フォントサイズ", "フォントサイズ\n 変更後")
                .ChangeAttributes("フォントサイズ", A(("fontSize", 12)));
            // 2ページ目だけ、ページ上部の線「Line3」を非表示にする。空文字と drawing=false を指定する。
            if (page == 2)
                p
                    .SetValue("Line3", "", 0, false);
            for (var i = 0; i < 15; i++)
            {
                int n = (page - 1) * 15 + i + 1;
                p
                    .SetValue("行番号", n, i)
                    .SetValue("10倍数", n * 10, i)
                    .SetValue("横線", "", i);
                // 100で割り切れる値だけ、この行の文字色を青にする。
                if ((n * 10) % 100 == 0) p.ChangeRowAttributes("10倍数", A(("FontColor", "#0000FF")), i);
            }
            p
                .PageEnd();
        }
        return p;
    }
    PrintData Postal()
    {
        var rows = Rows("postal", 1);
        var first = Load("postal-1");
        var second = Load("postal-2");
        var p = new PrintData().SetDefinition(first);
        for (int offset = 0, page = 0; offset < rows.Count; offset += 32, page++)
        {
            p
                .PageStart(page < 5 ? first : second)
                .SetValue("ページ", "Page-" + (page + 1))
                .SetValue("日時", Now("yyyy/MM/dd HH:mm:ss"));
            var chunk = rows.Skip(offset).Take(32).ToList();
            for (int i = 0; i < chunk.Count; i++)
            {
                var v = chunk[i].Select(x => x.Value?.ToString() ?? "").ToArray();
                p
                    .SetValue("郵便番号", v.ElementAtOrDefault(0) ?? "", i)
                    .SetValue("市区町村", v.ElementAtOrDefault(1) ?? "", i)
                    .SetValue("住所", v.ElementAtOrDefault(2) ?? "", i)
                    .SetValue("横罫線", "", i);
                if (page >= 5 && i % 2 == 1)
                    p
                        .SetValue("網掛け", "", i);
            }
            if (page < 5)
            {
                var v = chunk[0].Select(x => x.Value?.ToString() ?? "").ToArray();
                p
                    .SetValue("QR", (v[0] + " " + v[1] + v[2]).Trim());
            }
            p
                .PageEnd();
        }
        return p;
    }
    PrintData Estimate()
    {
        var headers = Rows("estimate", 1);
        var details = Rows("estimate", 2);
        var cover = Load("estimate-cover");
        var body = Load("estimate");
        var p = new PrintData().SetDefinition(cover);
        foreach (var h in headers)
        {
            p
                .PageStart(cover)
                .SetValue("お客様名", S(h, "お客様名"))
                .SetValue("担当者名", S(h, "担当者名"))
                .PageEnd()
                .PageStart(body)
                .SetValue("見積番号", S(h, "見積番号"))
                .SetValue("お客様名", S(h, "お客様名"))
                .SetValue("担当者名", S(h, "担当者名"))
                .SetValue("見積日", DateTime.Parse(S(h, "見積日"), CultureInfo.InvariantCulture).ToString("yyyy年M月d日", CultureInfo.InvariantCulture))
                .SetValue("ヘッダ合計", "\\ " + Number(S(h, "合計金額")))
                .SetValue("消費税額", Number(S(h, "消費税額")))
                .SetValue("フッタ合計", Number(S(h, "合計金額")));
            for (int i = 0; i <= 6; i++)
                foreach (var n in new[] {
 "品番白", "品名白", "数量白", "単価白", "金額白", "品番青", "品名青", "数量青", "単価青", "金額青" })
                    p
                        .SetValue(n, "", i);
            int row = 0;
            foreach (var r in Match(details, "見積番号", S(h, "見積番号")))
            {
                p
                    .SetValue("品番", S(r, "品番"), row)
                    .SetValue("品名", S(r, "品名"), row)
                    .SetValue("数量", S(r, "数量"), row)
                    .SetValue("単価", Number(S(r, "単価")), row)
                    .SetValue("金額", Number(S(r, "金額")), row);
                row++;
            }
            p
                .PageEnd();
        }
        return p;
    }
    PrintData Invoice()
    {
        var headers = Rows("invoice", 1);
        var details = Rows("invoice", 2);
        var d = Definition("invoice");
        var seal = Obj(d, "Image1");
        seal["ImagePath"] = "";
        seal["ImageDataBase64"] = Convert.ToBase64String(File.ReadAllBytes(Path.Combine(resources, "images", "kakuin.png")));
        var p = new PrintData().SetDefinition(d);
        double px = 96 / 25.4;
        foreach (var h in headers)
        {
            int maxH = Math.Max(4, Repeat(Obj(d, "hLine")) - 1), maxV = Math.Max(1, Repeat(Obj(d, "vLine")) - 1);
            p
                .PageStart()
                .SetValue("txtNo", S(h, "請求番号"))
                .SetValue("txtCustomer", S(h, "お客様名"))
                .SetValue("txtDate", Now("yyyy年M月d日"));
            double[] adjust = [-5, 44, -20, -10, -9], xs = new double[maxV];
            double next = 0;
            for (int j = 0; j < maxV; j++)
            {
                var b = Obj(d, "field" + (j + 1));
                xs[j] = j == 0 ? D(b, "X") * px : next;
                next = xs[j] + (D(b, "Width") + adjust[j]) * px;
            }
            for (int i = 0; i < maxH; i++)
            {
                p
                    .SetValue("hLine", "", i)
                    .SetValue("LineRect", "", i);
                if (i == 0)
                    p
                        .ChangeAttributes("hLine", A(("borderWidth", 0.5 * px)), i);
                if (i == 1)
                    p
                        .ChangeAttributes("hLine", A(("strokeStyle", "Double")), i);
                var color = i == 0 ? "#FFFFDAB9" : i < maxH - 3 ? (i % 2 == 1 ? "#FFFFFFFF" : "#FF87CEFA") : "#FFFFFFB4";
                p
                    .ChangeAttributes("LineRect", A(("background", color), ("fillEnabled", true), ("fillStyle", "Solid"), ("borderColor", "#FFFFFFFF")), i);
                for (int j = 0; j < maxV; j++)
                {
                    // Preserve the native distinction between selecting and writing a cell.
                    if (j < 3 && i > details.Count(r => S(r, "請求番号") == S(h, "請求番号"))) continue;
                    var b = Obj(d, "field" + (j + 1));
                    p
                        .SetValue("field" + (j + 1), "", i)
                        .ChangeAttributes("field" + (j + 1), A(("x", xs[j]), ("width", (D(b, "Width") + adjust[j]) * px), ("bold", i == 0), ("fontSize", i == 0 ? D(b, "FontSizePt") : 12), ("horizontalAlignment", i == 0 ? "Center" : j == 1 ? "Left" : j == 0 ? "Center" : "Right")), i);
                }
            }
            for (int j = 0; j <= maxV; j++)
            {
                p
                    .SetValue("vLine", "", j)
                    .ChangeAttributes("vLine", A(("x", j < maxV ? xs[j] : next)), j);
                if (j == 0 || j == maxV)
                    p
                        .ChangeAttributes("vLine", A(("borderWidth", 0.5 * px)), j);
            }
            string[] labels = ["品番", "品名", "数量", "単価", "金額"];
            for (int j = 0; j < labels.Length; j++)
                p
                    .SetValue("field" + (j + 1), labels[j], 0);
            long total = 0;
            int row = 1;
            foreach (var r in Match(details, "請求番号", S(h, "請求番号")))
            {
                long quantity = Convert.ToInt64(double.Parse(S(r, "数量"), CultureInfo.InvariantCulture));
                double unitPrice = double.Parse(S(r, "単価"), CultureInfo.InvariantCulture);
                long roundedUnitPrice = Convert.ToInt64(unitPrice);
                long amount = checked(quantity * roundedUnitPrice);
                total = checked(total + amount);
                p
                    .SetValue("field1", S(r, "品番"), row)
                    .SetValue("field2", S(r, "品名"), row)
                    .SetValue("field3", S(r, "数量"), row)
                    .SetValue("field4", unitPrice.ToString("N0", CultureInfo.InvariantCulture), row)
                    .SetValue("field5", amount.ToString("N0", CultureInfo.InvariantCulture), row);
                row++;
            }
            double tax = total * 0.05;
            string[] totals = ["小計", "消費税", "合計"];
            double[] amounts = [total, tax, total + tax];
            for (int k = 0; k < 3; k++)
            {
                int r = maxH - 3 + k;
                p
                    .SetValue("field4", totals[k], r)
                    .SetValue("field5", amounts[k].ToString("N0", CultureInfo.InvariantCulture), r)
                    .ChangeAttributes("field4", A(("fontSize", 16), ("bold", true), ("horizontalAlignment", "Center")), r);
            }
            p
                .SetValue("txtTotal", (total + tax).ToString("N0", CultureInfo.InvariantCulture))
                .ChangeAttributes("hLine", A(("strokeStyle", "Double")), maxH - 3)
                .SetValue("hLine", "", maxH)
                .ChangeAttributes("hLine", A(("borderWidth", 0.5 * px)), maxH)
                .PageEnd();
        }
        return p;
    }
    PrintData Products()
    {
        var big = Rows("products", 1).ToDictionary(r => S(r, "大分類コード"), r => S(r, "大分類名称"));
        var small = Rows("products", 2).ToDictionary(r => S(r, "大分類コード") + ":" + S(r, "小分類コード"), r => S(r, "小分類名称"));
        var stream = new List<Dictionary<string, string>>();
        string? pb = null, ps = null;
        int bc = 0, sc = 0;
        foreach (var r in Rows("products", 3))
        {
            var bn = big.GetValueOrDefault(S(r, "大分類コード"), "");
            var sn = small.GetValueOrDefault(S(r, "大分類コード") + ":" + S(r, "小分類コード"), "");
            if (ps != null && ps != sn)
            {
                stream.Add(Subtotal("small", ps, sc));
                sc = 0;
            }
            if (pb != null && pb != bn)
            {
                stream.Add(Subtotal("big", pb, bc));
                bc = 0;
            }
            stream.Add(new()
            {
                ["大分類"] = pb == bn ? "" : bn,
                ["小分類"] = ps == sn ? "" : sn,
                ["品番"] = S(r, "品番"),
                ["品名"] = S(r, "品名"),
                ["kind"] = "detail"
            });
            pb = bn;
            ps = sn;
            bc++;
            sc++;
        }
        if (ps != null)
            stream.Add(Subtotal("small", ps, sc));
        if (pb != null)
            stream.Add(Subtotal("big", pb, bc));
        var p = new PrintData().SetDefinition(Definition("products"));
        foreach (var chunk in stream.Chunk(20))
        {
            p
                .PageStart();
            for (int i = 0; i < chunk.Length; i++)
            {
                var r = chunk[i];
                foreach (var n in new[] {
 "大分類", "小分類", "品番", "品名" })
                    p
                        .SetValue(n, r[n], i);
                foreach (var n in new[] {
 "枠_大分類", "枠_小分類", "枠_品番", "枠_品名" })
                {
                    p
                        .SetValue(n, "", i);
                    if (r["kind"] != "detail")
                        p
                            .ChangeAttributes(n, A(("background", r["kind"] == "small" ? "#FFFFFFE0" : "#FFFFB6C1"), ("fillEnabled", true), ("fillStyle", "Solid")), i);
                }
            }
            p
                .PageEnd();
        }
        return p;
    }
    static Dictionary<string, string> Subtotal(string kind, string name, int count) => new()
    {
        ["大分類"] = "",
        ["小分類"] = (kind == "small" ? "小分類" : "大分類") + "(" + name + ")小計",
        ["品番"] = count + " 冊",
        ["品名"] = "",
        ["kind"] = kind
    };
    List<JsonObject> Rows(string sample, int sheet)
    {
        var table = (sample, sheet) switch
        {
            ("postal", 1) => "郵便番号テーブル",
            ("estimate", 1) => "見積ヘッダ",
            ("estimate", 2) => "見積明細",
            ("invoice", 1) => "請求ヘッダ",
            ("invoice", 2) => "請求明細",
            ("products", 1) => "M_大分類",
            ("products", 2) => "M_小分類",
            ("products", 3) => "M_商品",
            _ => throw new ArgumentException("未対応のデータ表です。")
        };
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        c.Open();
        using var q = c.CreateCommand();
        q.CommandText = $"SELECT * FROM [{table}] ORDER BY rowid";
        using var reader = q.ExecuteReader();
        var rows = new List<JsonObject>();
        while (reader.Read())
        {
            var row = new JsonObject();
            for (var i = 0; i < reader.FieldCount; i++)
                row[reader.GetName(i)] = reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            rows.Add(row);
        }
        if (sample == "products" && sheet == 3)
            rows = rows.OrderBy(r => decimal.Parse(S(r, "大分類コード"))).ThenBy(r => decimal.Parse(S(r, "小分類コード"))).ToList();
        return rows;
    }
    static IEnumerable<JsonObject> Match(IEnumerable<JsonObject> rows, string key, string value) => rows.Where(r => S(r, key) == value);
    static string S(JsonNode r, string key) => r[key]?.ToString() ?? "";
    static JsonNode Obj(JsonNode d, string name) => d["Objects"]!.AsArray().First(o => S(o!, "Name") == name)!;
    static int Repeat(JsonNode o) => int.Parse((o["Repeat"] ?? o["RepeatCount"])!.ToString());
    static double D(JsonNode o, string key) => double.Parse(S(o, key), CultureInfo.InvariantCulture);
    static string Number(object value) => decimal.Round(decimal.Parse(Convert.ToString(value, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture), 0, MidpointRounding.AwayFromZero).ToString("#,##0", CultureInfo.InvariantCulture);
    string Now(string format) => (clock?.Invoke() ?? TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo"))).ToString(format, CultureInfo.InvariantCulture);
    static Dictionary<string, object> A(params (string key, object value)[] pairs) => pairs.ToDictionary(x => x.key, x => x.value);
    static void Check(string sample)
    {
        if (!Samples.ContainsKey(sample))
            throw new ArgumentException("帳票を選択してください。");
    }
}
