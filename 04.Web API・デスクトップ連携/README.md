# 04. Web API・デスクトップ連携

## このサンプルで分かること

ServerがSQLiteから自己完結したPREPEJ印刷データを作り、ClientがHTTPで受信します。受信後のプレビュー、PREPEJ保存、PDF生成はクライアントPCのReports.web .NETエンジンが行います。サーバーからPDFをダウンロードするだけの例ではありません。

## 準備と開き方

Windows、.NET 10 SDK、Visual Studioの「.NETデスクトップ開発」と「ASP.NETとWeb開発」が必要です。このリポジトリを `git clone` してください。Visual Studioを2つ開き、最初に`Server/Pao.Reports.Wpf.Api.csproj`、次に`Client/Pao.Reports.Wpf.ApiClient.csproj`を開きます。VS CodeやCursorなら、このREADMEがある04フォルダーを開きます。Dockerと外部DBは不要です。

## 1. Serverを先に起動する

ServerをVisual Studioで実行するか、04フォルダーで次を実行します。

```powershell
dotnet run -c Release --project .\Server\Pao.Reports.Wpf.Api.csproj
```

`Now listening on: http://localhost:19340`を確認します。<http://localhost:19340/health> が`status: ok`を返せば準備完了です。

## 2. Clientを起動する

Serverを動かしたまま、別のターミナルで次を実行します。

```powershell
dotnet run -c Release --project .\Client\Pao.Reports.Wpf.ApiClient.csproj
```

帳票を選び「受信してプレビュー」を押します。Reports.web .NET印刷プレビューが開き、検索、拡大、印刷、PDF保存ができます。プレビューを閉じると「PREPEJ を保存」「ローカルで PDF 保存」も使えるようになります。どちらも直前に受信した帳票を使い、保存時には通信しません。別の帳票を選んだ場合は、まずその帳票を受信してください。

役割分担を試すには、請求書を受信した後でServerを停止し、ClientからPREPEJとPDFを保存してみてください。受信済みなので保存できます。一方、「受信してプレビュー」は毎回Serverの最新データを取得するため、停止中は接続エラーになります。Serverを再起動すればClientを開き直さず再受信できます。Clientを先に開いて一覧の取得に失敗した場合は、Server起動後に「一覧を再読込」を押します。

## ソースを変更する

Server側の役割は、`NativeSampleCatalog.cs`がSQLiteからの取得、`BusinessReports.cs`が請求書・見積書などへの値の設定と改ページ、`Program.cs`がHTTP受付です。Client側は`MainWindow.xaml.cs`の`Receive`でデータを受信し、保存ボタンでは保持したデータを使います。

短い変更を試すには`Server/SampleCatalog.cs`のクイックスタートにある

```csharp
.SetValue("Message", "Reports Web Quick Start — 同じ帳票定義から、プレビュー・PREPEJ・SVG・PDFを作成しました。")
```

の文字列を`"初めてのC#帳票"`へ変更して保存します。Serverを`Ctrl+C`で止めて再実行し、Clientでクイックスタートをもう一度受信してください。Server側C#は再コンパイル後にPREPEJへ反映されます。Client画面を変更した場合はClientを閉じて再実行します。共通DBを変更した場合もServerを再ビルドしてください。

## 外部アプリから操作UIを切り替える

受信後の`ReportPreviewWindow`で`SetPreviewChromeVisible(false)`を呼ぶと、ツールバー（メニュー・設定を含む）とステータスバーが隠れ、帳票ページだけを表示します。`true`で全操作UIを再表示できます。既定値は表示です。

## 停止

Clientウィンドウを閉じ、ServerをVisual Studioの停止ボタンまたは`Ctrl+C`で終了します。データベースはServerだけが読み、Clientへ配布しません。
