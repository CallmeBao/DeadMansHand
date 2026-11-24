#!/usr/bin/env bash
# Chạy máy chủ PvP của The Last King. Cần .NET 8 Runtime (chạy bản đã publish) hoặc .NET 8 SDK (chạy từ mã nguồn).
# Ví dụ:  scripts/run-server.sh --port 7777 --tls --match-log matches.csv
cd "$(dirname "$0")/.."
if [ -f ServerBin/LastKingServer.dll ]; then
  exec dotnet ServerBin/LastKingServer.dll "$@"
else
  exec dotnet run -c Release --project Server -- "$@"
fi
