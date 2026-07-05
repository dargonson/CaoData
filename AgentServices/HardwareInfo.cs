using System;
using System.Management; // Cần add thêm reference hoặc NuGet System.Management nếu .NET mới báo thiếu

namespace AgentService
{
    public static class HardwareInfo
    {
        public static string GetUniqueAgentID()
        {
            try
            {
                string drive = Environment.GetFolderPath(Environment.SpecialFolder.System).Substring(0, 1);
                using (var dsk = new ManagementObject($"win32_logicaldisk.deviceid=\"{drive}:\""))
                {
                    dsk.Get();
                    string volumeSerial = dsk["VolumeSerialNumber"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(volumeSerial))
                    {
                        return "AGT-" + volumeSerial; // Trả về ID độc nhất dựa trên Serial ổ cứng cài hệ điều hành
                    }
                }
            }
            catch { }

            // Phương án dự phòng nếu không bốc được phần cứng thì tự đẻ GUID ngẫu nhiên
            return "AGT-GEN-" + Guid.NewGuid().ToString().Substring(0, 8).ToUpper();
        }
    }
}