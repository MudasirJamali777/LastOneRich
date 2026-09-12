using LastOneRich.Core;
using LastOneRich.Season;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Alternate endings (GDD 6): champion, eliminated, or cash-out. Updates save.json.</summary>
public sealed class SeasonEndState : IGameState
{
    public enum Outcome { Champion, Eliminated, CashedOut }

    readonly StateMachine _sm;
    readonly SeasonRun _season;
    readonly Outcome _outcome;
    readonly double _amount;
    readonly int _rank;
    ArenaBackdrop _bg;
    readonly Particles _fx = new();
    readonly ScreenFX _screen = new();
    readonly List<(string, Color)> _lines = new();
    string _headline;
    Color _headlineColor;
    float _t;

    public SeasonEndState(StateMachine sm, SeasonRun season, Outcome outcome, double amount = 0, int rank = 0)
    {
        _sm = sm;
        _season = season;
        _outcome = outcome;
        _amount = amount;
        _rank = rank;
    }

    public void Enter()
    {
        _bg = new ArenaBackdrop(_season.CurrentLevel);
        var save = GameServices.Save;
        var wallet = _season.Wallet;
        double gained;

        switch (_outcome)
        {
            case Outcome.Champion:
                wallet.Banked += _season.Season.GrandPrize;
                gained = wallet.Banked;
                _headline = "SEASON COMPLETE!";
                _headlineColor = new Color(255, 210, 63);
                _lines.Add(("GRAND PRIZE ................ " + Ui.Money(_season.Season.GrandPrize), Color.White));
                _lines.Add(("FINAL BANK ................ " + Ui.Money(gained), new Color(141, 255, 63)));
                _lines.Add(("", Color.White));
                _lines.Add(("MAX VOLT: \"AND NEEEEW CHAMPION OF THE VOLT DOME!\"", new Color(255, 235, 150)));
                save.Championships++;
                if (_rank > 0 && _rank < save.BestFinish) save.BestFinish = 1;
                _fx.ConfettiBurst(new Vector2(640, 200), 220, 1200, 80);
                GameServices.Audio.Event("stinger_win");
                GameServices.Audio.Event("cheer");
                break;

            case Outcome.CashedOut:
                wallet.Banked += _amount;
                wallet.ForfeitRisked();
                gained = wallet.Banked;
                _headline = "YOU WALKED AWAY";
                _headlineColor = new Color(141, 255, 63);
                _lines.Add(("CASH-OUT PAYOUT ........... " + Ui.Money(_amount), Color.White));
                _lines.Add(("FINAL BANK ................ " + Ui.Money(gained), new Color(141, 255, 63)));
                _lines.Add(("", Color.White));
                _lines.Add(("MAX VOLT: \"A SMART PERSON... OR A COWARD? THE CROWD DECIDES!\"", new Color(255, 235, 150)));
                save.CashOuts++;
                if (_rank > 0 && _rank < save.BestFinish) save.BestFinish = _rank;
                GameServices.Audio.Event("cash");
                break;

            default: // Eliminated
                gained = wallet.Banked; // risked pot was already forfeited
                _headline = "ELIMINATED";
                _headlineColor = new Color(255, 70, 70);
                _lines.Add(("FINISHED IN ............... " + Ui.Ordinal(_rank), Color.White));
                _lines.Add(("RISKED POT ................ LOST", new Color(255, 90, 90)));
                _lines.Add(("KEPT (BANKED) ............. " + Ui.Money(gained), new Color(141, 255, 63)));
                _lines.Add(("", Color.White));
                _lines.Add(("MAX VOLT: \"SO CLOSE! DROPS OF CONFETTI FOR OUR FALLING STAR!\"", new Color(255, 235, 150)));
                if (_rank > 0 && _rank < save.BestFinish) save.BestFinish = _rank;
                GameServices.Audio.Event("stinger_elim");
                break;
        }

        // save.json (GDD 17)
        save.TotalBanked += gained;
        save.SeasonsPlayed++;
        SaveSystem.Store(save);
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        _screen.Update(dt);
        _fx.Update(dt);
        _bg.Update(dt);
        if (_outcome == Outcome.Champion && _t % 2.4f < dt * 1.2f)
            _fx.ConfettiBurst(new Vector2(Rng.Range(200, 1080), 160), 50, 400, 40);
        if (_t > 1.2f && Input.ConfirmPressed)
            _sm.Replace(new MainMenuState(_sm));
    }

    public void Draw()
    {
        _bg.Draw();
        var vp = GameServices.Gfx.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(6, 6, 14, 215));

        var hs = f.Measure(_headline, 2.1f);
        float wob = MathF.Sin(_t * 2.4f) * 0.015f;
        f.DrawOutlined(sb, _headline, new Vector2(640, 110), _headlineColor, 2.1f, wob, new Vector2(hs.X / 2, 0));
        f.Draw(sb, _season.Season.SeasonName, new Vector2(640, 205), new Color(160, 165, 190), 0.55f, 0f, new Vector2(f.Measure(_season.Season.SeasonName, 0.55f).X / 2, 0), true);

        var panel = new Rectangle(340, 270, 600, 250);
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), new Color(12, 14, 28, 240));
        Ui.Frame(panel, 3, _headlineColor);
        float y = panel.Y + 28;
        foreach (var (line, col) in _lines)
        {
            var ls = f.Measure(line, 0.52f);
            if (line.StartsWith("MAX") || line.Length == 0)
                f.Draw(sb, line, new Vector2(640 - ls.X / 2, y), col, 0.52f);
            else
                f.Draw(sb, line, new Vector2(panel.X + 28, y), col, 0.62f);
            y += line.Length == 0 ? 18 : 40;
        }

        var save = GameServices.Save;
        string career = $"CAREER: {Ui.Money(save.TotalBanked)} BANKED · {save.SeasonsPlayed} SEASONS · {save.Championships} WINS · {save.CashOuts} CASH-OUTS";
        var cs = f.Measure(career, 0.5f);
        f.Draw(sb, career, new Vector2(640, 575), new Color(150, 160, 190), 0.5f, 0f, new Vector2(cs.X / 2, 0), true);

        if (_t > 1.2f)
        {
            string hint = "ENTER: MAIN MENU";
            var hintS = f.Measure(hint, 0.55f);
            float blink = 0.6f + 0.4f * MathF.Sin(_t * 4f);
            f.Draw(sb, hint, new Vector2(640, 650), new Color(220, 225, 245) * blink, 0.55f, 0f, new Vector2(hintS.X / 2, 0), true);
        }

        _fx.Draw(sb);
        Ui.End();
        _screen.Draw(sb, vp);
    }
}
