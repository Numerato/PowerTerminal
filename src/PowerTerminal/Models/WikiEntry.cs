using System;
using System.Collections.Generic;

namespace PowerTerminal.Models
{
    public class WikiEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public List<WikiSection> Sections { get; set; } = new();
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string FileName { get; set; } = string.Empty;
    }

    public class WikiSection
    {
        public WikiSectionType Type { get; set; } = WikiSectionType.Text;
        public string Content { get; set; } = string.Empty;
        /// <summary>Language hint for command blocks (bash, powershell, etc.).</summary>
        public string? Language { get; set; }
    }

    public enum WikiSectionType
    {
        Text,
        Command
    }
}
