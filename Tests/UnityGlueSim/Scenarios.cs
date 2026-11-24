using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TheLastKing.Online;
using TheLastKing.Tests;
using UnityEngine;
using UnityEngine.UI;

namespace TheLastKing.Sim
{
    public static class Scenarios
    {
        public static void Register()
        {
            Registry.Add("glue.pve-regression.patched-controller-still-plays-pve", PveRegression);
            Registry.Add("glue.online.full-flow-connect-room-match-rematch-exit", HappyPath);
            Registry.Add("glue.online.link-loss-and-resume-mid-selection", LinkLoss);
            Registry.Add("glue.online.opponent-leaves-mid-match", OpponentLeaves);
            Registry.Add("glue.online.server-shutdown-mid-match", ServerShutdown);
            Registry.Add("glue.online.timeout-note-and-server-picked-card", TimeoutNote);
            Registry.Add("glue.online.esc-menu-does-not-pause-and-leave-works", EscMenu);
            Registry.Add("glue.online.rematch-button-waits-for-post-match-lobby", RematchRace);
            Registry.Add("glue.online.desync-fallback-path", DesyncFallback);
        }

        // ------------------------------------------------------------------ helpers

        static SimScene NewScene()
        {
            var s = new SimScene();
            s.Build();
            s.Start();
            return s;
        }

        static TestServer NewServer(Action<TheLastKing.Server.ServerOptions> more = null)
        {
            return new TestServer(o =>
            {
                o.SelectionMs = 10000; o.PresentationMaxMs = 20000; o.PresentationAckGraceMs = 20000;
                o.ReconnectGraceMs = 8000; o.MaxMessagesPerSecond = 5000; o.MaxConnectionsPerIp = 100;
                if (more != null) more(o);
            });
        }

        /// <summary>Chơi như người thật: chọn lá đầu rồi khóa; đặt xu khi tới lượt mình. Trả về số lượt đã lật.</summary>
        static int PlayToEnd(SimScene sim, int maxFrames, Func<bool> stop = null)
        {
            int revealed = 0;
            string prev = sim.Stage;
            for (int i = 0; i < maxFrames; i++)
            {
                if (sim.Game.EndPanel.activeSelf) return revealed;
                if (stop != null && stop()) return revealed;
                sim.Frame();
                string st = sim.Stage;
                if (st == "Revealing" && prev != "Revealing") revealed++;
                prev = st;
                if (st == "Choosing" && !sim.LockPending && sim.Selected < 0) { sim.Game.SelectCard(0); sim.Game.LockSelection(); }
                else if (st == "Depositing" && sim.Game.AutoPlaceButton.gameObject.activeSelf) sim.Click(sim.Game.AutoPlaceButton);
            }
            return revealed;
        }

        static void OpenAndConnect(SimScene sim, TestServer srv, string name = "Alice")
        {
            T.Check(sim.Click(sim.FindButton("Online Match")), "bấm CHƠI ONLINE trên Start Panel");
            T.Check(sim.IsActive("Online Menu Panel"), "menu online hiện ra");
            sim.SetInput("nameField", name);
            sim.SetInput("serverField", "127.0.0.1:" + srv.Port);
            T.Check(sim.Click("Connect"), "bấm KẾT NỐI");
            T.Check(sim.Until(() => sim.IsActive("Room Group"), 800), "kết nối xong => hiện nhóm tạo/vào phòng");
        }

        static string CreateRoom(SimScene sim)
        {
            T.Check(sim.Click("Create Room"), "bấm TẠO PHÒNG MỚI");
            T.Check(sim.Until(() => sim.IsActive("Online Lobby Panel"), 800), "vào phòng => hiện sảnh");
            string code = (sim.TextOf("Code") ?? "").Replace(" ", "");
            T.Check(OnlineProtocol.IsValidRoomCode(code), "mã phòng hiển thị hợp lệ: " + code);
            return code;
        }

        static Bot JoinBot(TestServer srv, string code, string name = "Bob", int seed = 5, bool rematch = true)
        {
            var tc = new TestClient(name).Connect(srv.Port);
            tc.WaitEvent(OnlineEventKind.Connected, 3000);
            tc.Do(() => tc.Client.JoinRoom(code));
            return new Bot(tc, seed) { AutoRematch = rematch, MaxMatches = 99 };
        }

        static void ReadyUp(SimScene sim, Bot bot)
        {
            T.Check(sim.Until(() => (sim.TextOf("Opponent") ?? "").Contains(bot.C.Name), 800), "sảnh hiện tên đối thủ");
            T.Check(sim.Click("Ready"), "bấm SẴN SÀNG");
            bot.C.Do(() => bot.C.Client.SetReady(true));
            T.Check(sim.Until(() => sim.Game.IsOnline, 800), "controller vào chế độ online");
        }

        /// <summary>Dựng tới lúc trận bắt đầu và lượt 1 đang ở trạng thái Choosing.</summary>
        static Bot ToFirstTurn(SimScene sim, TestServer srv, bool rematch = true, int seed = 5)
        {
            OpenAndConnect(sim, srv);
            string code = CreateRoom(sim);
            Bot bot = JoinBot(srv, code, "Bob", seed, rematch);
            ReadyUp(sim, bot);
            T.Check(sim.Until(() => sim.Stage == "Choosing", 800), "lượt 1: stage = Choosing");
            return bot;
        }

        static string EndTitleText(SimScene sim) { return sim.Game.EndTitle.text; }

        // ------------------------------------------------------------------ kịch bản

        static void PveRegression()
        {
            var sim = NewScene();
            T.Eq("Welcome", sim.Stage, "mở game ở trạng thái Welcome");
            T.Check(sim.Game.StartPanel.activeSelf, "Start Panel hiện");
            T.Check(sim.Obj("Online Match") != null, "nút CHƠI ONLINE đã được thêm vào Start Panel");
            T.Check(!sim.IsActive("Online Menu Panel") && !sim.IsActive("Online Lobby Panel"), "panel online ẩn khi mới mở");
            T.Check(sim.Game.PausePanel.transform.Find("Leave Match") != null && !sim.IsActive("Leave Match"), "nút RỜI TRẬN ẩn ở PvE");

            T.Check(sim.Click(sim.Game.StartButton), "bấm BƯỚC VÀO BÀN (PvE)");
            T.Check(sim.Until(() => sim.Stage == "Choosing", 200), "PvE: vào lượt chọn");
            T.Check(!sim.Game.StartPanel.activeSelf, "Start Panel ẩn");
            T.Check(!sim.Game.IsOnline, "không ở chế độ online");
            T.Check(sim.Game.OpponentCoinText.text.StartsWith("AI:"), "nhãn đối thủ là 'AI': " + sim.Game.OpponentCoinText.text);

            int turns = PlayToEnd(sim, 8000);
            T.Check(sim.Game.EndPanel.activeSelf, "PvE: ván kết thúc và hiện bảng kết quả (" + turns + " lượt)");
            T.Check(turns >= 4 && turns <= 12, "số lượt hợp lý: " + turns);
            T.Check(EndTitleText(sim) == "BẠN LÀ VUA CUỐI CÙNG" || EndTitleText(sim) == "NGÔI VUA ĐÃ ĐỔI CHỦ" || EndTitleText(sim) == "KHÔNG AI LÊN NGÔI", "tiêu đề kết quả hợp lệ: " + EndTitleText(sim));
            T.Check(sim.Game.OpponentCoinText.text.StartsWith("AI:"), "sau trận vẫn là nhãn AI");
            T.Check(sim.Game.RestartButton.interactable, "nút VÁN ĐẤU MỚI bấm được ở PvE");
            T.Check(sim.Physics.Started >= 1 && sim.Physics.Started == sim.Physics.Deposited, "mọi xu đã đặt xong (" + sim.Physics.Deposited + "/" + sim.Physics.Started + ")");

            // chơi lại một ván nữa (đường NewMatch của PvE)
            T.Check(sim.Click(sim.Game.RestartButton), "bấm VÁN ĐẤU MỚI");
            T.Check(sim.Until(() => sim.Stage == "Choosing" && !sim.Game.EndPanel.activeSelf, 200), "ván 2 bắt đầu");
            PlayToEnd(sim, 8000);
            T.Check(sim.Game.EndPanel.activeSelf, "PvE: ván 2 cũng kết thúc");

            // về màn hình chính qua nút mới rồi chọn lại được chế độ
            T.Check(sim.Click("Exit To Menu"), "bấm MÀN HÌNH CHÍNH");
            T.Eq("Welcome", sim.Stage, "về trạng thái Welcome");
            T.Check(sim.Game.StartPanel.activeSelf && !sim.Game.EndPanel.activeSelf, "Start Panel hiện lại, bảng kết quả ẩn");
            T.Check(sim.Click(sim.Game.StartButton) && sim.Until(() => sim.Stage == "Choosing", 200), "vẫn bắt đầu được PvE sau khi quay về màn hình chính");
        }

        static void HappyPath()
        {
            using (var srv = NewServer())
            {
                var sim = NewScene();
                Bot bot = ToFirstTurn(sim, srv);
                using (bot.C)
                {
                    int seat1 = sim.UiNet.Seat;
                    T.Check(sim.Game.IsOnline, "ở chế độ online");
                    T.Eq(seat1, sim.Me, "controller dùng đúng ghế luật của máy chủ");
                    T.Eq("BOB", sim.OpponentLabel, "nhãn đối thủ = tên đối thủ viết hoa");
                    T.Check(!sim.Game.StartPanel.activeSelf, "Start Panel ẩn trong trận");
                    T.Check(!sim.IsActive("Online Lobby Panel") && !sim.IsActive("Online Menu Panel"), "sảnh/menu online ẩn trong trận");
                    T.Check((sim.TextOf("Online Banner") ?? "").Contains("PHÒNG"), "banner trong trận hiện: " + sim.TextOf("Online Banner"));
                    T.Check(sim.Game.OpponentCoinText.text.StartsWith("BOB:"), "nhãn xu của đối thủ: " + sim.Game.OpponentCoinText.text);
                    T.Eq(8f, sim.Physics.PlacementTimeout, "thời gian đặt xu online được rút còn 8 s");
                    T.Check(sim.Game.PausePanel.transform.Find("Leave Match").gameObject.activeSelf, "nút RỜI TRẬN hiện ở menu Esc online");
                    T.Eq("MENU  ·  TRẬN KHÔNG DỪNG", sim.TextOf("Pause Title"), "tiêu đề menu Esc đổi cho online");
                    T.Check(sim.Game.CardButtons[0].interactable, "bài bấm được khi đang chọn");

                    // một lượt đầy đủ: chọn -> khóa -> trạng thái 'đang khóa' -> 'đã khóa' -> lật
                    sim.Game.SelectCard(0);
                    T.Eq(0, sim.Selected, "chọn lá đầu");
                    sim.Game.LockSelection();
                    T.Check(sim.LockPending, "bấm khóa => chờ máy chủ xác nhận (lockPending)");
                    T.Check(!sim.Game.LockButton.interactable, "nút khóa bị vô hiệu trong lúc chờ ACK");
                    T.Check(sim.Until(() => sim.Stage == "Locked" || sim.Stage == "Revealing", 400), "nhận LockConfirmed => Locked/Revealing");
                    T.Check(sim.Until(() => sim.Stage == "Revealing", 400), "cả hai khóa => lật bài");
                    T.Check(sim.Game.RevealPanel.activeSelf, "panel lật bài hiện");

                    int turns = 1 + PlayToEnd(sim, 8000);
                    T.Check(sim.Game.EndPanel.activeSelf, "trận 1 kết thúc, bảng kết quả hiện (" + turns + " lượt)");
                    MatchState final = sim.UiNet.Mirror.State;
                    string expectTitle = final.WinnerSeat == seat1 ? "BẠN LÀ VUA CUỐI CÙNG" : "NGÔI VUA ĐÃ ĐỔI CHỦ";
                    T.Eq(expectTitle, EndTitleText(sim), "tiêu đề kết quả đúng với người thắng của máy chủ");
                    T.Eq(MatchStateCodec.Hash(final), MatchStateCodec.Hash(bot.C.Get(() => bot.C.Client.Mirror.State)), "gương của giao diện và của bot giống nhau");
                    T.Check(!sim.UiNet.Mirror.State.IsDraw, "có người thắng");

                    // nút Đấu lại chỉ bấm được khi sảnh sau trận đã về
                    T.Check(sim.Until(() => sim.Game.RestartButton.interactable, 1200), "nút ĐẤU LẠI bấm được sau khi sảnh sau trận về");
                    string cap = sim.Game.RestartButton.GetComponentInChildren<Text>(true).text;
                    T.Check(cap.Contains("ĐẤU LẠI") || cap.Contains("ĐỒNG Ý"), "chữ trên nút đấu lại: " + cap);
                    T.Check((sim.TextOf("Online Series") ?? "").Contains("(1 ván)"), "chuỗi trận hiển thị 1 ván: " + sim.TextOf("Online Series"));

                    // đấu lại: ghế đổi
                    T.Check(sim.Click(sim.Game.RestartButton), "bấm ĐẤU LẠI");
                    T.Check(sim.Until(() => sim.Stage == "Choosing" && !sim.Game.EndPanel.activeSelf, 1200), "ván 2 bắt đầu");
                    T.Eq(1 - seat1, sim.Me, "ghế đã đổi sang " + (1 - seat1));
                    T.Eq(2, sim.UiNet.GameNo, "ván thứ 2 của chuỗi");
                    PlayToEnd(sim, 8000);
                    T.Check(sim.Game.EndPanel.activeSelf, "ván 2 kết thúc");
                    T.Check(sim.Until(() => (sim.TextOf("Online Series") ?? "").Contains("(2 ván)"), 1200), "chuỗi cập nhật 2 ván: " + sim.TextOf("Online Series"));

                    // rời phòng bằng nút trên bảng kết quả
                    T.Check(sim.Until(() => sim.Game.RestartButton.interactable || true, 1), "(đồng bộ)");
                    T.Check(sim.Click("Exit To Menu"), "bấm RỜI PHÒNG");
                    T.Check(!sim.Game.IsOnline, "thoát chế độ online");
                    T.Eq("Welcome", sim.Stage, "bàn về trạng thái chờ");
                    T.Check(sim.Game.StartPanel.activeSelf, "Start Panel hiện lại");
                    T.Check(sim.Game.OpponentCoinText.text.StartsWith("AI:"), "nhãn đối thủ về 'AI': " + sim.Game.OpponentCoinText.text);
                    T.Eq(20f, sim.Physics.PlacementTimeout, "thời gian đặt xu khôi phục về giá trị cũ");
                    T.Check(sim.IsActive("Online Menu Panel") && sim.IsActive("Room Group"), "menu online hiện lại ở nhóm phòng (vẫn đang kết nối)");
                    T.Eq("VÁN ĐẤU MỚI", sim.Game.RestartButton.GetComponentInChildren<Text>(true).text, "nhãn nút đấu lại khôi phục cho PvE");
                    T.Eq("TẠM DỪNG", sim.TextOf("Pause Title"), "tiêu đề menu tạm dừng khôi phục");
                    T.Check(!sim.IsActive("Leave Match"), "nút RỜI TRẬN ẩn lại");
                    T.Check(T.WaitUntil(() => { var l = bot.C.Get(() => bot.C.Client.Lobby); return l != null && !l.Slots[1 - l.MySlot].Present; }, 3000), "bot thấy người chơi đã rời phòng");

                    // thoát hẳn khỏi giao diện online
                    T.Check(sim.Click("Back"), "bấm QUAY LẠI");
                    T.Check(!sim.IsActive("Online Menu Panel") && sim.Game.StartPanel.activeSelf, "về Start Panel");
                    T.Check(sim.Ui != null && SimScene.Get<OnlineClient>(sim.Ui, "net") == null, "đã đóng kết nối");
                    T.Eq(0, srv.ErrorLines.Count, "server không ghi LỖI");
                }
            }
        }

        static void LinkLoss()
        {
            using (var srv = NewServer())
            {
                var sim = NewScene();
                Bot bot = ToFirstTurn(sim, srv);
                using (bot.C)
                {
                    sim.Game.SelectCard(1);
                    sim.KillUiSocket();
                    T.Check(sim.Until(() => sim.LinkLost, 600), "controller nhận LinkLost");
                    T.Check((sim.TextOf("Online Banner") ?? "").Contains("MẤT KẾT NỐI"), "banner báo mất kết nối: " + sim.TextOf("Online Banner"));
                    int selectedBefore = sim.Selected;
                    sim.Game.SelectCard(0);
                    T.Eq(selectedBefore, sim.Selected, "trong lúc mất kết nối không chọn thêm được bài");
                    sim.Game.LockSelection();
                    T.Check(!sim.LockPending, "trong lúc mất kết nối không khóa được");
                    T.Check(sim.Until(() => !sim.LinkLost && sim.Stage == "Choosing", 2500), "nối lại xong => Choosing lại");
                    T.Check(!(sim.TextOf("Online Banner") ?? "").Contains("MẤT KẾT NỐI"), "banner không còn báo mất kết nối");
                    T.Check(bot.C.Count(OnlineEventKind.OpponentConnection) >= 2, "bot được báo đối thủ mất kết nối rồi trở lại");
                    PlayToEnd(sim, 8000);
                    T.Check(sim.Game.EndPanel.activeSelf, "trận vẫn kết thúc bình thường sau khi nối lại");
                    T.Eq(MatchStateCodec.Hash(sim.UiNet.Mirror.State), MatchStateCodec.Hash(bot.C.Get(() => bot.C.Client.Mirror.State)), "gương cuối trận giống nhau");
                    T.Eq(1L, srv.Server.Resumes, "server ghi 1 lần nối lại");
                    T.Eq(0L, srv.Server.MatchesAborted, "không hủy trận");
                }
            }
        }

        static void OpponentLeaves()
        {
            using (var srv = NewServer())
            {
                var sim = NewScene();
                Bot bot = ToFirstTurn(sim, srv);
                using (bot.C)
                {
                    bot.C.Do(() => bot.C.Client.LeaveRoom());
                    T.Check(sim.Until(() => !sim.Game.IsOnline, 800), "đối thủ rời => controller thoát chế độ online");
                    T.Check(sim.Until(() => sim.IsActive("Online Lobby Panel"), 800), "UI hiện sảnh");
                    T.Check((sim.TextOf("Note") ?? "").Contains("hủy"), "ghi chú nêu trận bị hủy: " + sim.TextOf("Note"));
                    T.Check(!sim.Game.StartPanel.activeSelf, "Start Panel không bật lại (sảnh nằm trên bàn)");
                    T.Eq("Welcome", sim.Stage, "bàn về trạng thái chờ");
                    T.Eq(20f, sim.Physics.PlacementTimeout, "vật lý khôi phục");
                    T.Check((sim.TextOf("Opponent") ?? "").Contains("đang chờ"), "sảnh cho biết đang chờ người vào: " + sim.TextOf("Opponent"));
                    T.Check(sim.Click("Leave"), "bấm RỜI PHÒNG ở sảnh");
                    T.Check(sim.IsActive("Online Menu Panel") && sim.IsActive("Room Group"), "về menu nhóm phòng");
                    T.Check(sim.Click("Back"), "bấm QUAY LẠI");
                    T.Check(sim.Game.StartPanel.activeSelf, "Start Panel hiện lại (không bị kẹt màn hình trống)");
                }
            }
        }

        static void ServerShutdown()
        {
            var srv = NewServer();
            var sim = NewScene();
            Bot bot = ToFirstTurn(sim, srv);
            using (bot.C)
            {
                srv.Dispose();
                T.Check(sim.Until(() => !sim.Game.IsOnline, 1200), "máy chủ tắt => controller thoát chế độ online");
                T.Check(sim.Until(() => sim.IsActive("Online Menu Panel") && sim.IsActive("Connect Group"), 1200), "UI hiện menu ở nhóm kết nối");
                T.Check(!string.IsNullOrEmpty(sim.TextOf("Status")), "có thông báo lý do: " + sim.TextOf("Status"));
                T.Check(sim.Click("Back"), "bấm QUAY LẠI");
                T.Check(sim.Game.StartPanel.activeSelf && sim.Stage == "Welcome", "về Start Panel, bàn ở trạng thái chờ");
            }
        }

        static void TimeoutNote()
        {
            using (var srv = NewServer(o => { o.SelectionMs = 700; }))
            {
                var sim = NewScene();
                Bot bot = ToFirstTurn(sim, srv);
                using (bot.C)
                {
                    // không làm gì: máy chủ tự chốt lá
                    T.Check(sim.Until(() => sim.Stage == "Revealing", 1500), "hết giờ => máy chủ xử lý lượt, controller vào Revealing");
                    string reveal = sim.Game.RevealText.text;
                    T.Check(reveal.Contains("HẾT GIỜ"), "bảng lật bài nêu lý do hết giờ: " + reveal.Replace("\n", " | "));
                    T.Check(sim.Selected < 0 && !sim.LockPending, "không còn trạng thái chọn dở");
                    PlayToEnd(sim, 8000);
                    T.Check(sim.Game.EndPanel.activeSelf, "trận vẫn chạy tới kết thúc");
                }
            }
        }

        static void EscMenu()
        {
            using (var srv = NewServer())
            {
                var sim = NewScene();
                Bot bot = ToFirstTurn(sim, srv);
                using (bot.C)
                {
                    sim.PressKey(KeyCode.Escape);
                    sim.Frame();
                    T.Check(sim.MenuOpen && sim.Game.PausePanel.activeSelf, "Esc mở menu");
                    T.Eq(1f, Time.timeScale, "trận online KHÔNG bị dừng (timeScale vẫn 1)");
                    int before = sim.Selected;
                    sim.Game.SelectCard(0);
                    T.Eq(before, sim.Selected, "đang mở menu thì không chọn bài được");
                    sim.PressKey(KeyCode.Escape);
                    sim.Frame();
                    T.Check(!sim.MenuOpen && !sim.Game.PausePanel.activeSelf, "Esc lần hai đóng menu");
                    sim.Game.SelectCard(0);
                    T.Eq(0, sim.Selected, "đóng menu rồi chọn bài được");

                    sim.PressKey(KeyCode.Escape);
                    sim.Frame();
                    T.Check(sim.Click("Leave Match"), "bấm RỜI TRẬN");
                    T.Check(!sim.Game.IsOnline && sim.Game.StartPanel.activeSelf, "rời trận => về Start Panel");
                    T.Check(!sim.Game.PausePanel.activeSelf, "menu Esc đã đóng");
                    T.Check(bot.C.WaitEvent(OnlineEventKind.MatchAborted, 3000, e => e.Reason == AbortReason.OpponentLeft) != null, "bot được báo đối thủ rời trận (OpponentLeft)");
                }
            }
        }

        static void RematchRace()
        {
            using (var srv = NewServer(o => { o.PresentationAckGraceMs = 1500; o.PresentationMaxMs = 3000; }))
            {
                var sim = NewScene();
                OpenAndConnect(sim, srv);
                string code = CreateRoom(sim);
                Bot bot = JoinBot(srv, code, "Bob", 9, false);
                bot.AutoAck = false;                // bot không bao giờ báo "trình diễn xong": máy chủ phải chờ fallback
                using (bot.C)
                {
                    ReadyUp(sim, bot);
                    T.Check(sim.Until(() => sim.Stage == "Choosing", 800), "lượt 1");
                    PlayToEnd(sim, 12000);
                    T.Check(sim.Game.EndPanel.activeSelf, "bảng kết quả hiện");
                    var restart = sim.Game.RestartButton;
                    string cap = restart.GetComponentInChildren<Text>(true).text;
                    T.Check(!restart.interactable, "ngay khi hiện kết quả nút ĐẤU LẠI chưa bấm được (sảnh sau trận chưa về)");
                    T.Check(cap.Contains("CHỜ"), "nhãn nút báo đang chờ: " + cap);
                    T.Check(!sim.Click(restart), "bấm vào nút đang vô hiệu không có tác dụng");
                    T.Check(sim.Until(() => restart.interactable, 2500), "sau fallback của máy chủ, sảnh về => nút bấm được");
                    bot.C.Do(() => bot.C.Client.SetReady(true));
                    T.Check(sim.Until(() => restart.GetComponentInChildren<Text>(true).text.Contains("ĐỒNG Ý"), 600), "bot sẵn sàng => nút gợi ý ĐỒNG Ý");
                    T.Check(sim.Click(restart), "bấm đồng ý");
                    bot.AutoAck = true;
                    T.Check(sim.Until(() => sim.Stage == "Choosing" && !sim.Game.EndPanel.activeSelf, 1500), "ván 2 bắt đầu");
                }
            }
        }

        static void DesyncFallback()
        {
            using (var srv = NewServer())
            {
                var sim = NewScene();
                Bot bot = ToFirstTurn(sim, srv);
                using (bot.C)
                {
                    // làm hỏng "gương" của client (một trạng thái hợp lệ nhưng sai lượt) để ép nhánh lệch pha
                    var mirrorField = typeof(OnlineClient).GetField("mirror", BindingFlags.NonPublic | BindingFlags.Instance);
                    var wrong = new LastKingMatch();
                    wrong.ResolveTurn(0, 0);
                    mirrorField.SetValue(sim.UiNet, wrong);

                    sim.Game.SelectCard(0);
                    sim.Game.LockSelection();
                    T.Check(sim.Until(() => sim.TextOf("StatusText") != null && (sim.Game.StatusText.text ?? "").Contains("đồng bộ lại"), 1200), "lệch pha => controller báo đã đồng bộ lại: " + sim.Game.StatusText.text);
                    T.Check(sim.Until(() => sim.Stage == "Choosing", 1200), "máy chủ sang lượt 2 bình thường");
                    T.Eq(1, sim.UiNet.Mirror.State.TurnIndex, "gương đã được thay bằng trạng thái chuẩn của máy chủ (sau lượt 1)");
                    PlayToEnd(sim, 8000);
                    T.Check(sim.Game.EndPanel.activeSelf, "trận chạy tới kết thúc dù có lệch pha ở lượt 1");
                    T.Eq(MatchStateCodec.Hash(sim.UiNet.Mirror.State), MatchStateCodec.Hash(bot.C.Get(() => bot.C.Client.Mirror.State)), "cuối trận hai gương lại khớp nhau");
                }
            }
        }
    }
}
