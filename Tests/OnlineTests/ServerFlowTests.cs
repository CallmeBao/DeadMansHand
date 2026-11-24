using System;
using System.Collections.Generic;
using System.Linq;
using TheLastKing.Online;
using TheLastKing.Server;

namespace TheLastKing.Tests
{
    public static class ServerFlowTests
    {
        public static void Register()
        {
            Registry.Add("flow.handshake.and-heartbeat", Handshake);
            Registry.Add("flow.rooms.lifecycle-and-unique-codes", RoomsLifecycle);
            Registry.Add("flow.rooms.error-codes", RoomErrors);
            Registry.Add("flow.match.full-ai-vs-ai", FullMatch);
            Registry.Add("flow.match.server-applied-exactly-the-picks", ReplayEqualsServer);
            Registry.Add("flow.rematch.seat-swap-and-series-score", RematchSwap);
            Registry.Add("flow.seat.first-match-random-balanced", SeatRandom);
            Registry.Add("flow.timeout.tentative-selection-is-used", TimeoutTentative);
            Registry.Add("flow.timeout.silent-player-gets-random-card", TimeoutSilent);
            Registry.Add("flow.timeout.both-silent-server-finishes-match", TimeoutBoth);
            Registry.Add("flow.presentation.advances-immediately-when-both-ack", PresentationFast);
            Registry.Add("flow.presentation.one-sided-stall-is-bounded", PresentationStall);
            Registry.Add("flow.presentation.nobody-acks-fallback", PresentationFallback);
            Registry.Add("flow.secrecy.lock-ack-and-opponent-locked", LockSecrecy);
        }

        // ------------------------------------------------------------------ helpers

        static void StartAndWait(TestClient a, TestClient b, Bot ba, Bot bb, int matches, int timeoutMs)
        {
            ba.MaxMatches = matches; bb.MaxMatches = matches;
            Pairs.ReadyBoth(a, b);
            T.Check(T.WaitUntil(() => ba.FinishedMatches >= matches && bb.FinishedMatches >= matches, timeoutMs),
                "cả hai bot hoàn tất " + matches + " trận trong " + timeoutMs + " ms (A=" + ba.FinishedMatches + ", B=" + bb.FinishedMatches + ")");
        }

        // ------------------------------------------------------------------ tests

        static void Handshake()
        {
            TestServer srv = new TestServer(o => o.HeartbeatTimeoutMs = 1500);
            using (srv)
            using (var c = new TestClient("Alice").Connect(srv.Port))
            {
                OnlineEvent ev = c.WaitEvent(OnlineEventKind.Connected, 3000);
                T.Check(ev != null, "nhận sự kiện Connected");
                if (ev == null) return;
                T.Check(ev.Text.Contains("LAST KING"), "lời chào của máy chủ: " + ev.Text);
                T.Eq(OnlineState.Online, c.State, "trạng thái Online");
                T.Check(c.Get(() => c.Client.Welcome.ResumeToken) != 0, "có token nối lại");
                T.Eq(srv.Server.Options.SelectionMs, c.Get(() => c.Client.Welcome.SelectionMs), "client biết thời gian chọn của máy chủ");
                // nhịp tim: im lặng 3,2 giây (hơn gấp đôi ngưỡng 1,5 giây của server) mà vẫn sống vì client tự gửi Ping mỗi giây
                System.Threading.Thread.Sleep(3200);
                T.Eq(OnlineState.Online, c.State, "kết nối nhàn rỗi 3,2 s vẫn sống nhờ Ping/Pong");
                T.Eq(1, srv.Server.ConnectionCount, "server đếm 1 kết nối");
            }
            T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
        }

        static void RoomsLifecycle()
        {
            using (var srv = new TestServer(o => { o.MaxConnectionsPerIp = 200; o.MaxConnections = 400; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client trong một phòng");
                using (a) using (b)
                {
                    T.Check(OnlineProtocol.IsValidRoomCode(code), "mã phòng hợp lệ: " + code);
                    T.Eq(0, a.Get(() => a.Client.Lobby.MySlot), "người tạo ở slot 0");
                    T.Eq(1, b.Get(() => b.Client.Lobby.MySlot), "người vào ở slot 1");
                    T.Eq("Alice", b.Get(() => b.Client.Lobby.Slots[0].Name), "B thấy tên A");
                    T.Eq("Bob", a.Get(() => a.Client.Lobby.Slots[1].Name), "A thấy tên B");
                    T.Eq(OnlineState.InRoom, a.State, "A ở trạng thái InRoom");

                    a.Do(() => a.Client.SetReady(true));
                    T.Check(T.WaitUntil(() => b.Get(() => b.Client.Lobby.Slots[0].Ready), 2000), "B thấy A sẵn sàng");
                    T.Check(!b.Get(() => b.Client.Lobby.Slots[1].Ready), "B chưa sẵn sàng");
                    T.Eq(0L, srv.Server.MatchesStarted, "một người sẵn sàng thì chưa bắt đầu trận");
                    a.Do(() => a.Client.SetReady(false));
                    T.Check(T.WaitUntil(() => !b.Get(() => b.Client.Lobby.Slots[0].Ready), 2000), "hủy sẵn sàng được");

                    // B rời: A thấy ô trống và cờ sẵn sàng được đặt lại
                    a.Do(() => a.Client.SetReady(true));
                    b.Do(() => b.Client.LeaveRoom());
                    T.Check(T.WaitUntil(() => !a.Get(() => a.Client.Lobby.Slots[1].Present), 2000), "A thấy ô của B trống");
                    T.Check(!a.Get(() => a.Client.Lobby.Slots[0].Ready), "cờ sẵn sàng của A bị đặt lại khi người kia rời");
                    T.Eq(OnlineState.Online, b.State, "B quay về trạng thái Online sau khi rời");

                    // B vào lại bằng mã viết thường có khoảng trắng
                    string lower = " " + code.ToLowerInvariant().Insert(2, " ") + " ";
                    b.Do(() => b.Client.JoinRoom(lower));
                    T.Check(T.WaitUntil(() => a.Get(() => a.Client.Lobby.Slots[1].Present), 2000), "mã gõ thường/khoảng trắng vẫn vào được phòng");

                    a.Do(() => a.Client.LeaveRoom());
                    b.Do(() => b.Client.LeaveRoom());
                    T.Check(T.WaitUntil(() => srv.Server.RoomCount == 0, 2000), "phòng bị xóa khi cả hai rời");
                }

                // 40 phòng đồng thời: mã không trùng
                var clients = new List<TestClient>();
                try
                {
                    var codes = new HashSet<string>();
                    for (int i = 0; i < 40; i++) clients.Add(new TestClient("P" + i).Connect(srv.Port));
                    bool allUp = T.WaitUntil(() => clients.All(c => c.State == OnlineState.Online), 8000);
                    T.Check(allUp, "40 client cùng kết nối được (" + clients.Count(c => c.State == OnlineState.Online) + "/40)");
                    if (!allUp) return;
                    foreach (var c in clients) { var cc = c; cc.Do(() => cc.Client.CreateRoom()); }
                    foreach (var c in clients)
                    {
                        var cc = c;
                        T.Check(T.WaitUntil(() => cc.Get(() => cc.Client.Lobby) != null, 3000), "client " + c.Name + " nhận mã phòng");
                        codes.Add(c.Get(() => c.Client.Lobby.RoomCode));
                    }
                    T.Eq(40, codes.Count, "40 phòng có 40 mã khác nhau");
                    T.Eq(40, srv.Server.RoomCount, "server đang giữ 40 phòng");
                }
                finally { foreach (var c in clients) c.Dispose(); }
                T.Check(T.WaitUntil(() => srv.Server.RoomCount == 0 && srv.Server.ConnectionCount == 0, 4000), "dọn sạch phòng và kết nối sau khi client đóng");
            }
        }

        static void RoomErrors()
        {
            using (var srv = new TestServer())
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                using (var c = new TestClient("Carol").Connect(srv.Port))
                {
                    c.WaitEvent(OnlineEventKind.Connected, 3000);

                    int m = c.Mark();
                    c.Do(() => c.Client.JoinRoom("AAAAA"));
                    var e = c.WaitEvent(OnlineEventKind.ServerError, 2000, null, m);
                    T.Check(e != null && e.Code == ErrorCode.RoomNotFound, "mã không tồn tại => RoomNotFound");

                    m = c.Mark();
                    c.Do(() => c.Client.JoinRoom("12"));
                    e = c.WaitEvent(OnlineEventKind.ServerError, 500, null, m);
                    T.Check(e != null && e.Code == ErrorCode.BadRoomCode, "mã sai khuôn dạng => BadRoomCode ngay ở client");

                    m = c.Mark();
                    c.Do(() => c.Client.JoinRoom(code));
                    e = c.WaitEvent(OnlineEventKind.ServerError, 2000, null, m);
                    T.Check(e != null && e.Code == ErrorCode.RoomFull, "phòng đủ hai người => RoomFull");
                    T.Eq(OnlineState.Online, c.State, "người bị từ chối vẫn ở trạng thái Online (không treo)");

                    m = a.Mark();
                    a.Do(() => a.Client.CreateRoom());
                    e = a.WaitEvent(OnlineEventKind.ServerError, 2000, null, m);
                    T.Check(e != null && e.Code == ErrorCode.AlreadyInRoom, "đang ở phòng mà tạo thêm => AlreadyInRoom");

                    m = a.Mark();
                    a.Do(() => a.Client.JoinRoom(code));
                    e = a.WaitEvent(OnlineEventKind.ServerError, 2000, null, m);
                    T.Check(e != null && e.Code == ErrorCode.AlreadyInRoom, "đang ở phòng mà vào phòng khác => AlreadyInRoom");

                    m = c.Mark();
                    c.Do(() => c.Client.SetReady(true));
                    e = c.WaitEvent(OnlineEventKind.ServerError, 2000, null, m);
                    T.Check(e != null && e.Code == ErrorCode.NotInRoom, "Ready khi chưa vào phòng => NotInRoom");
                }
            }
        }

        static void FullMatch()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 3000; o.PresentationMaxMs = 3000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 1); var bb = new Bot(b, 2);
                    StartAndWait(a, b, ba, bb, 1, 10000);
                    T.Check(T.WaitUntil(() => a.Get(() => a.Client.Lobby.Games) == 1 && b.Get(() => b.Client.Lobby.Games) == 1, 3000),
                        "sau trận cả hai nhận LobbyState với Games = 1");

                    MatchState sa = a.Get(() => a.Client.Mirror.State), sb = b.Get(() => b.Client.Mirror.State);
                    T.Check(sa.IsFinished && sb.IsFinished, "gương của cả hai đều ở trạng thái kết thúc");
                    T.Eq(MatchStateCodec.Hash(sa), MatchStateCodec.Hash(sb), "hai gương cho cùng một băm trạng thái");
                    T.Check(sa.WinnerSeat >= 0, "có người thắng (xu máu / sụp đổ / hết bài)");
                    int seatA = a.Get(() => a.Client.Seat), seatB = b.Get(() => b.Client.Seat);
                    T.Check(seatA != seatB && (seatA == 0 || seatA == 1) && (seatB == 0 || seatB == 1), "hai bên ngồi hai ghế khác nhau: " + seatA + "/" + seatB);
                    T.Check(ba.Resolved.All(r => !r.Desync) && bb.Resolved.All(r => !r.Desync), "không có lần lệch pha (desync) nào giữa gương và máy chủ");
                    T.Eq(sa.TurnIndex, ba.Resolved.Count, "số lượt client nhận = số lượt máy chủ đã xử lý");
                    T.Check(ba.Resolved.All(r => r.TimeoutMask == 0) && bb.Resolved.All(r => r.TimeoutMask == 0), "không lượt nào hết giờ");

                    var lobbyA = a.Get(() => a.Client.Lobby);
                    T.Eq(RoomPhase.Lobby, lobbyA.Phase, "phòng quay về sảnh sau trận");
                    T.Eq(1, lobbyA.Wins[0] + lobbyA.Wins[1], "bảng chuỗi ghi đúng 1 trận thắng");
                    T.Check(!lobbyA.Slots[0].Ready && !lobbyA.Slots[1].Ready, "cờ sẵn sàng được đặt lại sau trận");
                    int winnerSlot = sa.WinnerSeat == seatA ? 0 : 1;
                    T.Eq(1, lobbyA.Wins[winnerSlot], "trận thắng được ghi cho đúng slot của người thắng");

                    // thứ tự lượt trong chuỗi sự kiện TurnBegan là 1,2,3... không lặp
                    var turns = a.Snapshot().Where(e => e.Kind == OnlineEventKind.TurnBegan && !e.Refresh).Select(e => e.Turn).ToList();
                    T.Check(turns.SequenceEqual(Enumerable.Range(1, turns.Count)), "số lượt tăng dần 1.." + turns.Count);
                    T.Eq(1L, srv.Server.MatchesFinished, "server đếm 1 trận hoàn tất");
                    T.Eq(0L, srv.Server.MatchesAborted, "không trận nào bị hủy");
                    T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
                }
            }
        }

        static void ReplayEqualsServer()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 3000; o.PresentationMaxMs = 3000; }))
            {
                for (int round = 0; round < 3; round++)
                {
                    TestClient a, b; string code;
                    T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                    using (a) using (b)
                    {
                        var ba = new Bot(a, 100 + round) { Mode = round == 0 ? BotMode.Ai : BotMode.Random };
                        var bb = new Bot(b, 200 + round) { Mode = round == 2 ? BotMode.Ai : BotMode.Random };
                        StartAndWait(a, b, ba, bb, 1, 10000);
                        int seatA = a.Get(() => a.Client.Seat), seatB = b.Get(() => b.Client.Seat);
                        var picks = new Dictionary<int, Dictionary<int, int>>
                        {
                            { seatA, ba.Picks.ToDictionary(p => p.Key, p => p.Value) },
                            { seatB, bb.Picks.ToDictionary(p => p.Key, p => p.Value) }
                        };
                        var local = new LastKingMatch();
                        int turns = a.Get(() => a.Client.Mirror.State.TurnIndex);
                        for (int turn = 1; turn <= turns; turn++)
                        {
                            int i0 = MatchStateCodec.IndexOfCard(local.State.Players[0].Hand, picks[0][turn]);
                            int i1 = MatchStateCodec.IndexOfCard(local.State.Players[1].Hand, picks[1][turn]);
                            if (i0 < 0 || i1 < 0) { T.Fail("lá đã chọn không có trong tay ở lượt " + turn); break; }
                            local.ResolveTurn(i0, i1);
                        }
                        T.Eq(MatchStateCodec.Hash(local.State), MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)),
                            "ván " + round + ": phát lại đúng các lá đã khóa bằng bộ luật cục bộ cho cùng trạng thái cuối với máy chủ");
                    }
                }
            }
        }

        static void RematchSwap()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 3000; o.PresentationMaxMs = 3000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 31) { AutoRematch = true }; var bb = new Bot(b, 32) { AutoRematch = true };
                    StartAndWait(a, b, ba, bb, 3, 20000);
                    T.Eq(3, ba.Seats.Count, "A đã ngồi 3 trận");
                    if (ba.Seats.Count < 3 || bb.Seats.Count < 3) return;
                    T.Check(ba.Seats[1] == 1 - ba.Seats[0] && ba.Seats[2] == ba.Seats[0], "ghế của A đổi luân phiên: " + string.Join(",", ba.Seats));
                    T.Check(bb.Seats[1] == 1 - bb.Seats[0] && bb.Seats[2] == bb.Seats[0], "ghế của B đổi luân phiên: " + string.Join(",", bb.Seats));
                    for (int i = 0; i < 3; i++) T.Check(ba.Seats[i] != bb.Seats[i], "trận " + (i + 1) + ": hai bên luôn ngồi hai ghế khác nhau");
                    T.Check(T.WaitUntil(() => a.Get(() => a.Client.Lobby.Games) == 3, 3000), "bảng chuỗi: Games = 3");
                    var lobby = a.Get(() => a.Client.Lobby);
                    T.Eq(3, lobby.Wins[0] + lobby.Wins[1], "tổng trận thắng = 3 (bộ luật này không có hòa)");
                    T.Eq(3, a.Get(() => a.Client.GameNo), "client biết đây là ván thứ 3 của chuỗi");
                    T.Eq(3, a.Count(OnlineEventKind.MatchStarted), "đã có 3 sự kiện MatchStarted");
                }
            }
        }

        static void SeatRandom()
        {
            using (var srv = new TestServer(o => o.Seed = 99))
            {
                int creatorSeat0 = 0, rounds = 100;
                for (int i = 0; i < rounds; i++)
                {
                    using (var a = RawClient.Handshake(srv.Port, "A"))
                    using (var b = RawClient.Handshake(srv.Port, "B"))
                    {
                        a.Send(new CreateRoomMsg());
                        var lobby = a.Wait<LobbyStateMsg>();
                        b.Send(new JoinRoomMsg { RoomCode = lobby.RoomCode });
                        b.Wait<LobbyStateMsg>();
                        a.Send(new ReadyMsg { Ready = true }); b.Send(new ReadyMsg { Ready = true });
                        var ms = a.Wait<MatchStartMsg>();
                        var mb = b.Wait<MatchStartMsg>();
                        if (ms == null || mb == null) { T.Fail("không nhận MatchStart ở vòng " + i); return; }
                        T.Check(ms.Seat != mb.Seat, "hai ghế khác nhau");
                        if (ms.Seat == 0) creatorSeat0++;
                    }
                }
                T.Check(creatorSeat0 >= 35 && creatorSeat0 <= 65, "ghế đầu tiên của người tạo phòng được bốc ngẫu nhiên đều: " + creatorSeat0 + "/" + rounds + " lần ngồi ghế 0 (mong đợi 35–65)");
            }
        }

        static void TimeoutTentative()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 400; o.PresentationMaxMs = 2000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 5) { AutoLock = false, SelectOnly = true };      // A chỉ chọn tạm, không bao giờ khóa
                    var bb = new Bot(b, 6);
                    StartAndWait(a, b, ba, bb, 1, 15000);
                    int seatA = a.Get(() => a.Client.Seat);
                    var turnsA = a.Snapshot().Where(e => e.Kind == OnlineEventKind.TurnResolved).ToList();
                    T.Check(turnsA.Count >= 5, "có ít nhất 5 lượt: " + turnsA.Count);
                    int tentativeUsed = 0;
                    foreach (var e in turnsA)
                    {
                        T.Check((e.TimeoutMask & (1 << seatA)) != 0, "lượt " + e.Turn + ": A bị đánh dấu hết giờ");
                        T.Check((e.TimeoutMask & (1 << (1 - seatA))) == 0, "lượt " + e.Turn + ": B không bị đánh dấu hết giờ");
                        int expected = ba.Picks.First(p => p.Key == e.Turn).Value;
                        if (e.Result != null && e.Result.PlayedCards[seatA].Id == expected) tentativeUsed++;
                    }
                    T.Eq(turnsA.Count, tentativeUsed, "mọi lượt hết giờ đều dùng đúng lá CHỌN TẠM cuối cùng của A");

                    // B khóa ngay nhưng vẫn phải chờ tới hạn của A: khoảng cách TurnBegan -> TurnResolved >= ~SelectionMs
                    var begin = b.Snapshot().First(e => e.Kind == OnlineEventKind.TurnBegan);
                    var resolved = b.Snapshot().First(e => e.Kind == OnlineEventKind.TurnResolved);
                    long dt = b.At(resolved) - b.At(begin);
                    T.Check(dt >= 330 && dt < 1200, "lượt đầu kết thúc đúng hạn của máy chủ (" + dt + " ms, hạn 400 ms)");
                }
            }
        }

        static void TimeoutSilent()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 300; o.PresentationMaxMs = 2000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 7) { AutoLock = false, SelectOnly = false };    // A hoàn toàn im lặng
                    var bb = new Bot(b, 8);
                    StartAndWait(a, b, ba, bb, 1, 15000);
                    int seatA = a.Get(() => a.Client.Seat);
                    var resolved = a.Snapshot().Where(e => e.Kind == OnlineEventKind.TurnResolved).ToList();
                    T.Check(resolved.All(e => (e.TimeoutMask & (1 << seatA)) != 0), "mọi lượt A im lặng đều bị đánh dấu hết giờ");
                    T.Check(resolved.All(e => !e.Desync && e.Result != null), "lá bốc ngẫu nhiên luôn hợp lệ (nằm trong tay A): gương không lệch");
                    T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
                }
            }
        }

        static void TimeoutBoth()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 200; o.PresentationMaxMs = 600; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 9) { AutoLock = false, SelectOnly = false, AutoAck = false };
                    var bb = new Bot(b, 10) { AutoLock = false, SelectOnly = false, AutoAck = false };
                    StartAndWait(a, b, ba, bb, 1, 20000);
                    T.Check(ba.Resolved.All(r => r.TimeoutMask == 3), "cả hai im lặng: mọi lượt đều do máy chủ tự chốt (mask = 3)");
                    T.Check(a.Get(() => a.Client.Mirror.State.IsFinished), "trận vẫn kết thúc đúng luật dù không ai thao tác");
                    // server chỉ chốt trận sau pha trình diễn (ở đây: fallback 600 ms)
                    T.Check(T.WaitUntil(() => srv.Server.MatchesFinished == 1, 3000), "server ghi nhận trận hoàn tất sau fallback trình diễn");
                }
            }
        }

        static void PresentationFast()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 5000; o.PresentationAckGraceMs = 5000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 11); var bb = new Bot(b, 12);
                    StartAndWait(a, b, ba, bb, 1, 10000);
                    var ev = a.Snapshot();
                    int gaps = 0;
                    for (int i = 0; i < ev.Count - 1; i++)
                    {
                        if (ev[i].Kind != OnlineEventKind.TurnResolved || ev[i].Snapshot.IsFinished) continue;
                        var next = ev.Skip(i + 1).FirstOrDefault(e => e.Kind == OnlineEventKind.TurnBegan);
                        if (next == null) continue;
                        long gap = a.At(next) - a.At(ev[i]);
                        gaps++;
                        T.Check(gap < 800, "lượt " + ev[i].Turn + ": cả hai báo xong => sang lượt kế sau " + gap + " ms (không chờ trần 5000 ms)");
                    }
                    T.Check(gaps >= 4, "đã đo " + gaps + " khoảng chuyển lượt");
                }
            }
        }

        static void PresentationStall()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 8000; o.PresentationAckGraceMs = 700; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 13); var bb = new Bot(b, 14) { AutoAck = false };   // B không bao giờ báo xong
                    StartAndWait(a, b, ba, bb, 1, 20000);
                    var ev = a.Snapshot();
                    var first = ev.First(e => e.Kind == OnlineEventKind.TurnResolved);
                    var next = ev.SkipWhile(e => e != first).First(e => e.Kind == OnlineEventKind.TurnBegan);
                    long gap = a.At(next) - a.At(first);
                    T.Check(gap >= 600 && gap < 2000, "một bên báo xong, bên kia im lặng: server chỉ chờ thêm ~700 ms rồi đi tiếp (" + gap + " ms; trần tuyệt đối 8000 ms)");
                }
            }
        }

        static void PresentationFallback()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 500; o.PresentationAckGraceMs = 5000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 15) { AutoAck = false }; var bb = new Bot(b, 16) { AutoAck = false };
                    StartAndWait(a, b, ba, bb, 1, 20000);
                    var ev = a.Snapshot();
                    var first = ev.First(e => e.Kind == OnlineEventKind.TurnResolved);
                    var next = ev.SkipWhile(e => e != first).First(e => e.Kind == OnlineEventKind.TurnBegan);
                    long gap = a.At(next) - a.At(first);
                    T.Check(gap >= 450 && gap < 1500, "không ai báo xong: máy chủ đi tiếp theo fallback 500 ms (" + gap + " ms)");
                }
            }
        }

        static void LockSecrecy()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 3000; }))
            using (var a = RawClient.Handshake(srv.Port, "A"))
            using (var b = RawClient.Handshake(srv.Port, "B"))
            {
                T.Check(a.Welcome != null && b.Welcome != null, "cả hai bắt tay");
                if (a.Welcome == null || b.Welcome == null) return;
                T.Check(a.Welcome.ResumeToken != b.Welcome.ResumeToken && a.Welcome.ResumeToken != 0, "token nối lại khác nhau và khác 0");
                a.Send(new CreateRoomMsg());
                var lobby = a.Wait<LobbyStateMsg>();
                b.Send(new JoinRoomMsg { RoomCode = lobby.RoomCode });
                b.Wait<LobbyStateMsg>();
                a.Send(new ReadyMsg { Ready = true }); b.Send(new ReadyMsg { Ready = true });
                var sa = a.Wait<MatchStartMsg>(); var sb = b.Wait<MatchStartMsg>();
                var ta = a.Wait<TurnBeginMsg>(); var tb = b.Wait<TurnBeginMsg>();
                T.Check(sa != null && sb != null && ta != null && tb != null, "nhận MatchStart + TurnBegin");
                if (sa == null || sb == null || ta == null || tb == null) return;
                int cardA = sa.State.Players[sa.Seat].Hand[0].Id;
                int cardB = sb.State.Players[sb.Seat].Hand[1].Id;

                // B chỉ chọn TẠM (không khóa): A không được biết gì cả
                b.Send(new SelectMsg { MatchId = sb.MatchId, Turn = 1, CardId = cardB, Seq = 1 });
                System.Threading.Thread.Sleep(150);
                T.Eq(0, a.CountOf<OpponentLockedMsg>(), "lựa chọn tạm của B không tạo thông điệp nào cho A");

                // A khóa => A nhận LockAck, B nhận OpponentLocked; B chưa thấy lá của A
                a.Send(new LockMsg { MatchId = sa.MatchId, Turn = 1, CardId = cardA, Seq = 1 });
                var ack = a.Wait<LockAckMsg>();
                var opp = b.Wait<OpponentLockedMsg>();
                T.Check(ack != null && ack.CardId == cardA, "A nhận LockAck");
                T.Check(opp != null && opp.Turn == 1, "B nhận OpponentLocked");
                if (opp != null) T.Eq(8, OnlineProtocol.Encode(opp).Length, "OpponentLocked chỉ có 8 byte (tiêu đề 2 + loại 1 + matchId 4 + lượt 1): không có chỗ chứa lá bài");
                T.Eq(0, b.CountOf<LockAckMsg>(), "B không nhận LockAck của A");
                T.Eq(0, a.CountOf<OpponentLockedMsg>(), "A chưa nhận OpponentLocked (B chưa khóa)");
                T.Eq(0, a.CountOf<TurnResolvedMsg>() + b.CountOf<TurnResolvedMsg>(), "chưa có TurnResolved khi chỉ một bên khóa");

                // A đổi ý sau khi khóa bằng Select/Lock lá khác: Select bị bỏ qua, Lock bị từ chối
                int otherA = sa.State.Players[sa.Seat].Hand[1].Id;
                a.Send(new SelectMsg { MatchId = sa.MatchId, Turn = 1, CardId = otherA, Seq = 2 });
                a.Send(new LockMsg { MatchId = sa.MatchId, Turn = 1, CardId = otherA, Seq = 3 });
                var err = a.Wait<ErrorMsg>();
                T.Check(err != null && err.Code == ErrorCode.InvalidAction, "khóa lá khác sau khi đã khóa bị từ chối (InvalidAction)");

                // B khóa => cả hai nhận TurnResolved với đúng hai lá đã khóa
                b.Send(new LockMsg { MatchId = sb.MatchId, Turn = 1, CardId = cardB, Seq = 2 });
                var ra = a.Wait<TurnResolvedMsg>(); var rb = b.Wait<TurnResolvedMsg>();
                T.Check(ra != null && rb != null, "cả hai nhận TurnResolved");
                if (ra == null || rb == null) return;
                int id0 = sa.Seat == 0 ? cardA : cardB, id1 = sa.Seat == 0 ? cardB : cardA;
                T.Check(ra.Id0 == id0 && ra.Id1 == id1 && rb.Id0 == id0 && rb.Id1 == id1, "TurnResolved mang đúng hai lá đã khóa (A không đổi ý được)");
                T.Eq(0, ra.TimeoutMask, "không ai hết giờ");
                T.Eq(ra.StateHash, MatchStateCodec.Hash(ra.State), "băm trong thông điệp khớp snapshot");
            }
        }
    }
}
