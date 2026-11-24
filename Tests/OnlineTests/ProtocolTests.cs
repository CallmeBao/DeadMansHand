using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TheLastKing.Online;

namespace TheLastKing.Tests
{
    public static class ProtocolTests
    {
        public static void Register()
        {
            Registry.Add("protocol.roundtrip.all-messages", RoundTripAll);
            Registry.Add("protocol.direction", Direction);
            Registry.Add("protocol.fuzz.random-bytes", FuzzRandom);
            Registry.Add("protocol.fuzz.mutated-valid", FuzzMutated);
            Registry.Add("protocol.frame.limits", FrameLimits);
            Registry.Add("protocol.strings.names-roomcodes", Strings);
            Registry.Add("protocol.address.parse", Address);
            Registry.Add("state.roundtrip.3000-random-matches", StateRoundTrip);
            Registry.Add("state.corrupt.rejected-or-hash-differs", StateCorrupt);
            Registry.Add("state.hash.no-collision-and-stable", StateHash);
            Registry.Add("rules.fingerprint.deterministic", Fingerprint);
            Registry.Add("rules.existing-35k-assertions-still-pass", ExistingRuleChecks);
            Registry.Add("rules.golden-fingerprint-matches-unity-editor-check", GoldenFingerprint);
            Registry.Add("protocol.unity-editor-check-passes-under-dotnet", EditorCheck);
        }

        // ------------------------------------------------------------------ danh sách thông điệp mẫu (kể cả giá trị biên)

        static MatchState MidState()
        {
            var m = new LastKingMatch();
            m.ResolveTurn(0, 0);
            m.ResolveTurn(1, 1);
            return m.State;
        }

        public static List<Message> Samples()
        {
            MatchState init = new LastKingMatch().State, mid = MidState();
            var list = new List<Message>
            {
                new HelloMsg { Protocol = 1, RulesVersion = "LK-1.1", RulesFingerprint = uint.MaxValue, Build = "", Name = "Người chơi Việt", ResumeToken = ulong.MaxValue },
                new HelloMsg { Protocol = 65535, RulesVersion = "", RulesFingerprint = 0, Build = "b", Name = "", ResumeToken = 0 },
                new CreateRoomMsg(), new LeaveRoomMsg(),
                new JoinRoomMsg { RoomCode = "ABCDE" }, new JoinRoomMsg { RoomCode = "" },
                new ReadyMsg { Ready = true }, new ReadyMsg { Ready = false },
                new SelectMsg { MatchId = uint.MaxValue, Turn = 255, CardId = 255, Seq = uint.MaxValue },
                new LockMsg { MatchId = 1, Turn = 1, CardId = 1, Seq = 1 },
                new PresentationDoneMsg { MatchId = 7, Turn = 9 },
                new PingMsg { Token = 123456789 },

                new WelcomeMsg { SessionId = ulong.MaxValue, ResumeToken = 42, Resumed = true, SelectionMs = 25000, ReconnectGraceMs = 25000, Banner = "THE LAST KING — máy chủ" },
                new PongMsg { Token = uint.MaxValue },
                new LockAckMsg { MatchId = 3, Turn = 2, CardId = 17 },
                new OpponentLockedMsg { MatchId = 3, Turn = 2 },
                new OpponentConnectionMsg { MatchId = 3, Connected = false, GraceMs = 25000 },
                new TurnBeginMsg { MatchId = 3, Turn = 4, PhaseSeq = 9, RemainingMs = 24999, TotalMs = 25000 },
                new MatchStartMsg { MatchId = 1, Seat = 1, OpponentName = "Bob", SelectionMs = 25000, PresentationMs = 22000, GameNo = 3, State = init },
                new TurnResolvedMsg { MatchId = 1, Turn = 2, PhaseSeq = 5, Id0 = 2, Id1 = 5, TimeoutMask = 3, StateHash = MatchStateCodec.Hash(mid), State = mid },
                new ResyncMsg { MatchId = 1, Seat = 0, OpponentName = "Bob", Phase = MatchPhase.Selecting, Turn = 3, PhaseSeq = 6, RemainingMs = 1234, TotalMs = 25000,
                    IAmLocked = true, MyLockedId = 3, OpponentLocked = false, OpponentConnected = true, GameNo = 1, State = mid },
            };
            foreach (ErrorCode code in Enum.GetValues(typeof(ErrorCode))) list.Add(new ErrorMsg { Code = code, Text = "lỗi " + code });
            foreach (AbortReason r in Enum.GetValues(typeof(AbortReason))) list.Add(new MatchAbortedMsg { MatchId = 8, Reason = r });
            var lobby = new LobbyStateMsg { RoomCode = "K7M2Q", MySlot = 1, Phase = RoomPhase.InMatch, Games = 4, Wins = new[] { 3, 1 } };
            lobby.Slots[0] = new LobbySlot { Present = true, Name = "Alice", Ready = true, Connected = true };
            lobby.Slots[1] = new LobbySlot { Present = true, Name = "Bob", Ready = false, Connected = false };
            list.Add(lobby);
            return list;
        }

        static byte[] Body(Message m)
        {
            byte[] frame = OnlineProtocol.Encode(m);
            int len = frame[0] | (frame[1] << 8);
            T.Eq(frame.Length - 2, len, "độ dài khung khớp tiêu đề (" + m.Type + ")");
            var body = new byte[len];
            Buffer.BlockCopy(frame, 2, body, 0, len);
            return body;
        }

        static void RoundTripAll()
        {
            foreach (Message m in Samples())
            {
                bool fromServer = (int)m.Type >= 64;
                byte[] body = Body(m);
                Message back = OnlineProtocol.Decode(body, fromServer);
                T.Eq(m.Type, back.Type, "loại sau giải mã (" + m.Type + ")");
                T.Check(Body(back).SequenceEqual(body), "mã hóa lại giống hệt (" + m.Type + ")");
            }
        }

        static void Direction()
        {
            foreach (Message m in Samples())
            {
                bool fromServer = (int)m.Type >= 64;
                byte[] body = Body(m);
                T.Throws<ProtocolException>(() => OnlineProtocol.Decode(body, !fromServer), "sai hướng bị từ chối (" + m.Type + ")");
            }
        }

        static void FuzzRandom()
        {
            var rng = new Random(20261116);
            int ok = 0, rejected = 0;
            for (int i = 0; i < 200000; i++)
            {
                int len = rng.Next(1, 90);
                var b = new byte[len];
                rng.NextBytes(b);
                if (rng.Next(3) != 0) b[0] = (byte)(rng.Next(2) == 0 ? rng.Next(1, 10) : rng.Next(64, 76));   // thiên về loại hợp lệ để chạm sâu hơn
                try { OnlineProtocol.Decode(b, b[0] >= 64); ok++; }
                catch (ProtocolException) { rejected++; }
                catch (Exception ex) { T.Fail("fuzz: ngoại lệ ngoài dự kiến " + ex.GetType().Name + " với " + BitConverter.ToString(b)); return; }
            }
            T.Check(rejected > 150000, "đa số đầu vào ngẫu nhiên bị từ chối (" + rejected + " từ chối, " + ok + " hợp lệ)");
        }

        static void FuzzMutated()
        {
            var rng = new Random(77);
            List<Message> samples = Samples();
            int mutations = 0;
            foreach (Message m in samples)
            {
                bool fromServer = (int)m.Type >= 64;
                byte[] body = Body(m);
                for (int i = 0; i < 1500; i++)
                {
                    byte[] b = (byte[])body.Clone();
                    switch (rng.Next(4))
                    {
                        case 0: b[rng.Next(b.Length)] ^= (byte)(1 << rng.Next(8)); break;                 // lật bit
                        case 1: Array.Resize(ref b, rng.Next(1, b.Length + 1)); break;                    // cắt cụt
                        case 2: Array.Resize(ref b, b.Length + rng.Next(1, 6)); break;                    // thừa đuôi
                        default: b[rng.Next(b.Length)] = (byte)rng.Next(256); break;                      // ghi đè byte
                    }
                    mutations++;
                    try
                    {
                        Message back = OnlineProtocol.Decode(b, b[0] >= 64);
                        // nếu vẫn giải mã được thì mã hóa lại không được nổ
                        OnlineProtocol.Encode(back);
                    }
                    catch (ProtocolException) { }
                    catch (Exception ex) { T.Fail("fuzz đột biến: " + ex.GetType().Name + " với " + m.Type + ": " + ex.Message); return; }
                }
            }
            T.Check(mutations > 20000, "đã thử " + mutations + " đột biến");
        }

        static void FrameLimits()
        {
            // độ dài 0, vượt trần
            T.Throws<ProtocolException>(() => OnlineProtocol.ReadFrameBody(new MemoryStream(new byte[] { 0, 0 })), "độ dài khung 0 bị từ chối");
            T.Throws<ProtocolException>(() => OnlineProtocol.ReadFrameBody(new MemoryStream(new byte[] { 0xFF, 0xFF })), "độ dài khung 65535 bị từ chối");
            int max = OnlineProtocol.MaxFrameBody;
            T.Throws<ProtocolException>(() => OnlineProtocol.ReadFrameBody(new MemoryStream(new byte[] { (byte)((max + 1) & 255), (byte)((max + 1) >> 8) })), "vượt MaxFrameBody bị từ chối");
            // luồng đóng sạch / giữa khung
            T.Check(OnlineProtocol.ReadFrameBody(new MemoryStream(new byte[0])) == null, "luồng rỗng => null (đóng sạch)");
            T.Throws<IOException>(() => OnlineProtocol.ReadFrameBody(new MemoryStream(new byte[] { 5 })), "đứt giữa tiêu đề");
            T.Throws<IOException>(() => OnlineProtocol.ReadFrameBody(new MemoryStream(new byte[] { 5, 0, 1, 2 })), "đứt giữa thân");
            // chuỗi quá dài khi mã hóa
            T.Throws<ProtocolException>(() => OnlineProtocol.Encode(new JoinRoomMsg { RoomCode = new string('x', 200) }), "chuỗi > MaxStringBytes bị từ chối khi mã hóa");
            // giá trị ngoài phạm vi U8
            T.Throws<ProtocolException>(() => OnlineProtocol.Encode(new SelectMsg { Turn = 256 }), "U8 ngoài phạm vi");
            // thân khung tối đa vẫn trong trần: snapshot lớn nhất
            int biggest = Samples().Max(m => OnlineProtocol.Encode(m).Length);
            T.Check(biggest < 400, "thông điệp lớn nhất chỉ " + biggest + " byte (trần " + OnlineProtocol.MaxFrameBody + ")");
            // chuỗi UTF-8 sai
            var bad = new byte[] { (byte)MsgType.JoinRoom, 2, 0xC3, 0x28 };
            T.Throws<ProtocolException>(() => OnlineProtocol.Decode(bad, false), "UTF-8 sai bị từ chối");
            // bool không hợp lệ
            T.Throws<ProtocolException>(() => OnlineProtocol.Decode(new byte[] { (byte)MsgType.Ready, 2 }, false), "bool = 2 bị từ chối");
            // thời lượng vô lý
            var tb = new BinWriter(); tb.U8((int)MsgType.TurnBegin); tb.U32(1); tb.U8(1); tb.U32(1); tb.U32(uint.MaxValue); tb.U32(1);
            T.Throws<ProtocolException>(() => OnlineProtocol.Decode(tb.ToArray(), true), "thời lượng 4 tỷ ms bị từ chối");
        }

        static void Strings()
        {
            T.Eq("Alice", OnlineProtocol.SanitizeName("  Alice  "), "cắt khoảng trắng");
            T.Eq("A B", OnlineProtocol.SanitizeName("A \t\n  B"), "gộp khoảng trắng");
            T.Eq("colorredx", OnlineProtocol.SanitizeName("<color=red>x").Replace("=", ""), "bỏ ký tự < >");
            T.Check(!OnlineProtocol.SanitizeName("<b>hi</b>").Contains("<"), "không còn '<'");
            T.Eq("", OnlineProtocol.SanitizeName("\u0001\u0002"), "toàn ký tự điều khiển => rỗng");
            T.Eq(OnlineProtocol.MaxNameChars, OnlineProtocol.SanitizeName(new string('a', 100)).Length, "cắt tối đa " + OnlineProtocol.MaxNameChars + " ký tự");
            T.Eq("", OnlineProtocol.SanitizeName(null), "null => rỗng");
            T.Eq("Nguyễn Văn", OnlineProtocol.SanitizeName("Nguyễn Văn"), "giữ dấu tiếng Việt");
            T.Check(Enumerable.Range(0, 200).All(i => OnlineProtocol.SanitizeName(new string('ệ', i)).Length <= OnlineProtocol.MaxNameChars
                        && System.Text.Encoding.UTF8.GetByteCount(OnlineProtocol.SanitizeName(new string('ệ', i))) <= OnlineProtocol.MaxStringBytes), "tên đã làm sạch luôn vừa trong giới hạn dây truyền");

            T.Eq("K7M2Q", OnlineProtocol.NormalizeRoomCode(" k7-m2 q "), "chuẩn hóa mã phòng");
            T.Check(OnlineProtocol.IsValidRoomCode("K7M2Q"), "mã hợp lệ");
            T.Check(!OnlineProtocol.IsValidRoomCode("K7M2"), "quá ngắn");
            T.Check(!OnlineProtocol.IsValidRoomCode("K7M2QQ"), "quá dài");
            T.Check(!OnlineProtocol.IsValidRoomCode("K7M20"), "chứa ký tự dễ nhầm '0'");
            T.Check(!OnlineProtocol.IsValidRoomCode("K7MIQ"), "chứa ký tự dễ nhầm 'I'");
            T.Check(!OnlineProtocol.IsValidRoomCode(null), "null");
            T.Eq(32 - 1, OnlineProtocol.RoomCodeAlphabet.Length, "bảng chữ 31 ký tự không nhập nhằng");
        }

        static void Address()
        {
            string h; int p;
            T.Check(OnlineClient.TryParseAddress("127.0.0.1", out h, out p) && h == "127.0.0.1" && p == 7777, "chỉ IP => cổng mặc định");
            T.Check(OnlineClient.TryParseAddress("192.168.1.10:9000", out h, out p) && h == "192.168.1.10" && p == 9000, "IP:cổng");
            T.Check(OnlineClient.TryParseAddress("  play.example.com  ", out h, out p) && h == "play.example.com" && p == 7777, "tên miền + khoảng trắng");
            T.Check(OnlineClient.TryParseAddress("tcp://play.example.com:1234/", out h, out p) && h == "play.example.com" && p == 1234, "có scheme + gạch cuối");
            T.Check(OnlineClient.TryParseAddress("[::1]:7777", out h, out p) && h == "::1" && p == 7777, "IPv6 trong ngoặc");
            T.Check(!OnlineClient.TryParseAddress("", out h, out p), "rỗng");
            T.Check(!OnlineClient.TryParseAddress("host:abc", out h, out p), "cổng không phải số");
            T.Check(!OnlineClient.TryParseAddress("host:0", out h, out p), "cổng 0");
            T.Check(!OnlineClient.TryParseAddress("host:70000", out h, out p), "cổng > 65535");
            T.Check(!OnlineClient.TryParseAddress(":7777", out h, out p), "thiếu host");
        }

        // ------------------------------------------------------------------ snapshot

        static List<MatchState> AllStates(int seed)
        {
            var rng = new Random(seed);
            var m = new LastKingMatch();
            var list = new List<MatchState> { m.State };
            while (!m.State.IsFinished)
            {
                m.ResolveTurn(rng.Next(m.State.Players[0].Hand.Count), rng.Next(m.State.Players[1].Hand.Count));
                list.Add(m.State);
            }
            return list;
        }

        static void StateRoundTrip()
        {
            int states = 0, finished = 0;
            for (int g = 0; g < 3000; g++)
            {
                foreach (MatchState s in AllStates(g))
                {
                    byte[] enc = MatchStateCodec.Encode(s);
                    MatchState back = MatchStateCodec.Read(new BinReader(enc, 0, enc.Length));
                    if (!MatchStateCodec.Encode(back).SequenceEqual(enc)) { T.Fail("snapshot không khứ hồi ở ván " + g + " lượt " + s.TurnIndex); return; }
                    if (MatchStateCodec.Hash(back) != MatchStateCodec.Hash(s)) { T.Fail("hash lệch sau khứ hồi"); return; }
                    states++;
                    if (s.IsFinished) finished++;
                }
            }
            T.Check(states > 20000, "đã kiểm " + states + " trạng thái (" + finished + " trạng thái kết thúc, gồm cả ván có lá còn InPlay)");
        }

        static void StateCorrupt()
        {
            var rng = new Random(5);
            int rejected = 0, accepted = 0;
            foreach (MatchState s in AllStates(11).Concat(AllStates(12)))
            {
                byte[] enc = MatchStateCodec.Encode(s);
                ulong h = MatchStateCodec.Hash(s);
                for (int i = 0; i < 400; i++)
                {
                    byte[] b = (byte[])enc.Clone();
                    int pos = rng.Next(b.Length);
                    byte old = b[pos];
                    while (b[pos] == old) b[pos] = (byte)rng.Next(256);
                    try
                    {
                        MatchState back = MatchStateCodec.Read(new BinReader(b, 0, b.Length));
                        accepted++;
                        T.Check(MatchStateCodec.Hash(back) != h, "snapshot bị sửa nhưng hợp lệ thì hash phải khác");
                    }
                    catch (ProtocolException) { rejected++; }
                    catch (Exception ex) { T.Fail("snapshot hỏng gây ngoại lệ lạ: " + ex.GetType().Name + ": " + ex.Message); return; }
                }
            }
            T.Check(rejected > accepted, "đa số snapshot bị sửa bị từ chối (" + rejected + " từ chối, " + accepted + " vẫn hợp lệ nhưng hash khác)");

            // hỏng có chủ đích: trùng ID, hai Vua, bộ bài chứa Vua
            var init = new LastKingMatch().State;
            byte[] good = MatchStateCodec.Encode(init);
            // seat0 hand = [King id1, Citizen id2, Citizen id3]; ép lá thứ 3 trùng id 2
            byte[] dup = (byte[])good.Clone();
            int off = 5 /*header*/ + 2 /*coins+count*/;      // đầu danh sách tay seat0
            dup[off + 4] = dup[off + 2];                      // id lá thứ 3 = id lá thứ 2
            T.Throws<ProtocolException>(() => MatchStateCodec.Read(new BinReader(dup, 0, dup.Length)), "trùng ID bị từ chối");
            byte[] twoKings = (byte[])good.Clone();
            twoKings[off + 3] = (byte)CardKind.King;          // lá thứ 2 cũng thành Vua
            T.Throws<ProtocolException>(() => MatchStateCodec.Read(new BinReader(twoKings, 0, twoKings.Length)), "hai Vua bị từ chối");
        }

        static void StateHash()
        {
            var seen = new Dictionary<ulong, byte[]>();
            int collisions = 0, total = 0;
            for (int g = 0; g < 2000; g++)
                foreach (MatchState s in AllStates(g * 7 + 1))
                {
                    byte[] enc = MatchStateCodec.Encode(s);
                    ulong h = MatchStateCodec.Hash(s);
                    T.Check(h == MatchStateCodec.Hash(s), "hash ổn định");
                    byte[] other;
                    if (seen.TryGetValue(h, out other)) { if (!other.SequenceEqual(enc)) collisions++; }
                    else seen[h] = enc;
                    total++;
                }
            T.Eq(0, collisions, "không có va chạm hash giữa các trạng thái khác nhau (" + seen.Count + " trạng thái riêng biệt / " + total + ")");
        }

        static void Fingerprint()
        {
            uint a = RulesFingerprint.Compute(), b = RulesFingerprint.Compute();
            T.Eq(a, b, "dấu vân tay tất định");
            T.Eq(a, RulesFingerprint.Value, "Value = Compute");
            T.Check(a != 0, "khác 0");
            Console.WriteLine("      dấu vân tay bộ luật " + OnlineProtocol.RulesVersion + " = " + a.ToString("x8"));
        }

        static void GoldenFingerprint()
        {
            T.Eq(LastKingNetChecks.ExpectedRulesFingerprint, RulesFingerprint.Compute(),
                "giá trị vàng trong LastKingNetChecks khớp dấu vân tay hiện tại (nếu sửa luật: cập nhật hằng số này)");
        }

        static void EditorCheck()
        {
            string r = LastKingNetChecks.RunAll();
            Console.WriteLine("      " + r.Split('\n')[0]);
            T.Check(r.StartsWith("PASS"), "LastKingNetChecks.RunAll(): " + r);
        }

        static void ExistingRuleChecks()
        {
            string result = LastKingRuleChecks.RunAll();
            T.Check(result.StartsWith("PASS"), "bộ kiểm tra luật có sẵn của nhóm: " + result.Substring(0, Math.Min(90, result.Length)));
        }
    }
}
