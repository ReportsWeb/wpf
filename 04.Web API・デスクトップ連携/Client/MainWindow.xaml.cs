using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Windows;
using Microsoft.Win32;
using Pao.Reports.Maui.Engine.Data;
using Pao.Reports.Maui.Engine.Rendering;
using Pao.Reports.Wpf.Windows;

namespace ReportsWpf.ApiClient;

public partial class MainWindow : Window
{
    readonly HttpClient api = new() { BaseAddress = new Uri("http://localhost:19340"), Timeout = TimeSpan.FromSeconds(60) };
    (byte[] Bytes, PrintDataDocument Data)? received;
    string? receivedId;
    bool busy;
    public MainWindow() { InitializeComponent(); Loaded += LoadSamples; Samples.SelectionChanged += (_, _) => UpdateButtons(); }
    void UpdateButtons()
    {
        PreviewButton.IsEnabled = !busy && Samples.SelectedItem is not null;
        SaveDataButton.IsEnabled = SavePdfButton.IsEnabled = !busy && received.HasValue && receivedId == Samples.SelectedValue?.ToString();
        ReloadButton.IsEnabled = Samples.IsEnabled = !busy;
    }
    async void LoadSamples(object sender, RoutedEventArgs e)
    {
        busy = true; UpdateButtons();
        try { Samples.ItemsSource = await api.GetFromJsonAsync<Sample[]>("/api/samples") ?? []; Samples.SelectedIndex = 0; Status.Text = "帳票を選び、受信してください。"; }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; UpdateButtons(); }
    }
    async Task<(byte[] Bytes, PrintDataDocument Data)> Receive()
    {
        var id = Samples.SelectedValue?.ToString() ?? throw new InvalidOperationException("帳票を選択してください。");
        var bytes = await api.GetByteArrayAsync($"/api/reports/{id}/data");
        var data = PrintDataStore.LoadJson(bytes, preserveArtText: true);
        received = (bytes, data); receivedId = id;
        Status.Text = $"{((Sample)Samples.SelectedItem).Name}：{bytes.Length:N0} バイト受信済み（保存に通信不要）";
        return (bytes, data);
    }
    async void Preview_Click(object sender, RoutedEventArgs e)
    {
        busy = true; UpdateButtons();
        try { var x = await Receive(); new ReportPreviewWindow(x.Data, new ReportPreviewOptions { Title = "API 受信帳票" }) { Owner = this }.ShowDialog(); }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; UpdateButtons(); }
    }
    void SaveData_Click(object sender, RoutedEventArgs e)
    {
        try { if (received is not { } x) return; var d = new SaveFileDialog { Filter = "PREPEJ 印刷データ|*.prepej", FileName = receivedId + ".prepej" }; if (d.ShowDialog(this) == true) File.WriteAllBytes(d.FileName, x.Bytes); }
        catch (Exception ex) { Error(ex); }
    }
    async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        busy = true; UpdateButtons();
        try { if (received is not { } x) return; var d = new SaveFileDialog { Filter = "PDF|*.pdf", FileName = receivedId + ".pdf" }; if (d.ShowDialog(this) == true) { var bytes = await Task.Run(() => { var pages = PrintDataStore.Resolve(x.Data).Select(p => p.Document).ToArray(); return new SkiaPdfRenderer().Render(pages, new PdfRenderOptions()); }); await File.WriteAllBytesAsync(d.FileName, bytes); } }
        catch (Exception ex) { Error(ex); }
        finally { busy = false; UpdateButtons(); }
    }
    void Error(Exception ex) => MessageBox.Show(this,
        (ex is HttpRequestException or TaskCanceledException ? "APIサーバーの起動と接続先を確認してください。起動後は［一覧を再読込］または［受信してプレビュー］を押せます。" : "帳票の処理または保存を完了できませんでした。保存先と帳票データを確認してください。") + "\n\n" + ex.Message,
        "操作を完了できませんでした", MessageBoxButton.OK, MessageBoxImage.Error);
    sealed record Sample(string Id, string Name);
}
