using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace AgentService
{
    public static class HardwareInfo
    {
        public static string GetUniqueAgentID()
        {
            string rawId = string.Empty;

            try
            {
                using (RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    rawId = key?.GetValue("MachineGuid")?.ToString() ?? string.Empty;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(rawId))
            {
                rawId = $"{Environment.MachineName}|{Environment.UserDomainName}|{Environment.OSVersion.VersionString}";
            }

            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(rawId));
                string hashString = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
                return $"AGT-{hashString.Substring(0, 5)}-{hashString.Substring(5, 5)}";
            }
        }
    }
}
