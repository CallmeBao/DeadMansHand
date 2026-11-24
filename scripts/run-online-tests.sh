#!/usr/bin/env bash
# Chạy bộ kiểm thử Online PvP bằng dotnet (máy chủ thật + client thật qua socket loopback, ~1 phút).
# Thêm tên nhóm để lọc:  scripts/run-online-tests.sh tls   |   scripts/run-online-tests.sh resilience   |   list
cd "$(dirname "$0")/.."
dotnet run -c Release --project Tests/OnlineTests -- "$@"
