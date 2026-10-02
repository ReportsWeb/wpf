using System.IO;
using System.Text;
using System.Windows;
using Pao.Reports.Wpf.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using Pao.Reports.Maui.Engine.Reports;
using Pao.Reports.Maui.Engine.Services;

namespace Pao.Reports.Wpf.QuickStart;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var report = QuickStartReport.Create(Path.Combine(AppContext.BaseDirectory, "quick-start.prepdj"));
            if (e.Args.Length == 2 && e.Args[0] == "--verify")
            {
                Verify(report, Path.GetFullPath(e.Args[1]));
                Shutdown(0);
                return;
            }

            var window = new ReportPreviewWindow(report.Data, new ReportPreviewOptions
            {
                Title = "あっという間に帳票出力 — プレビューとPDF保存"
            });
            await AddDesignControls(window, report);
            window.Show();
        }
        catch (Exception exception)
        {
            if (e.Args.Length == 2 && e.Args[0] == "--verify")
            {
                var outputDirectory = Path.GetFullPath(e.Args[1]);
                Directory.CreateDirectory(outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "verification-error.txt"), exception.ToString(), new UTF8Encoding(false));
                Shutdown(1);
                return;
            }
            MessageBox.Show(exception.Message, "Quick Startを起動できません", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static async System.Threading.Tasks.Task AddDesignControls(ReportPreviewWindow window, IReport original)
    {
        // Locate the sample rather than assuming a particular bin/Debug depth.
        var sample = new DirectoryInfo(AppContext.BaseDirectory);
        while (sample != null && !File.Exists(Path.Combine(sample.FullName, "Pao.Reports.Wpf.QuickStart.csproj"))) sample = sample.Parent;
        if (sample == null) throw new DirectoryNotFoundException("サンプルのフォルダーが見つかりません。プロジェクト全体を展開して起動してください。");
        var work = Path.Combine(sample.FullName, "output", "design-session");
        Directory.CreateDirectory(work);
        var definition = Path.Combine(work, "quick-report.prepdj");
        if (!File.Exists(definition))
        {
            var doc = original.ResolvePages().Single().Document;
            await JsonDesignStore.SaveAsync(doc, definition, preserveArtText: true);
        }
        var content = (UIElement)window.Content;
        window.Content = null;
        var panel = new DockPanel();
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6) };
        var edit = new Button { Content="デザイナーでデザインを変更", Padding=new Thickness(8) };
        bar.Children.Add(edit);
        DockPanel.SetDock(bar, Dock.Top);panel.Children.Add(bar);panel.Children.Add(content);window.Content=panel;
        DateTime stamp = DateTime.MinValue;
        bool applying=false;
        void Refresh()
        {
            if(applying)return;applying=true;
            try
            {
                var next=ReportCreator.Create();next.LoadDefFile(definition);next.PageStart();next.PageEnd();
                var data=Path.Combine(work,"quick-report.prepej");next.SaveJson(data);window.LoadData(data);
                stamp=File.GetLastWriteTimeUtc(definition);
                window.Title="あっという間に帳票出力 — 保存したデザインをプレビュー・PDFへ反映";
            }
            catch(Exception ex){MessageBox.Show(window,ex.Message,"変更を反映できません",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{applying=false;}
        }
        // デザイナーは別アプリとして起動します。終了を待つのではなく、
        // この画面へ戻った時に定義ファイルの更新日時を確認し、
        // 保存されていれば読み直して帳票とプレビューを作り直します。
        // 未保存の変更は反映しません。デザイナーを閉じる必要はありません。
        window.Activated+=(_,_)=>{if(File.GetLastWriteTimeUtc(definition)!=stamp)Refresh();};
        edit.Click+=(_,_)=>
        {
            try
            {
                var exe=Path.GetFullPath(Path.Combine(sample.FullName,"..","..","Tools","Designer-net10","Pao.Reports.Wpf.Designer.exe"));
                if(!File.Exists(exe))throw new FileNotFoundException("同梱デザイナーが見つかりません。Toolsの起動案内を確認してください。",exe);
                Process.Start(new ProcessStartInfo(exe, "\""+definition+"\"") { UseShellExecute=true, WorkingDirectory=Path.GetDirectoryName(exe)! });
                window.Title="デザイナーで上書き保存し、この画面に戻ってください（元の定義は変更しません）";
            }
            catch(Exception ex){MessageBox.Show(window,ex.Message,"デザイナーを開けません",MessageBoxButton.OK,MessageBoxImage.Warning);}
        };
    }

    private static void Verify(Pao.Reports.Maui.Engine.Reports.IReport report, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        report.SaveJson(Path.Combine(outputDirectory, "quick-start.prepej"));
        report.SavePdf(Path.Combine(outputDirectory, "quick-start.pdf"));
        File.WriteAllText(Path.Combine(outputDirectory, "quick-start-page-1.svg"), report.GetSvgPages().Single(), new UTF8Encoding(false));
    }
}
