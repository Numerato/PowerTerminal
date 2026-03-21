namespace Terminal.Vt;

using System.Windows.Media;

public enum ColorKind { Default, Indexed, TrueColor }

public readonly struct TerminalColor : IEquatable<TerminalColor>
{
    public ColorKind Kind { get; }
    public int Index { get; }
    public byte R { get; }
    public byte G { get; }
    public byte B { get; }

    public static readonly TerminalColor DefaultFg = new(ColorKind.Default, 0, 0, 0, 0);
    public static readonly TerminalColor DefaultBg = new(ColorKind.Default, 1, 0, 0, 0);

    public static bool BoldBrightensColors { get; set; } = true;

    private TerminalColor(ColorKind kind, int index, byte r, byte g, byte b)
    {
        Kind = kind; Index = index; R = r; G = g; B = b;
    }

    public static TerminalColor FromIndex(int index) => new(ColorKind.Indexed, index, 0, 0, 0);
    public static TerminalColor FromRgb(byte r, byte g, byte b) => new(ColorKind.TrueColor, 0, r, g, b);

    public Color Resolve(bool isForeground, bool bold)
    {
        switch (Kind)
        {
            case ColorKind.TrueColor:
                return Color.FromRgb(R, G, B);
            case ColorKind.Indexed:
                int idx = Index;
                if (bold && isForeground && BoldBrightensColors && idx >= 0 && idx <= 7)
                    idx += 8;
                if (idx >= 0 && idx < Palette.Length)
                    return Palette[idx];
                return Color.FromRgb(0xCC, 0xCC, 0xCC);
            case ColorKind.Default:
            default:
                return Index == 0
                    ? Color.FromRgb(0xEE, 0xEE, 0xEC)  // DefaultFg: Ubuntu #EEEEEC
                    : Color.FromRgb(0x30, 0x0A, 0x24);  // DefaultBg: Ubuntu #300A24
        }
    }

    private static Color[] BuildPalette()
    {
        var p = new Color[256];
        // 0-7: standard colors (Ubuntu GNOME Terminal palette)
        p[0]  = Color.FromRgb(0x2E, 0x34, 0x36); // Black
        p[1]  = Color.FromRgb(0xCC, 0x00, 0x00); // Red
        p[2]  = Color.FromRgb(0x4E, 0x9A, 0x06); // Green
        p[3]  = Color.FromRgb(0xC4, 0xA0, 0x00); // Yellow
        p[4]  = Color.FromRgb(0x34, 0x65, 0xA4); // Blue
        p[5]  = Color.FromRgb(0x75, 0x50, 0x7B); // Magenta
        p[6]  = Color.FromRgb(0x06, 0x98, 0x9A); // Cyan
        p[7]  = Color.FromRgb(0xD3, 0xD7, 0xCF); // White/Light Gray
        // 8-15: bright colors
        p[8]  = Color.FromRgb(0x55, 0x57, 0x53); // Bright Black (Dark Gray)
        p[9]  = Color.FromRgb(0xEF, 0x29, 0x29); // Bright Red
        p[10] = Color.FromRgb(0x8A, 0xE2, 0x34); // Bright Green
        p[11] = Color.FromRgb(0xFC, 0xE9, 0x4F); // Bright Yellow
        p[12] = Color.FromRgb(0x72, 0x9F, 0xCF); // Bright Blue
        p[13] = Color.FromRgb(0xAD, 0x7F, 0xA8); // Bright Magenta
        p[14] = Color.FromRgb(0x34, 0xE2, 0xE2); // Bright Cyan
        p[15] = Color.FromRgb(0xEE, 0xEE, 0xEC); // Bright White
        // 16-231: 6x6x6 color cube
        for (int i = 16; i < 232; i++)
        {
            int n = i - 16;
            int ri = n / 36;
            int gi = (n % 36) / 6;
            int bi = n % 6;
            byte rv = ri == 0 ? (byte)0 : (byte)(55 + 40 * ri);
            byte gv = gi == 0 ? (byte)0 : (byte)(55 + 40 * gi);
            byte bv = bi == 0 ? (byte)0 : (byte)(55 + 40 * bi);
            p[i] = Color.FromRgb(rv, gv, bv);
        }
        // 232-255: grayscale
        for (int i = 232; i < 256; i++)
        {
            byte v = (byte)(8 + 10 * (i - 232));
            p[i] = Color.FromRgb(v, v, v);
        }
        return p;
    }

    private static readonly Color[] Palette = BuildPalette();

    public bool Equals(TerminalColor other) =>
        Kind == other.Kind && Index == other.Index && R == other.R && G == other.G && B == other.B;

    public override bool Equals(object? obj) => obj is TerminalColor tc && Equals(tc);
    public override int GetHashCode() => HashCode.Combine(Kind, Index, R, G, B);
    public static bool operator ==(TerminalColor a, TerminalColor b) => a.Equals(b);
    public static bool operator !=(TerminalColor a, TerminalColor b) => !a.Equals(b);
}
