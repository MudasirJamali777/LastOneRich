using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>Screen-space effects: fades, flashes, letterbox bars for cutscenes.</summary>
public sealed class ScreenFX
{
    public float FadeAlpha;          // 0 = clear, 1 = black
    float _fadeTarget, _fadeSpeed = 2.5f;
    public float FlashAlpha;
    public float Letterbox;          // 0..1
    float _letterTarget;

    public void FadeTo(float alpha, float speed = 2.5f) { _fadeTarget = alpha; _fadeSpeed = speed; }
    public void Flash(float a = 0.55f) => FlashAlpha = MathF.Max(FlashAlpha, a);
    public void LetterboxTo(float v) => _letterTarget = v;

    public void Update(float dt)
    {
        float k = 1f - MathF.Exp(-_fadeSpeed * dt);
        FadeAlpha += (_fadeTarget - FadeAlpha) * k;
        FlashAlpha = MathF.Max(0f, FlashAlpha - 2.4f * dt);
        Letterbox += (_letterTarget - Letterbox) * (1f - MathF.Exp(-6f * dt));
    }

    public void Draw(SpriteBatch sb, Viewport vp)
    {
        var px = GameServices.Pixel;
        sb.Begin(transformMatrix: Ui.ComputeTransform(vp));
        if (FadeAlpha > 0.002f)
            sb.Draw(px, new Rectangle(0, 0, vp.Width, vp.Height), null, Color.Black * FadeAlpha, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        if (FlashAlpha > 0.002f)
            sb.Draw(px, new Rectangle(0, 0, vp.Width, vp.Height), null, Color.White * FlashAlpha, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        if (Letterbox > 0.002f)
        {
            int h = (int)(vp.Height * 0.11f * Letterbox);
            sb.Draw(px, new Rectangle(0, 0, vp.Width, h), null, Color.Black, 0f, Vector2.Zero, SpriteEffects.None, 0f);
            sb.Draw(px, new Rectangle(0, vp.Height - h, vp.Width, h), null, Color.Black, 0f, Vector2.Zero, SpriteEffects.None, 0f);
        }
        sb.End();
    }
}
