using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Pao.Reports.Web;
/// <summary>Request-local builder for the shared PREPEJ document. Does not render or invoke native Reports.NET.</summary>
public sealed class PrintData
{
    JsonObject? definition, page;
    readonly JsonArray pages = new();
    public PrintData SetDefinition(JsonObject value)
    {
        if (page != null)
            throw new InvalidOperationException("Finish the page first.");
        Validate(value);
        if (definition != null)
            foreach (var p in pages)
                if (p!["Definition"] == null)
                    p["Definition"] = definition.DeepClone();
        definition = (JsonObject)value.DeepClone();
        return this;
    }
    public PrintData PageStart(JsonObject? value = null)
    {
        if (page != null)
            throw new InvalidOperationException("Finish the page first.");
        if (value != null)
            Validate(value);
        if (value == null && definition == null)
            throw new InvalidOperationException("Set a definition first.");
        page = new()
        {
            ["Index"] = pages.Count + 1,
            ["Definition"] = value?.DeepClone(),
            ["Values"] = new JsonArray(),
            ["DynamicAttributes"] = new JsonArray()
        };
        return this;
    }
    public PrintData SetValue(string name, object? value, int index = 0, bool drawing = true) => Add(name, "DynamicText", value, index, null, null, drawing);
    public PrintData SetRepeatedValue(string name, object? value, int x, int y, bool drawing = true) => Add(name, "DynamicText", value, 0, x, y, drawing);
    public PrintData SetTypedValue(string name, string type, object? value, int index = 0, bool drawing = true) => Add(name, type, value, index, null, null, drawing);
    public PrintData SetImage(string name, string source, int index = 0, bool drawing = true) => SetTypedValue(name, "DynamicImage", source, index, drawing);
    public PrintData SetBarcode(string name, string code, int index = 0, bool drawing = true) => SetTypedValue(name, "DynamicBarcode", code, index, drawing);
    public PrintData DrawObject(string name, int index = 0, string type = "DynamicText") => Add(name, type, null, index, null, null, true);
    public PrintData ChangeAttributes(string name, IDictionary<string, object> values, int index = 0) => ChangeRepeatedAttributes(name, values, index, null, null);
    public PrintData ChangeRepeatedAttributes(string name, IDictionary<string, object> values, int index = 0, int? x = null, int? y = null, string type = "DynamicText")
    {
        RequirePage();
        var item = Item(name, type, index, x, y);
        var attrs = new JsonObject();
        foreach (var (k, v) in values)
            attrs[k] = Scalar(v ?? throw new ArgumentException("Null attribute"));
        item["Values"] = attrs;
        page!["DynamicAttributes"]!.AsArray().Add(item);
        return this;
    }
    // 現在のページの指定行（0始まり）だけ、文字の属性を変更する。
    public PrintData ChangeRowAttributes(string name, IDictionary<string, object> values, int row)
    {
        RequirePage();
        if (row < 0 || name.Contains(":::")) throw new ArgumentException("Invalid row or object name.");
        var attrs = new JsonObject();
        foreach (var (key, value) in values) attrs[key] = Scalar(value);
        page!["DynamicAttributes"]!.AsArray().Add(new JsonObject {
            ["ElementName"] = "DynamicAttr", ["Name"] = name, ["Type"] = "Text", ["Index"] = row,
            ["Selector"] = $"PmDefText:::{name}:::{pages.Count + 1}:::{row + 1}", ["Values"] = attrs
        });
        return this;
    }
    public PrintData PageEnd()
    {
        RequirePage();
        pages.Add(page);
        page = null;
        return this;
    }
    public JsonObject ToJsonObject()
    {
        if (page != null || definition == null || pages.Count == 0)
            throw new InvalidOperationException("A complete document is required.");
        return new()
        {
            ["Format"] = "Reports.net PrintData",
            ["Version"] = "1.0",
            ["Definition"] = definition.DeepClone(),
            ["Pages"] = pages.DeepClone()
        };
    }
    public string ToJson(bool pretty = true) => ToJsonObject().ToJsonString(new JsonSerializerOptions
    {
        WriteIndented = pretty
    }) + "\n";
    public void Save(string path)
    {
        var target = Path.GetFullPath(path);
        var temp = Path.Combine(Path.GetDirectoryName(target)!, Guid.NewGuid() + ".tmp");
        try
        {
            File.WriteAllText(temp, ToJson());
            File.Move(temp, target, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
    PrintData Add(string name, string type, object? value, int index, int? x, int? y, bool drawing)
    {
        RequirePage();
        var item = Item(name, type, index, x, y);
        item["Drawing"] = drawing;
        item["Value"] = value == null ? null : Scalar(value);
        page!["Values"]!.AsArray().Add(item);
        return this;
    }
    static JsonObject Item(string name, string type, int index, int? x, int? y)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type) || index < 0 || x < 0 || y < 0 || x.HasValue != y.HasValue)
            throw new ArgumentException("Invalid object/repeat index.");
        var o = new JsonObject
        {
            ["Name"] = name.Trim(),
            ["Type"] = type.Trim(),
            ["Index"] = index
        };
        if (x.HasValue)
        {
            o["IndexX"] = x.Value;
            o["IndexY"] = y!.Value;
        }
        return o;
    }
    void RequirePage()
    {
        if (page == null)
            throw new InvalidOperationException("Call PageStart first.");
    }
    static string Scalar(object v) => v switch
    {
        string s => s,
        bool b => b ? "true" : "false",
        double d when double.IsFinite(d) => d.ToString("R", CultureInfo.InvariantCulture),
        float f when float.IsFinite(f) => f.ToString("R", CultureInfo.InvariantCulture),
        byte or sbyte or short or ushort or int or uint or long or ulong or decimal => Convert.ToString(v, CultureInfo.InvariantCulture)!,
        _ => throw new ArgumentException("Only finite scalar values are supported.")
    };
    static void Validate(JsonObject d)
    {
        var unit = d["CoordinateUnit"]?.ToString() ?? "mm";
        var portable = d["Version"] != null && d["Objects"] is JsonArray && (unit.Equals("mm", StringComparison.OrdinalIgnoreCase) || unit.Equals("px", StringComparison.OrdinalIgnoreCase));
        var web = d["format"]?.ToString() == "pao-reports-web" && d["version"]?.ToString() == "1" && d["pages"] is JsonArray a && a.Count > 0;
        if (!portable && !web)
            throw new ArgumentException("Invalid report definition.");
    }
}
