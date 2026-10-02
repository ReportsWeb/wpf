using Pao.Reports.Maui.Engine.Reports;

namespace Pao.Reports.Wpf.QuickStart;

internal static class QuickStartReport
{
    internal static IReport Create(string definitionPath)
    {
        var report = ReportCreator.Create();
        report.LoadDefFile(definitionPath);
        report.PageStart();
        report.Write("Title", "あっという間に帳票出力");
        report.Write("Customer", "株式会社パオ・アット・オフィス 御中");
        report.Write("Message", "Reports Web Quick Start — 同じ帳票定義から、プレビュー・PREPEJ・SVG・PDFを作成しました。");
        report.Write("Amount", "¥123,456");
        report.PageEnd();
        return report;
    }
}
