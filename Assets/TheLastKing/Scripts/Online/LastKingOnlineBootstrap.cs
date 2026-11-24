// THE LAST KING — Online PvP: tự gắn lớp online vào scene The Last King khi game chạy.
//
// Không cần sửa scene/prefab đã lưu (README dặn không dựng lại scene): sau khi scene nạp xong, lớp này tìm
// LastKingGameController và thêm LastKingOnlineUi vào cùng GameObject. Scene khác (ví dụ RevolverMVP) không có
// controller nên không bị ảnh hưởng.
//
// Tắt hẳn tính năng: thêm LASTKING_NO_ONLINE vào Project Settings > Player > Scripting Define Symbols.
// Chế độ smoke test (-lastKingSmoke) bị bỏ qua để các ảnh/nhật ký QA của PvE giữ nguyên.
using System;
using UnityEngine;

namespace TheLastKing
{
    public static class LastKingOnlineBootstrap
    {
#if !LASTKING_NO_ONLINE
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            LastKingOnlineUi ui = null;
            try
            {
                if (Array.IndexOf(Environment.GetCommandLineArgs(), "-lastKingSmoke") >= 0) return;
                var game = UnityEngine.Object.FindFirstObjectByType<LastKingGameController>();
                if (game == null || game.StartPanel == null || game.EndPanel == null || game.PausePanel == null
                    || game.StatusText == null || game.RestartButton == null) return;
                if (game.GetComponent<LastKingOnlineUi>() != null) return;
                ui = game.gameObject.AddComponent<LastKingOnlineUi>();
                ui.Init(game);
            }
            catch (Exception ex)
            {
                // Cảnh báo (không phải lỗi) để không làm hỏng smoke test/QA nếu giao diện online gặp sự cố.
                Debug.LogWarning("[Online] Không khởi tạo được giao diện online: " + ex.Message + "\n" + ex.StackTrace);
                if (ui != null) UnityEngine.Object.Destroy(ui);
            }
        }
#endif
    }
}
