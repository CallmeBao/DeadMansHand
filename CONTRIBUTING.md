# Làm việc nhóm

## Cài đặt lần đầu

1. Nhận lời mời collaborator trên GitHub.
2. Clone repository về một thư mục riêng, ưu tiên ngoài OneDrive, ví dụ `C:\Dev\RevolverMVP`.
3. Cài Unity **6000.6.0f1** qua Unity Hub, kèm Windows Build Support nếu cần build Windows.
4. Unity Hub → Add project from disk → chọn thư mục clone có `Assets`, `Packages`, `ProjectSettings`.
5. Mở `Assets/RevolverMVP/Scenes/SingleMatch.unity`, chờ import xong rồi Play.

## Mỗi tính năng dùng một nhánh

Chạy tại thư mục repository. Trước khi chuyển nhánh hoặc cập nhật, lưu scene, thoát Play và đóng Unity. Commit công việc đang làm trước khi chuyển sang việc khác.

```powershell
git switch main
git pull --ff-only
git switch -c feature/card-ui
# Mở Unity, sửa và kiểm tra, sau đó lưu và đóng Unity.
git status
git add Assets Packages ProjectSettings
# Nếu sửa tài liệu, add riêng các tệp tài liệu tương ứng.
git diff --cached --stat
git commit -m "Improve card selection UI"
git push -u origin feature/card-ui
```

Trên GitHub tạo Pull Request từ nhánh đó vào `main`. Một thành viên khác đọc thay đổi, xác nhận kết quả kiểm tra rồi merge. Mỗi thành viên dùng tên nhánh riêng, ví dụ `feature/audio`, `fix/bullet-drag`. Không force-push hoặc commit trực tiếp vào `main` trong quy trình nhóm.

Để cập nhật nhánh đang làm sau khi người khác merge (working tree phải sạch):

```powershell
git fetch origin
git merge origin/main
```

Nếu xung đột: phối hợp với người sửa cùng tệp, giải quyết từng chỗ, kiểm tra lại rồi `git add <tep-da-sua>` và `git commit`. Có thể dùng `git merge --abort` để hủy lần merge đang xung đột. Không chọn toàn bộ “ours/theirs” nếu chưa hiểu phần thay đổi bị loại bỏ.

## Quy tắc riêng cho Unity

- Commit cả asset và `.meta` đi cùng; giữ `Packages/packages-lock.json` trong Git.
- Di chuyển/đổi tên asset bằng cửa sổ Project trong Unity để giữ GUID và tham chiếu.
- Giữ Version Control = **Visible Meta Files**, Asset Serialization = **Force Text**.
- Thống nhất phiên bản Unity; nâng cấp Editor/package bằng PR riêng.
- Phân công một người sửa `SingleMatch.unity` tại một thời điểm. Tách đối tượng dùng chung thành prefab khi cần để giảm xung đột.
- Hiện giao diện chủ yếu nằm trong `Runtime/MatchController.cs`; tránh giao nhiều người sửa đồng thời tệp này. Có thể phân việc theo luật bài, vật lý, âm thanh và tài liệu.
- Không commit `Library`, `Temp`, `Logs`, `UserSettings`, `Build` hoặc bản ZIP. Chia sẻ bản chơi bằng GitHub Releases; đóng gói toàn bộ thư mục build.
- Khi bổ sung asset nhị phân lớn, thống nhất Git LFS trước khi commit; cấu hình hiện tại chưa dùng LFS.

## Trước khi gửi Pull Request

1. Chạy **Revolver MVP → Run Checks** ngoài Play mode; xác nhận Console không có lỗi.
2. Play scene và thử phần đã sửa. Với gameplay: chọn/khóa bài → nạp đạn → xoay → bóp cò; thử pause.
3. Nếu thay đổi ảnh hưởng build, chạy **Revolver MVP → Build Windows Player** và thử bản xuất.
4. Xem diff, bỏ thay đổi ngoài phạm vi và ghi rõ đã kiểm tra gì trong PR. Không dùng `TEST-RESULTS.md` cũ để khẳng định bản sửa mới đã pass.
