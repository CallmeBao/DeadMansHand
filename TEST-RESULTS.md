# Kiểm tra bản UI brass / pixel — 10/11/2026

- Unity 6000.6.0f1: biên dịch và xuất bản Windows x64 thành công.
- RuleChecks, AuthorityChecks và PhysicsChecks đạt, gồm 1.536 quỹ đạo xoay.
- Bản Windows mới đã chạy kiểm tra tự động đầy đủ: hòa sớm, hết giờ khóa, hình phạt AI, tạm dừng, nạp đạn bằng lò xo, xoay, bóp cò và kết thúc trận. Dấu xác nhận: `Build/Smoke/passed.txt`.
- Đã quan sát trực tiếp cửa sổ game bằng công cụ Windows ở cả màn hình chọn bài và màn hình phạt. Nền/bài đã nạp, chữ tiếng Việt hiển thị, các khung nằm trong cửa sổ, ổ đạn/kim hỏa đúng tâm, nút cò bật khi chốt dừng.
- Đã sửa lỗi lớp ổ đạn và nét ECG lệch vị trí khi thu nhỏ cửa sổ bằng phép xoay trong hệ tọa độ UI.
- Đã sửa lịch sử hòa sớm để hiện các lá tự lật, và bỏ viền chọn của lá kế tiếp sau khi khóa bài.
- Chụp ảnh của lượt kiểm tra chạy ẩn không dùng làm bằng chứng hình ảnh (Windows không cung cấp bề mặt chụp trong chế độ đó); việc kiểm tra hình ảnh dùng cửa sổ game thật.
- Chưa xác minh mọi tỷ lệ màn hình và thao tác bằng tay ở tất cả độ phân giải. Bố cục giữ tỷ lệ16:9 và vùng vật lý dùng cùng hệ quy đổi.
- Luật chơi, phạm vi PvE và module host giữ nguyên; chưa bổ sung LAN.

Nhật ký build và chạy thử nằm trong thư mục `QA`. Asset dùng đúng bản đã lưu trong `Assets/RevolverMVP/Resources/Art`, không tham chiếu OneDrive.

