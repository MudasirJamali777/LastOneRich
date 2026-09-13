using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>
/// Plays one round. Update order follows GDD 18.3:
/// input → player → bots → platforms/hazards → collision integration → scoring → camera → HUD.
/// </summary>
public sealed class GameplayState : IGameState
{
    enum Phase { Countdown, Racing, Ended }

    readonly StateMachine _sm;
    readonly SeasonRun _season;

    Level _lv;
    readonly List<Actor> _actors = new();
    PlayerController _pc;
    readonly List<BotController> _bots = new();
    RaceTracker _tracker;

    readonly Camera3D _cam = new();
    readonly Particles _fx = new();
    readonly ScreenFX _screen = new();

    Phase _phase = Phase.Countdown;
    float _phaseT;
    const float CountTime = 3.6f;
    int _lastCount = 4;
    bool _celebrated;
    string _finishBanner;
    float _endHold = 2.2f;

    bool _pause;
    int _pauseSel;
    static readonly string[] PauseItems = { "RESUME", "RESTART ROUND", "QUIT TO MENU" };

    int _roundNo, _roundTotal;

    // Debug overlay data (F3): raw screen intent vs resolved world XZ.
    public static Vector2 DebugMoveRaw = Vector2.Zero;
    public static Vector2 DebugMoveXZ = Vector2.Zero;

    public GameplayState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    Vector2 ResolveMove(Vector2 raw)
    {
        if (raw.LengthSquared() < 0.001f) return Vector2.Zero;
        var right = _cam.RightDir; right.Y = 0f;
        if (right.LengthSquared() < 0.0001f) right = new Vector3(1f, 0f, 0f);
        right.Normalize();
        var fwd = _cam.ForwardDir; fwd.Y = 0f;
        if (fwd.LengthSquared() < 0.0001f) fwd = new Vector3(0f, 0f, 1f);
        fwd.Normalize();
        var w = right * raw.X + fwd * raw.Y;
        if (w.LengthSquared() < 0.0001f) return Vector2.Zero;
        w.Normalize();
        return new Vector2(w.X, w.Z);
    }

    public void Enter()
    {
        var round = _season.Round;
        if (string.Equals(round.Level, "none", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(round.Level, "auction", StringComparison.OrdinalIgnoreCase))
        {
            _sm.Replace(new AuctionState(_sm, _season));
            return;
        }
        var twists = _season.ActiveTwist != null ? new List<TwistDTO> { _season.ActiveTwist } : null;
        if (_season.ActiveTwist != null)
        {
            var errs = TwistValidator.ValidateSelection(twists, round.Round);
            if (errs.Count > 0)
            {
                foreach (var e in errs) System.Console.WriteLine($"[validator] rejecting twist: {e}");
                twists = null; // fail safe: run clean rather than invalid
                _season.ActiveTwist = null;
            }
        }

        _lv = new Level(Json.Load<LevelDTO>($"data/levels/{round.Level}.json"), twists, n => GameServices.Audio.Event(n));
        _season.CurrentLevel = _lv;

        int spawn = 0;
        foreach (var c in _season.Cast)
        {
            if (c.Eliminated) continue;
            var a = new Actor { Name = c.Name, IsPlayer = c.IsPlayer, Color = c.Color };
            var sp = spawn < _lv.Spawns.Length ? _lv.Spawns[spawn] : Vector3.Zero;
            spawn++;
            a.Pos = sp + new Vector3(0, Actor.HalfY + 0.06f, 0);
            _actors.Add(a);
            if (c.IsPlayer) _pc = new PlayerController();
            else _bots.Add(new BotController(_lv.Graph, c.Personality, _bots.Count + 1));
        }

        _tracker = new RaceTracker(_actors, _lv);
        _season.Tracker = _tracker;
        if (_season.Upgrades.Contains("shield"))
            _lv.ShieldHook = a => a.IsPlayer && _season.ConsumeUpgrade("shield");

        _roundNo = _season.RoundIdx + 1;
        _roundTotal = _season.Season.Rounds.Count;

        var p = _actors[0];
        _cam.Position = p.Pos + new Vector3(0, 5f, -10f);
        _cam.LookAt = p.Pos;

        GameServices.Audio.PlayMusic();
        _screen.FadeAlpha = 1f;
        _screen.FadeTo(0f, 2.5f);
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _screen.Update(dt);
        _fx.Update(dt);
        _phaseT += dt;

        if (_pause) { UpdatePause(dt); return; }

        if (Input.PausePressed && _phase == Phase.Racing)
        {
            _pause = true;
            _pauseSel = 0;
            GameServices.Audio.Event("blip");
            return;
        }

        if (_phase == Phase.Countdown)
        {
            if (_lv == null) return; // auction redirect pending — never tick a level-less round
            _lv.Update(dt, _actors); // scenery keeps moving during the countdown
            int n = (int)MathF.Ceiling(CountTime - _phaseT - 0.6f);
            if (n != _lastCount && n >= 1 && n <= 3) { _lastCount = n; GameServices.Audio.Event("blip"); }
            if (_phaseT >= CountTime)
            {
                _phase = Phase.Racing;
                _phaseT = 0;
                GameServices.Audio.Event("go");
                _fx.SparkBurst(new Vector2(640, 360), 60, new Color(255, 235, 150));
            }
            CameraFollow(dt);
            return;
        }

        // ---- racing / ended ----
        _lv.Update(dt, _actors);

        if (_phase == Phase.Racing)
        {
            var p = _actors[0];
            if (!p.Finished)
            {
                // Camera-relative movement: raw keys/stick are screen-space intent,
                // resolved into world XZ via the chase camera's flattened basis.
                // This keeps A = screen-left and D = screen-right at every yaw.
                var raw = Input.Move;
                DebugMoveRaw = raw;
                var mv = ResolveMove(raw);
                DebugMoveXZ = mv;
                var inp = new InputState
                {
                    Move = mv,
                    Jump = Input.JumpPressed,
                    Dive = Input.DivePressed,
                };
                _pc.Update(p, inp, _lv, dt);
            }
            else
            {
                _lv.World.Integrate(p, dt, _lv.CarryFor(p));
            }

            int bi = 0;
            foreach (var a in _actors)
            {
                if (a.IsPlayer) continue;
                _bots[bi++].Update(a, _lv, dt, _actors);
            }

            Modes.UpdateRound(_lv, _actors, _tracker, dt);

            if (_lv.Dto.Type == "Race" && p.Finished && !_celebrated)
            {
                _celebrated = true;
                _fx.ConfettiBurst(new Vector2(640, 240), 130);
                GameServices.Audio.Event("stinger_win");
                _finishBanner = $"FINISHED — {Ui.Ordinal(_tracker.LiveRank(p))}  ·  {p.FinishTime:0.0}s";
            }
            if (p.RoundOut && !_celebrated)
            {
                _celebrated = true;
                GameServices.Audio.Event("stinger_elim");
                _finishBanner = _lv.Dto.Type == "StrikesOut" ? "3 STRIKES — YOU'RE OUT!" : "ELIMINATED FROM THE ROUND";
            }
        }
        else
        {
            foreach (var a in _actors)
                _lv.World.Integrate(a, dt, _lv.CarryFor(a));
        }

        _tracker.Update(dt);

        if (_phase == Phase.Racing && _tracker.Ended)
        {
            _phase = Phase.Ended;
            _phaseT = 0;
            _screen.Flash(0.25f);
        }
        else if (_phase == Phase.Ended)
        {
            _endHold -= dt;
            if (_endHold <= 0)
            {
                _sm.Replace(new ResultsState(_sm, _season, _tracker.Ranking()));
                return;
            }
        }

        CameraFollow(dt);
    }

    void UpdatePause(float dt)
    {
        if (Input.UpPressed) { _pauseSel = (_pauseSel + PauseItems.Length - 1) % PauseItems.Length; GameServices.Audio.Event("blip"); }
        if (Input.DownPressed) { _pauseSel = (_pauseSel + 1) % PauseItems.Length; GameServices.Audio.Event("blip"); }
        if (Input.PausePressed && _pauseSel == 0) { _pause = false; return; }
        if (Input.ConfirmPressed)
        {
            switch (_pauseSel)
            {
                case 0:
                    _pause = false;
                    GameServices.Audio.Event("blip");
                    break;
                case 1:
                    GameServices.Audio.Event("blip");
                    _sm.Replace(new GameplayState(_sm, _season));
                    break;
                case 2:
                    GameServices.Audio.Event("blip");
                    _sm.Replace(new MainMenuState(_sm));
                    break;
            }
        }
    }

    void CameraFollow(float dt)
    {
        var p = _actors[0];
        if (p.Finished)
        {
            float ang = _phaseT * 0.9f + 1f;
            var target = p.Pos + new Vector3(MathF.Sin(ang) * 7f, 3.6f, MathF.Cos(ang) * 7f);
            _cam.SmoothTo(target, p.Pos + new Vector3(0, 1.2f, 0), 3f, dt);
            return;
        }
        var face = p.FaceDir; face.Y = 0;
        if (face.LengthSquared() < 0.001f) face = new Vector3(0, 0, 1);
        face.Normalize();
        var camPos = p.Pos - face * 7.5f + new Vector3(0, 4.6f, 0);
        var look = p.Pos + new Vector3(0, 1.4f, 0) + face * 2.5f;
        _cam.SmoothTo(camPos, look, 6f, dt);
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        float aspect = vp.Width / (float)vp.Height;
        GameServices.Gfx.Clear(_lv.SkyColor);
        var r = GameServices.Renderer;
        r.BeginFrame(_cam, aspect, _lv.SkyColor);
        _lv.Draw(r);
        foreach (var a in _actors) ActorRenderer.Draw(r, a);
        r.EndFrame();

        var f = GameServices.Font;
        var sb = GameServices.Sb;
        Ui.Begin(vp);

        DrawHud(f, sb, aspect);

        if (_phase == Phase.Countdown) DrawCountdown(f, sb);
        if (!string.IsNullOrEmpty(_finishBanner) && _phase != Phase.Countdown) DrawFinishBanner(f, sb);
        if (_pause) DrawPause(f, sb);

        _fx.Draw(sb);
        Ui.End();
        _screen.Draw(sb, vp);
    }

    void DrawHud(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, float aspect)
    {
        // timer (top-left)
        double remain = System.Math.Max(0, _lv.Dto.TimeLimit - _tracker.Time);
        string time = remain.ToString("0.0");
        var timeCol = remain < 15 ? new Color(255, 90, 90) : Color.White;
        Ui.Rect(new Vector2(18, 14), new Vector2(180, 64), new Color(8, 8, 18, 190));
        f.Draw(sb, "TIME", new Vector2(34, 20), new Color(150, 160, 190), 0.45f);
        f.DrawOutlined(sb, time, new Vector2(34, 36), timeCol, 1.35f);

        // round banner (top-center)
        string round = $"ROUND {_roundNo}/{_roundTotal} — {_lv.Dto.Name}";
        var rs = f.Measure(round, 0.8f);
        f.DrawOutlined(sb, round, new Vector2(640, 16), Color.White, 0.8f, 0f, new Vector2(rs.X / 2, 0));
        string objective = ObjectiveText();
        var os = f.Measure(objective, 0.5f);
        f.Draw(sb, objective, new Vector2(640, 52), new Color(63, 210, 255), 0.5f, 0f, new Vector2(os.X / 2, 0), true);
        if (_season.ActiveTwist != null)
        {
            string tw = $"TWIST: {_season.ActiveTwist.Name}";
            var tws = f.Measure(tw, 0.5f);
            f.DrawOutlined(sb, tw, new Vector2(640, 80), new Color(255, 157, 46), 0.5f, 0f, new Vector2(tws.X / 2, 0));
        }

        // runners (top-right)
        string runners = $"RUNNERS {_actors.Count}";
        var ru = f.Measure(runners, 0.6f);
        Ui.Rect(new Vector2(1280 - 20 - ru.X - 24, 14), new Vector2(ru.X + 24, 40), new Color(8, 8, 18, 190));
        f.Draw(sb, runners, new Vector2(1280 - 32 - ru.X, 24), Color.White, 0.6f);

        // live standings (right side)
        var sorted = _actors.OrderBy(a => _tracker.LiveRank(a)).ToList();
        float y = 96;
        Ui.Rect(new Vector2(1064, y - 8), new Vector2(200, sorted.Count * 30 + 16), new Color(8, 8, 18, 150));
        foreach (var a in sorted)
        {
            int rank = _tracker.LiveRank(a);
            var col = a.IsPlayer ? new Color(255, 210, 63) : a.Color;
            string line = $"{rank}  {a.Name}";
            if (a.Finished) line += "  OK";
            f.Draw(sb, line, new Vector2(1076, y), col, 0.52f, 0f, Vector2.Zero, !a.IsPlayer);
            y += 30;
        }

        // mode-specific player status (GDD §12 HUD)
        var pl = _actors[0];
        float hx = 18, hy = 70;
        if (_lv.Dto.Type == "StrikesOut")
        {
            for (int i = 0; i < 3; i++)
            {
                bool lit = pl.Strikes > i;
                Ui.Rect(new Vector2(hx + i * 34, hy), new Vector2(28, 28), lit ? new Color(255, 70, 70) : new Color(16, 18, 32, 220));
                Ui.Frame(new Rectangle((int)hx + i * 34, (int)hy, 28, 28), 2, lit ? Color.White : new Color(60, 66, 90));
                f.Draw(sb, "!", new Vector2(hx + i * 34 + 9, hy + 3), lit ? Color.White : new Color(90, 96, 120), 0.7f);
            }
        }
        if (_lv.Dto.Type == "ScoreCollect")
        {
            string carry = $"BLOCKS {pl.Carrying}/{_lv.Dto.CarryCap}   BANKED {Ui.Money(pl.Score)}";
            Ui.Rect(new Vector2(hx, hy), new Vector2(320, 34), new Color(8, 8, 18, 190));
            f.Draw(sb, carry, new Vector2(hx + 10, hy + 6), new Color(255, 235, 150), 0.6f);
        }
        if (_lv.Dto.Type == "SurvivalZone")
        {
            bool safe = pl.OnButton;
            string st = safe ? "IN THE ZONE" : "OUTSIDE — GET BACK!";
            var col = safe ? new Color(141, 255, 63) : new Color(255, 70, 70);
            Ui.Rect(new Vector2(hx, hy), new Vector2(280, 34), new Color(8, 8, 18, 190));
            f.Draw(sb, $"{st}  {pl.Score:0}s", new Vector2(hx + 10, hy + 6), col, 0.6f);
        }
        if (_lv.Dto.Type == "FinaleButton")
        {
            Ui.Rect(new Vector2(hx, hy), new Vector2(260, 40), new Color(8, 8, 18, 190));
            f.Draw(sb, pl.OnButton ? "ON THE BUTTON!" : $"RATE ×{1 + pl.WaitTime * 0.08:0.0}", new Vector2(hx + 10, hy + 4), pl.OnButton ? new Color(255, 90, 90) : new Color(141, 255, 63), 0.6f);
            Ui.Rect(new Vector2(hx + 10, hy + 26), new Vector2(240, 8), new Color(30, 34, 50));
            Ui.Rect(new Vector2(hx + 10, hy + 26), new Vector2((float)(240 * System.Math.Clamp(pl.Stamina / 100.0, 0, 1)), 8), new Color(63, 210, 255));
        }

        // wallet (bottom-left)
        var wallet = _season.Wallet;
        Ui.Rect(new Vector2(18, 656), new Vector2(360, 48), new Color(8, 8, 18, 190));
        f.Draw(sb, "BANK", new Vector2(30, 668), new Color(141, 255, 63), 0.45f);
        f.Draw(sb, Ui.Money(wallet.Banked), new Vector2(30, 682), Color.White, 0.62f);
        string potLabel = wallet.PendingMult > 1 ? $"POT ×{wallet.PendingMult:0.#}" : "POT";
        f.Draw(sb, potLabel, new Vector2(200, 668), new Color(255, 210, 63), 0.45f);
        f.Draw(sb, Ui.Money(wallet.Risked), new Vector2(200, 682), new Color(255, 235, 150), 0.62f);

        // controls hint during the first seconds
        if ((_phase == Phase.Countdown || _tracker.Time < 5) && !_pause)
        {
            string hint = "WASD MOVE · SPACE JUMP · SHIFT DIVE · ESC PAUSE";
            var hs = f.Measure(hint, 0.45f);
            f.Draw(sb, hint, new Vector2(1262, 692), new Color(160, 165, 190), 0.45f, 0f, new Vector2(hs.X, 0), true);
        }

        // player marker arrow (projected 3D → screen)
        var p = _actors[0];
        var gd = GameServices.Gfx;
        var ps = gd.Viewport.Project(p.Pos + new Vector3(0, 2.5f, 0), _cam.Projection(aspect), _cam.View, Matrix.Identity);
        if (ps.Z is > 0 and < 1)
        {
            var mat = Ui.ComputeTransform(gd.Viewport);
            var sp = Vector2.Transform(new Vector2(ps.X, ps.Y), mat);
            f.Draw(sb, "▼", sp, new Color(255, 71, 71), 0.6f, 0f, new Vector2(f.Measure("▼", 0.6f).X / 2, 0));
        }
    }

    string ObjectiveText() => _lv.Dto.Type switch
    {
        "SurvivalZone" => $"STAY IN THE GOLD ZONE  ·  BOTTOM {_lv.Dto.Elimination.Percent:0}% OUT",
        "StrikesOut" => "DODGE THE DRONES  ·  3 STRIKES AND YOU'RE OUT",
        "ScoreCollect" => "GRAB AT THE ORANGE VAULT  ·  DEPOSIT AT THE GREEN PAD",
        "FinaleButton" => "HOLD THE BUTTON TO DRAIN RIVALS  ·  WAITING BUILDS YOUR RATE",
        _ => $"REACH THE FINISH  ·  BOTTOM {_lv.Dto.Elimination.Percent:0}% ELIMINATED",
    };

    void DrawCountdown(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb)
    {
        float tLeft = CountTime - _phaseT;
        string label = tLeft > 0.6f ? MathF.Ceiling(tLeft - 0.6f).ToString() : "GO!";
        float frac = tLeft > 0.6f ? (tLeft - 0.6f) % 1f : (tLeft / 0.6f);
        float scale = (1.6f + (1f - frac) * 0.8f) * (label == "GO!" ? 1.4f : 1f);
        var size = f.Measure(label, scale);
        var col = label == "GO!" ? new Color(141, 255, 63) : Color.White;
        f.DrawOutlined(sb, label, new Vector2(640, 260), col, scale, 0f, new Vector2(size.X / 2, 0));
    }

    void DrawFinishBanner(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb)
    {
        var size = f.Measure(_finishBanner, 1.0f);
        Ui.Rect(new Vector2(640 - size.X / 2 - 26, 200), new Vector2(size.X + 52, 56), new Color(8, 8, 18, 210));
        f.DrawOutlined(sb, _finishBanner, new Vector2(640, 214), new Color(255, 235, 150), 1.0f, 0f, new Vector2(size.X / 2, 0));
        if (_phase == Phase.Ended)
            f.Draw(sb, "RACE COMPLETE", new Vector2(640, 270), new Color(200, 205, 230), 0.55f, 0f, new Vector2(f.Measure("RACE COMPLETE", 0.55f).X / 2, 0), true);
    }

    void DrawPause(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb)
    {
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(5, 5, 12, 190));
        string title = "PAUSED";
        var ts = f.Measure(title, 1.4f);
        f.DrawOutlined(sb, title, new Vector2(640, 190), Color.White, 1.4f, 0f, new Vector2(ts.X / 2, 0));
        for (int i = 0; i < PauseItems.Length; i++)
        {
            bool sel = i == _pauseSel;
            var size = f.Measure(PauseItems[i], 0.9f);
            var pos = new Vector2(640, 320 + i * 66);
            if (sel) Ui.Rect(new Vector2(640 - size.X / 2 - 24, pos.Y - 10), new Vector2(size.X + 48, size.Y + 18), new Color(255, 210, 63, 45));
            f.DrawOutlined(sb, PauseItems[i], pos, sel ? new Color(255, 240, 180) : new Color(185, 190, 215), 0.9f, 0f, new Vector2(size.X / 2, 0));
        }
    }
}
