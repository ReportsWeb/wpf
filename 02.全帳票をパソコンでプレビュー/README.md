# 02. 全帳票をパソコンでプレビュー

## このサンプルで分かること

SQLiteの業務データをC#で読み、8種類の帳票をPREPEJへ組み立て、Reports WebのWPF印刷プレビューで表示します。プレビューのツールバーから検索、拡大、印刷、PDF保存を試せます。生成済み帳票の固定配信ではありません。

## 準備と開き方

Windows、.NET 10 SDK、Visual Studioの「.NETデスクトップ開発」が必要です。このリポジトリを `git clone` し、`Pao.Reports.Wpf.AllReports.csproj`をVisual Studioで開いてください。VS CodeやCursorなら、このREADMEがあるフォルダーを開きます。初回のNuGet復元時だけインターネット接続が必要です。DockerやDBサーバーは不要です。

## 起動と成功確認

Visual Studioで実行するか、このフォルダーで次を実行します。

```powershell
dotnet run -c Release --project .\Pao.Reports.Wpf.AllReports.csproj
```

「全帳票をパソコンでプレビュー」画面が開いたら帳票を選び、「選択した帳票を開く」を押します。別ウィンドウに帳票が表示されれば成功です。郵便番号、見積書、請求書、商品一覧は`../common/database/sample.db`を実行時に読みます。

## ソースを変更する

読みたい内容に合わせ、次のファイルを開いてください。

| 読みたい処理 | 開くファイル |
| --- | --- |
| 画面で選んだ帳票をプレビュアーへ渡す | `MainWindow.xaml.cs` |
| SQLiteからデータを読む、帳票を選ぶ | `NativeSampleCatalog.cs` |
| 請求書の明細・合計・色、見積書、郵便番号の改ページ、商品小計 | `BusinessReports.cs` |
| 短い帳票・名刺・図形などの見本 | `SampleCatalog.cs` |

`BusinessReports.cs`はサンプル自身のソースです。SDKを作り直さず、明細の値や組み立て方を変更できます。例えば`CreateInvoice`内の`Add(page, "txtCustomer", Text(header, "お客様名"))`の第3引数だけを`"サンプル商事"`へ変えて再実行すると、請求書の宛名が変わります。`御中`は定義側にあるので、文字列へ重ねて付ける必要はありません。

まず短い例から試す場合は、次の文字列を変更してください。

```csharp
.SetValue("Message", "Reports Web Quick Start — 同じ帳票定義から、プレビュー・PREPEJ・SVG・PDFを作成しました。")
```

文字列を`"初めてのC#帳票"`へ変えて保存し、プレビューを閉じて再実行してください。C#は再実行時にコンパイルされ、クイックスタートへ反映されます。帳票定義や共通DBを変更した場合も、保存後に再ビルドして実行します。

## 外部アプリからプレビューだけを表示する

`ReportPreviewWindow`のツールバー（メニュー・設定を含む）とステータスバーは、外部アプリからまとめて切り替えられます。既定値は従来どおり表示です。帳票ページ自体は隠れません。

```csharp
var preview = new ReportPreviewWindow(printData);
preview.SetPreviewChromeVisible(false); // プレビューだけ
preview.SetPreviewChromeVisible(true);  // 全操作UIを再表示
preview.Show();
```

個別制御が必要なら`SetChromeVisibility(toolbar: ..., statusBar: ..., pageFrame: ...)`を使用します。

## 停止

プレビューと一覧画面を閉じます。ターミナルから起動した場合は`Ctrl+C`でも終了できます。常駐サービスや削除作業はありません。
