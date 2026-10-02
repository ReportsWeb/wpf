# .NET Frameworkでも「あっという間に帳票出力」

既存のWindowsアプリへ組み込む方のための入口です。このプロジェクトは **.NET Framework 4.7.2互換SDK** を参照し、ひとつ上の01サンプルと同じ帳票作成コード・デザインを使います。別のAPIを覚える必要はありません。

## Visual Studioで開いて試す

1. WindowsのVisual Studioで `QuickStart.Compat.csproj` を開きます。「.NETデスクトップ開発」と.NET Framework 4.7.2のターゲットパックが必要です。開発用のターゲットパックと、実行用ランタイムは別のものです。
2. このプロジェクトをスタートアッププロジェクトにして、F5で実行します。
3. プレビューが開きます。ツールバーからPDF保存や印刷を試せます。
4. ソリューションエクスプローラーの `QuickStartReport.cs` を開き、`report.Write` の文字列や金額を変更して、もう一度実行してください。

リンク表示されるソースの実体は、ひとつ上の01フォルダーにあります。変更は.NET 10版にも反映されます。サンプルをコピーするときは、このフォルダーだけでなく、01 全体を一緒に保持してください。

## どちらの対象フレームワークを選ぶか

| 組み込み先 | 使われる DLL（NuGet `Reports.Web.Wpf` の中） |
|---|---|
| .NET Framework 4.7.2以降 | net472 用（このプロジェクト） |
| .NET 5～9のWindowsアプリ | net472 用（互換版）。アプリの対象.NETに合わせてプロジェクトを設定 |
| .NET 10以降 | net10 用（ひとつ上の01プロジェクト） |

`ReportCreator.Create`、`LoadDefFile`、`Write`、`ReportPreviewWindow`などの公開APIは共通です。互換版はFramework 4.7.2と.NET 8／9で代表動作を確認しています。.NET 5～7の全環境を実行確認したという意味ではありません。

この例はWindows x64用です。プロジェクトには依存DLL、SkiaのネイティブDLL、Framework用binding redirectの設定を含めています。ご自身のプロジェクトへ移すときも、製品DLLだけでなくこれらの設定を移してください。

全体の操作は [01のREADME](../README.md)、パッケージの詳細は [`Reports.Web.Wpf`](https://www.nuget.org/packages/Reports.Web.Wpf) のページをご覧ください。
