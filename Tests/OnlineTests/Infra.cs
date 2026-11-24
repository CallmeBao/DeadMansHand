using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading;
using TheLastKing.Online;
using TheLastKing.Server;

namespace TheLastKing.Tests
{
    /// <summary>Máy chủ thật chạy trong tiến trình kiểm thử, cổng ngẫu nhiên, mọi bộ đếm thời gian được rút ngắn.</summary>
    public sealed class TestServer : IDisposable
    {
        public static bool Verbose;
        public readonly GameServer Server;
        public readonly List<string> ErrorLines = new List<string>();
        public int Port { get { return Server.Port; } }

        public TestServer(Action<ServerOptions> configure = null)
        {
            var o = new ServerOptions
            {
                Port = 0, BindAddress = "127.0.0.1",
                SelectionMs = 1500, PresentationMaxMs = 1500, PresentationAckGraceMs = 800,
                ReconnectGraceMs = 1500, HeartbeatTimeoutMs = 3000, HandshakeTimeoutMs = 2000,
                IdleRoomMs = 60000, Seed = 4242
            };
            o.Log = s =>
            {
                if (s.Contains("LỖI")) lock (ErrorLines) ErrorLines.Add(s);
                if (Verbose) Console.WriteLine("      [srv] " + s);
            };
            if (configure != null) configure(o);
            Server = new GameServer(o);
            Server.Start();
        }

        public void Dispose() { Server.Stop(); }
    }

    /// <summary>OnlineClient thật + luồng bơm sự kiện (mô phỏng vòng Update của Unity).</summary>
    public sealed class TestClient : IDisposable
    {
        public readonly OnlineClient Client = new OnlineClient();
        public readonly List<OnlineEvent> Events = new List<OnlineEvent>();
        public Action<OnlineEvent> Handler;
        public readonly string Name;
        readonly object gate = new object();
        readonly Dictionary<OnlineEvent, long> stamps = new Dictionary<OnlineEvent, long>();
        static readonly Stopwatch Clock = Stopwatch.StartNew();
        Thread pump;
        volatile bool stop;

        /// <summary>Thời điểm (ms, đồng hồ chung của tiến trình kiểm thử) sự kiện được lấy ra khỏi hàng đợi.</summary>
        public long At(OnlineEvent e) { lock (gate) return stamps[e]; }

        public TestClient(string name)
        {
            Name = name; Client.DisplayName = name; Client.Build = "tests";
        }

        public TestClient Connect(int port, bool tls = false, Func<string, string, bool> certCheck = null, string host = "127.0.0.1")
        {
            lock (gate) Client.Connect(host, port, tls, certCheck);
            if (pump == null)
            {
                pump = new Thread(Loop) { IsBackground = true, Name = "test-pump-" + Name };
                pump.Start();
            }
            return this;
        }

        void Loop()
        {
            while (!stop)
            {
                lock (gate)
                {
                    try
                    {
                        Client.Update();
                        OnlineEvent e;
                        while (Client.TryDequeue(out e))
                        {
                            Events.Add(e);
                            stamps[e] = Clock.ElapsedMilliseconds;
                            if (Handler != null) Handler(e);
                        }
                    }
                    catch (Exception ex) { Console.WriteLine("      ✗ ngoại lệ trong luồng bơm " + Name + ": " + ex); T.Fail("ngoại lệ trong luồng bơm " + Name + ": " + ex.Message); }
                }
                Thread.Sleep(PumpSleepMs);
            }
        }

        /// <summary>Chu kỳ bơm sự kiện (mô phỏng 1 frame Unity). Tăng lên khi chạy rất nhiều client để bớt tốn CPU.</summary>
        public static int PumpSleepMs = 2;

        public void Do(Action a) { lock (gate) a(); }
        public V Get<V>(Func<V> f) { lock (gate) return f(); }
        public int Mark() { lock (gate) return Events.Count; }
        public List<OnlineEvent> Snapshot() { lock (gate) return new List<OnlineEvent>(Events); }
        public int Count(OnlineEventKind kind) { lock (gate) { int n = 0; foreach (var e in Events) if (e.Kind == kind) n++; return n; } }
        public OnlineState State { get { return Get(() => Client.State); } }

        public OnlineEvent WaitEvent(OnlineEventKind kind, int timeoutMs = 3000, Func<OnlineEvent, bool> pred = null, int fromIndex = 0)
        {
            var sw = Stopwatch.StartNew();
            while (true)
            {
                lock (gate)
                {
                    for (int i = fromIndex; i < Events.Count; i++)
                        if (Events[i].Kind == kind && (pred == null || pred(Events[i]))) return Events[i];
                }
                if (sw.ElapsedMilliseconds >= timeoutMs) return null;
                Thread.Sleep(2);
            }
        }

        public bool WaitState(OnlineState s, int timeoutMs = 3000) { return T.WaitUntil(() => State == s, timeoutMs); }

        public void Dispose()
        {
            stop = true;
            if (pump != null) pump.Join(500);
            lock (gate) Client.Close();
        }

        /// <summary>Mô phỏng đứt mạng đột ngột: đóng socket phía client mà không nói gì với máy chủ.</summary>
        public void KillSocket()
        {
            lock (gate)
            {
                var f = typeof(OnlineClient).GetField("transport", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var t = (OnlineTransport)f.GetValue(Client);
                t.Dispose();
            }
        }
    }

    public enum BotMode { Ai, Random }

    /// <summary>Người chơi tự động dùng OnlineClient thật; AI dùng đúng LastKingAI của game.</summary>
    public sealed class Bot
    {
        public readonly TestClient C;
        readonly Random rng;
        public BotMode Mode = BotMode.Ai;
        public bool AutoLock = true;        // khóa ngay khi bắt đầu lượt
        public bool SelectOnly;             // chỉ gửi Select (không Lock) -> để máy chủ tự chốt khi hết giờ
        public bool AutoAck = true;         // báo PresentationDone ngay
        public bool AutoRematch;
        public int ThinkMs;                 // làm trễ khóa (chạy trong luồng bơm nên chỉ dùng giá trị nhỏ)
        public readonly List<KeyValuePair<int, int>> Picks = new List<KeyValuePair<int, int>>();   // (lượt, id lá đã khóa)
        public readonly List<TurnResolvedInfo> Resolved = new List<TurnResolvedInfo>();
        public MatchState LastState;
        public volatile int FinishedMatches;
        public volatile int StartedMatches;
        public int MaxMatches = 1000;
        public readonly List<int> Seats = new List<int>();

        public sealed class TurnResolvedInfo { public int Turn; public int TimeoutMask; public bool Desync; public MatchState After; }

        public Bot(TestClient c, int seed)
        {
            C = c; rng = new Random(seed);
            c.Handler = Handle;
        }

        void Handle(OnlineEvent e)
        {
            switch (e.Kind)
            {
                case OnlineEventKind.MatchStarted:
                    StartedMatches++;
                    Seats.Add(C.Client.Seat);
                    break;
                case OnlineEventKind.TurnBegan:
                    if (e.Refresh) break;
                    if (AutoLock) PickAndLock(e.Turn);
                    else if (SelectOnly) PickAndSelect(e.Turn);
                    break;
                case OnlineEventKind.TurnResolved:
                    LastState = e.Snapshot;
                    Resolved.Add(new TurnResolvedInfo { Turn = e.Turn, TimeoutMask = e.TimeoutMask, Desync = e.Desync, After = e.Snapshot });
                    if (AutoAck) C.Client.PresentationDone();
                    if (e.Snapshot.IsFinished) FinishedMatches++;
                    break;
                case OnlineEventKind.Resynced:
                    LastState = e.Snapshot;
                    if (e.Resync.Phase == MatchPhase.Selecting && !e.Resync.IAmLocked && AutoLock) PickAndLock(e.Resync.Turn);
                    break;
                case OnlineEventKind.LobbyChanged:
                    if (AutoRematch && FinishedMatches < MaxMatches && C.Client.Lobby != null && C.Client.Lobby.Phase == RoomPhase.Lobby && C.Client.Lobby.Games > 0
                        && C.Client.Lobby.Slots[0].Present && C.Client.Lobby.Slots[1].Present && !C.Client.Lobby.Slots[C.Client.Lobby.MySlot].Ready)
                        C.Client.SetReady(true);
                    break;
            }
        }

        int ChooseId()
        {
            MatchState st = C.Client.Mirror.State;
            int seat = C.Client.Seat;
            int idx = Mode == BotMode.Ai ? LastKingAI.ChooseCard(st, seat, rng) : rng.Next(st.Players[seat].Hand.Count);
            return st.Players[seat].Hand[idx].Id;
        }

        void PickAndLock(int turn)
        {
            int id = ChooseId();
            Picks.Add(new KeyValuePair<int, int>(turn, id));
            C.Client.Select(id);
            C.Client.Lock(id);
        }

        void PickAndSelect(int turn)
        {
            int id = ChooseId();
            Picks.Add(new KeyValuePair<int, int>(turn, id));
            C.Client.Select(id);
        }
    }

    /// <summary>Socket thô nói đúng giao thức: để kiểm thử hành vi của máy chủ trước thông điệp sai/độc hại.</summary>
    public sealed class RawClient : IDisposable
    {
        readonly TcpClient tcp;
        readonly NetworkStream stream;
        public readonly BlockingCollection<ServerMessage> Inbox = new BlockingCollection<ServerMessage>();
        public readonly List<ServerMessage> All = new List<ServerMessage>();
        public volatile bool Closed;
        public WelcomeMsg Welcome;

        public RawClient(int port, string host = "127.0.0.1")
        {
            tcp = new TcpClient(host, port) { NoDelay = true };
            stream = tcp.GetStream();
            new Thread(ReadLoop) { IsBackground = true, Name = "raw-read" }.Start();
        }

        void ReadLoop()
        {
            try
            {
                while (true)
                {
                    byte[] body = OnlineProtocol.ReadFrameBody(stream);
                    if (body == null) break;
                    var m = (ServerMessage)OnlineProtocol.Decode(body, true);
                    lock (All) All.Add(m);
                    Inbox.Add(m);
                }
            }
            catch { }
            Closed = true;
        }

        public void Send(ClientMessage m) { SendRaw(OnlineProtocol.Encode(m)); }

        public void SendRaw(byte[] bytes)
        {
            try { stream.Write(bytes, 0, bytes.Length); stream.Flush(); } catch { Closed = true; }
        }

        /// <summary>Chờ thông điệp kiểu M (bỏ qua loại khác). null nếu hết giờ.</summary>
        public M Wait<M>(int timeoutMs = 2000) where M : ServerMessage
        {
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                ServerMessage m;
                if (Inbox.TryTake(out m, 20))
                {
                    if (m is M) return (M)m;
                }
                else if (Closed && Inbox.Count == 0) return null;     // socket đã đóng và không còn gì để đọc
            }
            return null;
        }

        public bool WaitClosed(int timeoutMs = 2000) { return T.WaitUntil(() => Closed, timeoutMs); }

        public int CountOf<M>() where M : ServerMessage
        {
            lock (All) { int n = 0; foreach (var m in All) if (m is M) n++; return n; }
        }

        public static RawClient Handshake(int port, string name, ulong resume = 0)
        {
            var c = new RawClient(port);
            c.Send(new HelloMsg { Name = name, Build = "raw", RulesFingerprint = RulesFingerprint.Value, ResumeToken = resume });
            c.Welcome = c.Wait<WelcomeMsg>(2000);
            return c;
        }

        public void Dispose()
        {
            Closed = true;
            try { stream.Close(); } catch { }
            try { tcp.Close(); } catch { }
        }
    }

    public static class Pairs
    {
        /// <summary>Hai TestClient đã kết nối và cùng ở một phòng (A tạo, B vào). Trả về cả mã phòng.</summary>
        public static bool MakePair(TestServer srv, out TestClient a, out TestClient b, out string code, string na = "Alice", string nb = "Bob",
            bool tls = false, Func<string, string, bool> certCheck = null)
        {
            TestClient ta = new TestClient(na).Connect(srv.Port, tls, certCheck);
            TestClient tb = new TestClient(nb).Connect(srv.Port, tls, certCheck);
            a = ta; b = tb; code = null;
            if (ta.WaitEvent(OnlineEventKind.Connected, 3000) == null || tb.WaitEvent(OnlineEventKind.Connected, 3000) == null) return false;
            ta.Do(() => ta.Client.CreateRoom());
            if (ta.WaitEvent(OnlineEventKind.LobbyChanged, 3000) == null) return false;
            string roomCode = ta.Get(() => ta.Client.Lobby.RoomCode);
            code = roomCode;
            tb.Do(() => tb.Client.JoinRoom(roomCode));
            return tb.WaitEvent(OnlineEventKind.LobbyChanged, 3000) != null
                   && T.WaitUntil(() => ta.Get(() => ta.Client.Lobby.Slots[1].Present), 3000);
        }

        public static void ReadyBoth(TestClient a, TestClient b)
        {
            a.Do(() => a.Client.SetReady(true));
            b.Do(() => b.Client.SetReady(true));
        }
    }
}

namespace TheLastKing.Tests
{
    /// <summary>Hai RawClient đã vào cùng một phòng, đã bắt đầu trận và nhận TurnBegin lượt 1.</summary>
    public sealed class RawMatch : IDisposable
    {
        public RawClient A, B;
        public MatchStartMsg SA, SB;
        public TurnBeginMsg TA, TB;
        public string Code;

        public static RawMatch Start(TestServer srv, string na = "A", string nb = "B")
        {
            var m = new RawMatch { A = RawClient.Handshake(srv.Port, na), B = RawClient.Handshake(srv.Port, nb) };
            m.A.Send(new CreateRoomMsg());
            var lobby = m.A.Wait<LobbyStateMsg>();
            m.Code = lobby.RoomCode;
            m.B.Send(new JoinRoomMsg { RoomCode = m.Code });
            m.B.Wait<LobbyStateMsg>();
            m.A.Send(new ReadyMsg { Ready = true }); m.B.Send(new ReadyMsg { Ready = true });
            m.SA = m.A.Wait<MatchStartMsg>(); m.SB = m.B.Wait<MatchStartMsg>();
            m.TA = m.A.Wait<TurnBeginMsg>(); m.TB = m.B.Wait<TurnBeginMsg>();
            if (m.SA == null || m.SB == null || m.TA == null || m.TB == null) throw new InvalidOperationException("RawMatch.Start: không dựng được trận");
            return m;
        }

        public int CardA(int index) { return SA.State.Players[SA.Seat].Hand[index].Id; }
        public int CardB(int index) { return SB.State.Players[SB.Seat].Hand[index].Id; }
        /// <summary>Một lá thuộc về tay của B (để A giả mạo).</summary>
        public int ForeignForA { get { return CardB(0); } }

        public void Dispose() { A.Dispose(); B.Dispose(); }
    }
}
