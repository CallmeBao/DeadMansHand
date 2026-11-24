#!/usr/bin/env bash
# Kiểm tra BIÊN DỊCH mã Unity (Scripts + Net + Editor check) bằng stub UnityEngine, trước khi mở Unity.
cd "$(dirname "$0")/.."
dotnet build Tools/UnityStubs/UnityCompileCheck.csproj -nologo -v q && echo "Biên dịch stub: OK (không thay thế build trong Unity thật)"
