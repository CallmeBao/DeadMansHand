using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TheLastKing.Server
{
    /// <summary>
    /// Tạo/đọc chứng chỉ TLS tự ký. Lưu ra tệp PFX để dấu vân tay ổn định giữa các lần chạy
    /// (client ghim dấu vân tay theo kiểu "tin ở lần đầu"). Có thể thay bằng chứng chỉ thật (--cert tệp.pfx --cert-password ...).
    /// </summary>
    public static class CertificateFactory
    {
        public static X509Certificate2 LoadOrCreate(string path, string password)
        {
            if (File.Exists(path))
                return new X509Certificate2(path, password, X509KeyStorageFlags.Exportable);
            X509Certificate2 cert = CreateSelfSigned();
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx, password));
            return cert;
        }

        public static X509Certificate2 CreateSelfSigned()
        {
            using (RSA rsa = RSA.Create(2048))
            {
                var req = new CertificateRequest("CN=LastKingServer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
                req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, false));
                req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
                var san = new SubjectAlternativeNameBuilder();
                san.AddDnsName("localhost");
                san.AddIpAddress(IPAddress.Loopback);
                req.CertificateExtensions.Add(san.Build());
                using (X509Certificate2 tmp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5)))
                {
                    // Nạp lại từ PFX để khóa riêng được lưu bền (SslStream trên Windows cần điều này).
                    return new X509Certificate2(tmp.Export(X509ContentType.Pfx, ""), "", X509KeyStorageFlags.Exportable);
                }
            }
        }

        public static string Fingerprint(X509Certificate2 cert)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(cert.RawData);
                var sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
