using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TheLastKing.Online;

namespace TheLastKing.Tests
{
    /// <summary>Mất mạng, nối lại, hết hạn giữ chỗ, rời trận, tắt máy chủ, phòng nhàn rỗi.</summary>
    public static class ResilienceTests
    {
        public static void Register()
        {
            Registry.Add("resilience.lobby-disconnect-frees-slot", LobbyDisconnect);
            Registry.Add("resilience.match.resume-after-socket-loss-timers-frozen", ResumeFrozen);
            Registry.Add("resilience.match.resume-during-presentation-phase", ResumePresentation);
            Registry.Add("resilience.match.both-players-drop-and-resume", BothResume);
            Registry.Add("resilience.match.grace-expiry-aborts-without-winner", GraceExpiry);
            Registry.Add("resilience.match.explicit-leave-aborts-immediately", ExplicitLeave);
            Registry.Add("resilience.match.both-drop-and-never-return-room-removed", BothGone);
            Registry.Add("resilience.session.takeover-of-half-open-connection", Takeover);
            Registry.Add("resilience.session.late-resume-after-expiry-is-new-session", LateResume);
            Registry.Add("resilience.client.gives-up-after-grace", ClientGivesUp);
            Registry.Add("resilience.server.shutdown-notifies-clients", Shutdown);
            Registry.Add("resilience.room.idle-closed-and-client-recovers", IdleRoom);
            Registry.Add("resilience.heartbeat.silent-client-dropped", SilentDropped);
            Registry.Add("resilience.client.diverged-mirror-replaced-by-server-snapshot", DesyncSnapshot);
            Registry.Add("resilience.client.forced-resync-when-turn-begin-mismatches", ForcedResync);
        }

        static void LobbyDisconnect()
        {
            using (var srv = new TestServer())
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.KillSocket();
                    T.Check(T.WaitUntil(() => !b.Get(() => b.Client.Lobby.Slots[0].Present), 3000), "A đứt mạng ở sảnh => ô của A trống ở phía B");
                    T.Check(a.WaitEvent(OnlineEventKind.Disconnected, 2000) != null, "A nhận Disconnected (không nối lại ở sảnh vì chưa có trận)");
                    T.Eq(OnlineState.Closed, a.State, "A ở trạng thái Closed");
                    T.Eq(0L, srv.Server.MatchesAborted, "chưa có trận nào để hủy");
                }
            }
        }

        static void ResumeFrozen()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 1000; o.ReconnectGraceMs = 6000; o.PresentationMaxMs = 3000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 1800);        // chờ lâu hơn cả thời gian chọn (1000 ms)
                    var ba = new Bot(a, 1) { AutoLock = false };
                    var bb = new Bot(b, 2);
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.TurnBegan, 3000) != null, "lượt 1 bắt đầu");
                    T.Check(b.WaitEvent(OnlineEventKind.LockConfirmed, 3000) != null, "B đã khóa lá của mình");

                    a.KillSocket();
                    ba.AutoLock = true;      // sau khi nối lại, A khóa khi biết mình chưa khóa
                    long killedAt = 0;
                    var lost = a.WaitEvent(OnlineEventKind.LinkLost, 3000);
                    T.Check(lost != null && lost.Number == 6000, "A nhận LinkLost (thời gian giữ chỗ 6000 ms)");
                    if (lost != null) killedAt = a.At(lost);

                    var off = b.WaitEvent(OnlineEventKind.OpponentConnection, 3000, e => !e.Flag);
                    T.Check(off != null && off.Number == 6000, "B được báo đối thủ mất kết nối (kèm thời gian chờ)");

                    var restored = a.WaitEvent(OnlineEventKind.LinkRestored, 8000);
                    T.Check(restored != null, "A nối lại được bằng token");
                    var resync = a.WaitEvent(OnlineEventKind.Resynced, 3000);
                    T.Check(resync != null && resync.Resync.Phase == MatchPhase.Selecting && !resync.Resync.IAmLocked && resync.Resync.Turn == 1,
                        "A nhận Resync: đang chọn lượt 1, chưa khóa");
                    T.Check(resync != null && resync.Resync.RemainingMs >= 2900, "đồng hồ được giữ nguyên/bù tối thiểu 3 s sau khi nối lại (" + (resync != null ? resync.Resync.RemainingMs : -1) + " ms)");
                    T.Check(resync != null && resync.Resync.OpponentLocked, "Resync cho biết đối thủ đã khóa");
                    long outage = restored != null ? a.At(restored) - killedAt : -1;
                    T.Check(outage >= 1500, "thời gian mất kết nối thực tế " + outage + " ms > thời gian chọn 1000 ms");

                    var on = b.WaitEvent(OnlineEventKind.OpponentConnection, 3000, e => e.Flag);
                    T.Check(on != null, "B được báo đối thủ đã trở lại");

                    var r1 = b.WaitEvent(OnlineEventKind.TurnResolved, 5000);
                    T.Check(r1 != null && r1.Turn == 1, "lượt 1 được xử lý");
                    T.Eq(0, r1 != null ? r1.TimeoutMask : -1, "KHÔNG có lượt nào bị tính hết giờ: đồng hồ đã đóng băng suốt lúc A mất kết nối");

                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 15000), "trận vẫn chạy tới khi kết thúc sau khi nối lại");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương cuối trận giống nhau");
                    T.Check(ba.Resolved.All(r => !r.Desync) && bb.Resolved.All(r => !r.Desync), "không lệch pha");
                    T.Eq(1L, srv.Server.Resumes, "server đếm 1 lần nối lại");
                    T.Eq(0L, srv.Server.MatchesAborted, "không trận nào bị hủy");
                    T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
                }
            }
        }

        static void ResumePresentation()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 3000; o.PresentationMaxMs = 9000; o.PresentationAckGraceMs = 9000; o.ReconnectGraceMs = 6000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 200);
                    var ba = new Bot(a, 3) { AutoAck = false };     // A chưa báo xong trình diễn thì đứt mạng
                    var bb = new Bot(b, 4);
                    Pairs.ReadyBoth(a, b);
                    var res = a.WaitEvent(OnlineEventKind.TurnResolved, 5000);
                    T.Check(res != null, "lượt 1 đã xử lý");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    a.KillSocket();
                    var resync = a.WaitEvent(OnlineEventKind.Resynced, 5000);
                    T.Check(resync != null && resync.Resync.Phase == MatchPhase.Presenting && resync.Resync.Turn == 1, "Resync ở pha trình diễn của lượt 1");
                    ba.AutoAck = true;
                    int mark = a.Mark();
                    var t2 = a.WaitEvent(OnlineEventKind.TurnBegan, 4000, e => e.Turn == 2 && !e.Refresh);
                    T.Check(t2 != null, "sang lượt 2 ngay sau khi nối lại (server coi phần trình diễn đã lỡ là xong), không chờ trần 9000 ms");
                    T.Check(sw.ElapsedMilliseconds < 4000, "mất " + sw.ElapsedMilliseconds + " ms từ lúc đứt mạng");
                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 20000), "trận chạy tiếp tới khi kết thúc");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương cuối trận giống nhau");
                    T.Eq(0L, srv.Server.MatchesAborted, "không trận nào bị hủy");
                }
            }
        }

        static void BothResume()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 1000; o.ReconnectGraceMs = 6000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 300);
                    b.Do(() => b.Client.ReconnectInitialDelayMs = 1500);
                    var ba = new Bot(a, 5) { AutoLock = false };
                    var bb = new Bot(b, 6) { AutoLock = false };
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.TurnBegan, 3000) != null && b.WaitEvent(OnlineEventKind.TurnBegan, 3000) != null, "lượt 1 bắt đầu");
                    a.KillSocket(); b.KillSocket();
                    ba.AutoLock = true; bb.AutoLock = true;
                    var ra = a.WaitEvent(OnlineEventKind.Resynced, 8000);
                    T.Check(ra != null && !ra.Resync.OpponentConnected, "A nối lại trước: Resync cho biết đối thủ CHƯA trở lại");
                    var rb = b.WaitEvent(OnlineEventKind.Resynced, 8000);
                    T.Check(rb != null, "B nối lại sau");
                    var r1 = a.WaitEvent(OnlineEventKind.TurnResolved, 6000);
                    T.Check(r1 != null && r1.TimeoutMask == 0, "trận chỉ tiếp tục khi đủ cả hai; lượt 1 không có hết giờ (mask=" + (r1 != null ? r1.TimeoutMask : -1) + ")");
                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 15000), "trận kết thúc bình thường");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương cuối trận giống nhau");
                    T.Eq(2L, srv.Server.Resumes, "2 lần nối lại");
                    T.Eq(0L, srv.Server.MatchesAborted, "không hủy trận");
                }
            }
        }

        static void GraceExpiry()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.ReconnectGraceMs = 800; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 60000);      // không bao giờ quay lại kịp
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.MatchStarted, 3000) != null && b.WaitEvent(OnlineEventKind.MatchStarted, 3000) != null, "trận bắt đầu");
                    a.KillSocket();
                    var off = b.WaitEvent(OnlineEventKind.OpponentConnection, 3000, e => !e.Flag);
                    T.Check(off != null && off.Number == 800, "B được báo A mất kết nối, giữ chỗ 800 ms");
                    var ab = b.WaitEvent(OnlineEventKind.MatchAborted, 4000);
                    T.Check(ab != null && ab.Reason == AbortReason.OpponentDisconnected, "hết hạn giữ chỗ => trận bị hủy (OpponentDisconnected)");
                    T.Check(ab != null && ab.Text.Contains("không ai bị tính thua"), "thông báo nêu rõ không ai bị tính thua");
                    T.Check(T.WaitUntil(() => { var l = b.Get(() => b.Client.Lobby); return l != null && !l.Slots[1 - l.MySlot].Present; }, 2000), "B quay về sảnh một mình");
                    var lobby = b.Get(() => b.Client.Lobby);
                    T.Eq(0, lobby.Games, "không có trận nào được ghi vào chuỗi");
                    T.Eq(0, lobby.Wins[0] + lobby.Wins[1], "không ai được tính thắng");
                    T.Eq(1L, srv.Server.MatchesAborted, "server đếm 1 trận hủy");
                    T.Eq(0L, srv.Server.MatchesFinished, "không có trận hoàn tất");
                    var gone = a.WaitEvent(OnlineEventKind.Disconnected, 3000);
                    T.Check(gone != null, "A cũng từ bỏ nối lại khi hết thời gian cho phép");
                }
            }
        }

        static void ExplicitLeave()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.ReconnectGraceMs = 5000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.TurnBegan, 3000) != null, "lượt 1 bắt đầu");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    a.Do(() => a.Client.LeaveRoom());
                    var ab = b.WaitEvent(OnlineEventKind.MatchAborted, 2000);
                    T.Check(ab != null && ab.Reason == AbortReason.OpponentLeft, "B nhận MatchAborted(OpponentLeft)");
                    T.Check(sw.ElapsedMilliseconds < 800, "báo ngay (" + sw.ElapsedMilliseconds + " ms), không chờ thời gian giữ chỗ 5000 ms");
                    T.Eq(OnlineState.Online, a.State, "A đã về Online");
                    T.Check(T.WaitUntil(() => { var l = b.Get(() => b.Client.Lobby); return l != null && !l.Slots[1 - l.MySlot].Present; }, 2000), "B ở sảnh một mình");
                    T.Eq(0L, srv.Server.Resumes, "không có nối lại");
                }
            }
        }

        static void BothGone()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.ReconnectGraceMs = 700; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 60000);
                    b.Do(() => b.Client.ReconnectInitialDelayMs = 60000);
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.MatchStarted, 3000) != null, "trận bắt đầu");
                    a.KillSocket(); b.KillSocket();
                    T.Check(T.WaitUntil(() => srv.Server.RoomCount == 0 && srv.Server.MatchesAborted == 1, 4000), "cả hai biến mất => hết hạn giữ chỗ thì hủy trận và xóa phòng");
                    T.Check(T.WaitUntil(() => srv.Server.ConnectionCount == 0, 2000), "không còn kết nối nào");
                    T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
                }
            }
        }

        static void Takeover()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 3000; o.ReconnectGraceMs = 5000; }))
            using (var m = RawMatch.Start(srv))
            {
                ulong tokenA = m.A.Welcome.ResumeToken;
                using (var a2 = new RawClient(srv.Port))
                {
                    // A "nghĩ" kết nối cũ đã chết và nối lại trong khi server vẫn coi kết nối cũ còn sống
                    a2.Send(new HelloMsg { Name = "A", RulesFingerprint = RulesFingerprint.Value, ResumeToken = tokenA });
                    var w = a2.Wait<WelcomeMsg>();
                    T.Check(w != null && w.Resumed, "chiếm lại phiên bằng token => Resumed = true");
                    T.Check(w != null && w.ResumeToken != tokenA && w.ResumeToken != 0, "token được đổi mới sau mỗi lần nối lại");
                    var rs = a2.Wait<ResyncMsg>();
                    T.Check(rs != null && rs.Seat == m.SA.Seat && rs.Phase == MatchPhase.Selecting && rs.Turn == 1 && !rs.IAmLocked, "Resync đúng ghế/pha/lượt");
                    T.Check(m.A.WaitClosed(2000), "kết nối cũ bị server đóng");
                    var offs = m.B.All.OfType<OpponentConnectionMsg>().Where(x => !x.Connected).Count();
                    T.Eq(0, offs, "B không bị báo mất kết nối (không có khoảng tạm dừng nào)");

                    // kết nối mới chơi tiếp bình thường
                    a2.Send(new LockMsg { MatchId = m.SA.MatchId, Turn = 1, CardId = m.CardA(0), Seq = 1 });
                    m.B.Send(new LockMsg { MatchId = m.SB.MatchId, Turn = 1, CardId = m.CardB(0), Seq = 1 });
                    T.Check(a2.Wait<TurnResolvedMsg>() != null && m.B.Wait<TurnResolvedMsg>() != null, "lượt 1 được xử lý qua kết nối mới");

                    // token cũ đã vô hiệu
                    using (var imposter = RawClient.Handshake(srv.Port, "Imposter", tokenA))
                        T.Check(imposter.Welcome != null && !imposter.Welcome.Resumed, "token cũ không còn chiếm được phiên");
                }
            }
        }

        static void LateResume()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.ReconnectGraceMs = 500; }))
            using (var m = RawMatch.Start(srv))
            {
                ulong token = m.A.Welcome.ResumeToken;
                m.A.Dispose();
                var ab = m.B.Wait<MatchAbortedMsg>(4000);
                T.Check(ab != null && ab.Reason == AbortReason.OpponentDisconnected, "B nhận MatchAborted sau khi hết thời gian giữ chỗ");
                using (var late = RawClient.Handshake(srv.Port, "A", token))
                    T.Check(late.Welcome != null && !late.Welcome.Resumed, "nối lại SAU khi hết hạn => phiên mới (Resumed = false), không vào lại trận đã hủy");
            }
        }

        static void ClientGivesUp()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.ReconnectGraceMs = 700; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 60000);
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.MatchStarted, 3000) != null, "trận bắt đầu");
                    a.KillSocket();
                    T.Check(a.WaitEvent(OnlineEventKind.LinkLost, 2000) != null, "A nhận LinkLost");
                    var d = a.WaitEvent(OnlineEventKind.Disconnected, 3000);
                    T.Check(d != null && d.Text.Contains("nối lại"), "hết thời gian nối lại => Disconnected có lý do: " + (d != null ? d.Text : "null"));
                    T.Eq(OnlineState.Closed, a.State, "A ở trạng thái Closed");
                }
            }
        }

        static void Shutdown()
        {
            var srv = new TestServer(o => { o.SelectionMs = 5000; });
            TestClient a, b; string code;
            T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
            using (a) using (b)
            {
                Pairs.ReadyBoth(a, b);
                T.Check(a.WaitEvent(OnlineEventKind.TurnBegan, 3000) != null, "lượt 1 bắt đầu");
                srv.Dispose();      // tắt có trật tự giữa trận
                var ab = a.WaitEvent(OnlineEventKind.MatchAborted, 3000);
                T.Check(ab != null && ab.Reason == AbortReason.ServerShutdown, "A được báo MatchAborted(ServerShutdown)");
                var bb = b.WaitEvent(OnlineEventKind.MatchAborted, 3000);
                T.Check(bb != null && bb.Reason == AbortReason.ServerShutdown, "B được báo MatchAborted(ServerShutdown)");
                T.Check(a.WaitEvent(OnlineEventKind.Disconnected, 3000) != null, "A nhận Disconnected sau khi máy chủ đóng");
                T.Check(b.WaitEvent(OnlineEventKind.Disconnected, 3000) != null, "B nhận Disconnected sau khi máy chủ đóng");
            }
        }

        static void IdleRoom()
        {
            using (var srv = new TestServer(o => { o.IdleRoomMs = 500; }))
            using (var a = new TestClient("Idle").Connect(srv.Port))
            {
                a.WaitEvent(OnlineEventKind.Connected, 3000);
                a.Do(() => a.Client.CreateRoom());
                T.Check(a.WaitEvent(OnlineEventKind.LobbyChanged, 2000) != null, "tạo phòng");
                var err = a.WaitEvent(OnlineEventKind.ServerError, 3000, e => e.Code == ErrorCode.RoomClosed);
                T.Check(err != null, "phòng nhàn rỗi bị đóng và client được báo (RoomClosed)");
                T.Eq(OnlineState.Online, a.State, "client tự về Online (không kẹt ở màn phòng cũ)");
                T.Check(T.WaitUntil(() => srv.Server.RoomCount == 0, 1000), "server không còn phòng");
                int m = a.Mark();
                a.Do(() => a.Client.CreateRoom());
                T.Check(a.WaitEvent(OnlineEventKind.LobbyChanged, 2000, null, m) != null, "tạo lại phòng mới được ngay");
            }
        }

        /// <summary>Trạng thái hợp lệ nhưng đã đi trước <paramref name="turns"/> lượt so với đầu trận.</summary>
        static MatchState TamperedState(int turns = 1)
        {
            var m = new LastKingMatch();
            for (int i = 0; i < turns && !m.State.IsFinished; i++) m.ResolveTurn(0, 0);
            return m.State;
        }

        static void SetMirror(TestClient c, MatchState state)
        {
            var f = typeof(OnlineClient).GetField("mirror", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            f.SetValue(c.Client, new LastKingMatch(state));
        }

        static void DesyncSnapshot()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 3000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    var ba = new Bot(a, 1); var bb = new Bot(b, 2) { AutoLock = false };
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.LockConfirmed, 3000) != null, "A đã khóa");
                    a.Do(() => SetMirror(a, TamperedState()));          // gương của A đi lệch (hợp lệ nhưng sai lượt)
                    bb.AutoLock = true;                                 // từ lượt 2 trở đi B tự khóa; lượt 1 khóa tay ngay dưới đây
                    b.Do(() =>
                    {
                        MatchState st = b.Client.Mirror.State;
                        int id = st.Players[b.Client.Seat].Hand[0].Id;
                        b.Client.Select(id); b.Client.Lock(id);
                    });
                    var r1 = a.WaitEvent(OnlineEventKind.TurnResolved, 4000);
                    T.Check(r1 != null, "A vẫn nhận TurnResolved của lượt 1 (không bị nuốt)");
                    T.Check(r1 != null && r1.Desync && r1.Result == null, "A báo Desync và không có Result để trình diễn");
                    T.Check(r1 != null && MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)) == MatchStateCodec.Hash(r1.Snapshot), "gương của A được thay bằng snapshot chuẩn của máy chủ");
                    T.Check(a.WaitEvent(OnlineEventKind.TurnBegan, 4000, e => e.Turn == 2) != null, "lượt 2 bắt đầu bình thường");
                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 15000), "trận chạy tới kết thúc");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương cuối trận giống nhau");
                }
            }
        }

        static void ForcedResync()
        {
            using (var srv = new TestServer(o => { o.SelectionMs = 5000; o.PresentationMaxMs = 3000; o.ReconnectGraceMs = 5000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code), "dựng cặp client");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 100);
                    var ba = new Bot(a, 3); var bb = new Bot(b, 4);
                    Action<OnlineEvent> inner = a.Handler;
                    bool tampered = false;
                    a.Handler = e =>
                    {
                        if (e.Kind == OnlineEventKind.TurnResolved && e.Turn == 1 && !tampered)
                        {
                            tampered = true;
                            SetMirror(a, TamperedState(2));     // gương đi trước 2 lượt ngay trước khi báo xong => TurnBegin(2) sẽ không khớp
                        }
                        inner(e);
                    };
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.LinkLost, 5000) != null, "TurnBegin không khớp gương => A chủ động nối lại (LinkLost)");
                    T.Check(a.WaitEvent(OnlineEventKind.LinkRestored, 5000) != null, "nối lại thành công");
                    var rs = a.WaitEvent(OnlineEventKind.Resynced, 5000);
                    T.Check(rs != null && rs.Resync.Turn == 2, "nhận Resync cho lượt 2");
                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 15000), "trận chạy tới kết thúc sau khi tự chữa");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương cuối trận giống nhau");
                    T.Eq(0L, srv.Server.MatchesAborted, "không hủy trận");
                    T.Eq(1L, srv.Server.Resumes, "đúng 1 lần nối lại");
                }
            }
        }

        static void SilentDropped()
        {
            using (var srv = new TestServer(o => { o.HeartbeatTimeoutMs = 800; }))
            using (var c = RawClient.Handshake(srv.Port, "Silent"))
            {
                T.Check(c.Welcome != null, "bắt tay xong");
                T.Check(c.WaitClosed(4000), "im lặng quá ngưỡng nhịp tim => server ngắt");
                T.Check(T.WaitUntil(() => srv.Server.ConnectionCount == 0, 2000), "server dọn kết nối");
            }
        }
    }
}
