using System.Collections.Generic;

namespace PowerTerminal.Models
{
    /// <summary>A single Linux command entry in a command pack.</summary>
    public class LinuxCommand
    {
        public string Title { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public string Command { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
