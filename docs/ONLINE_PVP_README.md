# Hướng dẫn chạy & triển khai Online PvP — THE LAST KING (ver2)

Tài liệu này hướng dẫn cách cài đặt, chạy thử, kiểm thử và triển khai chế độ **đấu hai người qua mạng (Online PvP)** cho **The Last King (DeadMansHand ver2)**.

## 1. Yêu cầu môi trường

* **Unity Editor:** Phiên bản **`6000.6.0f1`** (cấu hình dự án: Built-in Render Pipeline, Legacy Input, API `.NET Standard 2.1`, Mono).
* **.NET 8:**
  * Cần **.NET 8.0 SDK** nếu bạn muốn chạy bot kiểm thử, chạy bộ test tự động hoặc biên dịch lại mã nguồn máy chủ.
  * Chỉ cần **.NET 8.0 Runtime** nếu bạn chỉ chạy bản máy chủ đã biên dịch sẵn trong `ServerBin/`.
* **Mạng:** Hỗ trợ **IPv4**, mặc định sử dụng cổng **TCP `7777`**.

## 2. Cấu trúc mã nguồn liên quan

* `Assets/TheLastKing/Net/Protocol/` — Giao thức nhị phân và bộ tính dấu vân tay luật (dùng chung cho cả Unity Client và Server).
* `Assets/TheLastKing/Net/Client/` — Tầng kết nối mạng TCP/TLS và đồng bộ trạng thái phía Client.
* `Assets/TheLastKing/Scripts/LastKingGameController.Online.cs` & `Assets/TheLastKing/Scripts/Online/` — Logic trận đấu online và giao diện uGUI tự khởi tạo lúc chạy (không sửa scene hay prefab gốc).
* `Assets/TheLastKing/Editor/LastKingNetChecks.cs` — Công cụ kiểm tra nhanh giao thức ngay trong Unity Editor.
* `Server/` & `ServerBin/` — Mã nguồn máy chủ độc lập (.NET 8) và bản thực thi đã publish sẵn.
* `scripts/` — Các script tự động hóa chạy server, bot, biên dịch kiểm tra và test.

## 3. Chạy thử nhanh trên máy cục bộ (Localhost + Bot)

### Bước 1: Kiểm tra giao thức trong Unity

1. Mở thư mục dự án trong **Unity `6000.6.0f1`**.
2. Trên thanh menu trên cùng, chọn **`DeadMansHand > Online > Run Protocol Checks`**.
3. Đảm bảo kết quả hiện **`PASS`** (mã dấu vân tay bộ luật hiện tại: `7f41e615`).

### Bước 2: Khởi động máy chủ cục bộ

Mở Terminal tại thư mục gốc dự án và chạy:

* **Windows:**
  ```bat
  scripts\run-server.bat
  ```
* **macOS / Linux:**
  ```bash
  ./scripts/run-server.sh
  ```

Giữ cửa sổ Terminal này mở trong suốt quá trình chơi (mặc định server lắng nghe tại `0.0.0.0:7777`).

### Bước 3: Kết nối từ Unity Client

1. Mở scene **`LastKingTable`** và nhấn **Play**.
2. Tại màn hình chính, nhấn nút **`CHƠI ONLINE (PvP)`**.
3. Nhập tên hiển thị, giữ nguyên địa chỉ mặc định **`127.0.0.1:7777`** (chưa cần bật TLS) và nhấn **Kết nối**.
4. Nhấn **Tạo phòng** và ghi lại **mã phòng gồm 5 ký tự** hiển thị trên màn hình.

### Bước 4: Đấu thử một mình với Bot

Mở thêm một cửa sổ Terminal khác và chạy lệnh gọi bot tham gia vào phòng vừa tạo (thay `<MÃ_PHÒNG>` bằng mã 5 ký tự):

* **Windows:**
  ```bat
  scripts\run-online-tests.bat bot --join <MÃ_PHÒNG> --matches 3
  ```
* **macOS / Linux:**
  ```bash
  ./scripts/run-online-tests.sh bot --join <MÃ_PHÒNG> --matches 3
  ```

Quay lại cửa sổ Game trên Unity và nhấn **SẴN SÀNG** để bắt đầu đấu.

## 4. Triển khai chơi qua LAN & Internet

Hai người chơi chỉ cần kết nối **ra** địa chỉ máy chủ, không cần mở cổng trên máy cá nhân.

### 4.1. Chơi qua mạng LAN (cùng Wi-Fi / mạng nội bộ)

1. Chạy `scripts\run-server.bat` (hoặc `.sh`) trên một máy làm chủ.
2. Mở cổng **TCP `7777`** (Inbound Rule) trong Windows Firewall / tường lửa của máy chủ.
3. Lấy địa chỉ IP nội bộ của máy chủ (chạy `ipconfig` trên Windows hoặc `ifconfig`/`ip a` trên Linux/macOS, ví dụ `192.168.1.15`).
4. Trên các máy người chơi, nhập **`192.168.1.15:7777`** vào ô địa chỉ máy chủ rồi nhấn **Kết nối**.

### 4.2. Chơi qua Internet

Chọn một trong ba cách sau để công khai cổng TCP của máy chủ:

* **Cách 1 — Chạy trên VPS (Khuyến nghị):** Đưa thư mục `ServerBin/` (hoặc build từ `Server/`) lên VPS có IP tĩnh, mở cổng TCP `7777` trên firewall của nhà cung cấp VPS và khởi chạy server kèm cờ `--tls`.
* **Cách 2 — Mở cổng Router (Port Forwarding):** Cấu hình NAT/Port Forwarding trên modem mạng nhà, chuyển tiếp cổng ngoài **TCP `7777`** về IP nội bộ của máy đang chạy server, sau đó dùng IP Public (`<IP-Public>:7777`) để kết nối.
* **Cách 3 — Mạng ảo / Đường hầm TCP:** Dùng phần mềm tạo mạng LAN ảo (ZeroTier, Tailscale) hoặc dịch vụ TCP Tunnel (như playit.gg, ngrok) nếu không thể cấu hình router.

### 4.3. Đặt sẵn địa chỉ máy chủ cho bản Build

Để người chơi không phải tự nhập lại IP mỗi lần mở game, tạo tệp văn bản thuần:

```text
Assets/StreamingAssets/lastking-server.txt
```

Ghi vào dòng đầu tiên địa chỉ mặc định của máy chủ (ví dụ: `203.0.113.10:7777`). Nếu không có tệp này, game sẽ dùng mặc định `127.0.0.1:7777` (và tự lưu địa chỉ người chơi nhập gần nhất vào `PlayerPrefs`).

## 5. Cấu hình tham số dòng lệnh cho Server

Bạn có thể truyền các tham số sau khi chạy trực tiếp `dotnet ServerBin/LastKingServer.dll [tham_số]` (hoặc sửa trong `scripts/run-server.bat|.sh`):

| Tham số | Mặc định | Ý nghĩa |
| :--- | :--- | :--- |
| `--port <số>` | `7777` | Cổng TCP máy chủ lắng nghe. |
| `--tls` | *Tắt* | Bật mã hóa TLS (tự sinh chứng chỉ `.pfx` nếu chưa truyền `--cert`; client tự ghim dấu vân tay chứng chỉ ở lần kết nối đầu). |
| `--cert <đường_dẫn.pfx>` | *Tự sinh* | Dùng tệp chứng chỉ PKCS#12 có sẵn khi bật `--tls`. |
| `--selection-seconds <giây>` | `25` | Thời gian tối đa chọn bài mỗi lượt. |
| `--reconnect-seconds <giây>` | `25` | Thời gian giữ chỗ tạm dừng trận khi một bên mất mạng. |
| `--max-rooms <số>` | `128` | Số phòng hoạt động tối đa cùng lúc. |
| `--max-connections <số>` | `256` | Tổng số kết nối TCP đồng thời tối đa. |
| `--max-per-ip <số>` | `32` | Số kết nối tối đa từ cùng một địa chỉ IP. |
| `--match-log <file.csv>` | *Tắt* | Ghi nhật ký kết quả từng ván (ghế khởi đầu, người thắng, số lượt, Xu Máu) ra tệp CSV. |
| `--quiet` | *Tắt* | Giảm bớt log in ra màn hình console. |

* **Dừng máy chủ an toàn:** Nhấn `Ctrl + C` (hoặc gửi `SIGTERM`). Máy chủ sẽ gửi thông báo đóng (`ServerShutdown`) tới các phòng đang chơi trước khi thoát.

## 6. Biên dịch lại Server & Chạy kiểm thử tự động

### 6.1. Biên dịch lại Server khi sửa luật hoặc giao thức

Máy chủ dùng chung trực tiếp `LastKingRules.cs` và `Assets/TheLastKing/Net/Protocol/`. Nếu bạn chỉnh sửa luật chơi hoặc thông điệp mạng, **bắt buộc phải build lại `ServerBin/`** để khớp dấu vân tay bộ luật với client:

* **Windows:** `scripts\publish-server.bat`
* **macOS / Linux:** `./scripts/publish-server.sh`

### 6.2. Bộ script kiểm thử tự động (không cần mở Unity)

* **Chạy toàn bộ kiểm tra (macOS / Linux / Git Bash):**
  ```bash
  ./scripts/run-all-checks.sh
  ```
* **Biên dịch kiểm tra mã nguồn Unity qua stub (C# 9 / netstandard2.1):**
  ```bat
  scripts\check-unity-code.bat       :: hoặc ./scripts/check-unity-code.sh
  ```
* **Chạy 64 bài test mạng, bảo mật, TLS và chịu tải:**
  ```bat
  scripts\run-online-tests.bat       :: hoặc ./scripts/run-online-tests.sh
  ```
* **Chạy 9 kịch bản mô phỏng Controller & UI Unity:**
  ```bat
  scripts\run-unity-glue-sim.bat     :: hoặc ./scripts/run-unity-glue-sim.sh
  ```

## 7. Cấu hình trong Unity & Lưu ý khi làm việc nhóm (Git)

* **Tắt hoàn toàn tính năng Online:** Thêm ký hiệu **`LASTKING_NO_ONLINE`** vào *Edit > Project Settings > Player > Scripting Define Symbols*.
* **Chế độ Smoke Test PvE:** Tham số dòng lệnh `-lastKingSmoke` tự động bỏ qua lớp Online để không ảnh hưởng quy trình kiểm tra tự động phần chơi với AI.
* **Quy tắc Commit Git:**
  * **Phải commit** toàn bộ các tệp `.meta` mới mà Unity tự sinh ra trong thư mục `Assets/`.
  * **Không commit** tệp chứng chỉ chứa khóa riêng (`*.pfx`), tệp nhật ký trận (`matches*.csv`), và các thư mục đầu ra biên dịch (`bin/`, `obj/`).
* **Danh mục kiểm tra nhanh trên Unity Editor / bản Build thật:**
  1. Gõ tên nhân vật bằng bộ gõ tiếng Việt có dấu trong ô nhập liệu uGUI.
  2. Kiểm tra cơ chế đặt Xu Máu vật lý lên cân (kéo thả thủ công và tự động đặt sau 8 giây).
  3. Kiểm tra tự động nối lại trận khi ngắt/bật lại mạng giữa ván.
  4. Thử kết nối có bật `--tls` trên bản build Windows (Mono) và chơi thử 2 máy qua LAN/Internet.