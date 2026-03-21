using System;

namespace PowerTerminal.Models
{
    public class AiMessage
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Role { get; set; } = "user"; // user | assistant | system
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
}
