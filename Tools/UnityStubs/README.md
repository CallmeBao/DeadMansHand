# UnityStubs — kiểm tra biên dịch / mô phỏng ngoài Unity

`UnityEngine.Stub.cs` là bản sao **chữ ký** (và một ít hành vi tối thiểu) của các API Unity mà dự án dùng:
`UnityEngine`, `UnityEngine.UI` (ugui 2.x), `UnityEngine.Events`, `UnityEditor.MenuItem/EditorApplication.Exit`.

Dùng ở hai nơi:

1. **`UnityCompileCheck.csproj`** — biên dịch toàn bộ `Assets/TheLastKing/Scripts` + `Net` + Editor check với C# 9 / netstandard2.1
   (cùng profile ngôn ngữ/API của Unity 6 với cài đặt dự án). Chạy: `scripts/check-unity-code.sh` hoặc `.bat`.
   Mục đích: bắt lỗi cú pháp/kiểu/API trước khi mở Unity.
2. **`Tests/UnityGlueSim`** — chạy nguyên văn controller + UI online trên stub có hành vi (cây GameObject, `Button.onClick`,
   coroutine theo thời gian ảo, phím) với máy chủ thật. Chạy: `scripts/run-unity-glue-sim.sh` hoặc `.bat`.

## Giới hạn — đọc trước khi tin kết quả

- Đây **không phải Unity**. Không có kết xuất, bố cục uGUI, âm thanh, vật lý 2D thật, IME, thread/TLS của Mono, hiệu năng.
- Chữ ký trong stub được viết theo hiểu biết về Unity 6; nếu Unity báo lỗi biên dịch dù bước này đạt, hãy sửa stub cho khớp rồi báo lại.
- Biên dịch/mô phỏng thành công **không thay thế** build và chạy thử trong Unity thật (xem `docs/ONLINE_ACCEPTANCE.md`).
- Nếu bạn thêm API Unity mới vào mã online, hãy bổ sung stub tương ứng.
