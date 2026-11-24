using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using TheLastKing.Online;

namespace TheLastKing.Tests
{
    public static class LoadTests
    {
        public static void Register()
        {
            Registry.Add("load.40-rooms-80-clients-2-matches-each", ManyRooms);
            Registry.Add("load.rematch-soak-6-rooms-12-matches", Soak);
            Registry.Add("load.connection-churn-300-handshakes", Churn);
        }

        static void ManyRooms()
        {
            int pairs = 40;
            int oldSleep = TestClient.PumpSleepMs;
            TestClient.PumpSleepMs = 4;
            try
            {
                using (var srv = new TestServer(o => { o.SelectionMs = 3000; o.PresentationMaxMs = 3000; o.MaxConnectionsPerIp = 500; o.MaxConnections = 500; o.MaxRooms = 500; }))
                {
                    var A = new List<TestClient>(); var B = new List<TestClient>(); var botsA = new List<Bot>(); var botsB = new List<Bot>();
                    try
                    {
                        for (int i = 0; i < pairs; i++) { A.Add(new TestClient("A" + i).Connect(srv.Port)); B.Add(new TestClient("B" + i).Connect(srv.Port)); }
                        T.Check(T.WaitUntil(() => A.All(c => c.State == OnlineState.Online) && B.All(c => c.State == OnlineState.Online), 10000), "80 client kết nối");
                        for (int i = 0; i < pairs; i++) { var a = A[i]; a.Do(() => a.Client.CreateRoom()); }
                        for (int i = 0; i < pairs; i++)
                        {
                            var a = A[i]; var b = B[i];
                            T.Check(T.WaitUntil(() => a.Get(() => a.Client.Lobby) != null, 5000), "phòng " + i);
                            string code = a.Get(() => a.Client.Lobby.RoomCode);
                            b.Do(() => b.Client.JoinRoom(code));
                        }
                        T.Check(T.WaitUntil(() => A.All(c => c.Get(() => c.Client.Lobby.Slots[1].Present)), 8000), "mọi phòng đủ 2 người");
                        for (int i = 0; i < pairs; i++)
                        {
                            botsA.Add(new Bot(A[i], 1000 + i) { AutoRematch = true, MaxMatches = 2 });
                            botsB.Add(new Bot(B[i], 2000 + i) { AutoRematch = true, MaxMatches = 2 });
                        }
                        var sw = Stopwatch.StartNew();
                        for (int i = 0; i < pairs; i++) Pairs.ReadyBoth(A[i], B[i]);
                        bool done = T.WaitUntil(() => botsA.All(x => x.FinishedMatches >= 2) && botsB.All(x => x.FinishedMatches >= 2), 90000);
                        T.Check(done, "cả 80 bot hoàn tất 2 trận (" + botsA.Count(x => x.FinishedMatches >= 2) + "/" + pairs + " cặp A xong)");
                        long ms = sw.ElapsedMilliseconds;
                        Console.WriteLine("      " + pairs * 2 + " trận (" + pairs + " phòng song song) xong trong " + ms + " ms");
                        T.Check(T.WaitUntil(() => srv.Server.MatchesFinished == pairs * 2, 10000), "server đếm đủ " + pairs * 2 + " trận hoàn tất (hiện " + srv.Server.MatchesFinished + ")");
                        T.Eq(0L, srv.Server.MatchesAborted, "không trận nào bị hủy");
                        int mismatches = 0, desyncs = 0;
                        for (int i = 0; i < pairs; i++)
                        {
                            int idx = i;
                            ulong ha = MatchStateCodec.Hash(A[idx].Get(() => A[idx].Client.Mirror.State)), hb = MatchStateCodec.Hash(B[idx].Get(() => B[idx].Client.Mirror.State));
                            if (ha != hb) mismatches++;
                            desyncs += botsA[idx].Resolved.Count(r => r.Desync) + botsB[idx].Resolved.Count(r => r.Desync);
                        }
                        T.Eq(0, mismatches, "mọi cặp có gương cuối trận trùng nhau");
                        T.Eq(0, desyncs, "không có lần lệch pha nào");
                        T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI: " + string.Join(" | ", srv.ErrorLines.Take(2)));
                    }
                    finally { foreach (var c in A.Concat(B)) c.Dispose(); }
                    T.Check(T.WaitUntil(() => srv.Server.RoomCount == 0 && srv.Server.ConnectionCount == 0, 8000), "dọn sạch phòng và kết nối");
                }
            }
            finally { TestClient.PumpSleepMs = oldSleep; }
        }

        static void Soak()
        {
            int rooms = 6, matches = 12;
            // Bot không có thời gian suy nghĩ nên gửi >60 thông điệp/giây (người thật chỉ vài thông điệp/giây).
            // Giới hạn tốc độ vẫn được kiểm riêng ở security.rate-limit-*; ở đây nới ra để đo độ bền chuỗi trận.
            using (var srv = new TestServer(o => { o.SelectionMs = 3000; o.PresentationMaxMs = 3000; o.MaxConnectionsPerIp = 100; o.MaxMessagesPerSecond = 2000; }))
            {
                var all = new List<TestClient>(); var bots = new List<Bot>();
                try
                {
                    var pairList = new List<KeyValuePair<TestClient, TestClient>>();
                    for (int i = 0; i < rooms; i++)
                    {
                        TestClient a, b; string code;
                        T.Check(Pairs.MakePair(srv, out a, out b, out code, "A" + i, "B" + i), "phòng " + i);
                        all.Add(a); all.Add(b);
                        bots.Add(new Bot(a, 10 + i) { AutoRematch = true, MaxMatches = matches });
                        bots.Add(new Bot(b, 50 + i) { AutoRematch = true, MaxMatches = matches });
                        pairList.Add(new KeyValuePair<TestClient, TestClient>(a, b));
                    }
                    var sw = Stopwatch.StartNew();
                    foreach (var p in pairList) Pairs.ReadyBoth(p.Key, p.Value);
                    bool soakDone = T.WaitUntil(() => bots.All(x => x.FinishedMatches >= matches), 30000);
                    T.Check(soakDone, "mọi phòng chơi đủ " + matches + " trận liên tiếp");
                    if (!soakDone)
                    {
                        for (int i = 0; i < pairList.Count; i++)
                        {
                            var pa = pairList[i].Key; var pb = pairList[i].Value;
                            Console.WriteLine("      [chẩn đoán] phòng " + i + ": A=" + pa.State + " finished=" + bots[2 * i].FinishedMatches + " started=" + bots[2 * i].StartedMatches +
                                " | B=" + pb.State + " finished=" + bots[2 * i + 1].FinishedMatches + " started=" + bots[2 * i + 1].StartedMatches +
                                " | lobby(A)=" + pa.Get(() => pa.Client.Lobby == null ? "null" : "games=" + pa.Client.Lobby.Games + " phase=" + pa.Client.Lobby.Phase + " readyA=" + pa.Client.Lobby.Slots[0].Ready + " readyB=" + pa.Client.Lobby.Slots[1].Ready));
                        }
                        for (int i = 0; i < pairList.Count; i++)
                        {
                            if (bots[2 * i].FinishedMatches >= matches) continue;
                            foreach (var who in new[] { pairList[i].Key, pairList[i].Value })
                            {
                                var evs = who.Snapshot();
                                Console.WriteLine("      [sự kiện cuối của " + who.Name + "]");
                                foreach (var e in evs.Skip(Math.Max(0, evs.Count - 9)))
                                    Console.WriteLine("         t=" + who.At(e) + " " + e.Kind + (e.Kind == OnlineEventKind.LobbyChanged ? "" : "") + " turn=" + e.Turn + (e.Kind == OnlineEventKind.ServerError ? " code=" + e.Code + " \"" + e.Text + "\"" : ""));
                            }
                        }
                        return;
                    }
                    Console.WriteLine("      " + rooms * matches + " trận liên tiếp xong trong " + sw.ElapsedMilliseconds + " ms");
                    T.Check(T.WaitUntil(() => srv.Server.MatchesFinished == rooms * matches, 10000), "server đếm " + rooms * matches + " trận (" + srv.Server.MatchesFinished + ")");
                    T.Eq(rooms, srv.Server.RoomCount, "số phòng không đổi (không rò rỉ phòng)");
                    T.Eq(rooms * 2, srv.Server.ConnectionCount, "số kết nối không đổi");
                    foreach (var p in pairList)
                    {
                        var l = p.Key.Get(() => p.Key.Client.Lobby);
                        T.Check(T.WaitUntil(() => p.Key.Get(() => p.Key.Client.Lobby.Games) == matches, 5000), "bảng chuỗi Games = " + matches);
                        l = p.Key.Get(() => p.Key.Client.Lobby);
                        T.Eq(matches, l.Wins[0] + l.Wins[1], "tổng thắng = số trận");
                    }
                    // ghế luân phiên suốt chuỗi
                    bool alternate = bots.Where((b, i) => i % 2 == 0).All(b => Enumerable.Range(1, matches - 1).All(k => b.Seats[k] == 1 - b.Seats[k - 1]));
                    T.Check(alternate, "ghế đổi luân phiên trong cả chuỗi " + matches + " trận ở mọi phòng");
                    T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
                }
                finally { foreach (var c in all) c.Dispose(); }
            }
        }

        static void Churn()
        {
            using (var srv = new TestServer(o => { o.MaxConnectionsPerIp = 1000; o.MaxConnections = 2000; }))
            {
                var sw = Stopwatch.StartNew();
                int ok = 0;
                for (int i = 0; i < 300; i++)
                {
                    using (var c = RawClient.Handshake(srv.Port, "Churn" + i))
                    {
                        if (c.Welcome != null) ok++;
                        if (i % 3 == 0) { c.Send(new CreateRoomMsg()); c.Wait<LobbyStateMsg>(1000); }
                    }
                }
                T.Eq(300, ok, "300 lượt kết nối/bắt tay/ngắt đều thành công (" + sw.ElapsedMilliseconds + " ms)");
                T.Check(T.WaitUntil(() => srv.Server.ConnectionCount == 0 && srv.Server.RoomCount == 0, 5000), "sau khi ngắt hết: 0 kết nối, 0 phòng (không rò rỉ)");
                T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
            }
        }
    }
}
