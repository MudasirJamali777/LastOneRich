using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Twist reveal between rounds (GDD 5 step 5). Validator-checked, data-driven (GDD 14).</summary>
public sealed class TwistRevealState : IGameState
{
    readonly StateMachine _sm;
    readonly SeasonRun _season;
    ArenaBackdrop _bg;
    TwistDTO _twist;
    float _t;
    bool _advanced;

    public TwistRevealState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    public void Enter()
    {
        _season.RoundIdx++;
        _bg = new ArenaBackdrop(_season.CurrentLevel);

        var pool = _season.Round.TwistPool;
        if (pool.Length == 0)
        {
            _season.ActiveTwist = null;
            // No twist for this round — straight to the arena.
            _sm.Replace(new GameplayState(_sm, _season));
            return;
        }

        _twist = _season.TwistById(pool[Rng.Int(pool.Length)]);
        _season.ActiveTwist = _twist;
        GameServices.Audio.Event("stinger_twist");
        _screen.Flash(0.3f);
    }

    readonly ScreenFX _screen = new();

    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        _screen.Update(dt);
        _bg.Update(dt);
        if (_twist == null || _advanced) return;
        if (_t > 0.9f && Input.ConfirmPressed)
        {
            _advanced = true;
            _sm.Replace(new GameplayState(_sm, _season));
        }
    }

    public void Draw()
    {
        _bg.Draw();
        var vp = GameServices.Gfx.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(24, 6, 6, 215));

        float zoom = System.MathF.Min(1f, _t / 0.35f);
        float ease = 1f - MathF.Pow(1f - zoom, 3f);
        float scale = 0.4f + ease * 0.6f;

        string round = $"ROUND {_season.RoundIdx + 1} TWIST";
        var rs = f.Measure(round, 0.7f * scale);
        f.DrawOutlined(sb, round, new Vector2(640, 170), new Color(255, 157, 46), 0.7f * scale, 0f, new Vector2(rs.X / 2, 0));

        var cardW = 760f * ease;
        var cardH = 260f * ease;
        Ui.Rect(new Vector2(640 - cardW / 2, 250), new Vector2(cardW, cardH), new Color(14, 10, 24, 245));
        Ui.Frame(new Rectangle((int)(640 - cardW / 2), 250, (int)cardW, (int)cardH), 4, new Color(255, 157, 46));

        if (ease > 0.55f)
        {
            string name = _twist.Name;
            var ns = f.Measure(name, 1.5f);
            f.DrawOutlined(sb, name, new Vector2(640, 300), new Color(255, 157, 46), 1.5f, 0f, new Vector2(ns.X / 2, 0));
            string desc = _twist.Desc;
            var ds = f.Measure(desc, 0.62f);
            f.Draw(sb, desc, new Vector2(640, 400), new Color(225, 228, 245), 0.62f, 0f, new Vector2(ds.X / 2, 0), true);
        }

        string rina = "RINA 'THE RULES' KAY: \"Per clause 7-B... this is completely legal.\"";
        var rinas = f.Measure(rina, 0.5f);
        f.Draw(sb, rina, new Vector2(640, 590), new Color(63, 210, 255), 0.5f, 0f, new Vector2(rinas.X / 2, 0), true);

        if (_t > 0.9f)
        {
            string hint = "ENTER: LET'S GO";
            var hs = f.Measure(hint, 0.55f);
            float blink = 0.6f + 0.4f * MathF.Sin(_t * 5f);
            f.Draw(sb, hint, new Vector2(640, 660), new Color(220, 225, 245) * blink, 0.55f, 0f, new Vector2(hs.X / 2, 0), true);
        }

        Ui.End();
        _screen.Draw(sb, vp);
    }
}
