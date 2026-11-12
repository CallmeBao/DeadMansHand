# Dead Man’s Hand — giao diện brass / pixel

Dự án Unity nằm tại **D:\Game Dev\DeadMansHand**.

## Chạy game
Mở `Build\RevolverMVP.exe`. Giữ nguyên thư mục dữ liệu và DLL cạnh file EXE.

## Mở để sửa
Unity Hub → Add project from disk → chọn thư mục này → Unity **6000.6.0f1**.
Mở `Assets/RevolverMVP/Scenes/SingleMatch.unity` và Play.
Menu **Revolver MVP → Build Windows Player** xuất lại game.
Menu **Revolver MVP → Run Checks** kiểm tra luật, vật lý và module host.

## Giao diện theo hai ảnh mẫu
- Phòng tối tông vàng đất, đèn treo, hai người que và bàn gỗ.
- HUD ổ đạn riêng từng người, nhịp tim, sợ hãi, ván/lượt và đồng hồ.
- Lá bài minh họa Vua / Dân / Nô lệ, xếp hình quạt, viền sáng khi chọn, lịch sử bài đã lật.
- Màn hình phạt có khung đồng cũ, ổ 6 khe quay độc lập, kim hỏa cố định và khay đạn.
- Viên đạn dùng vật lý thật; bàn tay minh họa đi theo viên đạn, đường lò xo nối với con trỏ.
- Nút khóa và bóp cò có trạng thái bật/tắt theo pha. Bấm Esc để tạm dừng.

Bố cục dùng khung 1024×576, mặc định cửa sổ 1280×720; giữ tỷ lệ khi đổi kích thước.
Các chữ và số được vẽ từ trạng thái trận, không nằm trong ảnh nền.

## Chơi
Mỗi bên có 1 Vua, 3 Dân, 1 Nô lệ. Vua thắng Dân, Dân thắng Nô lệ, Nô lệ thắng Vua.
Chọn bài và khóa trong 15 giây. AI chọn trước khi bạn khóa. Sau 4 lượt hòa hoặc khi các lá còn lại giống nhau, chia lại.
Người thua nạp một viên vào khe trống, vuốt ở **vành ngoài ổ đạn**, chờ dừng và bóp cò.
Sống sót thì giữ đạn và tăng sợ hãi 15%. Khi có đạn ở 12 giờ, trận kết thúc.

Hết giờ chọn: dùng lá đang chọn hoặc lấy ngẫu nhiên.
Hết 30 giây nạp: tự nạp khe trống đầu tiên; hết 20 giây chờ vuốt: tự xoay; hết 10 giây chờ cò: tự bóp cò.
Viên thứ sáu làm tất cả khe đều có đạn.
Mô phỏng quay 120 Hz, hãm từ giây thứ 6, chốt chậm nhất ở giây thứ 10.

## Các file để chỉnh UI
- `Runtime/MatchView.cs`: bố cục, HUD, bài, màn hình phạt, vùng bấm và trạng thái nút.
- `Runtime/BrassSkin.cs`: khung kim loại, ổ đạn, viên đạn, bàn tay, vạch ECG và thanh run.
- `Resources/Art/room.png`: nền phòng.
- `Resources/Art/cards.png`: bộ minh họa ba loại bài.
- `Runtime/MatchController.cs`: luật chuyển pha và điều khiển.
- `Runtime/BulletDragPhysics.cs`: lực lò xo, độ run, trọng lực và va chạm bàn.
- `ArtReferences`: ảnh người dùng cung cấp và mô tả tạo asset.

Art nền/bài được tạo dựa trên tham chiếu; khung, ổ đạn và bàn tay được dựng bằng code. Các asset là bản dựng theo phong cách và bố cục ảnh, không phải tách từng pixel nguyên bản. Code UI hiện dùng Unity IMGUI để dự án chạy trực tiếp, chưa chuyển sang prefab Canvas.

## Kiểm tra
Bản Development hỗ trợ **F8** để chạy chuỗi xem thử hai màn hình và **F12** để chụp màn hình hiện tại vào `Build/Preview`.
Tham số `-mvpSmoke` chạy tự động luật chuyển pha và thao tác vật lý rồi thoát.
Xem `TEST-RESULTS.md` để biết kết quả kiểm tra bản giao diện này.

Hiện chơi một máy với AI. Module phán quyết host có sẵn để tích hợp; chưa có LAN/lobby.


## GitHub và làm việc nhóm

- [Thiết lập GitHub](docs/GITHUB-SETUP.md)
- [Quy trình đóng góp](CONTRIBUTING.md)
