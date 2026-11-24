@echo off
rem Chay may chu PvP cua The Last King. Can .NET 8 Runtime (ban da publish) hoac .NET 8 SDK (chay tu ma nguon).
rem Vi du:  scripts\run-server.bat --port 7777 --tls --match-log matches.csv
cd /d "%~dp0.."
if exist ServerBin\LastKingServer.dll (
  dotnet ServerBin\LastKingServer.dll %*
) else (
  dotnet run -c Release --project Server -- %*
)
