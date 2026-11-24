#!/usr/bin/env bash
# Mô phỏng phần keo dán Unity (controller + giao diện online thật, stub UnityEngine, máy chủ thật, bot đối thủ).
# Đây KHÔNG phải Unity thật. Xem Tests/UnityGlueSim/UnityGlueSim.csproj để biết phạm vi.
cd "$(dirname "$0")/.."
dotnet run -c Release --project Tests/UnityGlueSim -- "$@"
