using System;

namespace PowerTerminal.Models
{
    public class MachineInfo
    {
        public string Hostname { get; set; } = string.Empty;
        public string OperatingSystem { get; set; } = string.Empty;
        public string OsVersion { get; set; } = string.Empty;
        public string KernelVersion { get; set; } = string.Empty;
        public string HomeFolder { get; set; } = string.Empty;
        public string CurrentDirectory { get; set; } = string.Empty;
        public string Hardware { get; set; } = string.Empty;
        public string CpuInfo { get; set; } = string.Empty;
        public string TotalMemory { get; set; } = string.Empty;
        public string DiskSizes { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DefaultShell { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string CpuCount { get; set; } = string.Empty;
        public string FreeMemory { get; set; } = string.Empty;
        public string FreeDisk { get; set; } = string.Empty;
        public string PublicIp { get; set; } = string.Empty;
        public string SudoUser { get; set; } = string.Empty;
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
