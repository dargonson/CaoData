using AgentShared;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AgentIntegrationTests;

public sealed class SharedProtocolTests
{
    [Theory]
    [InlineData("..\\outside.txt")]
    [InlineData("folder\\..\\outside.txt")]
    [InlineData("\\absolute.txt")]
    [InlineData("C:\\absolute.txt")]
    [InlineData("folder\\.\\file.txt")]
    [InlineData("folder\\file.txt:stream")]
    [InlineData("folder\\CON.txt")]
    [InlineData("folder\\name. ")]
    [InlineData("folder\\\\file.txt")]
    public void PathSafety_RejectsTraversalAndRootedPaths(string path)
    {
        Assert.Throws<InvalidDataException>(() => PathSafety.NormalizeRelativePath(path));
    }

    [Fact]
    public void PathSafety_ReturnsContainedChild()
    {
        string root = TestEnvironment.CreateDirectory("path-root");
        string child = PathSafety.GetSafeChildPath(root, "D\\Data\\file.bin");

        Assert.StartsWith(Path.GetFullPath(root), child, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("D", "Data", "file.bin"), child, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PathSafety_PreservesValidLeadingSpaces()
    {
        string normalized = PathSafety.NormalizeRelativePath("D\\ folder\\ file.txt");

        Assert.Equal("D\\ folder\\ file.txt", normalized);
    }

    [Fact]
    public async Task BinaryFrame_RoundTripsHeaderAndBody()
    {
        byte[] body = RandomNumberGenerator.GetBytes(350_123);
        var header = new FileChunkPacket
        {
            AgentID = "AGT-TEST",
            DownloadID = Guid.NewGuid().ToString("N"),
            RemotePath = @"D:\\Data\\file.bin",
            TotalBytes = body.Length,
            Offset = 0,
            IsLastChunk = true,
            ChecksumAlgorithm = "SHA256",
            SourceChecksum = Convert.ToHexString(SHA256.HashData(body))
        };

        await using MemoryStream frame = new();
        await TransferFrameProtocol.WriteBinaryDownloadChunkAsync(frame, header, body, body.Length);
        frame.Position = 0;

        byte[] prefix = new byte[4];
        await TransferFrameProtocol.ReadExactAsync(frame, prefix, 0, prefix.Length);
        int frameSize = BitConverter.ToInt32(prefix);
        Assert.Equal(TransferFrameProtocol.BinaryDownloadChunkMarker, frame.ReadByte());

        (FileChunkPacket parsed, int bodySize) =
            await TransferFrameProtocol.ReadBinaryChunkHeaderAsync(frame, frameSize);
        await using MemoryStream copied = new();
        await TransferFrameProtocol.CopyExactToAsync(frame, copied, bodySize);

        Assert.Equal(header.DownloadID, parsed.DownloadID);
        Assert.Equal(body.Length, bodySize);
        Assert.Equal(body, copied.ToArray());
    }

    [Fact]
    public async Task FrameProtocol_RejectsOversizedPayloads()
    {
        var packet = new SocketPacket
        {
            Type = "OVERSIZED",
            Data = new string('X', TransferFrameProtocol.MaxFrameSize)
        };
        await Assert.ThrowsAsync<InvalidDataException>(
            () => TransferFrameProtocol.WriteJsonPacketAsync(Stream.Null, packet));

        Assert.Throws<InvalidDataException>(
            () => TransferFrameProtocol.ValidateFrameSize(TransferFrameProtocol.MaxFrameSize + 1));
    }

    [Fact]
    public async Task SecureTransport_AuthenticatesBothEndsAndCarriesData()
    {
        const string key = "Correct-Integration-Key-With-More-Than-Thirty-Two-Characters";
        using X509Certificate2 certificate = CreateCertificate();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        Task<SecureServerConnection> serverTask = Task.Run(async () =>
        {
            TcpClient accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            return await SecureTransport.AuthenticateServerAsync(
                accepted.GetStream(), certificate, key, timeout.Token);
        }, timeout.Token);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await using Stream clientStream = await SecureTransport.AuthenticateClientAsync(
            client.GetStream(), "localhost", "AGT-TLS-TEST", key, timeout.Token);
        using SecureServerConnection server = await serverTask;

        byte[] payload = RandomNumberGenerator.GetBytes(4096);
        await clientStream.WriteAsync(payload, timeout.Token);
        await clientStream.FlushAsync(timeout.Token);
        byte[] received = new byte[payload.Length];
        await TransferFrameProtocol.ReadExactAsync(
            server.Stream, received, 0, received.Length, timeout.Token);

        Assert.Equal("AGT-TLS-TEST", server.AuthenticatedAgentId);
        Assert.Equal(payload, received);
    }

    [Fact]
    public async Task SecureTransport_RejectsWrongSharedKey()
    {
        const string serverKey = "Server-Key-Integration-Test-More-Than-Thirty-Two-Characters";
        const string clientKey = "Client-Key-Integration-Test-More-Than-Thirty-Two-Characters";
        using X509Certificate2 certificate = CreateCertificate();
        using TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        Task serverTask = Task.Run(async () =>
        {
            using TcpClient accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            using SecureServerConnection connection = await SecureTransport.AuthenticateServerAsync(
                accepted.GetStream(), certificate, serverKey, timeout.Token);
        }, timeout.Token);

        using TcpClient client = new();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        await Assert.ThrowsAnyAsync<AuthenticationException>(async () =>
        {
            await using Stream _ = await SecureTransport.AuthenticateClientAsync(
                client.GetStream(), "localhost", "AGT-WRONG-KEY", clientKey, timeout.Token);
        });
        await Assert.ThrowsAnyAsync<Exception>(() => serverTask);
    }

    private static X509Certificate2 CreateCertificate()
    {
        using RSA rsa = RSA.Create(2048);
        CertificateRequest request = new(
            "CN=localhost",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        using X509Certificate2 generated = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1));
        return new X509Certificate2(generated.Export(X509ContentType.Pfx));
    }
}
