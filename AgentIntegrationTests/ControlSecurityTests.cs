using AgentControl;
using AgentShared;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;

namespace AgentIntegrationTests;

public sealed class ControlSecurityTests
{
    [Fact]
    public async Task PersistedTransportCertificate_CanAuthenticateSchannelServer()
    {
        ControlSecurityConfiguration configuration = ControlSecurityConfiguration.Load();
        using (X509Certificate2 initialCertificate = configuration.GetOrCreateServerCertificate())
        {
            Assert.True(initialCertificate.HasPrivateKey);
        }

        // Nap lai tu PFX tren dia de test dung duong chay cua nhung lan mo Control sau.
        using X509Certificate2 certificate = configuration.GetOrCreateServerCertificate();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        Task<SecureServerConnection> serverTask = Task.Run(async () =>
        {
            TcpClient accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            try
            {
                return await SecureTransport.AuthenticateServerAsync(
                    accepted.GetStream(),
                    certificate,
                    configuration.SharedKey,
                    timeout.Token);
            }
            catch
            {
                accepted.Dispose();
                throw;
            }
        }, timeout.Token);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await using Stream clientStream = await SecureTransport.AuthenticateClientAsync(
            client.GetStream(),
            "localhost",
            "AGT-PFX-TEST",
            configuration.SharedKey,
            timeout.Token);
        using SecureServerConnection server = await serverTask;

        Assert.Equal("AGT-PFX-TEST", server.AuthenticatedAgentId);
    }

    [Fact]
    public void CorruptTransportCertificate_IsQuarantinedAndRegenerated()
    {
        string certificatePath = ControlDataPaths.ServerCertificatePath;
        Directory.CreateDirectory(Path.GetDirectoryName(certificatePath)!);
        File.WriteAllBytes(certificatePath, "not-a-pfx"u8.ToArray());

        ControlSecurityConfiguration configuration = ControlSecurityConfiguration.Load();
        using X509Certificate2 certificate = configuration.GetOrCreateServerCertificate();

        Assert.True(certificate.HasPrivateKey);
        Assert.True(File.Exists(certificatePath));
        Assert.Single(Directory.GetFiles(
            Path.GetDirectoryName(certificatePath)!,
            Path.GetFileName(certificatePath) + ".corrupt-*"));
    }
}
