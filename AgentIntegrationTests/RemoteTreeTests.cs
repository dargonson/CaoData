using AgentControl;
using AgentService;
using System.Text.Json;

namespace AgentIntegrationTests;

public sealed class RemoteTreeTests
{
    [Fact]
    public void LoadingPlaceholder_IsHiddenOnlyWhenAgentConfirmsFolderIsLeaf()
    {
        Assert.True(frmToolBackup.ShouldAddRemoteLoadingPlaceholder(null));
        Assert.True(frmToolBackup.ShouldAddRemoteLoadingPlaceholder(true));
        Assert.False(frmToolBackup.ShouldAddRemoteLoadingPlaceholder(false));
    }

    [Fact]
    public void DirectoryInspector_DetectsVisibleChildAndLeafFolder()
    {
        string root = TestEnvironment.CreateDirectory("remote-tree-inspector");
        string parent = Directory.CreateDirectory(Path.Combine(root, "parent")).FullName;
        string leaf = Directory.CreateDirectory(Path.Combine(parent, "leaf")).FullName;

        Assert.True(RemoteDirectoryInspector.HasVisibleSubdirectories(parent));
        Assert.False(RemoteDirectoryInspector.HasVisibleSubdirectories(leaf));
    }

    [Fact]
    public void HasSubDirectories_RemainsCompatibleWithLegacyAgentPayload()
    {
        RemoteFileSystemEntry? current = JsonSerializer.Deserialize<RemoteFileSystemEntry>(
            """{"FullPath":"D:\\leaf","Name":"leaf","IsFolder":true,"HasSubDirectories":false}""");
        RemoteFileSystemEntry? legacy = JsonSerializer.Deserialize<RemoteFileSystemEntry>(
            """{"FullPath":"D:\\folder","Name":"folder","IsFolder":true}""");

        Assert.False(current!.HasSubDirectories);
        Assert.Null(legacy!.HasSubDirectories);
        Assert.True(frmToolBackup.ShouldAddRemoteLoadingPlaceholder(legacy.HasSubDirectories));
    }
}
