namespace PowerTerminal.Models
{
    /// <summary>Represents a predefined tab icon entry shown in the icon picker ComboBox.</summary>
    public class IconOption
    {
        public string DisplayName { get; set; } = string.Empty;
        /// <summary>Full file-system path to the icon image.</summary>
        public string Path { get; set; } = string.Empty;

        public override string ToString() => DisplayName;
    }
}
