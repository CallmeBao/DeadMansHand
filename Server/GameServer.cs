// THE LAST KING — máy chủ PvP độc lập, có thẩm quyền.
//
// Mô hình luồng: mỗi kết nối có 2 luồng IO (đọc/ghi) chỉ làm việc với socket + giải mã khung. MỌI logic trò chơi
// (phòng, trận, đồng hồ, kiểm tra thao tác) chạy trên MỘT luồng logic duy nhất nên không cần khóa và thứ tự xử lý tất định.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using TheLastKing.Online;

namespace TheLastKing.Server
{
    public sealed class ServerOptions
    {
        public int Port = OnlineProtocol.DefaultPort;
        public string BindAddress = "0.0.0.0";
        public bool UseTls;
        public X509Certificate2 Certificate;

        public int SelectionMs = OnlineProtocol.DefaultSelectionMs;
        /// <summary>Trần thời gian chờ cả hai client trình diễn xong một lượt trước khi máy chủ tự đi tiếp.</summary>
        public int PresentationMaxMs = OnlineProtocol.DefaultPresentationMaxMs;
        /// <summary>Sau khi MỘT bên báo xong, bên kia chỉ còn chừng này thời gian (chống kéo dài lượt).</summary>
        public int PresentationAckGraceMs = 12000;
        public int ReconnectGraceMs = OnlineProtocol.DefaultReconnectGraceMs;
        public int HeartbeatTimeoutMs = OnlineProtocol.DisconnectMs;
        public int HandshakeTimeoutMs = 5000;
        public int IdleRoomMs = 10 * 60 * 1000;

        public int MaxConnections = 256;
        /// <summary>Giới hạn theo IP nguồn. Đặt rộng vì cả lớp/văn phòng thường dùng chung một IP công cộng (NAT).</summary>
        public int MaxConnectionsPerIp = 32;
        public int MaxRooms = 128;
        public int MaxMessagesPerSecond = 60;
        public int MaxViolations = 12;

        public string Banner = "THE LAST KING — máy chủ PvP";
        /// <summary>Nếu đặt: mỗi trận kết thúc/hủy ghi một dòng CSV (dữ liệu cân bằng ghế cho nhóm).</summary>
        public string MatchLogPath;
        /// <summary>Đặt để bốc ghế/lá ngẫu nhiên tái lập được khi kiểm thử.</summary>
        public int? Seed;
        public Action<string> Log = s => { };
    }

    /// <summary>Danh tính người chơi, sống qua nhiều kết nối socket (nối lại giữa trận).</summary>
    public sealed class Session
    {
        public readonly long Id;
        public ulong Token;
        public string Name;
        public Connection Conn;
        public Room Room;
        public int Slot = -1;
        public long DetachedAtMs;
        public uint LastSeq;
        public Session(long id, ulong token, string name) { Id = id; Token = token; Name = name; }
    }

    public sealed class Connection
    {
        public readonly long Id;
        public readonly string Remote;
        public readonly string RemoteIp;
        public Session Session;                 // chỉ luồng logic đọc/ghi
        public long ConnectedAtMs;              // chỉ luồng logic
        public volatile bool Dead;
        public int Violations;                  // chỉ luồng logic

        readonly GameServer server;
        readonly TcpClient tcp;
        Stream stream;
        readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 512);
        long lastRecvMs;
        int windowCount; long windowStart; int ioViolations; bool rateNoticeSent;
        readonly ManualResetEventSlim writerDone = new ManualResetEventSlim(false);
        volatile bool flushBeforeClose;

        public Connection(GameServer server, TcpClient tcp, long id)
        {
            this.server = server; this.tcp = tcp; Id = id;
            EndPoint ep = null;
            try { ep = tcp.Client.RemoteEndPoint; } catch { }
            Remote = ep != null ? ep.ToString() : "?";
            var ip = ep as IPEndPoint;
            RemoteIp = ip != null ? ip.Address.ToString() : "?";
            lastRecvMs = server.Now;
        }

        public long LastRecvMs { get { return Interlocked.Read(ref lastRecvMs); } }

        public void Start()
        {
            new Thread(Run) { IsBackground = true, Name = "lk-conn-" + Id }.Start();
        }

        void Run()
        {
            try
            {
                tcp.NoDelay = true;
                try { tcp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
                Stream s = tcp.GetStream();
                if (server.Options.UseTls)
                {
                    tcp.ReceiveTimeout = server.Options.HandshakeTimeoutMs; tcp.SendTimeout = server.Options.HandshakeTimeoutMs;
                    var ssl = new SslStream(s, false);
                    ssl.AuthenticateAsServer(server.Options.Certificate, false, SslProtocols.Tls12 | SslProtocols.Tls13, false);
                    tcp.ReceiveTimeout = 0; tcp.SendTimeout = 0;
                    s = ssl;
                }
                stream = s;
                new Thread(WriteLoop) { IsBackground = true, Name = "lk-write-" + Id }.Start();
                server.Post(new Inbound { Kind = InboundKind.Connected, Conn = this });
                while (!Dead)
                {
                    byte[] body = OnlineProtocol.ReadFrameBody(stream);
                    if (body == null) break;
                    long now = server.Now;
                    Interlocked.Exchange(ref lastRecvMs, now);
                    if (!AllowFrame(now))
                    {
                        if (++ioViolations >= server.Options.MaxViolations * 4)
                        {
                            server.Log("Kết nối " + Remote + " bị ngắt: gửi quá nhiều.");
                            flushBeforeClose = true;       // để thông báo RateLimited kịp tới client trước khi đóng
                            break;
                        }
                        continue;   // bỏ thông điệp thừa ngay tại luồng IO, không làm phiền luồng logic
                    }
                    var m = (ClientMessage)OnlineProtocol.Decode(body, false);
                    server.Post(new Inbound { Kind = InboundKind.Message, Conn = this, Msg = m });
                }
            }
            catch (ProtocolException ex)
            {
                if (!Dead)
                {
                    server.Log("Kết nối " + Remote + " bị ngắt do khung sai: " + ex.Message);
                    SendError(ErrorCode.BadProtocol, "Thông điệp sai khuôn dạng: " + ex.Message);
                    flushBeforeClose = true;
                }
            }
            catch (Exception ex)
            {
                if (!Dead) server.Log("Kết nối " + Remote + " kết thúc: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                server.Post(new Inbound { Kind = InboundKind.Disconnected, Conn = this });
                if (flushBeforeClose && !Dead)
                {
                    Shutdown();
                    writerDone.Wait(1000);      // cho luồng ghi xả nốt thông báo lỗi
                }
                Close();
            }
        }

        bool AllowFrame(long now)
        {
            if (now - windowStart >= 1000) { windowStart = now; windowCount = 0; rateNoticeSent = false; }
            if (++windowCount <= server.Options.MaxMessagesPerSecond) return true;
            if (!rateNoticeSent)
            {
                rateNoticeSent = true;      // mỗi cửa sổ 1 giây chỉ báo một lần
                Send(new ErrorMsg { Code = ErrorCode.RateLimited, Text = OnlineText.Describe(ErrorCode.RateLimited, "") });
            }
            return false;
        }

        void WriteLoop()
        {
            try
            {
                foreach (byte[] frame in outbox.GetConsumingEnumerable())
                {
                    stream.Write(frame, 0, frame.Length);
                    stream.Flush();
                }
            }
            catch { }
            finally { writerDone.Set(); Close(); }   // hàng đợi đã xả hết (đóng nhẹ nhàng) hoặc ghi lỗi
        }

        public void Send(ServerMessage m)
        {
            if (Dead) return;
            try
            {
                if (!outbox.TryAdd(OnlineProtocol.Encode(m))) { server.Log("Kết nối " + Remote + " bị ngắt: hàng đợi gửi đầy."); Close(); }
            }
            catch (InvalidOperationException) { }
        }

        public void SendError(ErrorCode code, string text) { Send(new ErrorMsg { Code = code, Text = text }); }

        /// <summary>Xả hết thông điệp đang chờ rồi mới đóng (dùng sau khi gửi lỗi nghiêm trọng).</summary>
        public void Shutdown() { try { outbox.CompleteAdding(); } catch { } }

        public void Close()
        {
            Dead = true;
            try { outbox.CompleteAdding(); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { tcp.Close(); } catch { }
        }
    }

    enum InboundKind { Connected, Message, Disconnected, Shutdown }

    sealed class Inbound
    {
        public InboundKind Kind;
        public Connection Conn;
        public ClientMessage Msg;
    }

    public sealed class GameServer
    {
        const string CodeAlphabet = OnlineProtocol.RoomCodeAlphabet;
        const int TickMs = 20;

        public readonly ServerOptions Options;
        readonly Stopwatch clock = Stopwatch.StartNew();
        TcpListener listener;
        Thread acceptThread, loopThread;
        volatile bool running, stopping;
        readonly BlockingCollection<Inbound> inbox = new BlockingCollection<Inbound>(new ConcurrentQueue<Inbound>());
        readonly ManualResetEventSlim shutdownDone = new ManualResetEventSlim(false);

        // trạng thái chỉ luồng logic được đụng vào
        readonly List<Connection> conns = new List<Connection>();
        readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        readonly Dictionary<ulong, Session> sessionsByToken = new Dictionary<ulong, Session>();
        internal readonly Random Rng;
        readonly RandomNumberGenerator crypto = RandomNumberGenerator.Create();
        long matchCounter, sessionCounter, connCounter;

        readonly Dictionary<string, int> perIp = new Dictionary<string, int>();

        // thống kê (đọc được từ mọi luồng)
        long matchesStarted, matchesFinished, matchesAborted, resumes, messagesHandled;
        volatile int connectionCount, roomCount;

        public int Port { get; private set; }
        public int ConnectionCount { get { return connectionCount; } }
        public int RoomCount { get { return roomCount; } }
        public long MatchesStarted { get { return Interlocked.Read(ref matchesStarted); } }
        public long MatchesFinished { get { return Interlocked.Read(ref matchesFinished); } }
        public long MatchesAborted { get { return Interlocked.Read(ref matchesAborted); } }
        public long Resumes { get { return Interlocked.Read(ref resumes); } }
        public long MessagesHandled { get { return Interlocked.Read(ref messagesHandled); } }

        internal void CountMatchStarted() { Interlocked.Increment(ref matchesStarted); }
        internal void CountMatchFinished() { Interlocked.Increment(ref matchesFinished); }
        internal void CountMatchAborted() { Interlocked.Increment(ref matchesAborted); }

        public GameServer(ServerOptions options)
        {
            Options = options;
            Rng = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random(NextInt(crypto));
        }

        static int NextInt(RandomNumberGenerator g)
        {
            var b = new byte[4];
            g.GetBytes(b);
            return BitConverter.ToInt32(b, 0);
        }

        internal long Now { get { return clock.ElapsedMilliseconds; } }
        internal void Log(string s) { Options.Log(s); }
        internal void Post(Inbound i) { try { inbox.Add(i); } catch (InvalidOperationException) { } }

        public void Start()
        {
            if (Options.UseTls && Options.Certificate == null) throw new InvalidOperationException("Bật TLS nhưng chưa có chứng chỉ.");
            IPAddress bind = Options.BindAddress == "0.0.0.0" || string.IsNullOrEmpty(Options.BindAddress) ? IPAddress.Any : IPAddress.Parse(Options.BindAddress);
            listener = new TcpListener(bind, Options.Port);
            listener.Start(128);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            running = true;
            loopThread = new Thread(MainLoop) { IsBackground = true, Name = "lk-logic" };
            loopThread.Start();
            acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "lk-accept" };
            acceptThread.Start();
            Log("Máy chủ lắng nghe tại " + bind + ":" + Port + (Options.UseTls ? " (TLS)" : " (không mã hóa)") +
                " · chọn bài " + Options.SelectionMs / 1000 + "s · bộ luật " + OnlineProtocol.RulesVersion + "/" + RulesFingerprint.Value.ToString("x8"));
        }

        /// <summary>Tắt có trật tự: hủy các trận đang chơi (báo ServerShutdown), xả thông điệp, đóng kết nối.</summary>
        public void Stop()
        {
            if (!running) return;
            stopping = true;
            Post(new Inbound { Kind = InboundKind.Shutdown });
            shutdownDone.Wait(3000);
            running = false;
            try { listener.Stop(); } catch { }
            try { inbox.CompleteAdding(); } catch { }
            if (loopThread != null) loopThread.Join(2000);
            if (acceptThread != null) acceptThread.Join(1000);
        }

        // ------------------------------------------------------------------ nhận kết nối
        void AcceptLoop()
        {
            while (running)
            {
                TcpClient tcp;
                try { tcp = listener.AcceptTcpClient(); }
                catch { break; }
                try
                {
                    string ip = "?";
                    try { ip = ((IPEndPoint)tcp.Client.RemoteEndPoint).Address.ToString(); } catch { }
                    bool allow;
                    lock (perIp)
                    {
                        int n; perIp.TryGetValue(ip, out n);
                        allow = n < Options.MaxConnectionsPerIp && connectionCount < Options.MaxConnections * 2;
                        if (allow) perIp[ip] = n + 1;
                    }
                    if (!allow) { Log("Từ chối kết nối " + ip + ": vượt giới hạn."); tcp.Close(); continue; }
                    var c = new Connection(this, tcp, Interlocked.Increment(ref connCounter));
                    c.Start();
                }
                catch (Exception ex) { Log("Lỗi nhận kết nối: " + ex.Message); try { tcp.Close(); } catch { } }
            }
        }

        void ReleaseIp(Connection c)
        {
            lock (perIp)
            {
                int n;
                if (perIp.TryGetValue(c.RemoteIp, out n)) { if (n <= 1) perIp.Remove(c.RemoteIp); else perIp[c.RemoteIp] = n - 1; }
            }
        }

        // ------------------------------------------------------------------ vòng lặp logic
        void MainLoop()
        {
            long lastTick = Now;
            while (running)
            {
                Inbound item;
                try
                {
                    if (inbox.TryTake(out item, TickMs))
                    {
                        int budget = 512;
                        do { SafeDispatch(item); } while (--budget > 0 && inbox.TryTake(out item));
                    }
                }
                catch (InvalidOperationException) { break; }
                long now = Now;
                if (now - lastTick >= TickMs)
                {
                    lastTick = now;
                    try { Tick(now); }
                    catch (Exception ex) { Log("LỖI tick: " + ex); }
                }
            }
        }

        void SafeDispatch(Inbound i)
        {
            try { Dispatch(i); }
            catch (Exception ex) { Log("LỖI xử lý " + i.Kind + ": " + ex); if (i.Conn != null) i.Conn.Close(); }
        }

        void Dispatch(Inbound i)
        {
            long now = Now;
            switch (i.Kind)
            {
                case InboundKind.Connected:
                    i.Conn.ConnectedAtMs = now;
                    conns.Add(i.Conn);
                    break;
                case InboundKind.Disconnected:
                    OnDisconnected(i.Conn, now);
                    break;
                case InboundKind.Message:
                    Interlocked.Increment(ref messagesHandled);
                    HandleMessage(i.Conn, i.Msg, now);
                    break;
                case InboundKind.Shutdown:
                    DoShutdown(now);
                    break;
            }
        }

        void Tick(long now)
        {
            // dọn kết nối chết / hết hạn bắt tay / mất nhịp tim
            for (int k = conns.Count - 1; k >= 0; k--)
            {
                Connection c = conns[k];
                if (c.Dead) continue;
                bool noHello = c.Session == null && now - c.ConnectedAtMs > Options.HandshakeTimeoutMs;
                bool silent = now - c.LastRecvMs > Options.HeartbeatTimeoutMs;
                if (noHello || silent)
                {
                    if (silent) Log("Ngắt " + c.Remote + ": không có tín hiệu quá " + Options.HeartbeatTimeoutMs + " ms.");
                    c.Close();
                }
            }
            var closed = new List<Room>();
            foreach (Room room in rooms.Values)
                if (room.Tick(now)) closed.Add(room);
            foreach (Room room in closed) RemoveRoom(room);
            connectionCount = conns.Count;
            roomCount = rooms.Count;
        }

        void DoShutdown(long now)
        {
            Log("Đang tắt máy chủ…");
            foreach (Room room in new List<Room>(rooms.Values)) room.Abort(AbortReason.ServerShutdown, now);
            foreach (Connection c in conns)
            {
                c.SendError(ErrorCode.ShuttingDown, "Máy chủ đang tắt.");
                c.Shutdown();
            }
            shutdownDone.Set();
        }

        // ------------------------------------------------------------------ kết nối & phiên
        void OnDisconnected(Connection c, long now)
        {
            conns.Remove(c);
            ReleaseIp(c);
            connectionCount = conns.Count;
            Session s = c.Session;
            if (s == null || s.Conn != c) return;       // chưa bắt tay, hoặc đã bị phiên mới chiếm chỗ
            s.Conn = null;
            if (s.Room != null) s.Room.OnConnectionLost(s, now);
            else DropSession(s);
        }

        internal void DropSession(Session s)
        {
            Session cur;
            if (sessionsByToken.TryGetValue(s.Token, out cur) && cur == s) sessionsByToken.Remove(s.Token);
        }

        internal void RemoveRoom(Room room)
        {
            Room cur;
            if (rooms.TryGetValue(room.Code, out cur) && cur == room) rooms.Remove(room.Code);
            roomCount = rooms.Count;
        }

        internal ulong NewToken()
        {
            var b = new byte[8];
            ulong v;
            do { crypto.GetBytes(b); v = BitConverter.ToUInt64(b, 0); } while (v == 0 || sessionsByToken.ContainsKey(v));
            return v;
        }

        string NewRoomCode()
        {
            var b = new byte[OnlineProtocol.RoomCodeLength];
            string code;
            do
            {
                crypto.GetBytes(b);
                var chars = new char[b.Length];
                for (int i = 0; i < b.Length; i++) chars[i] = CodeAlphabet[b[i] % CodeAlphabet.Length];
                code = new string(chars);
            } while (rooms.ContainsKey(code));
            return code;
        }

        internal uint NextMatchId() { return (uint)Interlocked.Increment(ref matchCounter); }

        /// <summary>Ghi nhận một thao tác sai. Quá <see cref="ServerOptions.MaxViolations"/> thì ngắt kết nối.</summary>
        internal void Violation(Connection c, ErrorCode code, string text)
        {
            if (c == null || c.Dead) return;
            c.SendError(code, text);
            if (++c.Violations >= Options.MaxViolations)
            {
                Log("Ngắt " + c.Remote + ": quá nhiều thao tác sai.");
                c.Shutdown();
            }
        }

        // ------------------------------------------------------------------ xử lý thông điệp
        void HandleMessage(Connection c, ClientMessage msg, long now)
        {
            if (c.Dead) return;
            if (c.Session == null)
            {
                var hello = msg as HelloMsg;
                if (hello != null) OnHello(c, hello, now);
                else { c.SendError(ErrorCode.HandshakeRequired, "Cần bắt tay (Hello) trước."); c.Shutdown(); }
                return;
            }
            Session s = c.Session;
            if (s.Conn != c) return;
            switch (msg.Type)
            {
                case MsgType.Hello:
                    Violation(c, ErrorCode.InvalidState, "Đã bắt tay rồi.");
                    break;
                case MsgType.Ping:
                    c.Send(new PongMsg { Token = ((PingMsg)msg).Token });
                    break;
                case MsgType.CreateRoom:
                    if (s.Room != null) { c.SendError(ErrorCode.AlreadyInRoom, OnlineText.Describe(ErrorCode.AlreadyInRoom, "")); break; }
                    if (rooms.Count >= Options.MaxRooms) { c.SendError(ErrorCode.TooManyRooms, OnlineText.Describe(ErrorCode.TooManyRooms, "")); break; }
                    {
                        var room = new Room(NewRoomCode(), this);
                        rooms[room.Code] = room;
                        roomCount = rooms.Count;
                        ErrorCode err;
                        room.TryJoin(s, now, out err);
                        Log("Phòng " + room.Code + " được tạo bởi " + s.Name + " (" + c.Remote + ").");
                    }
                    break;
                case MsgType.JoinRoom:
                    OnJoin(c, s, (JoinRoomMsg)msg, now);
                    break;
                case MsgType.LeaveRoom:
                    if (s.Room != null) s.Room.Leave(s, now, AbortReason.OpponentLeft);
                    break;
                case MsgType.Ready:
                    if (s.Room == null) c.SendError(ErrorCode.NotInRoom, OnlineText.Describe(ErrorCode.NotInRoom, ""));
                    else s.Room.SetReady(s, ((ReadyMsg)msg).Ready, now);
                    break;
                case MsgType.Select:
                    if (s.Room == null) c.SendError(ErrorCode.NotInRoom, OnlineText.Describe(ErrorCode.NotInRoom, ""));
                    else s.Room.OnSelect(s, (SelectMsg)msg, now);
                    break;
                case MsgType.Lock:
                    if (s.Room == null) c.SendError(ErrorCode.NotInRoom, OnlineText.Describe(ErrorCode.NotInRoom, ""));
                    else s.Room.OnLock(s, (LockMsg)msg, now);
                    break;
                case MsgType.PresentationDone:
                    if (s.Room != null) s.Room.OnPresentationDone(s, (PresentationDoneMsg)msg, now);
                    break;
            }
        }

        void OnJoin(Connection c, Session s, JoinRoomMsg m, long now)
        {
            if (s.Room != null) { c.SendError(ErrorCode.AlreadyInRoom, OnlineText.Describe(ErrorCode.AlreadyInRoom, "")); return; }
            string code = OnlineProtocol.NormalizeRoomCode(m.RoomCode);
            if (!OnlineProtocol.IsValidRoomCode(code)) { Violation(c, ErrorCode.BadRoomCode, OnlineText.Describe(ErrorCode.BadRoomCode, "")); return; }
            Room room;
            if (!rooms.TryGetValue(code, out room)) { Violation(c, ErrorCode.RoomNotFound, OnlineText.Describe(ErrorCode.RoomNotFound, "")); return; }
            ErrorCode err;
            if (!room.TryJoin(s, now, out err)) { c.SendError(err, OnlineText.Describe(err, "")); return; }
            Log(s.Name + " vào phòng " + room.Code + ".");
        }

        void OnHello(Connection c, HelloMsg h, long now)
        {
            if (stopping) { Fail(c, ErrorCode.ShuttingDown, OnlineText.Describe(ErrorCode.ShuttingDown, "")); return; }
            if (h.Protocol != OnlineProtocol.Version)
            {
                Fail(c, ErrorCode.VersionMismatch, "Khác phiên bản giao thức (máy chủ " + OnlineProtocol.Version + ", client " + h.Protocol + ").");
                return;
            }
            if (h.RulesVersion != OnlineProtocol.RulesVersion || h.RulesFingerprint != RulesFingerprint.Value)
            {
                Fail(c, ErrorCode.RulesMismatch, "Khác phiên bản bộ luật (máy chủ " + OnlineProtocol.RulesVersion + "/" +
                    RulesFingerprint.Value.ToString("x8") + ", client " + Truncate(h.RulesVersion) + "/" + h.RulesFingerprint.ToString("x8") + ").");
                return;
            }

            if (h.ResumeToken != 0)
            {
                Session old;
                if (sessionsByToken.TryGetValue(h.ResumeToken, out old) && old.Room != null && old.Room.CanResume(old))
                {
                    bool wasDetached = old.Conn == null;
                    if (!wasDetached)
                    {
                        // Nửa đứt: server chưa nhận ra kết nối cũ đã chết. Người giữ token hợp lệ được quyền chiếm chỗ.
                        Connection stale = old.Conn;
                        old.Conn = null; stale.Session = null; stale.Close();
                    }
                    sessionsByToken.Remove(old.Token);
                    old.Token = NewToken();
                    sessionsByToken[old.Token] = old;
                    old.Conn = c; c.Session = old;
                    Interlocked.Increment(ref resumes);
                    c.Send(MakeWelcome(old, true));
                    Log(old.Name + " nối lại phòng " + old.Room.Code + ".");
                    old.Room.OnReattach(old, now, wasDetached);
                    return;
                }
            }

            if (sessionsByToken.Count >= Options.MaxConnections) { Fail(c, ErrorCode.ServerFull, OnlineText.Describe(ErrorCode.ServerFull, "")); return; }
            string name = OnlineProtocol.SanitizeName(h.Name);
            if (name.Length == 0) name = "Người chơi " + Rng.Next(1000, 10000);
            var s = new Session(Interlocked.Increment(ref sessionCounter), NewToken(), name);
            s.Conn = c; c.Session = s;
            sessionsByToken[s.Token] = s;
            c.Send(MakeWelcome(s, false));
        }

        static string Truncate(string s) { return s != null && s.Length > 16 ? s.Substring(0, 16) : s; }

        WelcomeMsg MakeWelcome(Session s, bool resumed)
        {
            return new WelcomeMsg
            {
                SessionId = (ulong)s.Id, ResumeToken = s.Token, Resumed = resumed,
                SelectionMs = Options.SelectionMs, ReconnectGraceMs = Options.ReconnectGraceMs, Banner = Options.Banner
            };
        }

        void Fail(Connection c, ErrorCode code, string text)
        {
            c.SendError(code, text);
            c.Shutdown();
        }
    }
}
