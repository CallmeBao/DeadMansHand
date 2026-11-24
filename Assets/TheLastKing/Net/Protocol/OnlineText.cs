// THE LAST KING — Online PvP: thông báo tiếng Việt cho lỗi / hủy trận. Dùng chung cho server, client và giao diện.
using System;

namespace TheLastKing.Online
{
    /// <summary>Thông báo tiếng Việt cho lỗi/hủy trận, dùng chung cho giao diện và log.</summary>
    public static class OnlineText
    {
        public static string Describe(ErrorCode code, string serverText)
        {
            switch (code)
            {
                case ErrorCode.VersionMismatch: return "Khác phiên bản giao thức: hãy cập nhật game hoặc máy chủ.";
                case ErrorCode.RulesMismatch: return "Khác phiên bản bộ luật giữa game và máy chủ.";
                case ErrorCode.ServerFull: return "Máy chủ đang đầy, thử lại sau.";
                case ErrorCode.TooManyRooms: return "Máy chủ đã hết chỗ tạo phòng, thử lại sau.";
                case ErrorCode.RoomNotFound: return "Không có phòng với mã này.";
                case ErrorCode.RoomFull: return "Phòng đã đủ hai người.";
                case ErrorCode.BadRoomCode: return "Mã phòng gồm 5 ký tự (chữ và số).";
                case ErrorCode.NotInRoom: return "Bạn chưa ở trong phòng nào.";
                case ErrorCode.AlreadyInRoom: return "Bạn đang ở trong một phòng.";
                case ErrorCode.RateLimited: return "Bạn gửi yêu cầu quá nhanh.";
                case ErrorCode.TooLate: return "Quá hạn chọn bài.";
                case ErrorCode.ShuttingDown: return "Máy chủ đang tắt để bảo trì.";
                case ErrorCode.RoomClosed: return "Phòng đã đóng vì không hoạt động.";
                default: return string.IsNullOrEmpty(serverText) ? "Máy chủ báo lỗi (" + code + ")." : serverText;
            }
        }

        public static string Describe(AbortReason reason)
        {
            switch (reason)
            {
                case AbortReason.OpponentLeft: return "Đối thủ đã rời trận. Trận bị hủy, không ai bị tính thua.";
                case AbortReason.OpponentDisconnected: return "Đối thủ mất kết nối quá lâu. Trận bị hủy, không ai bị tính thua.";
                case AbortReason.ServerShutdown: return "Máy chủ đang tắt. Trận bị hủy.";
                case AbortReason.ProtocolViolation: return "Trận bị hủy do lỗi giao thức.";
                default: return "Trận bị hủy.";
            }
        }
    }
}
