# Đưa dự án lên GitHub

## Thư mục repository

Dùng chính `D:\Game Dev\DeadMansHand` làm gốc Git, không dùng thư mục OneDrive.
Không cần đổi tên/di chuyển thư mục `Assets/RevolverMVP` vì code Editor đang tham chiếu đường dẫn scene ở đó.

```text
DeadMansHand/
├── Assets/RevolverMVP/
│   ├── Runtime/         # Gameplay, UI, vật lý, hợp đồng host
│   ├── Editor/          # Công cụ tạo scene/build và checks
│   └── Scenes/          # SingleMatch.unity
├── Packages/            # manifest.json và packages-lock.json
├── ProjectSettings/     # Cấu hình Unity chung
├── docs/                # Hướng dẫn nhóm
├── .github/             # Mẫu Pull Request
├── .gitignore
├── .gitattributes
├── CONTRIBUTING.md
├── README.md
└── TEST-RESULTS.md       # Kết quả kiểm tra lịch sử
```

`Build`, `Library`, `Logs`, `UserSettings` có thể vẫn tồn tại trên máy nhưng được Git bỏ qua. Các tài liệu thiết kế ngoài repository chưa được nhập vì có nhiều phiên bản; nhóm chọn bản chuẩn rồi thêm vào `docs/design/` khi thống nhất.

## Người tạo repository thực hiện một lần

1. Đăng nhập GitHub → **New repository**, đặt tên `revolver-mvp`. Chọn **Private** nếu chỉ chia sẻ nội bộ.
2. Tạo repository trống: không chọn thêm README, `.gitignore` hay license vì đã có tệp local.
3. Mở PowerShell trong thư mục `D:\Game Dev\DeadMansHand`. Thay URL ví dụ bên dưới bằng URL repository vừa tạo.

```powershell
# Chỉ chạy nếu thư mục chưa được git init:
git init -b main
# Điền danh tính của chính bạn; có thể dùng email noreply từ GitHub Settings → Emails.
git config user.name "TEN_CUA_BAN"
git config user.email "EMAIL_CUA_BAN"
git status --short
git add .
git diff --cached --stat
git commit -m "Initial Unity project and team workflow"
git remote add origin https://github.com/TEN_TAI_KHOAN/revolver-mvp.git
git push -u origin main
```

Nếu Git yêu cầu đăng nhập, hoàn tất xác thực GitHub qua trình duyệt/Git Credential Manager. Không dùng mật khẩu tài khoản làm mật khẩu Git và không dán token vào URL hoặc tệp dự án.

4. Repository → **Settings → Collaborators → Add people**: mời từng thành viên bằng username GitHub. Thành viên phải chấp nhận lời mời.
5. Nhóm thống nhất: nhánh riêng → Pull Request → người khác review → merge. Nếu gói GitHub và loại repository hỗ trợ, tạo rule bảo vệ `main`, yêu cầu PR và một review, chặn force push/xóa nhánh. Nếu chưa có rule, áp dụng quy trình này bằng thỏa thuận nhóm.

## Thành viên tham gia

```powershell
# Chạy trong thư mục chứa các dự án local của bạn.
git clone https://github.com/TEN_TAI_KHOAN/revolver-mvp.git
cd revolver-mvp
```

Sau đó làm theo [CONTRIBUTING.md](../CONTRIBUTING.md). Mỗi người có bản clone riêng; GitHub dùng để trao đổi thay đổi qua commit/PR, không đồng bộ thao tác sửa trực tiếp như Google Docs.

## Tài liệu chính thức

- [Đẩy dự án local lên GitHub](https://docs.github.com/en/migrations/importing-source-code/using-the-command-line-to-import-source-code/adding-locally-hosted-code-to-github)
- [Mời collaborator](https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/repository-access-and-collaboration/inviting-collaborators-to-a-personal-repository)
- [Tổ chức dự án và quản lý meta của Unity](https://unity.com/how-to/organizing-your-project)
