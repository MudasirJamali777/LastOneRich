using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Readable error screen for invalid season/level content (GDD §14 guardrail).</summary>
public sealed class ContentErrorState : IGameState
{
    readonly StateMachine _sm;
    readonly List<string> _errors;
    float _t;

    public ContentErrorState(StateMachine sm, List<string> errors) { _sm = sm; _errors = errors; }

    public void Enter() { GameServices.Audio.Event("stinger_elim"); }
    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        if (_t > 1f && Input.ConfirmPressed) _sm.Replace(new MainMenuState(_sm));
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        GameServices.Gfx.Clear(new Color(24, 6, 6));
        var f = GameServices.Font;
        var sb = GameServices.Sb;
        Ui.Begin(vp);

        string title = "CONTENT VALIDATOR FAILED";
        var ts = f.Measure(title, 1.2f);
        f.DrawOutlined(sb, title, new Vector2(640, 70), new Color(255, 90, 90), 1.2f, 0f, new Vector2(ts.X / 2, 0));
        string sub = "RINA 'THE RULES' KAY HAS SOME NOTES.";
        var ss = f.Measure(sub, 0.55f);
        f.Draw(sb, sub, new Vector2(640, 128), new Color(230, 230, 245), 0.55f, 0f, new Vector2(ss.X / 2, 0), true);

        var panel = new Rectangle(140, 170, 1000, 440);
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), new Color(12, 8, 14, 245));
        Ui.Frame(panel, 3, new Color(255, 90, 90));
        float y = panel.Y + 20;
        foreach (var e in _errors.Take(13))
        {
            f.Draw(sb, "- " + e, new Vector2(panel.X + 24, y), new Color(255, 190, 190), 0.52f);
            y += 32;
        }

        string hint = "FIX content/data/*.json — ENTER: MAIN MENU";
        var hs = f.Measure(hint, 0.55f);
        float blink = 0.6f + 0.4f * MathF.Sin(_t * 4f);
        f.Draw(sb, hint, new Vector2(640, 650), new Color(220, 225, 245) * blink, 0.55f, 0f, new Vector2(hs.X / 2, 0), true);
        Ui.End();
    }
}
