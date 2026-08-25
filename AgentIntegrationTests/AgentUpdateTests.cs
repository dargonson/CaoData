using AgentService;
using AgentShared;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Cryptography;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class AgentUpdateTests
{
    [Fact]
    public void UpdatePaths_UseAgentDataRoot_AndRejectTraversal()
    {
        string expected = Path.Combine(
            Environment.GetEnvironmentVariable("CAODATA_AGENT_DATA_ROOT")!,
            "Updates");
        Assert.Equal(Path.GetFullPath(expected), AppVersion.GetAgentUpdateRootDirectory());
        Assert.Throws<InvalidDataException>(() =>
            AppVersion.GetAgentUpdateSessionDirectory(@"..\outside"));
    }

    [Fact]
    public async Task UpdateClient_RejectsWrongOffset_ThenReceivesAndVerifiesCleanFile()
    {
        string sessionId = Guid.NewGuid().ToString("N");
        byte[] serviceBytes = RandomNumberGenerator.GetBytes(700_123);
        byte[] updaterBytes = RandomNumberGenerator.GetBytes(64);
        string serviceHash = Convert.ToHexString(SHA256.HashData(serviceBytes));
        string updaterHash = Convert.ToHexString(SHA256.HashData(updaterBytes));
        List<AgentUpdateStatus> statuses = new();
        var client = new AgentUpdateClient(
            packet =>
            {
                statuses.Add(JsonSerializer.Deserialize<AgentUpdateStatus>(packet.Data)!);
                return Task.CompletedTask;
            },
            () => ("127.0.0.1", 9000),
            NullLogger.Instance);
        var request = new AgentUpdateRequest
        {
            SessionId = sessionId,
            TargetVersion = "1.8-test",
            ServiceFileName = "AgentServices.exe",
            ServiceFileSize = serviceBytes.Length,
            ServiceSha256 = serviceHash,
            UpdaterFileName = "AgentUpdater.exe",
            UpdaterFileSize = updaterBytes.Length,
            UpdaterSha256 = updaterHash
        };

        await client.HandlePacketAsync(Packet(
            AgentUpdatePacketTypes.UpdateAgent,
            JsonSerializer.Serialize(request)),
            CancellationToken.None);
        var begin = new AgentUpdateFileBegin
        {
            SessionId = sessionId,
            Role = AgentUpdateFileRoles.Service,
            FileName = request.ServiceFileName,
            TotalBytes = request.ServiceFileSize,
            Sha256 = request.ServiceSha256
        };
        await client.HandlePacketAsync(Packet(
            AgentUpdatePacketTypes.UpdateAgentFileBegin,
            JsonSerializer.Serialize(begin)),
            CancellationToken.None);

        await client.HandlePacketAsync(Packet(
            AgentUpdatePacketTypes.UpdateAgentFileChunk,
            JsonSerializer.Serialize(new AgentUpdateFileChunk
            {
                SessionId = sessionId,
                Role = AgentUpdateFileRoles.Service,
                Offset = 10,
                Base64Data = Convert.ToBase64String(serviceBytes, 0, 100)
            })),
            CancellationToken.None);
        Assert.Contains(statuses, status =>
            status.Status == "Error" && status.Message.Contains("Offset", StringComparison.OrdinalIgnoreCase));

        // Begin lai se reset file tam, sau do nhan dung hai chunk lien tiep.
        await client.HandlePacketAsync(Packet(
            AgentUpdatePacketTypes.UpdateAgentFileBegin,
            JsonSerializer.Serialize(begin)),
            CancellationToken.None);
        int firstChunkSize = 512 * 1024;
        await SendChunkAsync(client, sessionId, serviceBytes, 0, firstChunkSize);
        await SendChunkAsync(
            client,
            sessionId,
            serviceBytes,
            firstChunkSize,
            serviceBytes.Length - firstChunkSize);
        await client.HandlePacketAsync(Packet(
            AgentUpdatePacketTypes.UpdateAgentFileEnd,
            JsonSerializer.Serialize(new AgentUpdateFileEnd
            {
                SessionId = sessionId,
                Role = AgentUpdateFileRoles.Service
            })),
            CancellationToken.None);

        string receivedPath = Path.Combine(
            AppVersion.GetAgentUpdateSessionDirectory(sessionId),
            request.ServiceFileName);
        Assert.Equal(serviceBytes, await File.ReadAllBytesAsync(receivedPath));
        Assert.Contains(statuses, status => status.Status == "Downloaded");
    }

    private static Task SendChunkAsync(
        AgentUpdateClient client,
        string sessionId,
        byte[] source,
        int offset,
        int count)
    {
        return client.HandlePacketAsync(Packet(
            AgentUpdatePacketTypes.UpdateAgentFileChunk,
            JsonSerializer.Serialize(new AgentUpdateFileChunk
            {
                SessionId = sessionId,
                Role = AgentUpdateFileRoles.Service,
                Offset = offset,
                Base64Data = Convert.ToBase64String(source, offset, count)
            })),
            CancellationToken.None);
    }

    private static SocketPacket Packet(string type, string data) => new()
    {
        Type = type,
        AgentID = "AGT-UPDATE-TEST",
        Data = data
    };
}
