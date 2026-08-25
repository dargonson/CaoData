using AgentControl;
using System.Security.Cryptography.X509Certificates;

namespace AgentIntegrationTests;

public sealed class ControlSecurityTests
{
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
