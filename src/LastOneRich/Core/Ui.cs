using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>Small 2D UI helpers: virtual 1280x720 canvas, rects, money/ordinal formatting.</summary>
public static class Ui
{
    public const int W = 1280, H = 720;

    public static Matrix ComputeTransform(Viewport vp)
    {
        float s = MathF.Min(vp.Width / (float)W, vp.Height / (float)H);
        float ox = (vp.Width - W * s) * 0.5f;
        float oy = (vp.Height - H * s) * 0.5f;
        return Matrix.CreateTranslation(ox, oy, 0f) * Matrix.CreateScale(s, s, 1f);
    }

    public static void Begin(Viewport vp)
    {
        // 2D pass isolation: 3D depth/blend state must never bleed into the UI.
        GameServices.Gfx.DepthStencilState = DepthStencilState.None;
        GameServices.Gfx.BlendState = BlendState.AlphaBlend;
        GameServices.Gfx.SamplerStates[0] = SamplerState.LinearClamp;
        GameServices.Sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, transformMatrix: ComputeTransform(vp));
    }

    public static void End() => GameServices.Sb.End();

    public static void Rect(Vector2 pos, Vector2 size, Color c)
    {
        GameServices.Sb.Draw(GameServices.Pixel, pos, null, c, 0f, Vector2.Zero, size / 4f, SpriteEffects.None, 0f);
    }

    public static void Rect(Rectangle r, Color c) => Rect(new Vector2(r.X, r.Y), new Vector2(r.Width, r.Height), c);

    public static void Frame(Rectangle r, int thickness, Color c)
    {
        Rect(new Rectangle(r.X, r.Y, r.Width, thickness), c);
        Rect(new Rectangle(r.X, r.Bottom - thickness, r.Width, thickness), c);
        Rect(new Rectangle(r.X, r.Y, thickness, r.Height), c);
        Rect(new Rectangle(r.Right - thickness, r.Y, thickness, r.Height), c);
    }

    public static string Money(double v) => "$" + v.ToString("N0", CultureInfo.InvariantCulture);

    public static string Ordinal(int n) => n switch
    {
        1 => "1ST", 2 => "2ND", 3 => "3RD", 21 => "21ST", 22 => "22ND", 23 => "23RD",
        _ => (n % 100) is 11 or 12 or 13 ? n + "TH" : (n % 10) switch { 1 => n + "ST", 2 => n + "ND", 3 => n + "RD", _ => n + "TH" }
    };
}
