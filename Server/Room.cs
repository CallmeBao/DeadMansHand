// THE LAST KING — phòng và trận đấu có thẩm quyền (chạy hoàn toàn trên luồng logic của GameServer).
//
// Nguyên tắc:
//  * Bộ luật chạy DUY NHẤT ở đây (LastKingMatch). Client chỉ gửi "tôi chọn/khóa lá có ID n" và nhận kết quả.
//  * Lựa chọn của mỗi lượt là bí mật DUY NHẤT của trò chơi (mọi thứ khác là công khai): nó chỉ rời khỏi máy chủ
//    trong TurnResolved, khi cả hai đã khóa hoặc hết giờ. OpponentLocked không mang lá bài.
//  * Máy chủ là nguồn thời gian. Hết giờ => chốt lá TẠM cuối cùng hợp lệ, nếu chưa có thì bốc đều trong tay.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TheLastKing.Online;

namespace TheLastKing.Server
{
    sealed class MatchRun
    {
        public uint Id;
        public readonly LastKingMatch Rules = new LastKingMatch();
        public readonly int[] SlotOfSeat = new int[2];
        public readonly int[] SeatOfSlot = new int[2];
        public MatchPhase Phase;
        public uint PhaseSeq;
        public readonly int[] Tentative = { -1, -1 };
        public readonly int[] LockedId = { -1, -1 };
        public readonly bool[] Locked = new bool[2];
        public long Deadline, PresentDeadline;
        public readonly bool[] PresentDone = new bool[2];
        public int LastResolvedTurn;
        public bool Suspended;
        public long SuspendedAt, FrozenRemaining;
        public long StartedAt;
        public int GameNo;
        public readonly int[] Timeouts = new int[2];
        public int Turn { get { return Rules.State.TurnIndex + 1; } }
    }

    public sealed class Room
    {
        public readonly string Code;
        readonly GameServer server;
        ServerOptions Opt { get { return server.Options; } }

        public readonly Session[] Slots = new Session[2];
        public readonly bool[] Ready = new bool[2];
        public readonly int[] Wins = new int[2];
        public int Games;
        public RoomPhase Phase = RoomPhase.Lobby;

        MatchRun match;
        long lastActivity;
        int lastSeatOfSlot0 = -1;

        public Room(string code, GameServer server)
        {
            Code = code; this.server = server; lastActivity = server.Now;
        }

        public bool IsEmpty { get { return Slots[0] == null && Slots[1] == null; } }
        void Touch(long now) { lastActivity = now; }
        void Log(string s) { server.Log("[" + Code + "] " + s); }

        // ================================================================== sảnh

        public bool TryJoin(Session s, long now, out ErrorCode err)
        {
            err = ErrorCode.Unknown;
            int slot = Slots[0] == null ? 0 : Slots[1] == null ? 1 : -1;
            if (slot < 0 || Phase != RoomPhase.Lobby || match != null) { err = ErrorCode.RoomFull; return false; }
            Slots[slot] = s; s.Room = this; s.Slot = slot;
            Ready[0] = Ready[1] = false;
            ResetSeries();
            Touch(now);
            BroadcastLobby();
            return true;
        }

        void ResetSeries()
        {
            Wins[0] = Wins[1] = 0; Games = 0; lastSeatOfSlot0 = -1;
        }

        LobbyStateMsg BuildLobby(int forSlot)
        {
            var m = new LobbyStateMsg { RoomCode = Code, MySlot = forSlot, Phase = Phase, Games = Games, Wins = new[] { Wins[0], Wins[1] } };
            for (int i = 0; i < 2; i++)
            {
                Session s = Slots[i];
                m.Slots[i] = s == null ? new LobbySlot() : new LobbySlot { Present = true, Name = s.Name, Ready = Ready[i], Connected = s.Conn != null };
            }
            return m;
        }

        void BroadcastLobby()
        {
            for (int slot = 0; slot < 2; slot++) SendSlot(slot, BuildLobby(slot));
        }

        void SendSlot(int slot, ServerMessage m)
        {
            Session s = Slots[slot];
            if (s != null && s.Conn != null) s.Conn.Send(m);
        }

        void SendSeat(int seat, ServerMessage m) { SendSlot(match.SlotOfSeat[seat], m); }
        void SendBoth(ServerMessage m) { SendSeat(0, m); SendSeat(1, m); }

        public void SetReady(Session s, bool ready, long now)
        {
            Touch(now);
            if (Phase != RoomPhase.Lobby || match != null) return;
            Ready[s.Slot] = ready;
            BroadcastLobby();
            if (Slots[0] != null && Slots[1] != null && Ready[0] && Ready[1]) StartMatch(now);
        }

        /// <summary>Rời phòng. Đang trong trận = hủy trận cho phía còn lại (không ai bị tính thua).</summary>
        public void Leave(Session s, long now, AbortReason reasonIfInMatch)
        {
            int slot = s.Slot;
            if (slot < 0 || Slots[slot] != s) return;
            if (match != null) AbortMatch(reasonIfInMatch, now, s);
            Slots[slot] = null; s.Room = null; s.Slot = -1;
            Ready[0] = Ready[1] = false;
            ResetSeries();
            Touch(now);
            if (IsEmpty) server.RemoveRoom(this); else BroadcastLobby();
        }

        // ================================================================== bắt đầu trận

        void StartMatch(long now)
        {
            int seatOfSlot0 = lastSeatOfSlot0 < 0 ? server.Rng.Next(2) : 1 - lastSeatOfSlot0;   // bốc ngẫu nhiên lần đầu, sau đó đổi ghế
            lastSeatOfSlot0 = seatOfSlot0;
            var r = new MatchRun { Id = server.NextMatchId(), StartedAt = now, GameNo = Games + 1 };
            r.SeatOfSlot[0] = seatOfSlot0; r.SeatOfSlot[1] = 1 - seatOfSlot0;
            r.SlotOfSeat[r.SeatOfSlot[0]] = 0; r.SlotOfSeat[r.SeatOfSlot[1]] = 1;
            match = r;
            Phase = RoomPhase.InMatch;
            Ready[0] = Ready[1] = false;
            server.CountMatchStarted();
            Log("Trận #" + r.Id + " bắt đầu (ván " + r.GameNo + "): " + Slots[0].Name + " ghế " + r.SeatOfSlot[0] + " · " + Slots[1].Name + " ghế " + r.SeatOfSlot[1] + ".");
            for (int seat = 0; seat < 2; seat++)
            {
                Session opp = Slots[1 - r.SlotOfSeat[seat]];
                SendSeat(seat, new MatchStartMsg
                {
                    MatchId = r.Id, Seat = seat, OpponentName = opp.Name, SelectionMs = Opt.SelectionMs,
                    PresentationMs = Opt.PresentationMaxMs, GameNo = r.GameNo, State = r.Rules.State
                });
            }
            BeginSelecting(now);
        }

        // ================================================================== pha chọn bài

        void BeginSelecting(long now)
        {
            MatchRun r = match;
            r.PhaseSeq++;
            r.Phase = MatchPhase.Selecting;
            for (int i = 0; i < 2; i++) { r.Tentative[i] = -1; r.LockedId[i] = -1; r.Locked[i] = false; }
            r.Deadline = now + Opt.SelectionMs;
            SendTurnBegin(now);
        }

        void SendTurnBegin(long now)
        {
            MatchRun r = match;
            var m = new TurnBeginMsg
            {
                MatchId = r.Id, Turn = r.Turn, PhaseSeq = r.PhaseSeq,
                RemainingMs = (int)Math.Max(0, r.Deadline - now), TotalMs = Opt.SelectionMs
            };
            SendBoth(m);
        }

        public void OnSelect(Session s, SelectMsg m, long now)
        {
            Touch(now);
            MatchRun r = match;
            if (r == null || m.MatchId != r.Id) return;                 // thông điệp trễ của trận cũ: bỏ qua
            int seat = r.SeatOfSlot[s.Slot];
            if (r.Phase != MatchPhase.Selecting || m.Turn != r.Turn || r.Locked[seat]) return;
            if (m.Seq != 0 && m.Seq <= s.LastSeq) return;               // cũ hoặc trùng
            if (MatchStateCodec.IndexOfCard(r.Rules.State.Players[seat].Hand, m.CardId) < 0)
            {
                server.Violation(s.Conn, ErrorCode.InvalidAction, "Lá bài này không có trong tay bạn.");
                return;
            }
            if (m.Seq != 0) s.LastSeq = m.Seq;
            r.Tentative[seat] = m.CardId;
        }

        public void OnLock(Session s, LockMsg m, long now)
        {
            Touch(now);
            MatchRun r = match;
            if (r == null || m.MatchId != r.Id)
            {
                s.Conn.SendError(ErrorCode.InvalidState, "Không có trận nào đang diễn ra với mã này.");
                return;
            }
            int seat = r.SeatOfSlot[s.Slot];
            if (r.Phase != MatchPhase.Selecting || m.Turn != r.Turn)
            {
                s.Conn.SendError(ErrorCode.TooLate, OnlineText.Describe(ErrorCode.TooLate, ""));
                return;
            }
            if (r.Locked[seat])
            {
                if (r.LockedId[seat] == m.CardId) s.Conn.Send(new LockAckMsg { MatchId = r.Id, Turn = r.Turn, CardId = m.CardId });   // gửi lại: trả ACK cũ
                else server.Violation(s.Conn, ErrorCode.InvalidAction, "Bạn đã khóa lá khác rồi.");
                return;
            }
            if (!r.Suspended && now > r.Deadline)
            {
                s.Conn.SendError(ErrorCode.TooLate, OnlineText.Describe(ErrorCode.TooLate, ""));
                return;
            }
            if (MatchStateCodec.IndexOfCard(r.Rules.State.Players[seat].Hand, m.CardId) < 0)
            {
                server.Violation(s.Conn, ErrorCode.InvalidAction, "Lá bài này không có trong tay bạn.");
                return;
            }
            if (m.Seq != 0 && m.Seq > s.LastSeq) s.LastSeq = m.Seq;
            r.Locked[seat] = true; r.LockedId[seat] = m.CardId; r.Tentative[seat] = m.CardId;
            s.Conn.Send(new LockAckMsg { MatchId = r.Id, Turn = r.Turn, CardId = m.CardId });
            SendSeat(1 - seat, new OpponentLockedMsg { MatchId = r.Id, Turn = r.Turn });     // CỐ Ý không kèm lá bài
            if (r.Locked[0] && r.Locked[1]) Resolve(now, 0);
        }

        void AutoLockMissing(long now, out int timeoutMask)
        {
            MatchRun r = match;
            timeoutMask = 0;
            for (int seat = 0; seat < 2; seat++)
            {
                if (r.Locked[seat]) continue;
                IReadOnlyList<Card> hand = r.Rules.State.Players[seat].Hand;
                int id = r.Tentative[seat];
                if (id < 0 || MatchStateCodec.IndexOfCard(hand, id) < 0) id = hand[server.Rng.Next(hand.Count)].Id;
                r.LockedId[seat] = id; r.Locked[seat] = true;
                r.Timeouts[seat]++;
                timeoutMask |= 1 << seat;
            }
        }

        // ================================================================== xử lý lượt (nơi DUY NHẤT gọi bộ luật)

        void Resolve(long now, int timeoutMask)
        {
            MatchRun r = match;
            MatchState state = r.Rules.State;
            int i0 = MatchStateCodec.IndexOfCard(state.Players[0].Hand, r.LockedId[0]);
            int i1 = MatchStateCodec.IndexOfCard(state.Players[1].Hand, r.LockedId[1]);
            r.Rules.ResolveTurn(i0, i1);
            MatchState after = r.Rules.State;
            r.LastResolvedTurn = after.TurnIndex;
            r.PhaseSeq++;
            r.Phase = MatchPhase.Presenting;
            r.PresentDone[0] = r.PresentDone[1] = false;
            r.PresentDeadline = now + Opt.PresentationMaxMs;
            SendBoth(new TurnResolvedMsg
            {
                MatchId = r.Id, Turn = r.LastResolvedTurn, PhaseSeq = r.PhaseSeq, Id0 = r.LockedId[0], Id1 = r.LockedId[1],
                TimeoutMask = timeoutMask, StateHash = MatchStateCodec.Hash(after), State = after
            });
        }

        public void OnPresentationDone(Session s, PresentationDoneMsg m, long now)
        {
            Touch(now);
            MatchRun r = match;
            if (r == null || m.MatchId != r.Id || r.Phase != MatchPhase.Presenting || m.Turn != r.LastResolvedTurn) return;
            int seat = r.SeatOfSlot[s.Slot];
            if (r.PresentDone[seat]) return;
            r.PresentDone[seat] = true;
            if (!r.PresentDone[1 - seat]) r.PresentDeadline = Math.Min(r.PresentDeadline, now + Opt.PresentationAckGraceMs);
            if (r.PresentDone[0] && r.PresentDone[1] && !r.Suspended) FinishPresentation(now);
        }

        void FinishPresentation(long now)
        {
            if (match.Rules.State.IsFinished) FinishMatch(now);
            else BeginSelecting(now);
        }

        void FinishMatch(long now)
        {
            MatchRun r = match;
            MatchState st = r.Rules.State;
            if (st.WinnerSeat >= 0)
            {
                int slot = r.SlotOfSeat[st.WinnerSeat];
                Wins[slot] = Math.Min(255, Wins[slot] + 1);
            }
            Games = Math.Min(255, Games + 1);
            server.CountMatchFinished();
            Log("Trận #" + r.Id + " kết thúc: " + (st.IsDraw ? "hòa" : "ghế " + st.WinnerSeat + " thắng") + " (" + st.EndReason + "), " +
                st.TurnIndex + " lượt, xu " + st.Players[0].BloodCoins + "-" + st.Players[1].BloodCoins + ".");
            WriteMatchLog(r, now, "finished");
            match = null;
            Phase = RoomPhase.Lobby;
            Ready[0] = Ready[1] = false;
            Touch(now);
            BroadcastLobby();
        }

        // ================================================================== hủy trận / mất kết nối / nối lại

        /// <summary>Hủy trận: báo cho các phiên còn kết nối (trừ <paramref name="except"/>), gỡ các phiên đã đứt khỏi phòng. Không phát LobbyState.</summary>
        void AbortMatch(AbortReason reason, long now, Session except)
        {
            MatchRun r = match;
            if (r == null) return;
            server.CountMatchAborted();
            Log("Trận #" + r.Id + " bị hủy: " + reason + " (đã chơi " + r.Rules.State.TurnIndex + " lượt).");
            WriteMatchLog(r, now, "aborted:" + reason);
            for (int slot = 0; slot < 2; slot++)
            {
                Session s = Slots[slot];
                if (s == null || s == except) continue;
                if (s.Conn != null) s.Conn.Send(new MatchAbortedMsg { MatchId = r.Id, Reason = reason });
            }
            match = null;
            Phase = RoomPhase.Lobby;
            Ready[0] = Ready[1] = false;
            for (int slot = 0; slot < 2; slot++)
            {
                Session s = Slots[slot];
                if (s != null && s.Conn == null)
                {
                    server.DropSession(s);
                    s.Room = null; s.Slot = -1; Slots[slot] = null;
                    ResetSeries();
                }
            }
            Touch(now);
        }

        public void Abort(AbortReason reason, long now)
        {
            if (match == null) return;
            AbortMatch(reason, now, null);
            BroadcastLobby();
        }

        /// <summary>Socket của <paramref name="s"/> vừa đứt. Trong trận: tạm dừng và giữ chỗ; ở sảnh: rời phòng.</summary>
        public void OnConnectionLost(Session s, long now)
        {
            MatchRun r = match;
            if (r == null)
            {
                Leave(s, now, AbortReason.OpponentDisconnected);
                server.DropSession(s);
                return;
            }
            s.DetachedAtMs = now;
            if (!r.Suspended)
            {
                r.Suspended = true; r.SuspendedAt = now;
                long target = r.Phase == MatchPhase.Selecting ? r.Deadline : r.PresentDeadline;
                r.FrozenRemaining = Math.Max(0, target - now);
                if (r.Phase == MatchPhase.Selecting) r.FrozenRemaining = Math.Max(r.FrozenRemaining, 3000);   // luôn còn tối thiểu 3 giây sau khi nối lại
            }
            Log(s.Name + " mất kết nối giữa trận #" + r.Id + "; tạm dừng chờ tối đa " + Opt.ReconnectGraceMs + " ms.");
            Session other = Slots[1 - s.Slot];
            if (other != null && other.Conn != null)
                other.Conn.Send(new OpponentConnectionMsg { MatchId = r.Id, Connected = false, GraceMs = Opt.ReconnectGraceMs });
        }

        public bool CanResume(Session s)
        {
            return match != null && Phase == RoomPhase.InMatch && s.Room == this && s.Slot >= 0 && Slots[s.Slot] == s;
        }

        bool AllAttached()
        {
            return Slots[0] != null && Slots[0].Conn != null && Slots[1] != null && Slots[1].Conn != null;
        }

        /// <summary>Phiên <paramref name="s"/> đã gắn socket mới (Welcome(Resumed) đã gửi): gửi Resync, báo đối thủ, mở lại đồng hồ khi đủ người.</summary>
        public void OnReattach(Session s, long now, bool wasDetached)
        {
            MatchRun r = match;
            if (r == null) return;
            int seat = r.SeatOfSlot[s.Slot];
            Session other = Slots[1 - s.Slot];
            bool oppConnected = other != null && other.Conn != null;
            long remaining = r.Suspended ? r.FrozenRemaining : Math.Max(0, (r.Phase == MatchPhase.Selecting ? r.Deadline : r.PresentDeadline) - now);
            s.Conn.Send(new ResyncMsg
            {
                MatchId = r.Id, Seat = seat, OpponentName = other != null ? other.Name : "", Phase = r.Phase,
                Turn = r.Phase == MatchPhase.Selecting ? r.Turn : r.LastResolvedTurn, PhaseSeq = r.PhaseSeq,
                RemainingMs = (int)remaining, TotalMs = Opt.SelectionMs,
                IAmLocked = r.Locked[seat], MyLockedId = r.Locked[seat] ? r.LockedId[seat] : 255,
                OpponentLocked = r.Locked[1 - seat], OpponentConnected = oppConnected, GameNo = r.GameNo, State = r.Rules.State
            });
            if (oppConnected) other.Conn.Send(new OpponentConnectionMsg { MatchId = r.Id, Connected = true, GraceMs = 0 });
            if (!wasDetached) return;
            s.DetachedAtMs = 0;
            if (r.Phase == MatchPhase.Presenting) r.PresentDone[seat] = true;     // client nối lại bỏ qua phần trình diễn đã lỡ
            if (r.Suspended && AllAttached()) Resume(now);
        }

        void Resume(long now)
        {
            MatchRun r = match;
            r.Suspended = false;
            Log("Trận #" + r.Id + " tiếp tục.");
            if (r.Phase == MatchPhase.Selecting)
            {
                r.Deadline = now + r.FrozenRemaining;
                SendTurnBegin(now);          // cùng PhaseSeq: client chỉ làm mới đồng hồ
            }
            else
            {
                r.PresentDeadline = now + r.FrozenRemaining;
                if (r.PresentDone[0] && r.PresentDone[1]) FinishPresentation(now);
            }
        }

        // ================================================================== đồng hồ

        /// <summary>Trả true nếu phòng cần được xóa.</summary>
        public bool Tick(long now)
        {
            MatchRun r = match;
            if (r != null)
            {
                if (r.Suspended)
                {
                    for (int slot = 0; slot < 2; slot++)
                    {
                        Session s = Slots[slot];
                        if (s != null && s.Conn == null && now - s.DetachedAtMs > Opt.ReconnectGraceMs)
                        {
                            AbortMatch(AbortReason.OpponentDisconnected, now, null);
                            if (IsEmpty) return true;
                            BroadcastLobby();
                            return false;
                        }
                    }
                    return false;
                }
                if (r.Phase == MatchPhase.Selecting)
                {
                    if (now >= r.Deadline)
                    {
                        int mask;
                        AutoLockMissing(now, out mask);
                        Resolve(now, mask);
                    }
                }
                else if (now >= r.PresentDeadline) FinishPresentation(now);
                return false;
            }
            if (Phase == RoomPhase.Lobby && now - lastActivity > Opt.IdleRoomMs)
            {
                Log("Phòng đóng do không hoạt động.");
                for (int slot = 0; slot < 2; slot++)
                {
                    Session s = Slots[slot];
                    if (s == null) continue;
                    if (s.Conn != null) s.Conn.SendError(ErrorCode.RoomClosed, OnlineText.Describe(ErrorCode.RoomClosed, ""));
                    s.Room = null; s.Slot = -1; Slots[slot] = null;
                }
                return true;
            }
            return IsEmpty;
        }

        // ================================================================== nhật ký trận (CSV)

        void WriteMatchLog(MatchRun r, long now, string outcome)
        {
            string path = Opt.MatchLogPath;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                MatchState st = r.Rules.State;
                bool header = !File.Exists(path);
                var sb = new StringBuilder();
                if (header) sb.AppendLine("utc,match_id,room,game_no,name_seat0,name_seat1,winner_seat,end_reason,turns,coins_seat0,coins_seat1,timeouts_seat0,timeouts_seat1,duration_ms,outcome");
                string n0 = Slots[r.SlotOfSeat[0]] != null ? Slots[r.SlotOfSeat[0]].Name : "";
                string n1 = Slots[r.SlotOfSeat[1]] != null ? Slots[r.SlotOfSeat[1]].Name : "";
                sb.Append(DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)).Append(',')
                  .Append(r.Id).Append(',').Append(Code).Append(',').Append(r.GameNo).Append(',')
                  .Append(Csv(n0)).Append(',').Append(Csv(n1)).Append(',')
                  .Append(st.WinnerSeat).Append(',').Append(st.EndReason).Append(',').Append(st.TurnIndex).Append(',')
                  .Append(st.Players[0].BloodCoins).Append(',').Append(st.Players[1].BloodCoins).Append(',')
                  .Append(r.Timeouts[0]).Append(',').Append(r.Timeouts[1]).Append(',').Append(now - r.StartedAt).Append(',')
                  .Append(outcome).AppendLine();
                File.AppendAllText(path, sb.ToString());
            }
            catch (Exception ex) { Log("Không ghi được nhật ký trận: " + ex.Message); }
        }

        static string Csv(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }
}
