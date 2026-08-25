using System.Drawing;

namespace AgentIntegrationTests;

public sealed class ShellIconTests
{
    [Fact]
    public void ShellIcon_LoadsDirectoryAndFileTypeIcons()
    {
        using Icon directoryIcon = ShellIcon.GetSmallIcon(Environment.SystemDirectory, isDirectory: true);
        using Icon fileIcon = ShellIcon.GetSmallIcon("sample.txt", isDirectory: false);

        Assert.True(directoryIcon.Width > 0 && directoryIcon.Height > 0);
        Assert.True(fileIcon.Width > 0 && fileIcon.Height > 0);
    }
}
