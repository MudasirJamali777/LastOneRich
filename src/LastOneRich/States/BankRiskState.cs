using LastOneRich.Core;
using LastOneRich.Season;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>The signature decision: BANK the pot (safe) or RISK it for a double next round (GDD 6).</summary>
public sealed class BankRiskState : IGameState
{
    readonly StateMachine _sm;
    readonly SeasonRun _season;
    ArenaBackdrop _bg;
    int _sel;
    float _t;

    static readonly string[] MaxLines =
    {
        "BANK IT... OR RISK IT ALL?!",
        "SAFE MONEY... OR THE ROAD TO A MILLION?!",
        "THIS IS WHAT THEY PAY ME FOR, FOLKS!",
    };

    public BankRiskState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    public void Enter()
    {
        _bg = new ArenaBackdrop(_season.CurrentLevel);
        GameServices.Audio.Event("stinger_win");
    }

    public void Exit() { }

    void Advance()
    {
        if (_season.Round.CashOutAfter)
            _sm.Replace(new CashOutOfferState(_sm, _season));
        else
            _sm.Replace(new TwistRevealState(_sm, _season));
    }

    public void Update(float dt)
    {
        _t += dt;
        _bg.Update(dt);
        if (Input.LeftPressed || Input.Num1Pressed) { _sel = 0; GameServices.Audio.Event("blip"); }
        if (Input.RightPressed || Input.Num2Pressed) { _sel = 1; GameServices.Audio.Event("blip"); }

        if (Input.ConfirmPressed)
        {
            var wallet = _season.Wallet;
            if (_sel == 0)
            {
                wallet.ChooseBank();
                GameServices.Audio.Event("cash");
                Achievements.Unlock("first_bank");   // Priority 5
                Advance();
            }
            else
            {
                wallet.ChooseRisk(_season.Economy.RiskMultiplier);
                GameServices.Audio.Event("stinger_twist");
                Advance();
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
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(6, 6, 14, 200));

        string line = MaxLines[_season.RoundIdx % MaxLines.Length];
        var ls = f.Measure(line, 1.15f);
        f.DrawOutlined(sb, line, new Vector2(640, 90), new Color(255, 235, 150), 1.15f, 0f, new Vector2(ls.X / 2, 0));
        string speaker = "— MAX VOLT";
        f.Draw(sb, speaker, new Vector2(640, 158), new Color(160, 165, 190), 0.55f, 0f, new Vector2(f.Measure(speaker, 0.55f).X / 2, 0), true);

        var w = _season.Wallet;
        DrawChip(f, sb, 240, "BANKED — SAFE", Ui.Money(w.Banked), new Color(141, 255, 63));
        DrawChip(f, sb, 680, "PRIZE POT — AT RISK", Ui.Money(w.Risked), new Color(255, 210, 63));

        double mult = _season.Economy.RiskMultiplier;
        DrawButton(f, sb, 140, 330, "1  BANK IT", $"MOVE {Ui.Money(w.Risked)} TO BANKED CASH. IT CAN NEVER BE LOST.", _sel == 0, new Color(141, 255, 63));
        DrawButton(f, sb, 660, 330, "2  RISK IT", $"SURVIVE ROUND {_season.RoundIdx + 2} AND THE POT PAYS ×{mult:0.#} — OR LOSE IT ALL.", _sel == 1, new Color(255, 120, 90));

        string hint = "← → OR 1/2 TO CHOOSE · ENTER TO CONFIRM";
        var hs = f.Measure(hint, 0.5f);
        float blink = 0.6f + 0.4f * MathF.Sin(_t * 4f);
        f.Draw(sb, hint, new Vector2(640, 660), new Color(220, 225, 245) * blink, 0.5f, 0f, new Vector2(hs.X / 2, 0), true);
        Ui.End();
    }

    static void DrawChip(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, float x, string label, string value, Color c)
    {
        Ui.Rect(new Vector2(x, 218), new Vector2(360, 74), new Color(12, 14, 28, 235));
        Ui.Rect(new Vector2(x, 218), new Vector2(360, 4), c);
        f.Draw(sb, label, new Vector2(x + 20, 230), c, 0.5f);
        f.DrawOutlined(sb, value, new Vector2(x + 20, 250), Color.White, 0.85f);
    }

    static void DrawButton(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, float x, float y, string title, string desc, bool sel, Color c)
    {
        var r = new Rectangle((int)x, (int)y, 480, 190);
        Ui.Rect(new Vector2(x, y), new Vector2(480, 190), sel ? new Color(20, 24, 44, 250) : new Color(12, 14, 28, 220));
        Ui.Frame(r, sel ? 4 : 2, sel ? c : new Color(80, 86, 110));
        var ts = f.Measure(title, 1.0f);
        f.DrawOutlined(sb, title, new Vector2(x + 240, y + 26), sel ? c : new Color(170, 175, 200), 1.0f, 0f, new Vector2(ts.X / 2, 0));
        DrawWrapped(f, sb, desc, new Vector2(x + 30, y + 92), 420, new Color(200, 208, 235), 0.48f);
    }

    static void DrawWrapped(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, string text, Vector2 pos, float width, Color c, float scale)
    {
        var words = text.Split(' ');
        float x = pos.X, y = pos.Y;
        foreach (var w in words)
        {
            var ws = f.Measure(w, scale).X;
            if (x + ws > pos.X + width) { x = pos.X; y += f.LineHeight * scale; }
            f.Draw(sb, w, new Vector2(x, y), c, scale);
            x += ws + f.Measure(" ", scale).X;
        }
    }
}
