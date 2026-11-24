// THE LAST KING — Online PvP: tầng truyền tải phía client. C# thuần (không UnityEngine) nên cùng một lớp
// được dùng trong game và trong bộ kiểm thử tích hợp với server thật.
//
// Một OnlineTransport = một vòng đời kết nối TCP. Muốn nối lại thì OnlineClient tạo transport mới.
// Luồng nền chỉ đọc/ghi socket và đẩy thông điệp đã giải mã vào hàng đợi; mọi logic chạy ở luồng chính qua TryReceive().
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace TheLastKing.Online
{
    public enum TransportState { Idle, Connecting, Connected, Closed, Failed }

    public sealed class OnlineTransport : IDisposable
    {
        readonly ConcurrentQueue<ServerMessage> inbox = new ConcurrentQueue<ServerMessage>();
        readonly BlockingCollection<byte[]> outbox = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 256);
        readonly Stopwatch clock = Stopwatch.StartNew();

        TcpClient tcp;
        Stream stream;
        volatile bool closing;
        volatile int state = (int)TransportState.Idle;
        volatile string error = "";
        long lastRecvMs;

        public int ConnectTimeoutMs = 5000;
        /// <summary>SHA-256 (hex) của chứng chỉ máy chủ khi dùng TLS; rỗng nếu chưa bắt tay.</summary>
        public string ServerFingerprint { get; private set; }

        public OnlineTransport() { ServerFingerprint = ""; }

        public TransportState State { get { return (TransportState)state; } }
        public string Error { get { return error; } }
        public long MsSinceLastReceive { get { return clock.ElapsedMilliseconds - Interlocked.Read(ref lastRecvMs); } }

        /// <summary>
        /// Kết nối bất đồng bộ. <paramref name="certCheck"/>(host, sha256Hex) trả true để chấp nhận chứng chỉ (TLS tự ký, kiểu "tin lần đầu").
        /// Nếu certCheck = null thì chỉ chấp nhận chứng chỉ hợp lệ theo hệ điều hành.
        /// </summary>
        public void Connect(string host, int port, bool useTls, Func<string, string, bool> certCheck)
        {
            if (State != TransportState.Idle) throw new InvalidOperationException("Transport đã được dùng; hãy tạo cái mới.");
            state = (int)TransportState.Connecting;
            var t = new Thread(() => ConnectWorker(host, port, useTls, certCheck)) { IsBackground = true, Name = "lk-connect" };
            t.Start();
        }

        void ConnectWorker(string host, int port, bool useTls, Func<string, string, bool> certCheck)
        {
            try
            {
                var client = new TcpClient(AddressFamily.InterNetwork);
                tcp = client;
                client.NoDelay = true;
                IAsyncResult ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(ConnectTimeoutMs))
                {
                    try { client.Close(); } catch { }
                    throw new TimeoutException("Không truy cập được máy chủ (hết thời gian chờ).");
                }
                client.EndConnect(ar);
                try { client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true); } catch { }
                Stream s = client.GetStream();
                if (useTls)
                {
                    client.ReceiveTimeout = ConnectTimeoutMs; client.SendTimeout = ConnectTimeoutMs;   // bắt tay TLS không được treo vô hạn
                    var ssl = new SslStream(s, false, (sender, cert, chain, errors) =>
                    {
                        if (cert == null) return false;
                        string fp = Sha256Hex(cert.GetRawCertData());
                        ServerFingerprint = fp;
                        if (certCheck != null) return certCheck(host, fp);
                        return errors == SslPolicyErrors.None;
                    });
                    ssl.AuthenticateAsClient(host);
                    client.ReceiveTimeout = 0; client.SendTimeout = 0;
                    s = ssl;
                }
                if (closing) { try { s.Close(); } catch { } return; }   // người dùng đã hủy trong lúc đang kết nối
                stream = s;
                Interlocked.Exchange(ref lastRecvMs, clock.ElapsedMilliseconds);
                state = (int)TransportState.Connected;
                new Thread(ReadLoop) { IsBackground = true, Name = "lk-read" }.Start();
                new Thread(WriteLoop) { IsBackground = true, Name = "lk-write" }.Start();
            }
            catch (Exception ex)
            {
                if (!closing) Fail(Describe(ex), true);
            }
        }

        public static string Describe(Exception ex)
        {
            var se = ex as SocketException;
            if (se == null && ex.InnerException != null) se = ex.InnerException as SocketException;
            if (se != null)
            {
                switch (se.SocketErrorCode)
                {
                    case SocketError.ConnectionRefused: return "Máy chủ từ chối kết nối (sai IP/cổng hoặc server chưa chạy).";
                    case SocketError.HostNotFound:
                    case SocketError.TryAgain: return "Không tìm thấy máy chủ (sai địa chỉ).";
                    case SocketError.TimedOut: return "Không truy cập được máy chủ (hết thời gian chờ).";
                    case SocketError.NetworkUnreachable:
                    case SocketError.HostUnreachable: return "Không có đường tới máy chủ (kiểm tra mạng/tường lửa).";
                    default: return "Lỗi mạng: " + se.SocketErrorCode;
                }
            }
            if (ex is TimeoutException) return ex.Message;
            if (ex is System.Security.Authentication.AuthenticationException)
                return "Bắt tay TLS thất bại: chứng chỉ máy chủ không được chấp nhận (hoặc máy chủ không bật TLS).";
            if (ex is IOException && ex.InnerException is SocketException) return Describe(ex.InnerException);
            return ex.Message;
        }

        public static string Sha256Hex(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(data);
                var sb = new StringBuilder(h.Length * 2);
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        void ReadLoop()
        {
            try
            {
                while (!closing)
                {
                    byte[] body = OnlineProtocol.ReadFrameBody(stream);
                    if (body == null) { Fail("Máy chủ đã đóng kết nối.", false); return; }
                    var m = (ServerMessage)OnlineProtocol.Decode(body, true);
                    Interlocked.Exchange(ref lastRecvMs, clock.ElapsedMilliseconds);
                    inbox.Enqueue(m);
                }
            }
            catch (Exception ex)
            {
                if (!closing) Fail(ex is ProtocolException ? "Lỗi giao thức: " + ex.Message : "Mất kết nối: " + ex.Message, false);
            }
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
            catch (Exception ex)
            {
                if (!closing) Fail("Mất kết nối: " + ex.Message, false);
            }
        }

        void Fail(string message, bool whileConnecting)
        {
            if (State == TransportState.Closed || State == TransportState.Failed) return;
            error = message;
            state = (int)(whileConnecting ? TransportState.Failed : TransportState.Closed);
            closing = true;
            try { outbox.CompleteAdding(); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }

        /// <summary>Đưa thông điệp vào hàng đợi gửi. Trả false nếu chưa kết nối/đã đóng.</summary>
        public bool Send(ClientMessage msg)
        {
            if (State != TransportState.Connected || closing) return false;
            try { return outbox.TryAdd(OnlineProtocol.Encode(msg)); }
            catch (InvalidOperationException) { return false; }
        }

        public bool TryReceive(out ServerMessage msg) { return inbox.TryDequeue(out msg); }

        public void Dispose()
        {
            closing = true;
            if (State == TransportState.Connected || State == TransportState.Connecting) state = (int)TransportState.Closed;
            try { outbox.CompleteAdding(); } catch { }
            try { if (stream != null) stream.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }
    }
}
