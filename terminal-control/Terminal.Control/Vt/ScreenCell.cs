namespace Terminal.Vt;

using System.Text;

public struct ScreenCell
{
    public Rune Character = new Rune(' ');
    public CharacterAttributes Attributes = CharacterAttributes.Default;
    public bool IsWide;
    public bool IsWideRight;

    public ScreenCell() { }

    public static readonly ScreenCell Empty = new();
}
