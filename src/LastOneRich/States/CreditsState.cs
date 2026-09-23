using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>
/// Priority 7: a clean, single-card credits screen — game name, developer, engine and the
/// "everything you see and hear was made for this project" line the GDD calls for (all
/// geometry is procedural primitives, all audio is generated placeholder WAVs — see AudioBank).
/// Reachable from the main menu; ESC/Enter closes back to it, matching HOW TO PLAY / ACHIEVEMENTS.
/// </summary>
public sealed class CreditsState : IGameState
{
    readonly StateMachine _sm;
    float _t;

    public CreditsState(StateMachine sm) => _sm = sm;

    public void Enter() { }
    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        if (Input.ConfirmPressed || Input.BackPressed)
        {
            GameServices.Audio.Event("blip");
            _sm.Replace(new MainMenuState(_sm));
        }
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        GameServices.Gfx.Clear(new Color(10, 10, 20));
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);

        float ease = MathHelper.Clamp(_t / 0.25f, 0f, 1f);
        ease = 1f - (1f - ease) * (1f - ease);

        const int PanelW = 680, PanelH = 460;
        int px = (Ui.W - PanelW) / 2;
        int py = (int)MathHelper.Lerp(120f, 100f, ease);
        var panel = new Rectangle(px, py, PanelW, PanelH);

        Ui.Rect(panel, new Color(14, 16, 30, 246));
        Ui.Frame(panel, 3, new Color(255, 210, 63));
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(PanelW, 4), new Color(255, 210, 63));

        float y = panel.Y + 40;

        string title = "LAST ONE RICH!";
        var ts = f.Measure(title, 1.3f);
        f.DrawOutlined(sb, title, new Vector2(Ui.W / 2f, y), new Color(255, 210, 63), 1.3f, 0f, new Vector2(ts.X / 2f, 0));
        y += 56;

        string tagline = "A VOLT DOME MEGA-CHALLENGE";
        var tgs = f.Measure(tagline, 0.55f);
        f.Draw(sb, tagline, new Vector2(Ui.W / 2f, y), new Color(200, 210, 255), 0.55f, 0f, new Vector2(tgs.X / 2f, 0), true);
        y += 60;

        void Section(string label, string value, Color accent)
        {
            var ls = f.Measure(label, 0.5f);
            f.Draw(sb, label, new Vector2(Ui.W / 2f, y), accent, 0.5f, 0f, new Vector2(ls.X / 2f, 0), true);
            y += 28;
            var vs = f.Measure(value, 0.66f);
            f.DrawOutlined(sb, value, new Vector2(Ui.W / 2f, y), Color.White, 0.66f, 0f, new Vector2(vs.X / 2f, 0));
            y += 52;
        }

        Section("DEVELOPER", "VOLT DOME STUDIOS", new Color(63, 210, 255));
        Section("ENGINE", "MONOGAME  ·  .NET 8  ·  C#", new Color(63, 210, 255));

        string gfxLabel = "GRAPHICS & AUDIO";
        var gls = f.Measure(gfxLabel, 0.5f);
        f.Draw(sb, gfxLabel, new Vector2(Ui.W / 2f, y), new Color(63, 210, 255), 0.5f, 0f, new Vector2(gls.X / 2f, 0), true);
        y += 28;
        string[] gfxLines =
        {
            "EVERY SHAPE ON SCREEN IS PROCEDURAL GEOMETRY —",
            "NO IMPORTED 3D MODELS.",
            "EVERY SOUND EFFECT AND MUSIC BED IS GENERATED —",
            "NO LICENSED AUDIO.",
        };
        foreach (var line in gfxLines)
        {
            var ls2 = f.Measure(line, 0.46f);
            f.Draw(sb, line, new Vector2(Ui.W / 2f, y), new Color(200, 208, 235), 0.46f, 0f, new Vector2(ls2.X / 2f, 0), true);
            y += 26;
        }
        y += 20;

        string thanks = "THANKS FOR PLAYING.";
        var thk = f.Measure(thanks, 0.6f);
        f.DrawOutlined(sb, thanks, new Vector2(Ui.W / 2f, y), new Color(255, 210, 63), 0.6f, 0f, new Vector2(thk.X / 2f, 0));

        string hint = "ENTER / ESC: BACK";
        var hs = f.Measure(hint, 0.46f);
        f.Draw(sb, hint, new Vector2(Ui.W / 2f, panel.Bottom - 34), new Color(150, 160, 190), 0.46f, 0f, new Vector2(hs.X / 2f, 0), true);

        Ui.End();
    }
}
