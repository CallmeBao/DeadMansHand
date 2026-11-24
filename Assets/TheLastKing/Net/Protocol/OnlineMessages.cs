// THE LAST KING — Online PvP: định nghĩa thông điệp. C# thuần, dùng chung client/server/test.
//
// Quy ước số lượt: "Turn" luôn là số lượt ĐANG chơi/vừa xử lý, tính từ 1 (khớp nhãn "LƯỢT n" trên HUD)
// = MatchState.TurnIndex + 1 khi đang chọn bài, = MatchState.TurnIndex ngay sau khi xử lý.
// Quy ước ghế: "Seat" là ghế luật của máy chủ (0 = bắt đầu với Vua, 1 = bắt đầu với Nô lệ).
// "Slot" là chỗ ngồi trong phòng (ổn định giữa các trận); ghế luật của mỗi slot được máy chủ bốc ngẫu nhiên mỗi trận.
using System;

namespace TheLastKing.Online
{
    public enum MsgType : byte
    {
        // client -> server
        Hello = 1, CreateRoom = 2, JoinRoom = 3, LeaveRoom = 4, Ready = 5,
        Select = 6, Lock = 7, PresentationDone = 8, Ping = 9,
        // server -> client (>= 64)
        Welcome = 64, Error = 65, LobbyState = 66, MatchStart = 67, TurnBegin = 68, LockAck = 69,
        OpponentLocked = 70, TurnResolved = 71, MatchAborted = 72, OpponentConnection = 73, Resync = 74, Pong = 75
    }

    public enum ErrorCode : byte
    {
        Unknown = 0, BadProtocol = 1, VersionMismatch = 2, RulesMismatch = 3, ServerFull = 4,
        RoomNotFound = 5, RoomFull = 6, BadRoomCode = 7, NotInRoom = 8, AlreadyInRoom = 9,
        InvalidState = 10, InvalidAction = 11, TooLate = 12, RateLimited = 13, ShuttingDown = 14,
        HandshakeRequired = 15, TooManyRooms = 16, RoomClosed = 17
    }

    public enum AbortReason : byte
    {
        None = 0, OpponentLeft = 1, OpponentDisconnected = 2, ServerShutdown = 3, ProtocolViolation = 4, Internal = 5
    }

    public enum RoomPhase : byte { Lobby = 0, InMatch = 1 }
    public enum MatchPhase : byte { Selecting = 0, Presenting = 1 }

    public abstract class Message
    {
        public abstract MsgType Type { get; }
        public abstract void Write(BinWriter w);
    }
    public abstract class ClientMessage : Message { }
    public abstract class ServerMessage : Message { }

    // ===================================================================== client -> server

    public sealed class HelloMsg : ClientMessage
    {
        public int Protocol = OnlineProtocol.Version;
        public string RulesVersion = OnlineProtocol.RulesVersion;
        public uint RulesFingerprint;
        public string Build = "";
        public string Name = "";
        /// <summary>0 = kết nối mới; khác 0 = xin nối lại phiên đang giữ chỗ trong một trận.</summary>
        public ulong ResumeToken;
        public override MsgType Type { get { return MsgType.Hello; } }
        public override void Write(BinWriter w)
        {
            w.U16(Protocol); w.Str(RulesVersion); w.U32(RulesFingerprint); w.Str(Build); w.Str(Name); w.U64(ResumeToken);
        }
        public static HelloMsg Read(BinReader r)
        {
            var m = new HelloMsg();
            m.Protocol = r.U16(); m.RulesVersion = r.Str(); m.RulesFingerprint = r.U32();
            m.Build = r.Str(); m.Name = r.Str(); m.ResumeToken = r.U64();
            return m;
        }
    }

    public sealed class CreateRoomMsg : ClientMessage
    {
        public override MsgType Type { get { return MsgType.CreateRoom; } }
        public override void Write(BinWriter w) { }
    }

    public sealed class JoinRoomMsg : ClientMessage
    {
        public string RoomCode = "";
        public override MsgType Type { get { return MsgType.JoinRoom; } }
        public override void Write(BinWriter w) { w.Str(RoomCode); }
        public static JoinRoomMsg Read(BinReader r) { return new JoinRoomMsg { RoomCode = r.Str() }; }
    }

    public sealed class LeaveRoomMsg : ClientMessage
    {
        public override MsgType Type { get { return MsgType.LeaveRoom; } }
        public override void Write(BinWriter w) { }
    }

    public sealed class ReadyMsg : ClientMessage
    {
        public bool Ready;
        public override MsgType Type { get { return MsgType.Ready; } }
        public override void Write(BinWriter w) { w.Bool(Ready); }
        public static ReadyMsg Read(BinReader r) { return new ReadyMsg { Ready = r.Bool() }; }
    }

    /// <summary>Lựa chọn TẠM (chưa khóa). Chỉ để máy chủ có lá hợp lệ để chốt khi hết giờ; không bao giờ chuyển cho đối thủ.</summary>
    public sealed class SelectMsg : ClientMessage
    {
        public uint MatchId; public int Turn; public int CardId; public uint Seq;
        public override MsgType Type { get { return MsgType.Select; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.U8(Turn); w.U8(CardId); w.U32(Seq); }
        public static SelectMsg Read(BinReader r)
        {
            return new SelectMsg { MatchId = r.U32(), Turn = r.U8(), CardId = r.U8(), Seq = r.U32() };
        }
    }

    /// <summary>Khóa lá đã chọn. Giao diện chỉ coi là đã khóa khi nhận <see cref="LockAckMsg"/>.</summary>
    public sealed class LockMsg : ClientMessage
    {
        public uint MatchId; public int Turn; public int CardId; public uint Seq;
        public override MsgType Type { get { return MsgType.Lock; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.U8(Turn); w.U8(CardId); w.U32(Seq); }
        public static LockMsg Read(BinReader r)
        {
            return new LockMsg { MatchId = r.U32(), Turn = r.U8(), CardId = r.U8(), Seq = r.U32() };
        }
    }

    /// <summary>Client báo đã trình diễn xong lượt <see cref="Turn"/> (lật bài, đặt xu, kết thúc trận). Máy chủ chờ cả hai hoặc fallback.</summary>
    public sealed class PresentationDoneMsg : ClientMessage
    {
        public uint MatchId; public int Turn;
        public override MsgType Type { get { return MsgType.PresentationDone; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.U8(Turn); }
        public static PresentationDoneMsg Read(BinReader r) { return new PresentationDoneMsg { MatchId = r.U32(), Turn = r.U8() }; }
    }

    public sealed class PingMsg : ClientMessage
    {
        public uint Token;
        public override MsgType Type { get { return MsgType.Ping; } }
        public override void Write(BinWriter w) { w.U32(Token); }
        public static PingMsg Read(BinReader r) { return new PingMsg { Token = r.U32() }; }
    }

    // ===================================================================== server -> client

    public sealed class WelcomeMsg : ServerMessage
    {
        public int Protocol = OnlineProtocol.Version;
        public ulong SessionId;
        /// <summary>Bí mật để nối lại. Chỉ gửi cho đúng client này; không bao giờ phát cho đối thủ.</summary>
        public ulong ResumeToken;
        public bool Resumed;
        public int SelectionMs;
        public int ReconnectGraceMs;
        public string Banner = "";
        public override MsgType Type { get { return MsgType.Welcome; } }
        public override void Write(BinWriter w)
        {
            w.U16(Protocol); w.U64(SessionId); w.U64(ResumeToken); w.Bool(Resumed);
            w.U32((uint)SelectionMs); w.U32((uint)ReconnectGraceMs); w.Str(Banner);
        }
        public static WelcomeMsg Read(BinReader r)
        {
            var m = new WelcomeMsg();
            m.Protocol = r.U16(); m.SessionId = r.U64(); m.ResumeToken = r.U64(); m.Resumed = r.Bool();
            m.SelectionMs = Wire.Ms(r.U32()); m.ReconnectGraceMs = Wire.Ms(r.U32()); m.Banner = r.Str();
            return m;
        }
    }

    public sealed class ErrorMsg : ServerMessage
    {
        public ErrorCode Code; public string Text = "";
        public override MsgType Type { get { return MsgType.Error; } }
        public override void Write(BinWriter w) { w.U8((int)Code); w.Str(Text); }
        public static ErrorMsg Read(BinReader r)
        {
            int c = r.U8();
            if (!Enum.IsDefined(typeof(ErrorCode), (byte)c)) c = (int)ErrorCode.Unknown;
            return new ErrorMsg { Code = (ErrorCode)c, Text = r.Str() };
        }
    }

    public sealed class LobbySlot
    {
        public bool Present; public string Name = ""; public bool Ready; public bool Connected = true;
        public void Write(BinWriter w) { w.Bool(Present); w.Str(Name); w.Bool(Ready); w.Bool(Connected); }
        public static LobbySlot Read(BinReader r)
        {
            return new LobbySlot { Present = r.Bool(), Name = r.Str(), Ready = r.Bool(), Connected = r.Bool() };
        }
    }

    /// <summary>Trạng thái phòng. Nhận được lần đầu sau CreateRoom/JoinRoom nghĩa là "đã vào phòng".</summary>
    public sealed class LobbyStateMsg : ServerMessage
    {
        public string RoomCode = "";
        public int MySlot;
        public RoomPhase Phase;
        public LobbySlot[] Slots = { new LobbySlot(), new LobbySlot() };
        /// <summary>Số trận thắng của từng slot kể từ khi phòng có đủ hai người này.</summary>
        public int[] Wins = new int[2];
        public int Games;
        public override MsgType Type { get { return MsgType.LobbyState; } }
        public override void Write(BinWriter w)
        {
            w.Str(RoomCode); w.U8(MySlot); w.U8((int)Phase);
            Slots[0].Write(w); Slots[1].Write(w);
            w.U8(Wins[0]); w.U8(Wins[1]); w.U8(Games);
        }
        public static LobbyStateMsg Read(BinReader r)
        {
            var m = new LobbyStateMsg();
            m.RoomCode = r.Str();
            if (!OnlineProtocol.IsValidRoomCode(m.RoomCode)) throw new ProtocolException("Mã phòng không hợp lệ.");
            m.MySlot = r.U8(); if (m.MySlot > 1) throw new ProtocolException("Slot không hợp lệ.");
            int ph = r.U8(); if (ph > 1) throw new ProtocolException("Pha phòng không hợp lệ.");
            m.Phase = (RoomPhase)ph;
            m.Slots = new[] { LobbySlot.Read(r), LobbySlot.Read(r) };
            m.Wins = new[] { r.U8(), r.U8() };
            m.Games = r.U8();
            return m;
        }
    }

    public sealed class MatchStartMsg : ServerMessage
    {
        public uint MatchId; public int Seat; public string OpponentName = "";
        public int SelectionMs; public int PresentationMs;
        /// <summary>Ván thứ mấy trong chuỗi của phòng (1, 2, …).</summary>
        public int GameNo;
        public MatchState State;
        public override MsgType Type { get { return MsgType.MatchStart; } }
        public override void Write(BinWriter w)
        {
            w.U32(MatchId); w.U8(Seat); w.Str(OpponentName); w.U32((uint)SelectionMs); w.U32((uint)PresentationMs);
            w.U8(GameNo); MatchStateCodec.Write(w, State);
        }
        public static MatchStartMsg Read(BinReader r)
        {
            var m = new MatchStartMsg();
            m.MatchId = r.U32(); m.Seat = r.U8(); if (m.Seat > 1) throw new ProtocolException("Ghế không hợp lệ.");
            m.OpponentName = r.Str(); m.SelectionMs = Wire.Ms(r.U32()); m.PresentationMs = Wire.Ms(r.U32());
            m.GameNo = r.U8(); m.State = MatchStateCodec.Read(r);
            return m;
        }
    }

    /// <summary>Bắt đầu (hoặc làm mới đồng hồ của) pha chọn bài. Cùng PhaseSeq được gửi lại sau khi nối lại: client chỉ cập nhật đồng hồ.</summary>
    public sealed class TurnBeginMsg : ServerMessage
    {
        public uint MatchId; public int Turn; public uint PhaseSeq; public int RemainingMs; public int TotalMs;
        public override MsgType Type { get { return MsgType.TurnBegin; } }
        public override void Write(BinWriter w)
        {
            w.U32(MatchId); w.U8(Turn); w.U32(PhaseSeq); w.U32((uint)RemainingMs); w.U32((uint)TotalMs);
        }
        public static TurnBeginMsg Read(BinReader r)
        {
            return new TurnBeginMsg
            {
                MatchId = r.U32(), Turn = r.U8(), PhaseSeq = r.U32(),
                RemainingMs = Wire.Ms(r.U32()), TotalMs = Wire.Ms(r.U32())
            };
        }
    }

    /// <summary>Xác nhận khóa — chỉ gửi cho người vừa khóa.</summary>
    public sealed class LockAckMsg : ServerMessage
    {
        public uint MatchId; public int Turn; public int CardId;
        public override MsgType Type { get { return MsgType.LockAck; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.U8(Turn); w.U8(CardId); }
        public static LockAckMsg Read(BinReader r) { return new LockAckMsg { MatchId = r.U32(), Turn = r.U8(), CardId = r.U8() }; }
    }

    /// <summary>Đối thủ đã khóa. CỐ Ý không mang theo lá bài hay vị trí.</summary>
    public sealed class OpponentLockedMsg : ServerMessage
    {
        public uint MatchId; public int Turn;
        public override MsgType Type { get { return MsgType.OpponentLocked; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.U8(Turn); }
        public static OpponentLockedMsg Read(BinReader r) { return new OpponentLockedMsg { MatchId = r.U32(), Turn = r.U8() }; }
    }

    /// <summary>
    /// Công bố cả hai lá cùng lúc + trạng thái công khai sau lượt. Client phát lại lượt bằng bộ luật cục bộ để dựng hiệu ứng,
    /// rồi so <see cref="StateHash"/>; lệch thì lấy <see cref="State"/> của máy chủ làm chuẩn.
    /// </summary>
    public sealed class TurnResolvedMsg : ServerMessage
    {
        public uint MatchId; public int Turn; public uint PhaseSeq;
        public int Id0, Id1;
        /// <summary>Bit 0 = ghế 0 bị máy chủ chốt do hết giờ; bit 1 = ghế 1.</summary>
        public int TimeoutMask;
        public ulong StateHash;
        public MatchState State;
        public override MsgType Type { get { return MsgType.TurnResolved; } }
        public override void Write(BinWriter w)
        {
            w.U32(MatchId); w.U8(Turn); w.U32(PhaseSeq); w.U8(Id0); w.U8(Id1); w.U8(TimeoutMask);
            w.U64(StateHash); MatchStateCodec.Write(w, State);
        }
        public static TurnResolvedMsg Read(BinReader r)
        {
            var m = new TurnResolvedMsg();
            m.MatchId = r.U32(); m.Turn = r.U8(); m.PhaseSeq = r.U32(); m.Id0 = r.U8(); m.Id1 = r.U8();
            m.TimeoutMask = r.U8(); if (m.TimeoutMask > 3) throw new ProtocolException("TimeoutMask không hợp lệ.");
            m.StateHash = r.U64(); m.State = MatchStateCodec.Read(r);
            return m;
        }
    }

    /// <summary>Trận bị hủy (không phải trận thua). Máy chủ gửi tiếp LobbyState nếu phòng còn tồn tại.</summary>
    public sealed class MatchAbortedMsg : ServerMessage
    {
        public uint MatchId; public AbortReason Reason;
        public override MsgType Type { get { return MsgType.MatchAborted; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.U8((int)Reason); }
        public static MatchAbortedMsg Read(BinReader r)
        {
            uint id = r.U32(); int reason = r.U8();
            if (!Enum.IsDefined(typeof(AbortReason), (byte)reason)) reason = (int)AbortReason.Internal;
            return new MatchAbortedMsg { MatchId = id, Reason = (AbortReason)reason };
        }
    }

    /// <summary>Đối thủ mất/khôi phục kết nối. Khi mất, trận bị tạm dừng tối đa <see cref="GraceMs"/> mili giây.</summary>
    public sealed class OpponentConnectionMsg : ServerMessage
    {
        public uint MatchId; public bool Connected; public int GraceMs;
        public override MsgType Type { get { return MsgType.OpponentConnection; } }
        public override void Write(BinWriter w) { w.U32(MatchId); w.Bool(Connected); w.U32((uint)GraceMs); }
        public static OpponentConnectionMsg Read(BinReader r)
        {
            return new OpponentConnectionMsg { MatchId = r.U32(), Connected = r.Bool(), GraceMs = Wire.Ms(r.U32()) };
        }
    }

    /// <summary>
    /// Gửi cho client vừa nối lại: toàn bộ trạng thái công khai + pha hiện tại.
    /// Turn = lượt đang chọn (Selecting) hoặc lượt vừa xử lý (Presenting).
    /// </summary>
    public sealed class ResyncMsg : ServerMessage
    {
        public uint MatchId; public int Seat; public string OpponentName = "";
        public MatchPhase Phase; public int Turn; public uint PhaseSeq;
        public int RemainingMs, TotalMs;
        public bool IAmLocked; public int MyLockedId = 255; public bool OpponentLocked; public bool OpponentConnected = true;
        public int GameNo;
        public MatchState State;
        public override MsgType Type { get { return MsgType.Resync; } }
        public override void Write(BinWriter w)
        {
            w.U32(MatchId); w.U8(Seat); w.Str(OpponentName); w.U8((int)Phase); w.U8(Turn); w.U32(PhaseSeq);
            w.U32((uint)RemainingMs); w.U32((uint)TotalMs);
            w.Bool(IAmLocked); w.U8(MyLockedId); w.Bool(OpponentLocked); w.Bool(OpponentConnected);
            w.U8(GameNo); MatchStateCodec.Write(w, State);
        }
        public static ResyncMsg Read(BinReader r)
        {
            var m = new ResyncMsg();
            m.MatchId = r.U32(); m.Seat = r.U8(); if (m.Seat > 1) throw new ProtocolException("Ghế không hợp lệ.");
            m.OpponentName = r.Str();
            int ph = r.U8(); if (ph > 1) throw new ProtocolException("Pha trận không hợp lệ.");
            m.Phase = (MatchPhase)ph; m.Turn = r.U8(); m.PhaseSeq = r.U32();
            m.RemainingMs = Wire.Ms(r.U32()); m.TotalMs = Wire.Ms(r.U32());
            m.IAmLocked = r.Bool(); m.MyLockedId = r.U8(); m.OpponentLocked = r.Bool(); m.OpponentConnected = r.Bool();
            m.GameNo = r.U8(); m.State = MatchStateCodec.Read(r);
            return m;
        }
    }

    public sealed class PongMsg : ServerMessage
    {
        public uint Token;
        public override MsgType Type { get { return MsgType.Pong; } }
        public override void Write(BinWriter w) { w.U32(Token); }
        public static PongMsg Read(BinReader r) { return new PongMsg { Token = r.U32() }; }
    }

    static class Wire
    {
        /// <summary>Thời lượng trên dây là uint32 mili giây; giới hạn 1 giờ để chặn giá trị vô lý.</summary>
        public static int Ms(uint v)
        {
            if (v > 3600000u) throw new ProtocolException("Thời lượng vô lý: " + v);
            return (int)v;
        }
    }

    public static class Messages
    {
        public static Message Read(MsgType t, BinReader r)
        {
            switch (t)
            {
                case MsgType.Hello: return HelloMsg.Read(r);
                case MsgType.CreateRoom: return new CreateRoomMsg();
                case MsgType.JoinRoom: return JoinRoomMsg.Read(r);
                case MsgType.LeaveRoom: return new LeaveRoomMsg();
                case MsgType.Ready: return ReadyMsg.Read(r);
                case MsgType.Select: return SelectMsg.Read(r);
                case MsgType.Lock: return LockMsg.Read(r);
                case MsgType.PresentationDone: return PresentationDoneMsg.Read(r);
                case MsgType.Ping: return PingMsg.Read(r);

                case MsgType.Welcome: return WelcomeMsg.Read(r);
                case MsgType.Error: return ErrorMsg.Read(r);
                case MsgType.LobbyState: return LobbyStateMsg.Read(r);
                case MsgType.MatchStart: return MatchStartMsg.Read(r);
                case MsgType.TurnBegin: return TurnBeginMsg.Read(r);
                case MsgType.LockAck: return LockAckMsg.Read(r);
                case MsgType.OpponentLocked: return OpponentLockedMsg.Read(r);
                case MsgType.TurnResolved: return TurnResolvedMsg.Read(r);
                case MsgType.MatchAborted: return MatchAbortedMsg.Read(r);
                case MsgType.OpponentConnection: return OpponentConnectionMsg.Read(r);
                case MsgType.Resync: return ResyncMsg.Read(r);
                case MsgType.Pong: return PongMsg.Read(r);
                default: throw new ProtocolException("Loại thông điệp không biết: " + (int)t);
            }
        }
    }
}
