// THE LAST KING — Online PvP: mô hình phía client. C# thuần (không UnityEngine).
//
// Trách nhiệm: bắt tay + kiểm tra phiên bản luật, trạng thái phòng, một bản "gương" của trận (LastKingMatch) được phát lại
// bằng chính bộ luật của game để dựng TurnResult cho phần trình diễn, đối chiếu băm với máy chủ, nối lại khi đứt mạng,
// loại thông điệp lỗi thời. Client KHÔNG tự quyết định kết quả: thông điệp của máy chủ luôn thắng.
//
// Cách dùng từ luồng chính của Unity:  mỗi frame gọi Update(), rồi lấy sự kiện bằng TryDequeue().
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace TheLastKing.Online
{
    public enum OnlineState { Idle, Connecting, Handshaking, Online, InRoom, InMatch, Reconnecting, Closed }

    public enum OnlineEventKind
    {
        Connected,          // bắt tay xong. Text = lời chào của máy chủ
        ConnectFailed,      // không kết nối/bắt tay được. Text = lý do
        Disconnected,       // mất kết nối vĩnh viễn. Text = lý do
        LinkLost,           // đứt mạng giữa trận, đang thử nối lại. Number = thời gian chờ tối đa (ms)
        LinkRestored,       // đã nối lại; ngay sau đó là Resynced
        ServerError,        // máy chủ từ chối một yêu cầu (không nghiêm trọng). Code + Text
        LobbyChanged,       // client.Lobby đã đổi
        MatchStarted,       // trận mới; client.Mirror là trạng thái đầu
        TurnBegan,          // bắt đầu (hoặc làm mới đồng hồ, Refresh = true) pha chọn bài
        OpponentLocked,
        LockConfirmed,      // máy chủ xác nhận khóa của chính mình
        TurnResolved,       // hai lá đã lật; Result là kết quả phát lại cục bộ (có thể null nếu lệch)
        MatchAborted,       // trận bị hủy (không có người thắng)
        OpponentConnection, // đối thủ mất/khôi phục kết nối. Flag = đang kết nối, Number = thời gian chờ (ms)
        Resynced            // trạng thái trận đã được khôi phục sau khi nối lại. Resync = dữ liệu máy chủ gửi
    }

    public sealed class OnlineEvent
    {
        public OnlineEventKind Kind;
        public string Text = "";
        public ErrorCode Code;
        public AbortReason Reason;
        public int Turn;
        public uint PhaseSeq;
        public bool Refresh;
        public int RemainingMs, TotalMs;
        public int CardId = -1;
        public TurnResult Result;
        public MatchState Snapshot;
        public int TimeoutMask;
        public bool Desync;
        public bool Flag;
        public int Number;
        public ResyncMsg Resync;

        public override string ToString()
        {
            return Kind + (string.IsNullOrEmpty(Text) ? "" : " \"" + Text + "\"") + (Turn > 0 ? " turn=" + Turn : "");
        }
    }

    public sealed class OnlineClient : IDisposable
    {
        readonly Queue<OnlineEvent> events = new Queue<OnlineEvent>();
        readonly Stopwatch clock = Stopwatch.StartNew();
        OnlineTransport transport;

        string host = ""; int port; bool useTls; Func<string, string, bool> certCheck;
        ulong resumeToken; int reconnectGraceMs;
        long handshakeStartedAt, lastPingAt, pingSentAt, reconnectDeadline, nextReconnectAt;
        uint pingToken, seq;
        bool helloSent, ignoreRoomEvents;
        int forcedResyncs;

        // phiên trận
        LastKingMatch mirror;
        uint matchId; int seat; string opponentName = ""; int gameNo; int currentTurn, lastResolvedTurn; uint lastPhaseSeq;

        // ---- cấu hình (đặt trước Connect)
        public string DisplayName = "";
        public string Build = "";
        public int HandshakeTimeoutMs = 5000;
        public int ConnectTimeoutMs = 5000;
        /// <summary>Chờ bao lâu sau khi đứt mạng giữa trận thì thử nối lại lần đầu (các lần sau cách nhau 1 giây).</summary>
        public int ReconnectInitialDelayMs = 300;
        /// <summary>Ghi đè dấu vân tay bộ luật (chỉ để kiểm thử từ chối khác phiên bản).</summary>
        public uint? RulesFingerprintOverride;
        public int? ProtocolOverride;

        // ---- trạng thái chỉ đọc
        public OnlineState State { get; private set; }
        public string LastError { get; private set; }
        public WelcomeMsg Welcome { get; private set; }
        public LobbyStateMsg Lobby { get; private set; }
        public int RoundTripMs { get; private set; }
        public string ServerFingerprint { get { return transport != null ? transport.ServerFingerprint : ""; } }

        public LastKingMatch Mirror { get { return mirror; } }
        public uint MatchId { get { return matchId; } }
        /// <summary>Ghế luật của chính mình (0 = bắt đầu với Vua, 1 = bắt đầu với Nô lệ).</summary>
        public int Seat { get { return seat; } }
        public string OpponentName { get { return opponentName; } }
        public int GameNo { get { return gameNo; } }
        public int CurrentTurn { get { return currentTurn; } }
        public bool IsLive { get { return State == OnlineState.Online || State == OnlineState.InRoom || State == OnlineState.InMatch; } }

        public OnlineClient() { State = OnlineState.Idle; LastError = ""; }

        // ------------------------------------------------------------------ địa chỉ
        /// <summary>Phân tích "host", "host:port", "ip:port", "[v6]:port". Mặc định cổng 7777.</summary>
        public static bool TryParseAddress(string input, out string hostName, out int portNumber)
        {
            hostName = ""; portNumber = OnlineProtocol.DefaultPort;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0) s = s.Substring(scheme + 3);
            int slash = s.IndexOf('/');
            if (slash >= 0) s = s.Substring(0, slash);
            string h;
            if (s.StartsWith("[", StringComparison.Ordinal))
            {
                int close = s.IndexOf(']');
                if (close < 0) return false;
                h = s.Substring(1, close - 1);
                string rest = s.Substring(close + 1);
                if (rest.Length > 0)
                {
                    if (rest[0] != ':' || !int.TryParse(rest.Substring(1), out portNumber)) return false;
                }
            }
            else
            {
                int colon = s.LastIndexOf(':');
                if (colon >= 0 && s.IndexOf(':') == colon)
                {
                    h = s.Substring(0, colon);
                    if (!int.TryParse(s.Substring(colon + 1), out portNumber)) return false;
                }
                else h = s;
            }
            h = h.Trim();
            if (h.Length == 0 || h.Length > 253 || portNumber < 1 || portNumber > 65535) return false;
            hostName = h;
            return true;
        }

        // ------------------------------------------------------------------ vòng đời kết nối
        long Now { get { return clock.ElapsedMilliseconds; } }

        public void Connect(string hostName, int portNumber, bool tls, Func<string, string, bool> certificateCheck)
        {
            if (State != OnlineState.Idle && State != OnlineState.Closed) throw new InvalidOperationException("Client đang hoạt động; gọi Close() trước.");
            host = hostName; port = portNumber; useTls = tls; certCheck = certificateCheck;
            events.Clear();
            ResetSession();
            State = OnlineState.Connecting;
            StartTransport();
        }

        void ResetSession()
        {
            resumeToken = 0; reconnectGraceMs = 0; Welcome = null; Lobby = null; LastError = "";
            helloSent = false; ignoreRoomEvents = false; seq = 0;
            ClearMatch();
        }

        void ClearMatch()
        {
            mirror = null; matchId = 0; seat = 0; opponentName = ""; gameNo = 0; currentTurn = 0; lastResolvedTurn = 0; lastPhaseSeq = 0;
        }

        void StartTransport()
        {
            if (transport != null) transport.Dispose();
            transport = new OnlineTransport { ConnectTimeoutMs = ConnectTimeoutMs };
            transport.Connect(host, port, useTls, certCheck);
            helloSent = false;
            handshakeStartedAt = Now;
        }

        void SendHello()
        {
            var hello = new HelloMsg
            {
                Protocol = ProtocolOverride ?? OnlineProtocol.Version,
                RulesVersion = OnlineProtocol.RulesVersion,
                RulesFingerprint = RulesFingerprintOverride ?? RulesFingerprint.Value,
                Build = Build ?? "",
                Name = DisplayName ?? "",
                ResumeToken = State == OnlineState.Reconnecting ? resumeToken : 0
            };
            transport.Send(hello);
            helloSent = true;
            handshakeStartedAt = Now;
        }

        /// <summary>Đóng chủ động (không phát sự kiện).</summary>
        public void Close()
        {
            if (transport != null) { transport.Dispose(); transport = null; }
            if (State != OnlineState.Idle) State = OnlineState.Closed;
            events.Clear();
        }

        public void Dispose() { Close(); }

        public bool TryDequeue(out OnlineEvent e)
        {
            if (events.Count == 0) { e = null; return false; }
            e = events.Dequeue();
            return true;
        }

        void Emit(OnlineEvent e) { events.Enqueue(e); }
        void Emit(OnlineEventKind kind, string text) { events.Enqueue(new OnlineEvent { Kind = kind, Text = text ?? "" }); }

        // ------------------------------------------------------------------ vòng lặp chính (gọi mỗi frame)
        public void Update()
        {
            long now = Now;
            switch (State)
            {
                case OnlineState.Connecting: UpdateConnecting(now); break;
                case OnlineState.Handshaking:
                case OnlineState.Online:
                case OnlineState.InRoom:
                case OnlineState.InMatch: UpdateLive(now); break;
                case OnlineState.Reconnecting: UpdateReconnecting(now); break;
            }
        }

        void UpdateConnecting(long now)
        {
            OnlineTransport t = transport;
            if (t == null) return;
            if (t.State == TransportState.Connected) { State = OnlineState.Handshaking; SendHello(); }
            else if (t.State == TransportState.Failed || t.State == TransportState.Closed) FailConnect(t.Error);
            else if (now - handshakeStartedAt > ConnectTimeoutMs + HandshakeTimeoutMs) FailConnect("Hết thời gian chờ kết nối tới máy chủ.");
        }

        void UpdateLive(long now)
        {
            OnlineTransport t = transport;
            if (t == null) return;
            if (State != OnlineState.Handshaking && now - lastPingAt >= OnlineProtocol.HeartbeatMs)
            {
                lastPingAt = now; pingSentAt = now;
                t.Send(new PingMsg { Token = ++pingToken });
            }
            ServerMessage m;
            while (transport == t && t.TryReceive(out m)) Handle(m, now);
            if (transport != t || !IsConnectedState(State)) return;
            if (State == OnlineState.Handshaking && now - handshakeStartedAt > HandshakeTimeoutMs)
            {
                FailConnect("Máy chủ không phản hồi lúc bắt tay (có thể không đúng cổng hoặc không bật cùng chế độ TLS).");
                return;
            }
            if (t.State == TransportState.Closed || t.State == TransportState.Failed)
            {
                if (State == OnlineState.Handshaking)
                    FailConnect("Máy chủ đóng kết nối ngay khi bắt tay (có thể bị giới hạn số kết nối, hoặc client/máy chủ khác chế độ TLS).");
                else OnLinkLost(string.IsNullOrEmpty(t.Error) ? "Mất kết nối với máy chủ." : t.Error);
            }
            else if (t.MsSinceLastReceive > OnlineProtocol.DisconnectMs)
                OnLinkLost("Mất kết nối: máy chủ không phản hồi sau " + (OnlineProtocol.DisconnectMs / 1000) + " giây.");
        }

        static bool IsConnectedState(OnlineState s)
        {
            return s == OnlineState.Handshaking || s == OnlineState.Online || s == OnlineState.InRoom || s == OnlineState.InMatch;
        }

        void UpdateReconnecting(long now)
        {
            if (now > reconnectDeadline) { GiveUp("Không nối lại được với máy chủ trong thời gian cho phép."); return; }
            if (transport == null)
            {
                if (now >= nextReconnectAt) StartTransport();
                return;
            }
            OnlineTransport t = transport;
            if (t.State == TransportState.Connected && !helloSent) SendHello();
            ServerMessage m;
            while (transport == t && State == OnlineState.Reconnecting && t.TryReceive(out m)) Handle(m, now);
            if (transport != t || State != OnlineState.Reconnecting) return;
            bool failed = t.State == TransportState.Failed || t.State == TransportState.Closed;
            bool stuck = helloSent && now - handshakeStartedAt > HandshakeTimeoutMs;
            if (failed || stuck)
            {
                t.Dispose(); transport = null;
                nextReconnectAt = now + 1000;
            }
        }

        void OnLinkLost(string text)
        {
            if (transport != null) { transport.Dispose(); transport = null; }
            if (State == OnlineState.InMatch && resumeToken != 0 && reconnectGraceMs > 0)
            {
                State = OnlineState.Reconnecting;
                long now = Now;
                reconnectDeadline = now + reconnectGraceMs;
                nextReconnectAt = now + ReconnectInitialDelayMs;
                helloSent = false;
                Emit(new OnlineEvent { Kind = OnlineEventKind.LinkLost, Text = text, Number = reconnectGraceMs });
            }
            else
            {
                State = OnlineState.Closed; LastError = text;
                Emit(OnlineEventKind.Disconnected, text);
            }
        }

        void ForceResync(string reason)
        {
            if (++forcedResyncs > 3) { GiveUp("Không đồng bộ được dữ liệu trận với máy chủ. Hãy vào lại phòng."); return; }
            OnLinkLost(reason);
        }

        void FailConnect(string text)
        {
            if (transport != null) { transport.Dispose(); transport = null; }
            State = OnlineState.Closed; LastError = text ?? "";
            Emit(OnlineEventKind.ConnectFailed, text);
        }

        void GiveUp(string text)
        {
            if (transport != null) { transport.Dispose(); transport = null; }
            State = OnlineState.Closed; LastError = text ?? "";
            ClearMatch();
            Emit(OnlineEventKind.Disconnected, text);
        }

        // ------------------------------------------------------------------ xử lý thông điệp của máy chủ
        void Handle(ServerMessage m, long now)
        {
            switch (m.Type)
            {
                case MsgType.Welcome: OnWelcome((WelcomeMsg)m); break;
                case MsgType.Error: OnError((ErrorMsg)m); break;
                case MsgType.Pong:
                    if (((PongMsg)m).Token == pingToken) RoundTripMs = (int)Math.Max(0, now - pingSentAt);
                    break;
                case MsgType.LobbyState: OnLobby((LobbyStateMsg)m); break;
                case MsgType.MatchStart: OnMatchStart((MatchStartMsg)m); break;
                case MsgType.TurnBegin: OnTurnBegin((TurnBeginMsg)m); break;
                case MsgType.LockAck: OnLockAck((LockAckMsg)m); break;
                case MsgType.OpponentLocked: OnOpponentLocked((OpponentLockedMsg)m); break;
                case MsgType.TurnResolved: OnTurnResolved((TurnResolvedMsg)m); break;
                case MsgType.MatchAborted: OnMatchAborted((MatchAbortedMsg)m); break;
                case MsgType.OpponentConnection: OnOpponentConnection((OpponentConnectionMsg)m); break;
                case MsgType.Resync: OnResync((ResyncMsg)m); break;
            }
        }

        void OnWelcome(WelcomeMsg m)
        {
            if (State == OnlineState.Handshaking)
            {
                Welcome = m; resumeToken = m.ResumeToken; reconnectGraceMs = m.ReconnectGraceMs;
                State = OnlineState.Online;
                Emit(OnlineEventKind.Connected, m.Banner);
            }
            else if (State == OnlineState.Reconnecting)
            {
                if (m.Resumed)
                {
                    Welcome = m; resumeToken = m.ResumeToken; reconnectGraceMs = m.ReconnectGraceMs;
                    State = OnlineState.InMatch;
                    lastPingAt = Now;
                    Emit(OnlineEventKind.LinkRestored, "");
                }
                else GiveUp("Phiên chơi đã hết hạn nên không nối lại được; trận đã bị hủy.");
            }
        }

        void OnError(ErrorMsg m)
        {
            string text = OnlineText.Describe(m.Code, m.Text);
            if (State == OnlineState.Handshaking) { FailConnect(text); return; }
            if (State == OnlineState.Reconnecting) { GiveUp(text); return; }
            if ((m.Code == ErrorCode.RoomClosed || m.Code == ErrorCode.NotInRoom) && (State == OnlineState.InRoom || State == OnlineState.InMatch))
            {
                // máy chủ nói không còn phòng cho ta: trạng thái cục bộ đã cũ, quay về "đã kết nối, chưa vào phòng"
                State = OnlineState.Online; Lobby = null; ClearMatch();
            }
            Emit(new OnlineEvent { Kind = OnlineEventKind.ServerError, Code = m.Code, Text = text });
        }

        void OnLobby(LobbyStateMsg m)
        {
            if (ignoreRoomEvents) return;
            Lobby = m;
            if (State == OnlineState.Online) State = OnlineState.InRoom;
            else if (State == OnlineState.InMatch && m.Phase == RoomPhase.Lobby) State = OnlineState.InRoom;
            Emit(OnlineEventKind.LobbyChanged, "");
        }

        void OnMatchStart(MatchStartMsg m)
        {
            if (ignoreRoomEvents || (State != OnlineState.InRoom && State != OnlineState.InMatch)) return;
            mirror = new LastKingMatch(m.State);
            matchId = m.MatchId; seat = m.Seat; gameNo = m.GameNo;
            opponentName = CleanOpponent(m.OpponentName);
            currentTurn = 0; lastPhaseSeq = 0; lastResolvedTurn = m.State.TurnIndex; forcedResyncs = 0;
            State = OnlineState.InMatch;
            Emit(new OnlineEvent { Kind = OnlineEventKind.MatchStarted, Snapshot = m.State, Text = opponentName, Number = m.SelectionMs });
        }

        static string CleanOpponent(string raw)
        {
            string s = OnlineProtocol.SanitizeName(raw);
            return s.Length == 0 ? "Đối thủ" : s;
        }

        bool Current(uint id) { return State == OnlineState.InMatch && mirror != null && id == matchId; }

        void OnTurnBegin(TurnBeginMsg m)
        {
            if (!Current(m.MatchId) || m.PhaseSeq < lastPhaseSeq) return;
            if (m.Turn != mirror.State.TurnIndex + 1)
            {
                // Gương cục bộ lệch khỏi máy chủ mà không tự sửa được: ngắt có chủ đích để đi đường nối lại + Resync
                // (máy chủ gửi lại toàn bộ trạng thái công khai). Giới hạn số lần để không lặp vô hạn nếu máy chủ lỗi.
                ForceResync("Dữ liệu trận lệch với máy chủ, đang đồng bộ lại…");
                return;
            }
            bool refresh = m.PhaseSeq == lastPhaseSeq;
            lastPhaseSeq = m.PhaseSeq; currentTurn = m.Turn;
            int half = RoundTripMs / 2;
            Emit(new OnlineEvent
            {
                Kind = OnlineEventKind.TurnBegan, Turn = m.Turn, PhaseSeq = m.PhaseSeq, Refresh = refresh,
                RemainingMs = Math.Max(0, m.RemainingMs - half), TotalMs = m.TotalMs
            });
        }

        void OnLockAck(LockAckMsg m)
        {
            if (!Current(m.MatchId)) return;
            Emit(new OnlineEvent { Kind = OnlineEventKind.LockConfirmed, Turn = m.Turn, CardId = m.CardId });
        }

        void OnOpponentLocked(OpponentLockedMsg m)
        {
            if (!Current(m.MatchId)) return;
            Emit(new OnlineEvent { Kind = OnlineEventKind.OpponentLocked, Turn = m.Turn });
        }

        void OnTurnResolved(TurnResolvedMsg m)
        {
            if (!Current(m.MatchId) || m.PhaseSeq < lastPhaseSeq) return;
            if (m.Turn <= lastResolvedTurn) return;                 // máy chủ gửi trùng / lượt đã xử lý rồi
            lastPhaseSeq = m.PhaseSeq;
            TurnResult result = null;
            bool desync = false;
            if (m.Turn == mirror.State.TurnIndex + 1)
            {
                int i0 = MatchStateCodec.IndexOfCard(mirror.State.Players[0].Hand, m.Id0);
                int i1 = MatchStateCodec.IndexOfCard(mirror.State.Players[1].Hand, m.Id1);
                if (i0 >= 0 && i1 >= 0)
                {
                    try { result = mirror.ResolveTurn(i0, i1); }
                    catch (Exception) { result = null; }
                }
            }
            if (result == null || MatchStateCodec.Hash(mirror.State) != m.StateHash)
            {
                desync = true;
                mirror = new LastKingMatch(m.State);      // máy chủ luôn là chuẩn
            }
            lastResolvedTurn = m.Turn;
            currentTurn = m.Turn;
            Emit(new OnlineEvent
            {
                Kind = OnlineEventKind.TurnResolved, Turn = m.Turn, PhaseSeq = m.PhaseSeq, Result = result,
                Snapshot = m.State, TimeoutMask = m.TimeoutMask, Desync = desync
            });
        }

        void OnMatchAborted(MatchAbortedMsg m)
        {
            if (ignoreRoomEvents) return;
            if (State != OnlineState.InMatch && State != OnlineState.InRoom) return;
            if (matchId != 0 && m.MatchId != matchId) return;
            ClearMatch();
            State = OnlineState.InRoom;
            Emit(new OnlineEvent { Kind = OnlineEventKind.MatchAborted, Reason = m.Reason, Text = OnlineText.Describe(m.Reason) });
        }

        void OnOpponentConnection(OpponentConnectionMsg m)
        {
            if (!Current(m.MatchId)) return;
            Emit(new OnlineEvent { Kind = OnlineEventKind.OpponentConnection, Flag = m.Connected, Number = m.GraceMs });
        }

        void OnResync(ResyncMsg m)
        {
            if (State != OnlineState.InMatch) return;
            mirror = new LastKingMatch(m.State);
            matchId = m.MatchId; seat = m.Seat; gameNo = m.GameNo; opponentName = CleanOpponent(m.OpponentName);
            lastPhaseSeq = m.PhaseSeq;
            lastResolvedTurn = m.State.TurnIndex;
            currentTurn = m.Phase == MatchPhase.Selecting ? m.Turn : m.State.TurnIndex;
            Emit(new OnlineEvent
            {
                Kind = OnlineEventKind.Resynced, Resync = m, Snapshot = m.State, Turn = m.Turn, PhaseSeq = m.PhaseSeq,
                RemainingMs = m.RemainingMs, TotalMs = m.TotalMs
            });
        }

        // ------------------------------------------------------------------ hành động của người chơi
        bool SendLive(ClientMessage msg)
        {
            if (transport == null || !IsConnectedState(State) || State == OnlineState.Handshaking) return false;
            return transport.Send(msg);
        }

        public bool CreateRoom()
        {
            if (!SendLive(new CreateRoomMsg())) return false;
            ignoreRoomEvents = false;
            return true;
        }

        /// <summary>Trả false (kèm sự kiện ServerError/BadRoomCode) nếu mã sai khuôn dạng.</summary>
        public bool JoinRoom(string code)
        {
            code = OnlineProtocol.NormalizeRoomCode(code);
            if (!OnlineProtocol.IsValidRoomCode(code))
            {
                Emit(new OnlineEvent { Kind = OnlineEventKind.ServerError, Code = ErrorCode.BadRoomCode, Text = OnlineText.Describe(ErrorCode.BadRoomCode, "") });
                return false;
            }
            if (!SendLive(new JoinRoomMsg { RoomCode = code })) return false;
            ignoreRoomEvents = false;
            return true;
        }

        public bool SetReady(bool ready) { return SendLive(new ReadyMsg { Ready = ready }); }

        /// <summary>Rời phòng (đang trong trận = bỏ cuộc: trận bị hủy ở phía đối thủ).</summary>
        public bool LeaveRoom()
        {
            bool sent = SendLive(new LeaveRoomMsg());
            if (State == OnlineState.InRoom || State == OnlineState.InMatch)
            {
                State = OnlineState.Online; Lobby = null; ClearMatch();
                ignoreRoomEvents = true;
            }
            return sent;
        }

        /// <summary>Lựa chọn tạm. Gửi nhiều lần được; máy chủ chỉ giữ lá cuối để chốt khi hết giờ.</summary>
        public bool Select(int cardId)
        {
            if (State != OnlineState.InMatch || currentTurn <= 0) return false;
            return SendLive(new SelectMsg { MatchId = matchId, Turn = currentTurn, CardId = cardId, Seq = ++seq });
        }

        /// <summary>Yêu cầu khóa. Chỉ coi là đã khóa khi nhận sự kiện LockConfirmed.</summary>
        public bool Lock(int cardId)
        {
            if (State != OnlineState.InMatch || currentTurn <= 0) return false;
            return SendLive(new LockMsg { MatchId = matchId, Turn = currentTurn, CardId = cardId, Seq = ++seq });
        }

        /// <summary>Báo đã trình diễn xong lượt vừa xử lý (kể cả lượt kết thúc trận).</summary>
        public bool PresentationDone()
        {
            if (State != OnlineState.InMatch || lastResolvedTurn <= 0) return false;
            return SendLive(new PresentationDoneMsg { MatchId = matchId, Turn = lastResolvedTurn });
        }
    }
}
