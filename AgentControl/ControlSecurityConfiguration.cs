using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace AgentControl
{
    internal sealed class ControlSecurityConfiguration
    {
        public string SharedKey { get; }
        public TimeSpan HandshakeTimeout { get; }

        private ControlSecurityConfiguration(string sharedKey, TimeSpan handshakeTimeout)
        {
            SharedKey = sharedKey;
            HandshakeTimeout = handshakeTimeout;
        }

        public static ControlSecurityConfiguration Load()
        {
            string sharedKey = Environment.GetEnvironmentVariable("CAODATA_SHARED_KEY") ?? string.Empty;
            int timeoutSeconds = 15;
            string path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (File.Exists(path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
                if (document.RootElement.TryGetProperty("ConnectionSecurity", out JsonElement section))
                {
                    if (string.IsNullOrWhiteSpace(sharedKey) &&
                        section.TryGetProperty("SharedKey", out JsonElement keyElement))
                    {
                        sharedKey = keyElement.GetString() ?? string.Empty;
                    }
                    if (section.TryGetProperty("HandshakeTimeoutSeconds", out JsonElement timeoutElement) &&
                        timeoutElement.TryGetInt32(out int configuredTimeout))
                    {
                        timeoutSeconds = Math.Clamp(configuredTimeout, 5, 60);
                    }
                }
            }

            if (sharedKey.Trim().Length < 32)
            {
                throw new InvalidOperationException(
                    "ConnectionSecurity:SharedKey chưa được cấu hình hoặc ngắn hơn 32 ký tự.");
            }
            return new ControlSecurityConfiguration(sharedKey.Trim(), TimeSpan.FromSeconds(timeoutSeconds));
        }

        public X509Certificate2 GetOrCreateServerCertificate()
        {
            string path = ControlDataPaths.ServerCertificatePath;
            if (File.Exists(path))
            {
                try
                {
                    return LoadCertificate(path);
                }
                catch (CryptographicException)
                {
                    // File co the bi cat ngan neu mat dien dung luc tao lan dau. Giu lai de
                    // chan doan va tao cert moi; PSK van la danh tinh chinh cua ket noi.
                    string corruptPath = path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                    File.Move(path, corruptPath);
                }
            }

            using RSA rsa = RSA.Create(3072);
            CertificateRequest request = new CertificateRequest(
                "CN=CaoData AgentControl",
                rsa,
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));
            OidCollection usages = new OidCollection
            {
                new Oid("1.3.6.1.5.5.7.3.1")
            };
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
            SubjectAlternativeNameBuilder san = new SubjectAlternativeNameBuilder();
            san.AddDnsName("localhost");
            san.AddDnsName(Environment.MachineName);
            request.CertificateExtensions.Add(san.Build());

            using X509Certificate2 generated = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddYears(10));
            byte[] pfx = generated.Export(X509ContentType.Pfx);
            string temporaryPath = path + ".tmp";
            using (FileStream destination = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough))
            {
                destination.Write(pfx);
                destination.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
            return new X509Certificate2(
                pfx,
                (string?)null,
                GetServerKeyStorageFlags());
        }

        private static X509Certificate2 LoadCertificate(string path) =>
            new X509Certificate2(
                path,
                (string?)null,
                GetServerKeyStorageFlags());

        private static X509KeyStorageFlags GetServerKeyStorageFlags()
        {
            // BO SUNG BAO MAT KET NOI: Windows Schannel can private key nam trong
            // key store de dung certificate lam TLS server credential. EphemeralKeySet
            // van bao HasPrivateKey=true nhung Schannel se dong handshake voi event 36869.
            return X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable;
        }
    }
}
