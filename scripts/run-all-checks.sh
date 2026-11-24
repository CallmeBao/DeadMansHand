#!/usr/bin/env bash
# Chạy toàn bộ kiểm tra tự động của tính năng Online PvP (không cần Unity). Dừng ở lỗi đầu tiên.
set -e
cd "$(dirname "$0")/.."
echo "== 1/3  Biên dịch mã Unity bằng stub =="; scripts/check-unity-code.sh
echo "== 2/3  Kiểm thử giao thức + máy chủ + client (dotnet) =="; scripts/run-online-tests.sh
echo "== 3/3  Mô phỏng keo dán Unity =="; scripts/run-unity-glue-sim.sh
echo "TẤT CẢ ĐẠT"
