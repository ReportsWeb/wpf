$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run -c Release --project (Join-Path $here 'Pao.Reports.Wpf.QuickStart.csproj') -- @args
exit $LASTEXITCODE
