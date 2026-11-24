using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TheLastKing.Online;

namespace TheLastKing.Tests
{
    /// <summary>Máy chủ phải đứng vững trước client sai/độc hại: mọi thao tác đều bị kiểm tra phía server.</summary>
    public static class SecurityTests
    {
        public static void Register()
        {
            Registry.Add("security.handshake-required-before-anything", HandshakeRequired);
            Registry.Add("security.handshake-timeout-silent-connection", HandshakeTimeout);
            Registry.Add("security.version-and-rules-mismatch-rejected", VersionRules);
            Registry.Add("security.garbage-oversize-wrong-direction-disconnect", Garbage);
            Registry.Add("security.forged-actions-rejected-and-counted", Forged);
            Registry.Add("security.cannot-touch-foreign-match-or-room", Foreign);
            Registry.Add("security.presentation-ack-spoofing-ignored", AckSpoof);
            Registry.Add("security.stale-seq-ignored-and-late-lock-rejected", StaleSeqLate);
            Registry.Add("security.rate-limit-drops-flood-and-server-survives", RateLimit);
            Registry.Add("security.names-sanitized-for-opponent", NameSanitize);
            Registry.Add("security.hello-twice-and-bad-resume-token", HelloTwice);
            Registry.Add("security.per-ip-and-room-limits", Limits);
            Registry.Add("security.ready-spam-starts-exactly-one-match", ReadySpam);
        }

        static void HandshakeRequired()
        {
            using (var srv = new TestServer())
            using (var c = new RawClient(srv.Port))
            {
                c.Send(new CreateRoomMsg());
                var err = c.Wait<ErrorMsg>();
                T.Check(err != null && err.Code == ErrorCode.HandshakeRequired, "thao tác trước khi bắt tay bị từ chối (HandshakeRequired)");
                T.Check(c.WaitClosed(2000), "server đóng kết nối sau đó");
                T.Eq(0, srv.Server.RoomCount, "không phòng nào được tạo");
            }
        }

        static void HandshakeTimeout()
        {
            using (var srv = new TestServer(o => o.HandshakeTimeoutMs = 500))
            using (var c = new RawClient(srv.Port))
            {
                T.Check(c.WaitClosed(3000), "kết nối im lặng (không gửi Hello) bị đóng sau ~500 ms");
                T.Check(T.WaitUntil(() => srv.Server.ConnectionCount == 0, 2000), "server dọn kết nối đó");
            }
        }

        static void VersionRules()
        {
            using (var srv = new TestServer())
            {
                using (var c = new TestClient("V"))
                {
                    c.Client.ProtocolOverride = 99;
                    c.Connect(srv.Port);
                    var e = c.WaitEvent(OnlineEventKind.ConnectFailed, 3000);
                    T.Check(e != null && e.Text.Contains("phiên bản"), "khác phiên bản giao thức => ConnectFailed có thông báo riêng: " + (e != null ? e.Text : "null"));
                    T.Eq(OnlineState.Closed, c.State, "client dừng ở Closed (không treo)");
                }
                using (var c = new TestClient("R"))
                {
                    c.Client.RulesFingerprintOverride = RulesFingerprint.Value ^ 1u;
                    c.Connect(srv.Port);
                    var e = c.WaitEvent(OnlineEventKind.ConnectFailed, 3000);
                    T.Check(e != null && e.Text.Contains("bộ luật"), "khác phiên bản bộ luật => ConnectFailed có thông báo riêng: " + (e != null ? e.Text : "null"));
                }
                using (var r = new RawClient(srv.Port))
                {
                    r.Send(new HelloMsg { Protocol = 99, RulesFingerprint = RulesFingerprint.Value });
                    var err = r.Wait<ErrorMsg>();
                    T.Check(err != null && err.Code == ErrorCode.VersionMismatch, "mã lỗi VersionMismatch");
                    T.Check(r.WaitClosed(2000), "server đóng sau khi báo lỗi (đã xả thông điệp lỗi trước)");
                }
                using (var r = new RawClient(srv.Port))
                {
                    r.Send(new HelloMsg { RulesVersion = "LK-9.9", RulesFingerprint = RulesFingerprint.Value });
                    var err = r.Wait<ErrorMsg>();
                    T.Check(err != null && err.Code == ErrorCode.RulesMismatch, "mã lỗi RulesMismatch");
                }
                T.Eq(0L, srv.Server.MatchesStarted, "không có trận nào");
            }
        }

        static void Garbage()
        {
            using (var srv = new TestServer())
            {
                // thân khung có loại không biết
                using (var c = new RawClient(srv.Port))
                {
                    c.SendRaw(new byte[] { 5, 0, 0xEE, 1, 2, 3, 4 });
                    T.Check(c.WaitClosed(2000), "loại thông điệp không biết => ngắt");
                }
                // độ dài vượt trần
                using (var c = new RawClient(srv.Port))
                {
                    c.SendRaw(new byte[] { 0xFF, 0xFF, 1, 2, 3 });
                    T.Check(c.WaitClosed(2000), "độ dài khung 65535 => ngắt");
                }
                // độ dài 0
                using (var c = new RawClient(srv.Port))
                {
                    c.SendRaw(new byte[] { 0, 0 });
                    T.Check(c.WaitClosed(2000), "độ dài khung 0 => ngắt");
                }
                // sai hướng: client gửi thông điệp của server (sau khi đã bắt tay)
                using (var c = RawClient.Handshake(srv.Port, "X"))
                {
                    var forged = OnlineProtocol.Encode(new TurnResolvedMsg { MatchId = 1, Turn = 1, State = new LastKingMatch().State, StateHash = 1 });
                    c.SendRaw(forged);
                    T.Check(c.WaitClosed(2000), "client gửi thông điệp của server (TurnResolved) => ngắt");
                }
                // chuỗi byte ngẫu nhiên đủ dài để lấp đầy một khung hợp lệ về độ dài
                var rng = new Random(3);
                for (int i = 0; i < 20; i++)
                {
                    using (var c = RawClient.Handshake(srv.Port, "Y" + i))
                    {
                        var body = new byte[1 + rng.Next(1, 40)];
                        rng.NextBytes(body);
                        body[0] = (byte)rng.Next(1, 10);
                        var frame = new byte[2 + body.Length];
                        frame[0] = (byte)body.Length;
                        Buffer.BlockCopy(body, 0, frame, 2, body.Length);
                        c.SendRaw(frame);
                        Thread.Sleep(30);
                    }
                }
                // server vẫn sống và phục vụ bình thường
                using (var ok = new TestClient("Ok").Connect(srv.Port))
                    T.Check(ok.WaitEvent(OnlineEventKind.Connected, 3000) != null, "sau tất cả, client hợp lệ vẫn kết nối được");
                T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI nội bộ (chỉ ngắt kết nối xấu): " + string.Join(" | ", srv.ErrorLines.Take(2)));
            }
        }

        static void Forged()
        {
            using (var srv = new TestServer(o => { o.MaxViolations = 5; o.SelectionMs = 5000; o.PresentationMaxMs = 3000; }))
            using (var m = RawMatch.Start(srv))
            {
                int own = m.CardA(0), foreign = m.ForeignForA, ownB = m.CardB(0);
                uint id = m.SA.MatchId;

                m.A.Send(new LockMsg { MatchId = id, Turn = 1, CardId = foreign, Seq = 1 });
                var e1 = m.A.Wait<ErrorMsg>();
                T.Check(e1 != null && e1.Code == ErrorCode.InvalidAction, "khóa lá của ĐỐI THỦ => InvalidAction");

                m.A.Send(new LockMsg { MatchId = id, Turn = 2, CardId = own, Seq = 2 });
                var e2 = m.A.Wait<ErrorMsg>();
                T.Check(e2 != null && e2.Code == ErrorCode.TooLate, "khóa sai lượt => TooLate");

                m.A.Send(new LockMsg { MatchId = id + 1000, Turn = 1, CardId = own, Seq = 3 });
                var e3 = m.A.Wait<ErrorMsg>();
                T.Check(e3 != null && e3.Code == ErrorCode.InvalidState, "khóa sai mã trận => InvalidState");

                m.A.Send(new SelectMsg { MatchId = id, Turn = 1, CardId = 255, Seq = 4 });
                var e4 = m.A.Wait<ErrorMsg>();
                T.Check(e4 != null && e4.Code == ErrorCode.InvalidAction, "chọn lá không tồn tại => InvalidAction");

                // các nỗ lực giả mạo không làm thay đổi trạng thái: B khóa, A vẫn chưa khóa => chưa xử lý
                m.B.Send(new LockMsg { MatchId = m.SB.MatchId, Turn = 1, CardId = ownB, Seq = 1 });
                m.B.Wait<LockAckMsg>();
                Thread.Sleep(150);
                T.Eq(0, m.A.CountOf<TurnResolvedMsg>(), "giả mạo của A không khiến lượt bị xử lý sớm");
                m.A.Send(new LockMsg { MatchId = id, Turn = 1, CardId = own, Seq = 5 });
                var res = m.A.Wait<TurnResolvedMsg>();
                T.Check(res != null, "A khóa hợp lệ => lượt được xử lý");
                if (res == null) return;
                int expect0 = m.SA.Seat == 0 ? own : ownB, expect1 = m.SA.Seat == 0 ? ownB : own;
                T.Check(res.Id0 == expect0 && res.Id1 == expect1, "kết quả dùng đúng các lá hợp lệ đã khóa");

                // sang lượt 2 đúng quy trình (cả hai báo đã trình diễn xong), rồi mới spam thao tác sai
                m.A.Send(new PresentationDoneMsg { MatchId = id, Turn = 1 });
                m.B.Send(new PresentationDoneMsg { MatchId = m.SB.MatchId, Turn = 1 });
                T.Check(m.A.Wait<TurnBeginMsg>(2000) != null, "sang lượt 2");
                // vượt ngưỡng vi phạm (5) => bị ngắt
                for (int i = 0; i < 6; i++) m.A.Send(new SelectMsg { MatchId = id, Turn = 2, CardId = 255, Seq = (uint)(10 + i) });
                T.Check(m.A.WaitClosed(3000), "quá " + srv.Server.Options.MaxViolations + " thao tác sai => server ngắt kết nối");
                T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI nội bộ");
            }
        }

        static void Foreign()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; }))
            using (var m1 = RawMatch.Start(srv, "A1", "B1"))
            using (var m2 = RawMatch.Start(srv, "A2", "B2"))
            using (var lone = RawClient.Handshake(srv.Port, "Lone"))
            {
                // chưa ở phòng nào
                lone.Send(new LockMsg { MatchId = m1.SA.MatchId, Turn = 1, CardId = 1, Seq = 1 });
                var e = lone.Wait<ErrorMsg>();
                T.Check(e != null && e.Code == ErrorCode.NotInRoom, "không ở phòng nào mà khóa => NotInRoom");

                // đang ở một trận khác, dùng mã trận của người khác
                m2.A.Send(new LockMsg { MatchId = m1.SA.MatchId, Turn = 1, CardId = m2.CardA(0), Seq = 1 });
                e = m2.A.Wait<ErrorMsg>();
                T.Check(e != null && e.Code == ErrorCode.InvalidState, "dùng mã trận của phòng khác => InvalidState");
                m2.A.Send(new PresentationDoneMsg { MatchId = m1.SA.MatchId, Turn = 1 });   // bị bỏ qua lặng lẽ
                Thread.Sleep(100);

                // phòng riêng chưa có trận
                lone.Send(new CreateRoomMsg());
                lone.Wait<LobbyStateMsg>();
                lone.Send(new LockMsg { MatchId = m1.SA.MatchId, Turn = 1, CardId = 1, Seq = 2 });
                e = lone.Wait<ErrorMsg>();
                T.Check(e != null && e.Code == ErrorCode.InvalidState, "phòng riêng chưa có trận mà khóa => InvalidState");

                // trận của phòng 1 hoàn toàn không bị ảnh hưởng
                T.Eq(0, m1.A.CountOf<TurnResolvedMsg>() + m1.B.CountOf<TurnResolvedMsg>(), "trận 1 không bị tác động");
                T.Eq(0, m1.A.CountOf<ErrorMsg>() + m1.B.CountOf<ErrorMsg>(), "người trong trận 1 không nhận lỗi nào");
            }
        }

        static void AckSpoof()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 8000; o.PresentationAckGraceMs = 3000; }))
            using (var m = RawMatch.Start(srv))
            {
                uint ida = m.SA.MatchId, idb = m.SB.MatchId;
                // báo "xong" khi chưa có gì để trình diễn
                m.A.Send(new PresentationDoneMsg { MatchId = ida, Turn = 1 });
                Thread.Sleep(200);
                T.Eq(1, m.A.CountOf<TurnBeginMsg>() + m.B.CountOf<TurnBeginMsg>() - 1, "(đã có đúng 1 TurnBegin mỗi bên: A+B-1=1)");

                m.A.Send(new LockMsg { MatchId = ida, Turn = 1, CardId = m.CardA(0), Seq = 1 });
                m.B.Send(new LockMsg { MatchId = idb, Turn = 1, CardId = m.CardB(0), Seq = 1 });
                T.Check(m.A.Wait<TurnResolvedMsg>() != null && m.B.Wait<TurnResolvedMsg>() != null, "lượt 1 được xử lý");

                // A báo xong hai lần, B báo sai lượt: không được đi tiếp
                m.A.Send(new PresentationDoneMsg { MatchId = ida, Turn = 1 });
                m.A.Send(new PresentationDoneMsg { MatchId = ida, Turn = 1 });
                m.B.Send(new PresentationDoneMsg { MatchId = idb, Turn = 7 });
                m.B.Send(new PresentationDoneMsg { MatchId = idb + 5, Turn = 1 });
                int before = m.A.CountOf<TurnBeginMsg>();
                Thread.Sleep(900);
                T.Eq(before, m.A.CountOf<TurnBeginMsg>(), "báo xong trùng lặp / sai lượt / sai mã trận không làm lượt sau bắt đầu sớm");

                // B báo đúng => sang lượt 2 NGAY (không chờ hết hạn 3000 ms)
                var sw = System.Diagnostics.Stopwatch.StartNew();
                m.B.Send(new PresentationDoneMsg { MatchId = idb, Turn = 1 });
                var t2 = m.A.Wait<TurnBeginMsg>(1500);
                T.Check(t2 != null && t2.Turn == 2 && sw.ElapsedMilliseconds < 700, "ack đúng của B kích hoạt lượt 2 ngay (" + sw.ElapsedMilliseconds + " ms)");
            }
        }

        static void StaleSeqLate()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 400; o.PresentationMaxMs = 3000; }))
            using (var m = RawMatch.Start(srv))
            {
                int c1 = m.CardA(0), c2 = m.CardA(1);
                m.A.Send(new SelectMsg { MatchId = m.SA.MatchId, Turn = 1, CardId = c1, Seq = 10 });
                m.A.Send(new SelectMsg { MatchId = m.SA.MatchId, Turn = 1, CardId = c2, Seq = 9 });   // seq cũ hơn: phải bị bỏ qua
                var res = m.A.Wait<TurnResolvedMsg>(3000);
                T.Check(res != null, "hết giờ => máy chủ tự xử lý lượt");
                if (res == null) return;
                int played = m.SA.Seat == 0 ? res.Id0 : res.Id1;
                T.Eq(c1, played, "lựa chọn tạm có seq cũ hơn bị bỏ qua; dùng lá có seq mới nhất");
                T.Check((res.TimeoutMask & (1 << m.SA.Seat)) != 0, "A bị đánh dấu hết giờ");

                m.A.Send(new LockMsg { MatchId = m.SA.MatchId, Turn = 1, CardId = c1, Seq = 20 });
                var err = m.A.Wait<ErrorMsg>();
                T.Check(err != null && err.Code == ErrorCode.TooLate, "khóa sau khi lượt đã xử lý => TooLate");
            }
        }

        static void RateLimit()
        {
            using (var srv = new TestServer(o => { o.MaxMessagesPerSecond = 60; o.MaxViolations = 12; }))
            {
                using (var c = RawClient.Handshake(srv.Port, "Flood"))
                {
                    var burst = new List<byte>();
                    for (int i = 0; i < 400; i++) burst.AddRange(OnlineProtocol.Encode(new PingMsg { Token = (uint)i }));
                    c.SendRaw(burst.ToArray());
                    T.Check(c.WaitClosed(4000), "gửi dồn 400 thông điệp trong một khoảnh khắc => bị ngắt");
                    T.Check(c.CountOf<PongMsg>() <= 70, "chỉ khoảng " + srv.Server.Options.MaxMessagesPerSecond + " thông điệp được xử lý (Pong nhận: " + c.CountOf<PongMsg>() + ")");
                    T.Check(c.CountOf<ErrorMsg>() >= 1, "có báo lỗi RateLimited trước khi ngắt");
                }
                using (var ok = new TestClient("Ok").Connect(srv.Port))
                    T.Check(ok.WaitEvent(OnlineEventKind.Connected, 3000) != null, "server vẫn phục vụ bình thường sau đợt tràn");
            }
        }

        static void NameSanitize()
        {
            using (var srv = new TestServer())
            {
                using (var a = new RawClient(srv.Port))
                using (var b = RawClient.Handshake(srv.Port, "B"))
                {
                    a.Send(new HelloMsg { Name = "<color=red>Hacker</color>\u0001\u0002   xxxxxxxxxxxxxxxxxxxxx", RulesFingerprint = RulesFingerprint.Value });
                    a.Wait<WelcomeMsg>();
                    a.Send(new CreateRoomMsg());
                    var lobby = a.Wait<LobbyStateMsg>();
                    b.Send(new JoinRoomMsg { RoomCode = lobby.RoomCode });
                    var lb = b.Wait<LobbyStateMsg>();
                    string seen = lb.Slots[0].Name;
                    T.Check(seen.Length >= 1 && seen.Length <= OnlineProtocol.MaxNameChars, "tên hiển thị ngắn gọn: \"" + seen + "\"");
                    T.Check(seen.IndexOf('<') < 0 && seen.IndexOf('>') < 0 && !seen.Any(char.IsControl), "tên đã bỏ thẻ rich-text và ký tự điều khiển");
                }
                using (var e = new RawClient(srv.Port))
                using (var b = RawClient.Handshake(srv.Port, "B"))
                {
                    e.Send(new HelloMsg { Name = "", RulesFingerprint = RulesFingerprint.Value });
                    e.Wait<WelcomeMsg>();
                    e.Send(new CreateRoomMsg());
                    var lobby = e.Wait<LobbyStateMsg>();
                    T.Check(lobby.Slots[0].Name.StartsWith("Người chơi "), "tên rỗng => tên mặc định: " + lobby.Slots[0].Name);
                }
            }
        }

        static void HelloTwice()
        {
            using (var srv = new TestServer())
            {
                using (var c = RawClient.Handshake(srv.Port, "X"))
                {
                    c.Send(new HelloMsg { Name = "again", RulesFingerprint = RulesFingerprint.Value });
                    var err = c.Wait<ErrorMsg>();
                    T.Check(err != null && err.Code == ErrorCode.InvalidState, "Hello lần hai => InvalidState");
                    T.Check(!c.Closed, "nhưng chưa bị ngắt vì mới vi phạm một lần");
                }
                using (var c = RawClient.Handshake(srv.Port, "Y", 0xDEADBEEFCAFEUL))
                {
                    T.Check(c.Welcome != null && !c.Welcome.Resumed, "token nối lại bịa đặt => phiên mới (Resumed = false), không chiếm được phiên nào");
                }
            }
        }

        static void Limits()
        {
            using (var srv = new TestServer(o => { o.MaxConnectionsPerIp = 4; }))
            {
                var clients = new List<RawClient>();
                try
                {
                    for (int i = 0; i < 10; i++)
                    {
                        try { clients.Add(RawClient.Handshake(srv.Port, "C" + i)); }
                        catch (Exception) { }
                    }
                    int welcomed = clients.Count(c => c.Welcome != null);
                    T.Eq(4, welcomed, "chỉ 4 kết nối đầu từ cùng một IP được bắt tay");
                }
                finally { foreach (var c in clients) c.Dispose(); }
            }
            using (var srv = new TestServer(o => { o.MaxRooms = 3; }))
            {
                var cs = new List<RawClient>();
                try
                {
                    for (int i = 0; i < 4; i++) cs.Add(RawClient.Handshake(srv.Port, "R" + i));
                    for (int i = 0; i < 3; i++) { cs[i].Send(new CreateRoomMsg()); T.Check(cs[i].Wait<LobbyStateMsg>() != null, "phòng " + (i + 1) + " được tạo"); }
                    cs[3].Send(new CreateRoomMsg());
                    var err = cs[3].Wait<ErrorMsg>();
                    T.Check(err != null && err.Code == ErrorCode.TooManyRooms, "phòng thứ 4 vượt MaxRooms => TooManyRooms");
                }
                finally { foreach (var c in cs) c.Dispose(); }
            }
        }

        static void ReadySpam()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; }))
            using (var a = RawClient.Handshake(srv.Port, "A"))
            using (var b = RawClient.Handshake(srv.Port, "B"))
            {
                a.Send(new CreateRoomMsg());
                var lobby = a.Wait<LobbyStateMsg>();
                b.Send(new JoinRoomMsg { RoomCode = lobby.RoomCode });
                b.Wait<LobbyStateMsg>();
                for (int i = 0; i < 10; i++) { a.Send(new ReadyMsg { Ready = true }); b.Send(new ReadyMsg { Ready = true }); }
                T.Check(a.Wait<MatchStartMsg>() != null && b.Wait<MatchStartMsg>() != null, "trận bắt đầu");
                Thread.Sleep(300);
                T.Eq(1, a.CountOf<MatchStartMsg>(), "A chỉ nhận đúng 1 MatchStart dù spam Ready");
                T.Eq(1, b.CountOf<MatchStartMsg>(), "B chỉ nhận đúng 1 MatchStart dù spam Ready");
                T.Eq(1L, srv.Server.MatchesStarted, "server chỉ bắt đầu 1 trận");
                T.Eq(0L, srv.Server.MatchesAborted, "Ready trong lúc đang chơi không làm hỏng trận");
            }
        }
    }
}
