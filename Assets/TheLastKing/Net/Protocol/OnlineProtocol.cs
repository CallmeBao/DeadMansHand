// THE LAST KING — Online PvP: giao thức nhị phân dùng chung cho client (Unity), server (.NET 8) và bộ kiểm thử.
// C# thuần (C# 9, .NET Standard 2.1), KHÔNG phụ thuộc UnityEngine. Server và test biên dịch trực tiếp tệp này.
using System;
using System.IO;
using System.Text;

namespace TheLastKing.Online
{
    /// <summary>Dữ liệu sai khuôn dạng, quá dài, thừa/cụt hoặc loại thông điệp không biết.</summary>
    public sealed class ProtocolException : Exception
    {
        public ProtocolException(string message) : base(message) { }
    }

    /// <summary>Hằng số giao thức. Đổi bất kỳ giá trị nào ảnh hưởng tới dây truyền thì phải tăng <see cref="Version"/>.</summary>
    public static class OnlineProtocol
    {
        public const int Version = 1;
        /// <summary>Nhãn bộ luật; dấu vân tay thật nằm ở <see cref="RulesFingerprint"/>.</summary>
        public const string RulesVersion = "LK-1.1";
        public const int DefaultPort = 7777;

        /// <summary>Khung tối đa (gồm byte loại). Thông điệp lớn nhất là snapshot (~200 byte) nên 2 KB là dư.</summary>
        public const int MaxFrameBody = 2048;
        public const int MaxStringBytes = 96;
        public const int MaxNameChars = 16;

        public const int RoomCodeLength = 5;
        /// <summary>32 ký tự không nhập nhằng (bỏ 0/O, 1/I/L).</summary>
        public const string RoomCodeAlphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

        public const int HeartbeatMs = 1000;
        /// <summary>Quá ngưỡng này không nhận được byte nào thì coi là mất kết nối (GDD 6.5: 5 giây).</summary>
        public const int DisconnectMs = 5000;

        public const int DefaultSelectionMs = 25000;
        public const int DefaultPresentationMaxMs = 22000;
        public const int DefaultReconnectGraceMs = 25000;

        public static bool IsValidRoomCode(string code)
        {
            if (code == null || code.Length != RoomCodeLength) return false;
            for (int i = 0; i < code.Length; i++)
                if (RoomCodeAlphabet.IndexOf(code[i]) < 0) return false;
            return true;
        }

        /// <summary>Chuẩn hóa mã người dùng gõ: bỏ khoảng trắng/gạch, in hoa.</summary>
        public static string NormalizeRoomCode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c) || c == '-' || c == '_') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Làm sạch tên hiển thị: bỏ ký tự điều khiển, gộp khoảng trắng, cắt độ dài. Trả về chuỗi rỗng nếu không còn gì.</summary>
        public static string SanitizeName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder();
            bool lastSpace = true;
            foreach (char c in raw)
            {
                if (char.IsControl(c) || c == '<' || c == '>') continue;   // chặn thẻ rich text của uGUI
                if (char.IsWhiteSpace(c)) { if (!lastSpace) sb.Append(' '); lastSpace = true; continue; }
                if (char.IsSurrogate(c)) continue;
                sb.Append(c); lastSpace = false;
                if (sb.Length >= MaxNameChars) break;
            }
            return sb.ToString().Trim();
        }

        // ------------------------------------------------------------------ khung thông điệp
        // [uint16 độ dài LE][byte loại][payload]; độ dài tính cả byte loại.

        public static byte[] Encode(Message m)
        {
            if (m == null) throw new ArgumentNullException("m");
            var w = new BinWriter();
            w.U8((int)m.Type);
            m.Write(w);
            byte[] body = w.ToArray();
            if (body.Length > MaxFrameBody) throw new ProtocolException("Thông điệp quá lớn: " + body.Length);
            byte[] frame = new byte[2 + body.Length];
            frame[0] = (byte)(body.Length & 0xFF);
            frame[1] = (byte)((body.Length >> 8) & 0xFF);
            Buffer.BlockCopy(body, 0, frame, 2, body.Length);
            return frame;
        }

        /// <summary>Đọc một khung từ luồng. Trả về null khi luồng đóng sạch đúng ranh giới khung.</summary>
        public static byte[] ReadFrameBody(Stream s)
        {
            byte[] hdr = new byte[2];
            int got = ReadFully(s, hdr, 0, 2);
            if (got == 0) return null;
            if (got < 2) throw new IOException("Luồng đóng giữa khung.");
            int len = hdr[0] | (hdr[1] << 8);
            if (len < 1 || len > MaxFrameBody) throw new ProtocolException("Độ dài khung không hợp lệ: " + len);
            byte[] body = new byte[len];
            if (ReadFully(s, body, 0, len) < len) throw new IOException("Luồng đóng giữa khung.");
            return body;
        }

        static int ReadFully(Stream s, byte[] buf, int off, int count)
        {
            int total = 0;
            while (total < count)
            {
                int n = s.Read(buf, off + total, count - total);
                if (n <= 0) break;
                total += n;
            }
            return total;
        }

        /// <summary>Giải mã thân khung. <paramref name="fromServer"/> quyết định hướng hợp lệ: client không được gửi thông điệp của server và ngược lại.</summary>
        public static Message Decode(byte[] body, bool fromServer)
        {
            if (body == null || body.Length < 1) throw new ProtocolException("Khung rỗng.");
            byte type = body[0];
            bool serverType = type >= 64;
            if (serverType != fromServer) throw new ProtocolException("Sai hướng thông điệp (loại " + type + ").");
            var r = new BinReader(body, 1, body.Length - 1);
            Message m = Messages.Read((MsgType)type, r);
            if (r.Remaining != 0) throw new ProtocolException("Thừa dữ liệu cuối thông điệp loại " + type + ".");
            return m;
        }
    }

    public sealed class BinWriter
    {
        readonly MemoryStream ms = new MemoryStream(64);

        public void U8(int v) { if (v < 0 || v > 255) throw new ProtocolException("U8 ngoài phạm vi: " + v); ms.WriteByte((byte)v); }
        public void I8(int v) { if (v < -128 || v > 127) throw new ProtocolException("I8 ngoài phạm vi: " + v); ms.WriteByte(unchecked((byte)(sbyte)v)); }
        public void U16(int v) { if (v < 0 || v > 65535) throw new ProtocolException("U16 ngoài phạm vi: " + v); ms.WriteByte((byte)v); ms.WriteByte((byte)(v >> 8)); }
        public void U32(uint v) { for (int i = 0; i < 4; i++) ms.WriteByte((byte)(v >> (8 * i))); }
        public void U64(ulong v) { for (int i = 0; i < 8; i++) ms.WriteByte((byte)(v >> (8 * i))); }
        public void Bool(bool v) { ms.WriteByte((byte)(v ? 1 : 0)); }

        public void Str(string s)
        {
            byte[] b = Encoding.UTF8.GetBytes(s ?? "");
            if (b.Length > OnlineProtocol.MaxStringBytes) throw new ProtocolException("Chuỗi quá dài (" + b.Length + " byte).");
            ms.WriteByte((byte)b.Length);
            ms.Write(b, 0, b.Length);
        }

        public byte[] ToArray() { return ms.ToArray(); }
    }

    public sealed class BinReader
    {
        readonly byte[] buf;
        int pos;
        readonly int end;

        public BinReader(byte[] data, int offset, int length)
        {
            if (data == null || offset < 0 || length < 0 || offset + length > data.Length) throw new ProtocolException("Vùng đọc không hợp lệ.");
            buf = data; pos = offset; end = offset + length;
        }

        public int Remaining { get { return end - pos; } }

        void Need(int n)
        {
            if (n < 0 || pos + n > end) throw new ProtocolException("Thông điệp bị cụt.");
        }

        public int U8() { Need(1); return buf[pos++]; }
        public int I8() { Need(1); return unchecked((sbyte)buf[pos++]); }
        public int U16() { Need(2); int v = buf[pos] | (buf[pos + 1] << 8); pos += 2; return v; }

        public uint U32()
        {
            Need(4);
            uint v = 0;
            for (int i = 0; i < 4; i++) v |= (uint)buf[pos + i] << (8 * i);
            pos += 4;
            return v;
        }

        public ulong U64()
        {
            Need(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++) v |= (ulong)buf[pos + i] << (8 * i);
            pos += 8;
            return v;
        }

        public bool Bool()
        {
            int b = U8();
            if (b > 1) throw new ProtocolException("Giá trị bool không hợp lệ: " + b);
            return b == 1;
        }

        public string Str()
        {
            int n = U8();
            if (n > OnlineProtocol.MaxStringBytes) throw new ProtocolException("Chuỗi quá dài.");
            Need(n);
            string s;
            try { s = new UTF8Encoding(false, true).GetString(buf, pos, n); }
            catch (ArgumentException) { throw new ProtocolException("Chuỗi UTF-8 không hợp lệ."); }
            pos += n;
            return s;
        }
    }
}
