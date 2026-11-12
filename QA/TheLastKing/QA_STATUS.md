# Last King MVP — trạng thái bàn giao

Ngày 11/11/2026. Project chính: `D:\Game Dev\RevolverMVP`.

## Đã xác nhận

- Unity 6000.6.0f1 biên dịch thành công và build Windows x64 thành công.
- 35.331 assertion của luật, gồm 1.500 trận random có seed, đã qua trong Unity.
- Các nhánh chuyển hóa, Thuế Máu, ưu tiên xử thua ở xu thứ năm, lá rút cuối, Social Collapse, hết bài và bảo toàn 18 ID đều được kiểm tra riêng.
- Kiểm tra scene xác nhận đủ tham chiếu Canvas, nút, sprite, camera, hai đĩa cân và bốn prefab.
- Giao diện chào đã được chụp từ bản game thực tế: chữ tiếng Việt hiển thị đúng; nhân vật có cổ tay tự do.

## Phần bạn tự thử theo yêu cầu

Lần chạy vật lý đầu tiên phát hiện lỗi tại kiểm tra trả xu bị rơi. Bản hiện tại đã bổ sung đồng bộ vị trí Rigidbody2D/Transform và reset interpolation, sau đó build lại thành công. Chưa chạy lại kiểm thử vật lý sau bản sửa này vì bạn chọn tự chạy. Vì vậy không coi phần thu hồi xu hoặc toàn bộ trận là đã được xác nhận thực tế.

1. Mở `Assets/TheLastKing/Scenes/LastKingTable.unity`, nhấn Play rồi **Bước vào bàn**.
2. Chọn bài bằng chuột hoặc 1/2/3, nhấn **Khóa bài** hoặc Enter. Kiểm tra hai lá lật cùng lúc và thông tin công khai cập nhật.
3. Khi bạn nhận xu, kéo xu từ vị trí lấy xu sang đĩa bên trái rồi thả. Đĩa bên phải thuộc AI. Xu phải rơi và làm cân chuyển động.
4. Thử thả xu khỏi hai đĩa: cùng viên xu phải quay về vị trí lấy, số Xu Máu không bị mất hoặc cộng thêm. Thả nhầm đĩa không được hoàn tất hình phạt.
5. Có thể dùng **Tự động đặt xu**. Đạt 5 xu phải kết thúc trận; **Ván đấu mới** phải xóa xu và trả cân về trạng thái đầu.

Bản chạy riêng: `Build/TheLastKing/TheLastKing.exe`. Đây là bản mới, khác `Build/RevolverMVP.exe` cũ.

## Giới hạn

MVP hiện chơi offline với AI. Mô phỏng heuristic AI-vs-AI cho thấy Seat 1 có lợi thế lớn (128/150 trận); cần playtest cân bằng tiếp, không dùng số liệu này làm bằng chứng cân bằng tối ưu. Khối lượng xu ảnh hưởng cân, số xu theo luật mới quyết định thắng thua.

Nguồn ảnh và mô tả prompt nằm trong `ASSET_PROVENANCE.md`. Hướng dẫn chỉnh scene và chia việc nhóm nằm trong `README_LAST_KING.md`.
