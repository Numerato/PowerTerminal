namespace PowerTerminal.Models
{
    public class AppSettings
    {
        public AiSettings Ai { get; set; } = new();
        public ThemeSettings Theme { get; set; } = new();
        public string LogDirectory { get; set; } = "logs";
        public string WikiDirectory { get; set; } = "config/wikis";
    }

    public class AiSettings
    {
        public string Provider { get; set; } = "openai";
        /// <summary>Base URL for the AI API (OpenAI-compatible).</summary>
        public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
        public string ApiToken { get; set; } = string.Empty;
        public string Model { get; set; } = "gpt-4o-mini";
        public string Username { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 2048;
        public string SystemPrompt { get; set; } =
            "You are a helpful Linux system administrator assistant. " +
            "Provide concise, accurate answers about Linux commands, configuration, and administration.";
    }

    public class ThemeSettings
    {
        public string Background { get; set; } = "#0C0C0C";
        public string Foreground { get; set; } = "#CCCCCC";
        public string AccentColor { get; set; } = "#0078D4";
        public string FontFamily { get; set; } = "Cascadia Code, Consolas, Courier New";
        public double FontSize { get; set; } = 13.0;
    }
}
