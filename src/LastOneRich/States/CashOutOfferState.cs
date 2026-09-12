using LastOneRich.Core;
using LastOneRich.Season;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Branching ending hook (GDD 6): walk away with cash, or chase the million.</summary>
public sealed class CashOutOfferState : IGameState
{
    readonly StateMachine _sm;
    readonly SeasonRun _season;
    ArenaBackdrop _bg;
    int _sel = 1; // default: keep competing
    float _t;
    double _offer;

    public CashOutOfferState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    public void Enter()
    {
        _bg = new ArenaBackdrop(_season.CurrentLevel);
        _offer = _season.Season.CashOutOffers.TryGetValue(_season.Round.Round.ToString(), out var v) ? v : 50000;
        GameServices.Audio.Event("stinger_twist");
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        _bg.Update(dt);
        if (Input.LeftPressed || Input.Num1Pressed) { _sel = 0; GameServices.Audio.Event("blip"); }
        if (Input.RightPressed || Input.Num2Pressed) { _sel = 1; GameServices.Audio.Event("blip"); }

        if (Input.ConfirmPressed)
        {
            if (_sel == 0)
            {
                GameServices.Audio.Event("cash");
                _sm.Replace(new SeasonEndState(_sm, _season, SeasonEndState.Outcome.CashedOut, _offer));
            }
            else
            {
                GameServices.Audio.Event("stinger_win");
                _sm.Replace(new TwistRevealState(_sm, _season));
            }
        }
    }

    public void Draw()
    {
        _bg.Draw();
        var vp = GameServices.Gfx.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(6, 6, 14, 205));

        string line = $"TAKE {Ui.Money(_offer)} AND WALK AWAY... OR KEEP COMPETING?!";
        var ls = f.Measure(line, 0.95f);
        f.DrawOutlined(sb, line, new Vector2(640, 100), new Color(255, 235, 150), 0.95f, 0f, new Vector2(ls.X / 2, 0));
        string speaker = "— MAX VOLT, sliding a briefcase across the desk";
        f.Draw(sb, speaker, new Vector2(640, 168), new Color(160, 165, 190), 0.52f, 0f, new Vector2(f.Measure(speaker, 0.52f).X / 2, 0), true);

        // two doors
        DrawDoor(f, sb, 170, "1  WALK AWAY", Ui.Money(_offer), "GUARANTEED. COLD. HARD. CASH.", _sel == 0, new Color(141, 255, 63));
        DrawDoor(f, sb, 670, "2  KEEP GOING", $"{Ui.Money(_season.Season.GrandPrize)} DREAM", "TWO ROUNDS BETWEEN YOU AND GLORY.", _sel == 1, new Color(255, 210, 63));

        string hint = "← → OR 1/2 TO CHOOSE · ENTER TO DECIDE";
        var hs = f.Measure(hint, 0.5f);
        float blink = 0.6f + 0.4f * MathF.Sin(_t * 4f);
        f.Draw(sb, hint, new Vector2(640, 660), new Color(220, 225, 245) * blink, 0.5f, 0f, new Vector2(hs.X / 2, 0), true);
        Ui.End();
    }

    static void DrawDoor(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, float x, string title, string big, string desc, bool sel, Color c)
    {
        var r = new Rectangle((int)x, (int)240, 440, 300);
        Ui.Rect(new Vector2(x, 240), new Vector2(440, 300), sel ? new Color(20, 24, 44, 250) : new Color(12, 14, 28, 220));
        Ui.Frame(r, sel ? 4 : 2, sel ? c : new Color(80, 86, 110));
        var ts = f.Measure(title, 0.85f);
        f.DrawOutlined(sb, title, new Vector2(x + 220, 268), sel ? c : new Color(170, 175, 200), 0.85f, 0f, new Vector2(ts.X / 2, 0));
        var bs = f.Measure(big, 1.05f);
        f.DrawOutlined(sb, big, new Vector2(x + 220, 340), Color.White, 1.05f, 0f, new Vector2(bs.X / 2, 0));
        DrawCentered(f, sb, desc, x + 220, 470, new Color(200, 208, 235), 0.46f);
    }

    static void DrawCentered(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, string text, float cx, float y, Color c, float scale)
    {
        var s = f.Measure(text, scale);
        f.Draw(sb, text, new Vector2(cx - s.X / 2, y), c, scale);
    }
}
