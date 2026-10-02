# 03. 全帳票をWebでプレビュー

## このサンプルで分かること

ASP.NET CoreがSQLiteから帳票データを作り、Reports.webの.NETネイティブエンジンでSVGとPDFを生成します。WebAssemblyは使いません。

## 準備と開き方

Windows、.NET 10 SDK、Visual Studioの「ASP.NETとWeb開発」が必要です。このリポジトリを `git clone` し、`Pao.Reports.Wpf.WebPreview.csproj`をVisual Studioで開きます。VS CodeやCursorでは、このREADMEがあるフォルダーを開きます。Dockerと外部DBは不要です。

## サーバーを起動してからURLを開く

Visual Studioで実行するか、このフォルダーで次を実行します。

```powershell
dotnet run -c Release --project .\Pao.Reports.Wpf.WebPreview.csproj
```

`Now listening on: http://localhost:19330`が表示されてから、ブラウザーで <http://localhost:19330/> を開きます。8帳票から選ぶと、まずプレビューだけを表示します。「ツールバーを表示」を押すと、ページ移動、拡大縮小、検索、個別ツール設定、PDF表示、PREPEJ保存と再読込を試せます。PDFは「PDF表示」を押した時に生成します。`/health`は稼働確認、`/api/samples`はページ数を含む帳票一覧です。

## プレビューだけをアプリに組み込む

帳票選択の横の「ツールバーを表示／非表示」は、プレビュアーの外側にあるサンプルアプリのボタンです。非表示にすると設定ボタン・ステータスバーまで消えますが、この外側のボタンから必ず再表示できます。

実装は `resources/previewer/index.html` の末尾です。次の外部APIを自分のボタンから呼び出しています。

```javascript
window.ReportsWebNative.hideAll(); // プレビューだけにする
window.ReportsWebNative.showAll(); // PDF・検索・設定などを再表示する
```

非表示でも `showPdfPreview()` や `search("請求")` などの外部APIは使えます。

## ソースを変更する

請求書・見積書・郵便番号・商品一覧の業務処理は`BusinessReports.cs`、SQLiteからの読込は`NativeSampleCatalog.cs`です。どちらもこのサンプル内のソースで、SDKを再ビルドせず変更できます。請求書なら`BusinessReports.CreateInvoice`の`Add(page, "txtCustomer", Text(header, "お客様名"))`の第3引数を`"サンプル商事"`へ変え、サーバーを再起動すると宛名に反映されます。

APIとブラウザー画面は`Program.cs`、ネイティブ帳票の入口は`NativeSampleCatalog.cs`です。`SampleCatalog.cs`のクイックスタートにある

```csharp
.SetValue("Message", "Reports Web Quick Start — 同じ帳票定義から、プレビュー・PREPEJ・SVG・PDFを作成しました。")
```

の文字列を`"初めてのC#帳票"`へ変更して保存します。サーバーを`Ctrl+C`で止め、上の`dotnet run`をもう一度実行してからブラウザーを再読み込みしてください。C#の変更は再コンパイル後に反映されます。`resources/definitions`の帳票定義や共通DBを変更した場合も再ビルドします。

## 停止

Visual Studioの停止ボタン、または起動したターミナルで`Ctrl+C`を押します。Dockerコンテナーや外部DBは作られません。
