# DeadMansHand — Unity 2D MVP

Project: `D:\Game Dev\RevolverMVP` · Unity `6000.6.0f1` · Built-in renderer.

## Mở và chơi

Mở project trong Unity Hub, rồi mở `Assets/TheLastKing/Scenes/LastKingTable.unity` hoặc menu **DeadMansHand > Open Table**. Nhấn Play, rồi Bắt đầu.

- Click một lá bài hoặc nhấn 1/2/3, sau đó **Khóa bài** / Enter / Space.
- AI khóa lựa chọn trước khi bạn chọn. Số lượng bài hai bên là thông tin công khai.
- Khi nhận Xu Máu, giữ chuột trái để bốc xu rồi thả vào đĩa **bên trái**. Đĩa bên phải thuộc AI.
- Nút đặt xu tự động hỗ trợ khi cần. Xu rơi ra ngoài được trả lại; không làm giảm hình phạt.
- Esc tạm dừng; Chơi lại bắt đầu trận mới.

## Luật đang dùng

Theo MASTER RULESET v1.1: 18 lá, ban đầu Bạn có Vua + 2 Dân; AI có Nô lệ + 2 Dân; xấp chung 12 Dân. Mỗi lượt đánh một lá từ tay tối đa ba lá, không chia lại tay sau ba lượt.

Vua thắng Dân; Dân thắng Nô lệ; Nô lệ thắng Vua. Người thua nhận một Xu Máu. Hai Dân hòa nhau gây Thuế Máu cho người còn giữ Vua. Năm xu xử thua; kiểm tra này xảy ra trước chuyển hóa. Ngoài ra người giữ Vua thua khi không còn Dân hoạt động, và người hết bài thua khi không thể rút.

Khối lượng xu chỉ ảnh hưởng chuyển động cân. Số xu theo luật quyết định thắng thua, không dựa vào góc nghiêng hay số collider nằm trên đĩa. Xu được tính một lần khi giải quyết lượt; thao tác thả xu là bước trình diễn và tương tác.

## Chỗ chỉnh trong Unity

| Phần | Nơi chỉnh |
|---|---|
| Bố cục, chữ, nút, vị trí lá bài | Canvas trong Hierarchy của LastKingTable |
| Phòng, bàn, nhân vật đối diện | SpriteRenderer trong nhánh World |
| Ảnh gốc | Assets/TheLastKing/Art |
| Cân, đĩa, giới hạn quay | Scale prefab: Rigidbody2D, HingeJoint2D, collider |
| Xu, kích thước collider | Coin prefab |
| Thời gian chọn/lật bài, âm thanh | LastKingGameController trong Inspector |
| Luật và AI | Scripts/LastKingRules.cs |
| Kéo/thả, khối lượng và trả xu rơi | Scripts/LastKingPhysics.cs |
| Nối UI với gameplay | Scripts/LastKingGameController.cs |

Scene và prefab đã được lưu dưới dạng asset. Sửa trực tiếp chúng rồi Ctrl+S. Không cần tạo lại scene mỗi lần chạy. Scene và code MVP cũ vẫn được giữ nguyên.

## Làm việc nhóm

Cả ba dùng cùng phiên bản Unity. Khi đưa vào Git, commit `Assets` (gồm tất cả `.meta`), `Packages`, `ProjectSettings`; bỏ qua `Library`, `Temp`, `Logs`, `obj`, `Build` và `QA/UnityValidation`. Không xóa/đổi `.meta` vì scene dùng GUID để tham chiếu asset. Phân công tránh sửa cùng scene một lúc: một người UI/scene, một người luật/AI, một người art/âm thanh/physics prefab. Đây là bản chơi offline với AI, chưa có multiplayer qua mạng.

## Kiểm tra và build

Menu **DeadMansHand > Run Checks** kiểm tra các nhánh luật và tính toàn vẹn scene. **DeadMansHand > Build Windows** tạo `Build/DeadMansHand/DeadMansHand.exe`. Kết quả kiểm tra và ảnh QA ở thư mục `QA/TheLastKing`.

Đổi tên hiển thị: chọn `HUD Canvas/Header/Title` và `HUD Canvas/Start Panel/Welcome Title`, sửa trường **Text** trong Inspector. Đổi tên ứng dụng tại **Edit > Project Settings > Player > Product Name**. Công cụ tạo/build scene dùng hằng `GameTitle` trong `Assets/TheLastKing/Editor/LastKingSetup.cs`.

Art mới là hình tạo bằng công cụ image generation, không phải asset mua từ Unity Asset Store. Xem `ASSET_PROVENANCE.md` để biết nguồn và nội dung yêu cầu tạo hình. Nhân vật có hai cổ tay tự do để có thể chơi bài.
