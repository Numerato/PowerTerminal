namespace PowerTerminal.Models
{
    /// <summary>Represents a named template variable and its current resolved value.</summary>
    public class VariableItem
    {
        public string Name  { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }
}
