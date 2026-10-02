using System.Windows;
using Pao.Reports.Wpf.Windows;
using ReportsWpf.Learning;
using ReportsWeb.Sample;

namespace ReportsWpf.AllReports;

public partial class MainWindow : Window
{
    readonly NativeSampleCatalog catalog;
    public MainWindow()
    {
        InitializeComponent();
        catalog = new NativeSampleCatalog(Path.Combine(AppContext.BaseDirectory, "resources"), Path.Combine(AppContext.BaseDirectory, "database", "sample.db"));
        Samples.ItemsSource = SampleCatalog.Samples;
        Samples.SelectedIndex = 0;
    }

    void Preview_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var id = Samples.SelectedValue?.ToString() ?? "quick-start";
            var data = catalog.Create(id);
            Status.Text = $"{SampleCatalog.Samples[id]} を生成しました。";
            new ReportPreviewWindow(data, new ReportPreviewOptions { Title = SampleCatalog.Samples[id] }) { Owner = this }.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "帳票生成エラー", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
