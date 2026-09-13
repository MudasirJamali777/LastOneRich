using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>Global systems (service-locator lite: fine for a slice, documented in README).</summary>
public static class GameServices
{
    public static GraphicsDevice Gfx;
    public static SpriteBatch Sb;
    public static BitmapFont Font;
    public static AudioBank Audio;
    public static GeometryRenderer Renderer;
    public static Texture2D Pixel;
    public static Texture2D Particle;
    public static SaveData Save;
    public static LaunchArgs Launch;
    public static bool DebugOverlay; // toggled with F3 (controls.json: debugOverlay)

    public static void Init(GraphicsDevice gfx, SpriteBatch sb, LaunchArgs launch)
    {
        Gfx = gfx;
        Sb = sb;
        Launch = launch;
        Font = new BitmapFont(gfx, Json.PathFor("gfx/font.png"), Json.PathFor("gfx/font.json"));
        Pixel = Texture2D.FromStream(gfx, File.OpenRead(Json.PathFor("gfx/pixel.png")));
        Particle = Texture2D.FromStream(gfx, File.OpenRead(Json.PathFor("gfx/particle.png")));
        Audio = new AudioBank(Json.PathFor("sfx"));
        Renderer = new GeometryRenderer(gfx);
        Save = SaveSystem.Load();
    }
}
