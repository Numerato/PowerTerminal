using System;

namespace PowerTerminal.Models
{
    public class SshConnection
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string? PrivateKeyPath { get; set; }
        public string? LastConnected { get; set; }

        public override string ToString() => $"{Name} ({Username}@{Host}:{Port})";
    }
}
