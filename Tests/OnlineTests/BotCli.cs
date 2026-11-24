using System;
using System.Diagnostics;
using System.Threading;
using TheLastKing.Online;

namespace TheLastKing.Tests
{
    /// <summary>
    /// Đối thủ tự động chạy ngoài Unity: kết nối máy chủ thật bằng chính OnlineClient của game, tạo/vào phòng, bấm sẵn sàng,
    /// chơi bằng LastKingAI. Dùng để thử client Unity khi chỉ có MỘT máy/MỘT người chơi, hoặc để chạy bot đối kháng nhau.
    /// </summary>
    public static class BotCli
    {
        const string Usage = @"bot — đối thủ tự động cho THE LAST KING online

  bot (--create | --join MÃ_PHÒNG) [tùy chọn]

  --host ĐỊA_CHỈ     máy chủ (mặc định 127.0.0.1)         --port N        cổng (mặc định 7777)
  --tls              dùng TLS (tin chứng chỉ tự ký, in dấu vân tay)
  --name TÊN         tên hiển thị (mặc định Bot)           --matches N     số trận rồi thoát (mặc định 1)
  --think-ms N       độ trễ trước khi khóa bài (mặc định 900)    --ack-ms N   độ trễ báo trình diễn xong (mặc định 1500)
  --random           chọn bài ngẫu nhiên thay vì LastKingAI

Ví dụ: tạo phòng bằng bot rồi vào từ game Unity bằng mã in ra:   bot --create --name Bot --matches 3";

        static void Log(string s) { Console.WriteLine(DateTime.Now.ToString("HH:mm:ss") + "  " + s); }

        public static int Run(string[] args)
        {
            string host = "127.0.0.1", name = "Bot", join = null;
            int port = OnlineProtocol.DefaultPort, matches = 1, thinkMs = 900, ackMs = 1500;
            bool tls = false, create = false, random = false;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--host": host = args[++i]; break;
                        case "--port": port = int.Parse(args[++i]); break;
                        case "--tls": tls = true; break;
                        case "--name": name = args[++i]; break;
                        case "--create": create = true; break;
                        case "--join": join = args[++i]; break;
                        case "--matches": matches = int.Parse(args[++i]); break;
                        case "--think-ms": thinkMs = int.Parse(args[++i]); break;
                        case "--ack-ms": ackMs = int.Parse(args[++i]); break;
                        case "--random": random = true; break;
                        case "--help": case "-h": Console.WriteLine(Usage); return 0;
                        default: Console.Error.WriteLine("Tùy chọn không biết: " + args[i] + "\n" + Usage); return 2;
                    }
                }
            }
            catch (Exception) { Console.Error.WriteLine("Tham số sai.\n" + Usage); return 2; }
            if (create == (join != null)) { Console.Error.WriteLine("Chọn đúng một trong --create hoặc --join MÃ.\n" + Usage); return 2; }

            var client = new OnlineClient { DisplayName = name, Build = "bot-cli" };
            var rng = new Random();
            var clock = Stopwatch.StartNew();
            long lockAt = -1, ackAt = -1;
            int finished = 0;
            bool lastWasFinal = false, codePrinted = false;

            client.Connect(host, port, tls, (h, fp) => { Log("TLS: dấu vân tay chứng chỉ máy chủ " + fp); return true; });
            Log("Đang kết nối " + host + ":" + port + (tls ? " (TLS)" : "") + "…");
            while (true)
            {
                client.Update();
                OnlineEvent e;
                while (client.TryDequeue(out e))
                {
                    switch (e.Kind)
                    {
                        case OnlineEventKind.Connected:
                            Log("Đã kết nối. " + e.Text);
                            if (create) client.CreateRoom(); else client.JoinRoom(join);
                            break;
                        case OnlineEventKind.ConnectFailed: Log("KHÔNG kết nối được: " + e.Text); return 1;
                        case OnlineEventKind.Disconnected: Log("MẤT KẾT NỐI: " + e.Text); return 1;
                        case OnlineEventKind.ServerError:
                            Log("Máy chủ báo: " + e.Text);
                            if (e.Code == ErrorCode.RoomNotFound || e.Code == ErrorCode.RoomFull || e.Code == ErrorCode.BadRoomCode) return 1;
                            break;
                        case OnlineEventKind.LobbyChanged:
                        {
                            LobbyStateMsg l = client.Lobby;
                            if (!codePrinted) { codePrinted = true; Log("MÃ PHÒNG: " + l.RoomCode + "   (vào từ game bằng mã này)"); }
                            Log("Phòng " + l.RoomCode + ": " + (l.Slots[0].Present ? l.Slots[0].Name : "-") + " / " + (l.Slots[1].Present ? l.Slots[1].Name : "-") + "  chuỗi " + l.Wins[0] + "-" + l.Wins[1] + " (" + l.Games + " ván)");
                            if (l.Slots[0].Present && l.Slots[1].Present && !l.Slots[l.MySlot].Ready && finished < matches)
                            {
                                client.SetReady(true);
                                Log("Bot sẵn sàng.");
                            }
                            break;
                        }
                        case OnlineEventKind.MatchStarted:
                            Log("TRẬN BẮT ĐẦU: bot ngồi ghế " + client.Seat + (client.Seat == 0 ? " (mở đầu với VUA)" : " (mở đầu với NÔ LỆ)") + ", đối thủ " + client.OpponentName + ", ván " + client.GameNo);
                            break;
                        case OnlineEventKind.TurnBegan:
                            if (!e.Refresh) lockAt = clock.ElapsedMilliseconds + thinkMs;
                            break;
                        case OnlineEventKind.OpponentLocked: Log("  đối thủ đã khóa"); break;
                        case OnlineEventKind.TurnResolved:
                        {
                            lockAt = -1;
                            MatchState st = e.Snapshot;
                            Log("  lượt " + e.Turn + " xong: xu " + st.Players[0].BloodCoins + "-" + st.Players[1].BloodCoins + (e.TimeoutMask != 0 ? "  (có bên hết giờ)" : "") + (e.Desync ? "  (đã đồng bộ lại)" : ""));
                            ackAt = clock.ElapsedMilliseconds + ackMs;
                            if (st.IsFinished)
                            {
                                finished++; lastWasFinal = true;
                                Log("KẾT THÚC TRẬN: " + (st.IsDraw ? "hòa" : st.WinnerSeat == client.Seat ? "BOT THẮNG" : "BOT THUA") + " (" + st.EndReason + "). " + finished + "/" + matches);
                            }
                            break;
                        }
                        case OnlineEventKind.MatchAborted: Log("Trận bị hủy: " + e.Text); lockAt = -1; break;
                        case OnlineEventKind.OpponentConnection: Log(e.Flag ? "  đối thủ đã nối lại" : "  đối thủ mất kết nối (trận tạm dừng tối đa " + e.Number / 1000 + " s)"); break;
                        case OnlineEventKind.LinkLost: Log("Bot mất kết nối, đang thử nối lại…"); break;
                        case OnlineEventKind.LinkRestored: Log("Bot đã nối lại."); break;
                        case OnlineEventKind.Resynced:
                            Log("Bot đồng bộ lại trạng thái.");
                            if (e.Resync.Phase == MatchPhase.Selecting && !e.Resync.IAmLocked) lockAt = clock.ElapsedMilliseconds + thinkMs;
                            break;
                    }
                }
                long now = clock.ElapsedMilliseconds;
                if (lockAt >= 0 && now >= lockAt && client.State == OnlineState.InMatch && client.Mirror != null)
                {
                    lockAt = -1;
                    MatchState st = client.Mirror.State;
                    int seat = client.Seat;
                    int idx = random ? rng.Next(st.Players[seat].Hand.Count) : LastKingAI.ChooseCard(st, seat, rng);
                    int id = st.Players[seat].Hand[idx].Id;
                    client.Select(id); client.Lock(id);
                }
                if (ackAt >= 0 && now >= ackAt)
                {
                    ackAt = -1;
                    client.PresentationDone();
                    if (lastWasFinal && finished >= matches)
                    {
                        Thread.Sleep(600);
                        client.LeaveRoom();
                        Thread.Sleep(300);
                        client.Close();
                        Log("Xong " + matches + " trận. Thoát.");
                        return 0;
                    }
                    lastWasFinal = false;
                }
                Thread.Sleep(10);
            }
        }
    }
}
