using Microsoft.Xna.Framework;

namespace LastOneRich.Core;

public static class ColorUtil
{
    public static Color Parse(string hex, byte alpha = 255)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
            return new Color(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16), alpha);
        if (hex.Length == 8)
            return new Color(Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex.Substring(2, 2), 16), Convert.ToInt32(hex.Substring(4, 2), 16), Convert.ToInt32(hex.Substring(6, 2), 16));
        return Color.Magenta;
    }

    public static Color Shade(Color c, float mul) => new((int)(c.R * mul), (int)(c.G * mul), (int)(c.B * mul), c.A);

    /// <summary>Premultiplied color for translucent geometry (MonoGame AlphaBlend expects premult).</summary>
    public static Color Premult(Color c, float alpha)
    {
        byte a = (byte)(alpha * 255);
        return new Color((int)(c.R * alpha), (int)(c.G * alpha), (int)(c.B * alpha), a);
    }
}
