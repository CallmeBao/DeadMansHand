// THE LAST KING — Online PvP: mã hóa trạng thái công khai của trận + dấu vân tay bộ luật.
// Trong bộ luật v1.1 mọi thứ đều công khai ngoại trừ lá đang chọn đồng thời (xem docs/ONLINE_PVP_DESIGN.md §3),
// nên snapshot đầy đủ có thể gửi cho cả hai client mà không lộ bí mật nào.
using System;
using System.Collections.Generic;

namespace TheLastKing.Online
{
    static class Fnv
    {
        public const ulong Offset = 14695981039346656037UL;
        const ulong Prime = 1099511628211UL;

        public static ulong Bytes(ulong h, byte[] data)
        {
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= Prime; }
            return h;
        }

        public static ulong Mix(ulong h, ulong v)
        {
            for (int i = 0; i < 8; i++) { h ^= (byte)(v >> (8 * i)); h *= Prime; }
            return h;
        }
    }

    public static class MatchStateCodec
    {
        const int MaxCards = LastKingRules.TotalCards;

        public static void Write(BinWriter w, MatchState s)
        {
            if (s == null) throw new ProtocolException("Thiếu trạng thái trận.");
            w.U8(s.TurnIndex);
            w.U8(s.InitiativeSeat);
            w.U8(s.IsFinished ? 1 : 0);
            w.I8(s.WinnerSeat);
            w.U8((int)s.EndReason);
            for (int seat = 0; seat < 2; seat++)
            {
                PlayerState p = s.Players[seat];
                w.U8(p.BloodCoins);
                WriteCards(w, p.Hand);
            }
            WriteCards(w, s.Deck);
            WriteCards(w, s.DestroyedCards);
            WriteCards(w, s.InPlayCards);
        }

        static void WriteCards(BinWriter w, IReadOnlyList<Card> cards)
        {
            w.U8(cards.Count);
            for (int i = 0; i < cards.Count; i++) { w.U8(cards[i].Id); w.U8((int)cards[i].Kind); }
        }

        public static MatchState Read(BinReader r)
        {
            int turn = r.U8();
            int initiative = r.U8(); if (initiative > 1) throw new ProtocolException("Initiative không hợp lệ.");
            int flags = r.U8(); if (flags > 1) throw new ProtocolException("Cờ trạng thái không hợp lệ.");
            int winner = r.I8(); if (winner < -1 || winner > 1) throw new ProtocolException("Người thắng không hợp lệ.");
            int reason = r.U8(); if (reason > (int)MatchEndReason.InvalidKingState) throw new ProtocolException("Lý do kết thúc không hợp lệ.");
            var players = new PlayerState[2];
            for (int seat = 0; seat < 2; seat++)
            {
                int coins = r.U8();
                List<Card> hand = ReadCards(r, LastKingRules.HandLimit);
                players[seat] = new PlayerState(hand, coins);
            }
            List<Card> deck = ReadCards(r, MaxCards);
            List<Card> destroyed = ReadCards(r, MaxCards);
            List<Card> inPlay = ReadCards(r, 2);
            var state = new MatchState(players, deck, destroyed, initiative, turn, inPlay, flags == 1, winner, (MatchEndReason)reason);
            try { LastKingRules.ValidateState(state); }
            catch (ArgumentException ex) { throw new ProtocolException("Snapshot không hợp lệ: " + ex.Message); }
            catch (InvalidOperationException ex) { throw new ProtocolException("Snapshot không hợp lệ: " + ex.Message); }
            return state;
        }

        static List<Card> ReadCards(BinReader r, int max)
        {
            int n = r.U8();
            if (n > max) throw new ProtocolException("Quá nhiều lá trong một nhóm: " + n);
            var list = new List<Card>(n);
            for (int i = 0; i < n; i++)
            {
                int id = r.U8(), kind = r.U8();
                if (kind > (int)CardKind.Slave) throw new ProtocolException("Loại lá không hợp lệ: " + kind);
                list.Add(new Card(id, (CardKind)kind));
            }
            return list;
        }

        public static byte[] Encode(MatchState s)
        {
            var w = new BinWriter();
            Write(w, s);
            return w.ToArray();
        }

        /// <summary>FNV-1a 64 bit trên dạng mã hóa chuẩn của trạng thái (kể cả thứ tự lá trong tay).</summary>
        public static ulong Hash(MatchState s)
        {
            return Fnv.Bytes(Fnv.Offset, Encode(s));
        }

        /// <summary>Vị trí của lá có ID trong tay; -1 nếu không có.</summary>
        public static int IndexOfCard(IReadOnlyList<Card> hand, int cardId)
        {
            for (int i = 0; i < hand.Count; i++) if (hand[i].Id == cardId) return i;
            return -1;
        }
    }

    /// <summary>
    /// Dấu vân tay bộ luật: phát lại các trận có kịch bản cố định rồi băm mọi trạng thái trung gian.
    /// Client và server phải cho cùng giá trị — khác nghĩa là hai bên đang chạy hai phiên bản <c>LastKingRules.cs</c> khác nhau,
    /// khi đó bản phát lại cục bộ của client sẽ lệch máy chủ nên server từ chối từ bước bắt tay.
    /// Dùng bộ sinh xorshift riêng (không dùng System.Random vì thuật toán có thể khác giữa Mono và .NET).
    /// </summary>
    public static class RulesFingerprint
    {
        static uint cached;
        static bool computed;

        public static uint Value
        {
            get
            {
                if (!computed) { cached = Compute(); computed = true; }   // tính lặp lại vô hại: kết quả tất định
                return cached;
            }
        }

        static uint Next(ref uint s)
        {
            s ^= s << 13; s ^= s >> 17; s ^= s << 5;
            return s;
        }

        public static uint Compute()
        {
            ulong h = Fnv.Offset;
            h = Fnv.Mix(h, (ulong)LastKingRules.HandLimit);
            h = Fnv.Mix(h, (ulong)LastKingRules.BloodCoinLimit);
            h = Fnv.Mix(h, (ulong)LastKingRules.TotalCards);
            for (uint seed = 1; seed <= 48; seed++)
            {
                uint s = (seed * 2654435761u) | 1u;
                var m = new LastKingMatch();
                h = Fnv.Mix(h, MatchStateCodec.Hash(m.State));
                int guard = 0;
                while (!m.State.IsFinished && guard++ < 64)
                {
                    int a = (int)(Next(ref s) % (uint)m.State.Players[0].Hand.Count);
                    int b = (int)(Next(ref s) % (uint)m.State.Players[1].Hand.Count);
                    m.ResolveTurn(a, b);
                    h = Fnv.Mix(h, MatchStateCodec.Hash(m.State));
                }
            }
            return (uint)(h ^ (h >> 32));
        }
    }
}
