using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Results ceremony (GDD 12): podium reveal, dramatic elimination stamps, payout summary.</summary>
public sealed class ResultsState : IGameState
{
    sealed class Row
    {
        public int Rank;
        public string Name;
        public Color Color;
        public string Status;
        public bool IsPlayer;
        public bool Eliminated;
    }

    readonly StateMachine _sm;
    readonly SeasonRun _season;
    readonly List<Actor> _ranking;
    ArenaBackdrop _bg;
    readonly List<Row> _rows = new();
    readonly Particles _fx = new();
    readonly ScreenFX _screen = new();

    float _t;
    int _revealed;
    bool _stamped;
    bool _payoutShown;
    double _payout;
    readonly List<string> _payoutLines = new();
    bool _playerEliminated;
    int _playerRank;
    double _playerTime;
    bool _playerFinished;
    bool _done;

    public ResultsState(StateMachine sm, SeasonRun season, List<Actor> ranking)
    {
        _sm = sm;
        _season = season;
        _ranking = ranking;
    }

    public void Enter()
    {
        _bg = new ArenaBackdrop(_season.CurrentLevel);

        var elimSet = RaceTracker.Eliminate(_ranking, _season.Tracker.ElimPercent);
        int rank = 1;
        foreach (var a in _ranking)
        {
            _rows.Add(new Row
            {
                Rank = rank,
                Name = a.Name,
                Color = a.Color,
                Status = a.Finished ? a.FinishTime.ToString("0.0") + "s" : "DNF",
                IsPlayer = a.IsPlayer,
                Eliminated = elimSet.Contains(a),
            });
            var c = _season.Cast.First(x => x.Name == a.Name);
            c.LastRank = rank;
            if (elimSet.Contains(a)) c.Eliminated = true;
            if (a.IsPlayer)
            {
                _playerRank = rank;
                _playerTime = a.Finished ? a.FinishTime : 9999;
                _playerFinished = a.Finished;
                _playerEliminated = elimSet.Contains(a);
            }
            rank++;
        }

        if (_playerEliminated)
        {
            _season.Wallet.ForfeitRisked();
            GameServices.Save.Eliminations++;
        }
        else
        {
            _season.Wallet.ApplyPending();
            _payout = _season.ComputePayout(_playerRank, _playerTime, _playerFinished, _ranking.Count);
            _season.Wallet.AddPayout(_payout);

            var round = _season.Round;
            _payoutLines.Add($"BASE REWARD .......... {Ui.Money(round.BaseReward)}");
            if (_season.Economy.PlacementBonus.TryGetValue(_playerRank.ToString(), out var pb) && _playerFinished)
                _payoutLines.Add($"PLACEMENT ({Ui.Ordinal(_playerRank)}) ....... {Ui.Money(pb)}");
            if (_playerFinished && _playerTime <= _season.Economy.SpeedBonusParSeconds)
                _payoutLines.Add($"SPEED BONUS ........... {Ui.Money(_season.Economy.SpeedBonusAmount)}");
            if (_season.Wallet.PendingMult > 1)
                _payoutLines.Add($"PENDING POT ×{_season.Wallet.PendingMult:0.#} .......... RISKED!");
        }

        _screen.FadeAlpha = 1f;
        _screen.FadeTo(0f, 1.6f);
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        _screen.Update(dt);
        _fx.Update(dt);
        _bg.Update(dt);

        int target = System.Math.Min(_rows.Count, (int)(_t / 0.22f));
        if (target > _revealed) { _revealed = target; GameServices.Audio.Event("blip"); }

        if (!_stamped && _revealed >= _rows.Count && _t > _rows.Count * 0.22f + 0.6f)
        {
            _stamped = true;
            if (_rows.Any(r => r.Eliminated)) { GameServices.Audio.Event("stinger_elim"); _screen.Flash(0.18f); }
        }

        if (_stamped && !_payoutShown && _t > _rows.Count * 0.22f + 1.7f)
        {
            _payoutShown = true;
            if (!_playerEliminated)
            {
                GameServices.Audio.Event("cash");
                if (_playerRank <= 3) _fx.ConfettiBurst(new Vector2(640, 220), 120);
            }
        }

        if (Input.ConfirmPressed)
        {
            if (!_payoutShown)
            {
                _t = _rows.Count * 0.22f + 1.8f; // skip animation
                return;
            }
            if (_done) return;
            _done = true;
            if (_playerEliminated)
                _sm.Replace(new SeasonEndState(_sm, _season, SeasonEndState.Outcome.Eliminated, 0, _playerRank));
            else if (_season.IsFinalRound)
            {
                _season.Wallet.ChooseBank(); // finale: everything banks, plus the grand prize
                _sm.Replace(new SeasonEndState(_sm, _season, SeasonEndState.Outcome.Champion));
            }
            else
                _sm.Replace(new BankRiskState(_sm, _season));
        }
    }

    public void Draw()
    {
        _bg.Draw();
        var vp = GameServices.Gfx.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(6, 6, 14, 208));

        string title = $"RESULTS — ROUND {_season.RoundIdx + 1}";
        var ts = f.Measure(title, 1.1f);
        f.DrawOutlined(sb, title, new Vector2(640, 34), Color.White, 1.1f, 0f, new Vector2(ts.X / 2, 0));
        string sub = $"{_season.Round.Level.ToUpperInvariant()} · {_lvName()}";
        var ss = f.Measure(sub, 0.5f);
        f.Draw(sb, sub, new Vector2(640, 78), new Color(63, 210, 255), 0.5f, 0f, new Vector2(ss.X / 2, 0), true);

        float y = 120;
        for (int i = 0; i < _revealed; i++)
        {
            var row = _rows[i];
            float slide = System.MathF.Max(0f, 1f - (_t - i * 0.22f) * 4f);
            var x = 400 - slide * 260f;
            bool hot = row.IsPlayer;
            Ui.Rect(new Vector2(x, y), new Vector2(480, 40), hot ? new Color(255, 210, 63, 40) : new Color(14, 16, 30, 220));
            Ui.Rect(new Vector2(x, y), new Vector2(6, 40), row.Eliminated && _stamped ? new Color(255, 60, 60) : row.Color);
            f.Draw(sb, $"{row.Rank}", new Vector2(x + 18, y + 8), new Color(150, 160, 190), 0.55f);
            f.Draw(sb, row.Name + (row.IsPlayer ? " (YOU)" : ""), new Vector2(x + 64, y + 5), hot ? new Color(255, 240, 180) : Color.White, 0.72f);
            f.Draw(sb, row.Status, new Vector2(x + 340, y + 8), new Color(185, 190, 215), 0.55f);

            if (row.Eliminated && _stamped)
            {
                var stamp = "OUT";
                var stSize = f.Measure(stamp, 0.8f);
                float shake = MathF.Sin(_t * 20f) * 1.5f;
                f.DrawOutlined(sb, stamp, new Vector2(x + 425 + shake, y + 3 + shake), new Color(255, 70, 70), 0.8f, -0.18f, new Vector2(stSize.X / 2, 0));
            }
            y += 44;
        }

        if (_stamped && _playerEliminated && _payoutShown)
        {
            string big = "ELIMINATED";
            var bs = f.Measure(big, 2.0f);
            float rot = MathF.Sin(_t * 2f) * 0.02f - 0.06f;
            f.DrawOutlined(sb, big, new Vector2(640, 300), new Color(255, 70, 70), 2.0f, rot, new Vector2(bs.X / 2, 0));
        }

        if (_payoutShown && !_playerEliminated)
        {
            var panel = new Rectangle(800, 140, 420, 300);
            Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), new Color(12, 14, 28, 245));
            Ui.Frame(panel, 2, new Color(255, 210, 63));
            f.Draw(sb, "YOUR PAYOUT", new Vector2(panel.X + 24, panel.Y + 18), new Color(255, 210, 63), 0.65f);
            float ly = panel.Y + 66;
            foreach (var line in _payoutLines)
            {
                f.Draw(sb, line, new Vector2(panel.X + 24, ly), new Color(200, 208, 235), 0.5f);
                ly += 30;
            }
            string total = $"+{Ui.Money(_payout)}";
            f.DrawOutlined(sb, total, new Vector2(panel.X + 24, panel.Y + 220), new Color(141, 255, 63), 1.1f);
            f.Draw(sb, "added to the PRIZE POT", new Vector2(panel.X + 24, panel.Y + 262), new Color(160, 165, 190), 0.42f);
        }

        if (_payoutShown)
        {
            string hint = "ENTER: CONTINUE";
            var hs = f.Measure(hint, 0.55f);
            float blink = 0.6f + 0.4f * MathF.Sin(_t * 4f);
            f.Draw(sb, hint, new Vector2(640, 668), new Color(220, 225, 245) * blink, 0.55f, 0f, new Vector2(hs.X / 2, 0), true);
        }

        _fx.Draw(sb);
        Ui.End();
        _screen.Draw(sb, vp);
    }

    string _lvName() => _season.CurrentLevel?.Dto.Name ?? _season.Round.Level;
}
