using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using TheLastKing.Online;

namespace TheLastKing.Server
{
    public static class Program
    {
        const string Usage = @"THE LAST KING — máy chủ PvP (giao thức v{0}, bộ luật {1})

Cách dùng:  dotnet LastKingServer.dll [tùy chọn]

  --port N               Cổng TCP lắng nghe (mặc định 7777)
  --bind ĐỊA_CHỈ         Địa chỉ gắn (mặc định 0.0.0.0 = mọi giao diện)
  --tls                  Bật TLS (chứng chỉ tự ký được tạo/đọc từ --cert)
  --cert TỆP.pfx         Tệp chứng chỉ (mặc định server-cert.pfx cạnh máy chủ)
  --cert-password MK     Mật khẩu PFX (mặc định lastking)
  --selection-seconds N  Thời gian chọn bài mỗi lượt (mặc định 25)
  --reconnect-seconds N  Thời gian giữ chỗ khi một người đứt mạng giữa trận (mặc định 25)
  --max-rooms N          Số phòng tối đa (mặc định 128)
  --max-connections N    Số kết nối tối đa (mặc định 256)
  --max-per-ip N         Số kết nối tối đa từ một địa chỉ IP (mặc định 32)
  --match-log TỆP.csv    Ghi mỗi trận một dòng CSV (phục vụ phân tích cân bằng ghế)
  --quiet                Chỉ in cảnh báo/lỗi chính
  --help                 Hiện trợ giúp";

        public static int Main(string[] args)
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { }   // tiếng Việt đúng dấu trong cmd/PowerShell
            var opt = new ServerOptions();
            bool quiet = false;
            string certPath = null, certPassword = "lastking";
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    switch (a)
                    {
                        case "--help": case "-h": case "/?":
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, Usage, OnlineProtocol.Version, OnlineProtocol.RulesVersion));
                            return 0;
                        case "--port": opt.Port = ParseInt(args, ref i, 0, 65535); break;
                        case "--bind": opt.BindAddress = Next(args, ref i); break;
                        case "--tls": opt.UseTls = true; break;
                        case "--cert": certPath = Next(args, ref i); break;
                        case "--cert-password": certPassword = Next(args, ref i); break;
                        case "--selection-seconds": opt.SelectionMs = ParseInt(args, ref i, 3, 600) * 1000; break;
                        case "--reconnect-seconds": opt.ReconnectGraceMs = ParseInt(args, ref i, 0, 600) * 1000; break;
                        case "--max-rooms": opt.MaxRooms = ParseInt(args, ref i, 1, 100000); break;
                        case "--max-connections": opt.MaxConnections = ParseInt(args, ref i, 2, 100000); break;
                        case "--max-per-ip": opt.MaxConnectionsPerIp = ParseInt(args, ref i, 1, 100000); break;
                        case "--match-log": opt.MatchLogPath = Next(args, ref i); break;
                        case "--quiet": quiet = true; break;
                        default:
                            Console.Error.WriteLine("Tùy chọn không biết: " + a + "  (dùng --help)");
                            return 2;
                    }
                }
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }

            opt.Log = s => { if (!quiet || s.Contains("LỖI") || s.Contains("Lỗi")) Console.WriteLine(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + s); };

            if (opt.UseTls)
            {
                certPath = certPath ?? Path.Combine(AppContext.BaseDirectory, "server-cert.pfx");
                opt.Certificate = CertificateFactory.LoadOrCreate(certPath, certPassword);
                Console.WriteLine("TLS bật. Dấu vân tay chứng chỉ (SHA-256): " + CertificateFactory.Fingerprint(opt.Certificate));
            }

            var server = new GameServer(opt);
            try { server.Start(); }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Không khởi động được máy chủ: " + ex.Message);
                return 1;
            }

            var quit = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (s, e) => { e.Cancel = true; quit.Set(); };
            try { PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; quit.Set(); }); } catch { }

            Console.WriteLine("Sẵn sàng. Nhấn Ctrl+C để tắt.");
            long lastStats = Environment.TickCount64;
            while (!quit.Wait(1000))
            {
                if (Environment.TickCount64 - lastStats >= 5 * 60 * 1000)
                {
                    lastStats = Environment.TickCount64;
                    opt.Log("Thống kê: " + server.ConnectionCount + " kết nối, " + server.RoomCount + " phòng, trận bắt đầu/xong/hủy = " +
                            server.MatchesStarted + "/" + server.MatchesFinished + "/" + server.MatchesAborted + ", nối lại " + server.Resumes + ".");
                }
            }
            server.Stop();
            Console.WriteLine("Đã tắt.");
            return 0;
        }

        static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Thiếu giá trị sau " + args[i] + ".");
            return args[++i];
        }

        static int ParseInt(string[] args, ref int i, int min, int max)
        {
            string name = args[i];
            string v = Next(args, ref i);
            int n;
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max)
                throw new ArgumentException(name + " cần số nguyên trong khoảng " + min + ".." + max + ".");
            return n;
        }
    }
}
