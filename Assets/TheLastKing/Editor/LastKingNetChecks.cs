// THE LAST KING — Online PvP: kiểm tra giao thức ngay trong Unity (không cần mạng, không cần máy chủ).
//
// Chạy: menu  DeadMansHand > Online > Run Protocol Checks
//       hoặc dòng lệnh: Unity -batchmode -quit -projectPath <dự án> -executeMethod TheLastKing.LastKingNetChecks.RunBatch
//
// Mục đích: xác nhận mã mạng dùng chung (Assets/TheLastKing/Net) biên dịch và chạy đúng trên Mono của Unity,
// giống hệt kết quả trên .NET 8 của máy chủ (bộ kiểm thử đầy đủ ở Tests/OnlineTests chạy bằng dotnet).
using System;
using System.Collections.Generic;
using System.Text;
using TheLastKing.Online;
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
#endif

namespace TheLastKing
{
    public static class LastKingNetChecks
    {
        /// <summary>
        /// Dấu vân tay bộ luật đã được kiểm chứng với máy chủ cùng phiên bản. Nếu bạn CHỦ Ý sửa LastKingRules.cs,
        /// hãy tăng OnlineProtocol.RulesVersion, build lại máy chủ, rồi cập nhật hằng số này theo giá trị mà check in ra.
        /// </summary>
        public const uint ExpectedRulesFingerprint = 0x7f41e615;

        static int checks, failures;
        static readonly StringBuilder log = new StringBuilder();

        static void Check(bool ok, string what)
        {
            checks++;
            if (ok) return;
            failures++;
            log.AppendLine("  ✗ " + what);
        }

        static uint Next(ref uint s) { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return s; }

        public static string RunAll()
        {
            checks = 0; failures = 0; log.Length = 0;
            try
            {
                Messages_RoundTrip();
                Direction_And_Truncation();
                Snapshots();
                Fingerprint();
                Strings();
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("  ✗ Ngoại lệ không bắt: " + ex);
            }
            string head = (failures == 0 ? "PASS" : "FAIL") + ": giao thức online " + OnlineProtocol.RulesVersion + " v" + OnlineProtocol.Version + " — " + checks + " khẳng định, " + failures + " lỗi.";
            return failures == 0 ? head : head + "\n" + log;
        }

        static byte[] Body(Message m)
        {
            byte[] frame = OnlineProtocol.Encode(m);
            int len = frame[0] | (frame[1] << 8);
            var body = new byte[len];
            Buffer.BlockCopy(frame, 2, body, 0, len);
            return body;
        }

        static bool Same(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        static List<Message> Samples()
        {
            MatchState init = new LastKingMatch().State;
            var m = new LastKingMatch();
            m.ResolveTurn(0, 0); m.ResolveTurn(1, 1);
            MatchState mid = m.State;
            var lobby = new LobbyStateMsg { RoomCode = "K7M2Q", MySlot = 1, Phase = RoomPhase.InMatch, Games = 4, Wins = new[] { 3, 1 } };
            lobby.Slots[0] = new LobbySlot { Present = true, Name = "Alice", Ready = true, Connected = true };
            lobby.Slots[1] = new LobbySlot { Present = true, Name = "Người chơi Việt", Ready = false, Connected = false };
            return new List<Message>
            {
                new HelloMsg { RulesFingerprint = uint.MaxValue, Build = "1.0", Name = "Người chơi Việt", ResumeToken = ulong.MaxValue },
                new CreateRoomMsg(), new LeaveRoomMsg(), new JoinRoomMsg { RoomCode = "ABCDE" }, new ReadyMsg { Ready = true },
                new SelectMsg { MatchId = uint.MaxValue, Turn = 255, CardId = 255, Seq = uint.MaxValue },
                new LockMsg { MatchId = 1, Turn = 1, CardId = 1, Seq = 1 }, new PresentationDoneMsg { MatchId = 7, Turn = 9 }, new PingMsg { Token = 123456789 },
                new WelcomeMsg { SessionId = ulong.MaxValue, ResumeToken = 42, Resumed = true, SelectionMs = 25000, ReconnectGraceMs = 25000, Banner = "THE LAST KING — máy chủ" },
                new ErrorMsg { Code = ErrorCode.RoomClosed, Text = "lỗi" }, lobby,
                new MatchStartMsg { MatchId = 1, Seat = 1, OpponentName = "Bob", SelectionMs = 25000, PresentationMs = 22000, GameNo = 3, State = init },
                new TurnBeginMsg { MatchId = 3, Turn = 4, PhaseSeq = 9, RemainingMs = 24999, TotalMs = 25000 },
                new LockAckMsg { MatchId = 3, Turn = 2, CardId = 17 }, new OpponentLockedMsg { MatchId = 3, Turn = 2 },
                new TurnResolvedMsg { MatchId = 1, Turn = 2, PhaseSeq = 5, Id0 = 2, Id1 = 5, TimeoutMask = 3, StateHash = MatchStateCodec.Hash(mid), State = mid },
                new MatchAbortedMsg { MatchId = 8, Reason = AbortReason.OpponentDisconnected },
                new OpponentConnectionMsg { MatchId = 3, Connected = false, GraceMs = 25000 },
                new ResyncMsg { MatchId = 1, Seat = 0, OpponentName = "Bob", Phase = MatchPhase.Selecting, Turn = 3, PhaseSeq = 6, RemainingMs = 1234, TotalMs = 25000,
                    IAmLocked = true, MyLockedId = 3, OpponentLocked = false, OpponentConnected = true, GameNo = 1, State = mid },
                new PongMsg { Token = uint.MaxValue },
            };
        }

        static void Messages_RoundTrip()
        {
            foreach (Message msg in Samples())
            {
                bool fromServer = (int)msg.Type >= 64;
                byte[] body = Body(msg);
                Message back = OnlineProtocol.Decode(body, fromServer);
                Check(back.Type == msg.Type, "loại thông điệp sau giải mã: " + msg.Type);
                Check(Same(Body(back), body), "mã hóa lại giống hệt: " + msg.Type);
            }
        }

        static void Direction_And_Truncation()
        {
            foreach (Message msg in Samples())
            {
                bool fromServer = (int)msg.Type >= 64;
                byte[] body = Body(msg);
                bool rejected = false;
                try { OnlineProtocol.Decode(body, !fromServer); } catch (ProtocolException) { rejected = true; }
                Check(rejected, "sai hướng bị từ chối: " + msg.Type);
                if (body.Length > 1)
                {
                    var cut = new byte[body.Length - 1];
                    Buffer.BlockCopy(body, 0, cut, 0, cut.Length);
                    bool bad = false;
                    try { OnlineProtocol.Decode(cut, fromServer); } catch (ProtocolException) { bad = true; }
                    Check(bad, "thân bị cắt cụt bị từ chối: " + msg.Type);
                }
            }
            uint s = 12345;
            for (int i = 0; i < 3000; i++)
            {
                var b = new byte[1 + (int)(Next(ref s) % 60)];
                for (int k = 0; k < b.Length; k++) b[k] = (byte)Next(ref s);
                b[0] = (byte)(Next(ref s) % 2 == 0 ? 1 + Next(ref s) % 9 : 64 + Next(ref s) % 12);
                try { OnlineProtocol.Decode(b, b[0] >= 64); }
                catch (ProtocolException) { }
                catch (Exception ex) { Check(false, "fuzz: ngoại lệ lạ " + ex.GetType().Name); return; }
            }
            Check(true, "fuzz 3000 đầu vào ngẫu nhiên chỉ cho ProtocolException");
        }

        static void Snapshots()
        {
            uint s = 99;
            int states = 0;
            for (int g = 0; g < 300; g++)
            {
                var m = new LastKingMatch();
                var all = new List<MatchState> { m.State };
                while (!m.State.IsFinished)
                {
                    m.ResolveTurn((int)(Next(ref s) % (uint)m.State.Players[0].Hand.Count), (int)(Next(ref s) % (uint)m.State.Players[1].Hand.Count));
                    all.Add(m.State);
                }
                foreach (MatchState st in all)
                {
                    byte[] enc = MatchStateCodec.Encode(st);
                    MatchState back = MatchStateCodec.Read(new BinReader(enc, 0, enc.Length));
                    if (!Same(MatchStateCodec.Encode(back), enc) || MatchStateCodec.Hash(back) != MatchStateCodec.Hash(st)) { Check(false, "snapshot không khứ hồi (ván " + g + ", lượt " + st.TurnIndex + ")"); return; }
                    states++;
                }
            }
            Check(states > 1500, "snapshot khứ hồi đúng cho " + states + " trạng thái");
        }

        static void Fingerprint()
        {
            uint a = RulesFingerprint.Compute();
            Check(a == RulesFingerprint.Compute(), "dấu vân tay bộ luật tất định");
            Check(a == ExpectedRulesFingerprint,
                "dấu vân tay bộ luật = " + a.ToString("x8") + " nhưng giá trị đã kiểm chứng là " + ExpectedRulesFingerprint.ToString("x8") +
                ". Nếu CHỦ Ý sửa LastKingRules.cs: tăng OnlineProtocol.RulesVersion, build lại máy chủ, cập nhật ExpectedRulesFingerprint. " +
                "Nếu không chủ ý: có thể Mono của Unity cho kết quả khác .NET — hãy báo lại.");
        }

        static void Strings()
        {
            Check(OnlineProtocol.SanitizeName("<b>Hi</b>\u0001").IndexOf('<') < 0, "tên không còn thẻ rich-text");
            Check(OnlineProtocol.NormalizeRoomCode(" k7-m2 q ") == "K7M2Q", "chuẩn hóa mã phòng");
            string h; int p;
            Check(OnlineClient.TryParseAddress("192.168.1.10:9000", out h, out p) && h == "192.168.1.10" && p == 9000, "phân tích địa chỉ IP:cổng");
            Check(OnlineClient.TryParseAddress("play.example.com", out h, out p) && p == OnlineProtocol.DefaultPort, "địa chỉ không cổng dùng cổng mặc định");
        }

#if UNITY_EDITOR
        [MenuItem("DeadMansHand/Online/Run Protocol Checks")]
        static void RunFromMenu()
        {
            string result = RunAll();
            if (result.StartsWith("PASS", StringComparison.Ordinal)) Debug.Log(result);
            else Debug.LogError(result);
        }

        /// <summary>Dùng với -batchmode -executeMethod: thoát với mã 0 nếu đạt, 1 nếu lỗi.</summary>
        public static void RunBatch()
        {
            string result = RunAll();
            Debug.Log(result);
            EditorApplication.Exit(result.StartsWith("PASS", StringComparison.Ordinal) ? 0 : 1);
        }
#endif
    }
}
