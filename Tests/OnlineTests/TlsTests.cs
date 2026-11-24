using System;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using TheLastKing.Online;
using TheLastKing.Server;

namespace TheLastKing.Tests
{
    /// <summary>TLS tùy chọn với chứng chỉ tự ký + ghim dấu vân tay kiểu "tin ở lần đầu" (TOFU).</summary>
    public static class TlsTests
    {
        public static void Register()
        {
            Registry.Add("tls.full-match-over-tls-with-pinned-fingerprint", FullMatch);
            Registry.Add("tls.pin-mismatch-is-rejected", PinMismatch);
            Registry.Add("tls.plain-client-against-tls-server-fails-cleanly", PlainToTls);
            Registry.Add("tls.tls-client-against-plain-server-fails-cleanly", TlsToPlain);
            Registry.Add("tls.resume-works-over-tls", ResumeOverTls);
        }

        static void FullMatch()
        {
            X509Certificate2 cert = CertificateFactory.CreateSelfSigned();
            string fp = CertificateFactory.Fingerprint(cert);
            using (var srv = new TestServer(o => { o.UseTls = true; o.Certificate = cert; o.SelectionMs = 3000; o.PresentationMaxMs = 3000; }))
            {
                string seen = null;
                Func<string, string, bool> pin = (host, f) => { seen = f; return f == fp; };
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code, "Alice", "Bob", true, pin), "hai client vào cùng phòng qua TLS");
                using (a) using (b)
                {
                    T.Eq(fp, seen, "dấu vân tay chứng chỉ client thấy = dấu vân tay của server");
                    T.Eq(fp, a.Get(() => a.Client.ServerFingerprint), "client lưu lại dấu vân tay để ghim");
                    var ba = new Bot(a, 1); var bb = new Bot(b, 2);
                    Pairs.ReadyBoth(a, b);
                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 10000), "trận hoàn chỉnh chạy qua TLS");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương giống nhau");
                }
                T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
            }
        }

        static void PinMismatch()
        {
            X509Certificate2 cert = CertificateFactory.CreateSelfSigned();
            using (var srv = new TestServer(o => { o.UseTls = true; o.Certificate = cert; }))
            using (var c = new TestClient("Paranoid").Connect(srv.Port, true, (h, f) => false))
            {
                var e = c.WaitEvent(OnlineEventKind.ConnectFailed, 5000);
                T.Check(e != null, "dấu vân tay không khớp => ConnectFailed");
                T.Check(e != null && e.Text.IndexOf("TLS", StringComparison.OrdinalIgnoreCase) >= 0, "thông báo nhắc tới TLS/chứng chỉ: " + (e != null ? e.Text : "null"));
                T.Check(c.Count(OnlineEventKind.Connected) == 0, "không bao giờ coi là đã kết nối");
                T.Eq(0, srv.Server.ConnectionCount, "server không giữ kết nối thừa (sau khi dọn)");
            }
        }

        static void PlainToTls()
        {
            X509Certificate2 cert = CertificateFactory.CreateSelfSigned();
            using (var srv = new TestServer(o => { o.UseTls = true; o.Certificate = cert; o.HandshakeTimeoutMs = 1000; }))
            {
                using (var c = new TestClient("Plain").Connect(srv.Port, false))
                {
                    var e = c.WaitEvent(OnlineEventKind.ConnectFailed, 5000);
                    T.Check(e != null, "client không TLS tới server TLS => ConnectFailed, không treo");
                    T.Check(c.Count(OnlineEventKind.Connected) == 0, "không bao giờ Connected");
                }
                // server vẫn phục vụ client TLS hợp lệ
                using (var ok = new TestClient("Ok").Connect(srv.Port, true, (h, f) => true))
                    T.Check(ok.WaitEvent(OnlineEventKind.Connected, 5000) != null, "client TLS hợp lệ vẫn vào được sau đó");
            }
        }

        static void TlsToPlain()
        {
            using (var srv = new TestServer(o => { o.HandshakeTimeoutMs = 800; }))
            {
                using (var c = new TestClient("Tls").Connect(srv.Port, true, (h, f) => true))
                {
                    var e = c.WaitEvent(OnlineEventKind.ConnectFailed, 8000);
                    T.Check(e != null, "client TLS tới server không TLS => ConnectFailed, không treo");
                }
                using (var ok = new TestClient("Ok").Connect(srv.Port))
                    T.Check(ok.WaitEvent(OnlineEventKind.Connected, 3000) != null, "client thường vẫn vào được sau đó");
                T.Check(srv.ErrorLines.Count == 0, "server không ghi LỖI");
            }
        }

        static void ResumeOverTls()
        {
            X509Certificate2 cert = CertificateFactory.CreateSelfSigned();
            using (var srv = new TestServer(o => { o.UseTls = true; o.Certificate = cert; o.SelectionMs = 1500; o.ReconnectGraceMs = 5000; }))
            {
                TestClient a, b; string code;
                T.Check(Pairs.MakePair(srv, out a, out b, out code, "Alice", "Bob", true, (h, f) => true), "dựng cặp qua TLS");
                using (a) using (b)
                {
                    a.Do(() => a.Client.ReconnectInitialDelayMs = 200);
                    var ba = new Bot(a, 7) { AutoLock = false }; var bb = new Bot(b, 8);
                    Pairs.ReadyBoth(a, b);
                    T.Check(a.WaitEvent(OnlineEventKind.TurnBegan, 3000) != null, "lượt 1");
                    a.KillSocket(); ba.AutoLock = true;
                    T.Check(a.WaitEvent(OnlineEventKind.Resynced, 8000) != null, "nối lại được qua TLS và nhận Resync");
                    T.Check(T.WaitUntil(() => ba.FinishedMatches >= 1 && bb.FinishedMatches >= 1, 15000), "trận chạy tiếp tới khi kết thúc");
                    T.Eq(MatchStateCodec.Hash(a.Get(() => a.Client.Mirror.State)), MatchStateCodec.Hash(b.Get(() => b.Client.Mirror.State)), "hai gương giống nhau");
                }
            }
        }
    }
}
