using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Pao.Reports.Maui.Engine.Data;

namespace ReportsWpf.Learning;

public sealed class NativeSampleCatalog(string resources, string databasePath)
{
    readonly ReportsWeb.Sample.SampleCatalog simple = new(resources, databasePath);

    public PrintDataDocument Create(string id) => id switch
    {
        "postal" => BusinessReports.CreatePostalList(Load, preserveArtText: true),
        "estimate" => BusinessReports.CreateEstimate(Load, preserveArtText: true),
        "invoice" => BusinessReports.CreateInvoice(Load, preserveArtText: true),
        "products" => BusinessReports.CreateProductClassification(Load, preserveArtText: true),
        _ => PrintDataStore.LoadJson(Encoding.UTF8.GetBytes(simple.Create(id).ToJson()), preserveArtText: true)
    };

    byte[] Load(string name)
    {
        // サーバー自身の資産だけを、資産フォルダーの中に限って読む。
        var asset = UntrustedImageSourcePolicy.ResolveTrustedAssetPath(Path.Combine(resources, "native"), name);
        if (File.Exists(asset)) return File.ReadAllBytes(asset);
        return name switch
        {
            "postal.data.json" => DatabaseJson("zip.xls", "郵便番号テーブル"),
            "estimate.data.json" => DatabaseJson("見積書.xls", "見積ヘッダ", "見積明細"),
            "invoice.data.json" => DatabaseJson("請求書.xls", "請求ヘッダ", "請求明細"),
            "products.data.json" => DatabaseJson("商品マスタ.xls", "M_大分類", "M_小分類", "M_商品"),
            _ => throw new FileNotFoundException("帳票資産がありません。", name)
        };
    }

    byte[] DatabaseJson(string source, params string[] tables)
    {
        var sheets = new JsonObject();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM [{table}] ORDER BY rowid";
            using var reader = command.ExecuteReader();
            var rows = new JsonArray();
            while (reader.Read())
            {
                var row = new JsonObject();
                for (var i = 0; i < reader.FieldCount; i++)
                    row[reader.GetName(i)] = Cell(table, reader.GetName(i), reader.IsDBNull(i) ? null : reader.GetValue(i));
                rows.Add(row);
            }
            sheets[table] = rows;
        }
        return Encoding.UTF8.GetBytes(new JsonObject { ["source"] = source, ["sheets"] = sheets }.ToJsonString());
    }
    static JsonNode? Cell(string table, string column, object? value) => table == "見積ヘッダ" && column == "見積日" && DateTime.TryParse(value?.ToString(), out var date)
        ? JsonValue.Create((date - new DateTime(1899, 12, 30)).TotalDays) : JsonValue.Create(value);
}
