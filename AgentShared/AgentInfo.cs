using System;

namespace AgentShared
{
    public class AgentInfo
    {
        public string MachineName { get; set; } = string.Empty; // Tên máy (e.g., DOTHAI-LT)
        public string Username { get; set; } = string.Empty;    // Tên User hệ điều hành (e.g., DOTHAI)
        public string IPAddress { get; set; } = string.Empty;   // IP của Agent (e.g., 172.16.16.10)
        public string OSVersion { get; set; } = string.Empty;   // Hệ điều hành (e.g., Windows 10)
        public string AgentVersion { get; set; } = string.Empty;// Phiên bản phần mềm (e.g., 1.0.0)
    }
}