using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace AgentShared
{
    /// <summary>
    /// TLS + mutual pre-shared-key proof bound to the TLS session. The certificate
    /// may be self-signed because the channel-bound PSK proof authenticates both peers.
    /// </summary>
    public static class SecureTransport
    {
        private const int NonceSize = 32;
        private const int MaxHandshakeMessageSize = 16 * 1024;

        public static async Task<SslStream> AuthenticateClientAsync(
            Stream transport,
            string targetHost,
            string agentId,
            string sharedKey,
            CancellationToken token = default)
        {
            ValidateAgentId(agentId);
            byte[] key = DeriveKey(sharedKey);
            var ssl = new SslStream(
                transport,
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) => certificate != null);

            try
            {
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = string.IsNullOrWhiteSpace(targetHost) ? "AgentControl" : targetHost,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, token);

                byte[] clientNonce = RandomNumberGenerator.GetBytes(NonceSize);
                await WriteMessageAsync(ssl, new ClientHello
                {
                    AgentId = agentId,
                    ClientNonce = Convert.ToBase64String(clientNonce)
                }, token);

                ServerChallenge challenge = await ReadMessageAsync<ServerChallenge>(ssl, token);
                byte[] serverNonce = ParseNonce(challenge.ServerNonce);
                byte[] channelBinding = GetCertificateBinding(ssl.RemoteCertificate);
                byte[] expectedServerProof = CreateProof(
                    key, "SERVER", channelBinding, agentId, clientNonce, serverNonce);
                byte[] actualServerProof = ParseProof(challenge.Proof);
                if (!CryptographicOperations.FixedTimeEquals(expectedServerProof, actualServerProof))
                {
                    throw new AuthenticationException("AgentControl không chứng minh được khóa kết nối.");
                }

                byte[] clientProof = CreateProof(
                    key, "CLIENT", channelBinding, agentId, clientNonce, serverNonce);
                await WriteMessageAsync(ssl, new ClientProof
                {
                    Proof = Convert.ToBase64String(clientProof)
                }, token);
                return ssl;
            }
            catch
            {
                ssl.Dispose();
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public static async Task<SecureServerConnection> AuthenticateServerAsync(
            Stream transport,
            X509Certificate2 serverCertificate,
            string sharedKey,
            CancellationToken token = default)
        {
            byte[] key = DeriveKey(sharedKey);
            var ssl = new SslStream(transport, leaveInnerStreamOpen: false);
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = serverCertificate,
                    ClientCertificateRequired = false,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, token);

                ClientHello hello = await ReadMessageAsync<ClientHello>(ssl, token);
                ValidateAgentId(hello.AgentId);
                byte[] clientNonce = ParseNonce(hello.ClientNonce);
                byte[] serverNonce = RandomNumberGenerator.GetBytes(NonceSize);
                byte[] channelBinding = SHA256.HashData(serverCertificate.RawData);
                byte[] serverProof = CreateProof(
                    key, "SERVER", channelBinding, hello.AgentId, clientNonce, serverNonce);
                await WriteMessageAsync(ssl, new ServerChallenge
                {
                    ServerNonce = Convert.ToBase64String(serverNonce),
                    Proof = Convert.ToBase64String(serverProof)
                }, token);

                ClientProof proof = await ReadMessageAsync<ClientProof>(ssl, token);
                byte[] expectedClientProof = CreateProof(
                    key, "CLIENT", channelBinding, hello.AgentId, clientNonce, serverNonce);
                byte[] actualClientProof = ParseProof(proof.Proof);
                if (!CryptographicOperations.FixedTimeEquals(expectedClientProof, actualClientProof))
                {
                    throw new AuthenticationException("Agent không chứng minh được khóa kết nối.");
                }

                return new SecureServerConnection(ssl, hello.AgentId);
            }
            catch
            {
                ssl.Dispose();
                throw;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        private static byte[] DeriveKey(string sharedKey)
        {
            string value = (sharedKey ?? string.Empty).Trim();
            if (value.Length < 32)
            {
                throw new InvalidOperationException(
                    "ConnectionConfig:SharedKey phải có ít nhất 32 ký tự.");
            }
            return SHA256.HashData(Encoding.UTF8.GetBytes(value));
        }

        private static byte[] GetCertificateBinding(X509Certificate? certificate)
        {
            if (certificate == null)
            {
                throw new AuthenticationException("AgentControl không cung cấp chứng chỉ TLS.");
            }
            return SHA256.HashData(certificate.GetRawCertData());
        }

        private static byte[] CreateProof(
            byte[] key,
            string role,
            byte[] channelBinding,
            string agentId,
            byte[] clientNonce,
            byte[] serverNonce)
        {
            using MemoryStream data = new();
            WritePart(data, Encoding.UTF8.GetBytes(role));
            WritePart(data, channelBinding);
            WritePart(data, Encoding.UTF8.GetBytes(agentId));
            WritePart(data, clientNonce);
            WritePart(data, serverNonce);
            return HMACSHA256.HashData(key, data.ToArray());
        }

        private static void WritePart(Stream stream, byte[] value)
        {
            stream.Write(BitConverter.GetBytes(value.Length));
            stream.Write(value);
        }

        private static async Task WriteMessageAsync<T>(Stream stream, T message, CancellationToken token)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
            if (payload.Length <= 0 || payload.Length > MaxHandshakeMessageSize)
            {
                throw new InvalidDataException("Kích thước gói xác thực không hợp lệ.");
            }
            await stream.WriteAsync(BitConverter.GetBytes(payload.Length), token);
            await stream.WriteAsync(payload, token);
            await stream.FlushAsync(token);
        }

        private static async Task<T> ReadMessageAsync<T>(Stream stream, CancellationToken token)
        {
            byte[] lengthBytes = new byte[4];
            await TransferFrameProtocol.ReadExactAsync(stream, lengthBytes, 0, lengthBytes.Length, token);
            int length = BitConverter.ToInt32(lengthBytes);
            if (length <= 0 || length > MaxHandshakeMessageSize)
            {
                throw new InvalidDataException("Kích thước gói xác thực không hợp lệ.");
            }
            byte[] payload = new byte[length];
            await TransferFrameProtocol.ReadExactAsync(stream, payload, 0, payload.Length, token);
            return JsonSerializer.Deserialize<T>(payload)
                ?? throw new InvalidDataException("Không đọc được gói xác thực.");
        }

        private static byte[] ParseNonce(string value)
        {
            byte[] nonce;
            try { nonce = Convert.FromBase64String(value ?? string.Empty); }
            catch (FormatException ex) { throw new AuthenticationException("Nonce xác thực không hợp lệ.", ex); }
            if (nonce.Length != NonceSize)
            {
                throw new AuthenticationException("Nonce xác thực không đúng kích thước.");
            }
            return nonce;
        }

        private static byte[] ParseProof(string value)
        {
            byte[] proof;
            try { proof = Convert.FromBase64String(value ?? string.Empty); }
            catch (FormatException ex) { throw new AuthenticationException("Proof xác thực không hợp lệ.", ex); }
            if (proof.Length != 32)
            {
                throw new AuthenticationException("Proof xác thực không đúng kích thước.");
            }
            return proof;
        }

        private static void ValidateAgentId(string agentId)
        {
            if (string.IsNullOrWhiteSpace(agentId) || agentId.Length > 128 ||
                agentId.Any(ch => char.IsControl(ch)))
            {
                throw new AuthenticationException("AgentID xác thực không hợp lệ.");
            }
        }

        private sealed class ClientHello
        {
            public string AgentId { get; set; } = string.Empty;
            public string ClientNonce { get; set; } = string.Empty;
        }

        private sealed class ServerChallenge
        {
            public string ServerNonce { get; set; } = string.Empty;
            public string Proof { get; set; } = string.Empty;
        }

        private sealed class ClientProof
        {
            public string Proof { get; set; } = string.Empty;
        }
    }

    public sealed class SecureServerConnection : IDisposable
    {
        public SslStream Stream { get; }
        public string AuthenticatedAgentId { get; }

        public SecureServerConnection(SslStream stream, string authenticatedAgentId)
        {
            Stream = stream;
            AuthenticatedAgentId = authenticatedAgentId;
        }

        public void Dispose() => Stream.Dispose();
    }
}
