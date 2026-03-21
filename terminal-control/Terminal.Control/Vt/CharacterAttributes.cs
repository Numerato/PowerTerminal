namespace Terminal.Vt;

public struct CharacterAttributes : IEquatable<CharacterAttributes>
{
    public TerminalColor Foreground = TerminalColor.DefaultFg;
    public TerminalColor Background = TerminalColor.DefaultBg;
    public bool Bold;
    public bool Dim;
    public bool Italic;
    public bool Underline;
    public bool Blink;
    public bool ReverseVideo;
    public bool Invisible;
    public bool Strikethrough;

    public CharacterAttributes() { }

    public static readonly CharacterAttributes Default = new();

    public (TerminalColor fg, TerminalColor bg) EffectiveColors
    {
        get
        {
            if (ReverseVideo)
                return (Background, Foreground);
            return (Foreground, Background);
        }
    }

    public bool Equals(CharacterAttributes other) =>
        Foreground == other.Foreground &&
        Background == other.Background &&
        Bold == other.Bold &&
        Dim == other.Dim &&
        Italic == other.Italic &&
        Underline == other.Underline &&
        Blink == other.Blink &&
        ReverseVideo == other.ReverseVideo &&
        Invisible == other.Invisible &&
        Strikethrough == other.Strikethrough;

    public override bool Equals(object? obj) => obj is CharacterAttributes ca && Equals(ca);
    public override int GetHashCode() => HashCode.Combine(Bold, Italic, Underline, ReverseVideo, Invisible, Foreground, Background);
}
