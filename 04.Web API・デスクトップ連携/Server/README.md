# Server

このフォルダーは04サンプルの帳票生成APIです。先に [親フォルダーのREADME](../README.md) の手順を確認してください。

Visual Studioでは `Pao.Reports.Wpf.Api.csproj` を開きます。ターミナルからは04フォルダーで次を実行します。

```powershell
dotnet run -c Release --project .\Server\Pao.Reports.Wpf.Api.csproj
```

`http://localhost:19340/health` が `{"status":"ok"}` を返したら、Serverを動かしたままClientを起動します。

## 帳票の内容を変えるには

`Program.cs`はHTTPの受付、`NativeSampleCatalog.cs`はSQLiteの読込、`BusinessReports.cs`は請求書の明細・合計や改ページを作る処理です。クライアントを変更しなくても、サーバー側の帳票処理を変更して再起動すれば、次の受信から新しい内容になります。

例えば`BusinessReports.cs`の`CreateInvoice`で、`Add(page, "txtCustomer", Text(header, "お客様名"))`の第3引数を`"サンプル商事"`へ変更します。Serverを停止・再実行してからClientで請求書をもう一度受信し、宛名を確認してください。SDKやデータベースを書き換える必要はありません。
