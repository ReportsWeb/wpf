# Client

このフォルダーは04サンプルのPREPEJ受信・ローカルプレビュー用Clientです。先に [親フォルダーのREADME](../README.md) に従ってServerを起動してください。

Visual Studioでは `Pao.Reports.Wpf.ApiClient.csproj` を開きます。ターミナルからは04フォルダーで次を実行します。

```powershell
dotnet run -c Release --project .\Client\Pao.Reports.Wpf.ApiClient.csproj
```

帳票を選んで「受信してプレビュー」を押すと、`http://localhost:19340` からPREPEJを受信してローカルプレビューを開きます。
