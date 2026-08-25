using System.Runtime.CompilerServices;
using Xunit;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace AgentIntegrationTests;

internal static class TestEnvironment
{
    internal static string Root { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Initialize()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "CaoDataAgentIntegrationTests",
            Environment.ProcessId.ToString());
        Directory.CreateDirectory(Root);
        Environment.SetEnvironmentVariable("CAODATA_CONTROL_DATA_ROOT", Path.Combine(Root, "ControlData"));
        Environment.SetEnvironmentVariable("CAODATA_AGENT_DATA_ROOT", Path.Combine(Root, "AgentData"));
        Environment.SetEnvironmentVariable(
            "CAODATA_SHARED_KEY",
            "AgentIntegrationTests-Shared-Key-2026-08-25-At-Least-32-Chars");
    }

    internal static string CreateDirectory(string name)
    {
        string path = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
