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
    readonly WorldParticles _worldFx = new();   // Priority 6: 3D debris (glass shards, dust)
    readonly ScreenFX _screen = new();

    Phase _phase = Phase.Countdown;
    float _phaseT;
    const float CountTime = 3.6f;
    int _lastCount = 4;
    bool _celebrated;
    string _finishBanner;
    float _endHold = 2.2f;

    bool _pause;
    readonly PauseMenu _pauseMenu = new();

    // Mouse-look: the boom is driven by Camera3D.Yaw/Pitch; the player body turns to match.
    float _bodyYaw;
    bool _bodyYawInit;

    int _roundNo, _roundTotal;
    float _twistBannerT = 99f;
    Color _twistSev = new(255, 210, 63);

#if DEBUG
    // Debug overlay data (F3): raw screen intent vs resolved world XZ.
    public static Vector2 DebugMoveRaw = Vector2.Zero;
    public static Vector2 DebugMoveXZ = Vector2.Zero;
#endif

    public GameplayState(StateMachine sm, SeasonRun season) { _sm = sm; _season = season; }

    /// <summary>
    /// Mouse-look movement basis: W drives straight down the camera's yaw-forward axis,
    /// A/D strafe along yaw-right. Pitch deliberately does not tilt the move plane.
    /// </summary>
    Vector2 ResolveMove(Vector2 raw)
    {
        if (raw.LengthSquared() < 0.001f) return Vector2.Zero;
        var w = _cam.YawRight * raw.X + _cam.YawForward * raw.Y;
        if (w.LengthSquared() < 0.0001f) return Vector2.Zero;
        w.Normalize();
        return new Vector2(w.X, w.Z);
    }

    /// <summary>Feed mouse (+ right stick) into the camera angles. Never runs while paused.</summary>
    void UpdateLook(float dt)
    {
        float sens = Keybinds.MouseSensitivity;
        var md = Input.MouseDelta;

        // Mouse right (+X) must swing the view right, which means Yaw DECREASES
        // under XNA's right-handed look-at basis (verified against CreateLookAt).
        float yaw = -md.X * sens;
        float pitch = md.Y * sens;

        // Pad look: scaled to a comfortable rad/sec rate rather than pixels.
        var stick = Input.LookStick;
        yaw -= stick.X * 2.6f * dt;
        pitch -= stick.Y * 2.0f * dt;

        if (Keybinds.InvertY) pitch = -pitch;

        // Pitch is "camera raised, looking down at the player", so pushing the mouse
        // forward (md.Y negative = look up) must lower it. Straight sign, no negation.
        _cam.ApplyLook(yaw, pitch);
    }

    /// <summary>Turn the body toward the camera yaw while movement is held; otherwise hold the last facing.</summary>
    void UpdateBodyFacing(Actor p, Vector2 rawMove, float dt)
    {
        if (!_bodyYawInit)
        {
            _bodyYaw = MathF.Atan2(p.FaceDir.X, p.FaceDir.Z);
            _bodyYawInit = true;
        }

        if (rawMove.LengthSquared() > 0.001f)
        {
            float diff = MathHelper.WrapAngle(_cam.Yaw - _bodyYaw);
            float k = 1f - MathF.Exp(-12f * dt);   // smooth turn, never a snap
            _bodyYaw = MathHelper.WrapAngle(_bodyYaw + diff * k);
        }

        p.FaceDir = new Vector3(MathF.Sin(_bodyYaw), 0f, MathF.Cos(_bodyYaw));
    }

    /// <summary>Capture the cursor only while the round is actually being played.</summary>
    void SyncMouseCapture()
    {
        bool want = !_pause && _phase != Phase.Ended && !_actors[0].Finished;
        Input.SetMouseCapture(want);
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
                foreach (var e in errs) GameLog.Log($"[validator] rejecting twist: {e}");
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

        // Difficulty (Settings ▸ Gameplay) is applied HERE rather than inside World code, so the
        // physics/AI layer keeps zero UI dependencies and HeadlessSim runs at the 1.0 defaults.
        Phys.BotPaceMult = Keybinds.DifficultyBotPaceMult;
        Phys.HazardMult = Keybinds.DifficultyHazardMult;
        foreach (var d in _lv.Drones) d.Speed *= Phys.HazardMult;   // drone patrols (StrikesOut)

        // Priority 6: the World layer raises a shatter event, the presentation layer answers with
        // shards. Keeping the arrow pointing this way is what lets HeadlessSim run the same
        // simulation with no renderer attached.
        _lv.ShatterFx = (center, size, tint) =>
        {
            _worldFx.GlassShatter(center, size, tint);
            var p = _actors.Count > 0 ? _actors[0] : null;
            if (p != null && Vector3.DistanceSquared(p.Pos, center) < 12f * 12f) _screen.Flash(0.12f);
        };

        _tracker = new RaceTracker(_actors, _lv);
        _season.Tracker = _tracker;
        if (_season.Upgrades.Contains("shield"))
            _lv.ShieldHook = a => a.IsPlayer && _season.ConsumeUpgrade("shield");

        _roundNo = _season.RoundIdx + 1;
        _roundTotal = _season.Season.Rounds.Count;

        // twist severity (banner color): strongest |log mult| across effects
        if (_season.ActiveTwist != null)
        {
            double sev = 0;
            foreach (var e in _season.ActiveTwist.Effects)
                sev = System.Math.Max(sev, System.Math.Abs(System.Math.Log((double)e.Mult)));
            _twistSev = ColorPalette.TwistSeverity(sev);
        }

        var p = _actors[0];

        // Start the boom behind the spawn facing, then let the orbit take over.
        var face0 = p.FaceDir; face0.Y = 0f;
        if (face0.LengthSquared() < 0.001f) face0 = new Vector3(0f, 0f, 1f);
        face0.Normalize();
        _cam.Yaw = MathF.Atan2(face0.X, face0.Z);
        _cam.Pitch = MathHelper.ToRadians(14f);
        _bodyYaw = _cam.Yaw;
        _bodyYawInit = true;
        _cam.UpdateOrbit(p.Pos, Keybinds.CameraDistance, Keybinds.CameraHeight, 1f, CamSweep);

        Input.SetMouseCapture(true);

        GameServices.Audio.PlayMusic();
        _screen.FadeAlpha = 1f;
        _screen.FadeTo(0f, 2.5f);
    }

    public void Exit() => Input.SetMouseCapture(false);

    public void Update(float dt)
    {
        // Pause freezes ALL game time — screen fx, particles and the phase clock included.
        // Only the pause menu itself ticks (bug fix 3).
        if (_pause) { UpdatePause(dt); return; }

        // Auction redirect pending (round 10 has no level JSON) — never tick a level-less round.
        if (_lv == null || _actors.Count == 0) return;

        // Pausing is allowed in every phase, not just Racing (bug fix 4).
        if (Input.PausePressed)
        {
            _pause = true;
            _pauseMenu.Open();
            Input.SetMouseCapture(false);
            GameServices.Audio.Event("blip");
            return;
        }

        _screen.Update(dt);
        _fx.Update(dt);
        _worldFx.Update(dt);   // Priority 6: 3D debris ticks with everything else (pause freezes it)
        _phaseT += dt;

        SyncMouseCapture();
        if (!_actors[0].Finished && _phase != Phase.Ended) UpdateLook(dt);

        if (_phase == Phase.Countdown)
        {
            _lv.Update(dt, _actors); // scenery keeps moving during the countdown
            int n = (int)MathF.Ceiling(CountTime - _phaseT - 0.6f);
            if (n != _lastCount && n >= 1 && n <= 3) { _lastCount = n; GameServices.Audio.Event("blip"); }
            if (_phaseT >= CountTime)
            {
                _phase = Phase.Racing;
                _phaseT = 0;
                _twistBannerT = 0f;
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
                // Mouse-look movement: W goes where the camera looks, A/D strafe,
                // S backs away — all relative to the camera yaw.
                var raw = Input.Move;
#if DEBUG
                DebugMoveRaw = raw;
#endif
                var mv = ResolveMove(raw);
#if DEBUG
                DebugMoveXZ = mv;
#endif
                var inp = new InputState
                {
                    Move = mv,
                    Jump = Input.JumpPressed,
                    Dive = Input.DivePressed,
                };
                _pc.Update(p, inp, _lv, dt);

                // Body turns to the camera yaw while moving; holds its last heading when idle.
                // Runs after the controller so it wins over the controller's intent-facing.
                UpdateBodyFacing(p, raw, dt);
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
        _twistBannerT += dt;

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
        // Cursor stays visible and free for the whole pause, in every sub-page.
        Input.SetMouseCapture(false);

        switch (_pauseMenu.Update(dt, GameServices.Gfx.Viewport))
        {
            case PauseMenu.Result.Resume:
                _pause = false;
                SyncMouseCapture();   // recapture only if the round is still live
                break;

            case PauseMenu.Result.RestartRound:
                _sm.Replace(new GameplayState(_sm, _season));
                break;

            case PauseMenu.Result.QuitToMenu:
                _sm.Replace(new MainMenuState(_sm));
                break;

            case PauseMenu.Result.QuitToDesktop:
                _sm.Quit();           // empties the machine; LorGame.Update then exits
                break;
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
        // Mouse-look orbit: boom length/height from controls.json, pulled in on geometry hits.
        _cam.UpdateOrbit(p.Pos, Keybinds.CameraDistance, Keybinds.CameraHeight, dt, CamSweep);
    }

    /// <summary>Camera collision probe — how far the boom can extend before hitting level geometry.</summary>
    float CamSweep(Vector3 origin, Vector3 dir, float maxDist) =>
        _lv?.World == null ? maxDist : _lv.World.RaySweep(origin, dir, maxDist);

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        float aspect = vp.Width / (float)vp.Height;
        GameServices.Gfx.Clear(_lv.SkyColor);
        var r = GameServices.Renderer;
        // Settings ▸ Graphics ▸ FOV, pushed per draw so a change made in the pause menu is
        // visible on the frozen frame behind it (fog is applied globally in LorGame.Draw).
        _cam.FovDeg = Keybinds.FovDeg;
        r.BeginFrame(_cam, aspect, _lv.SkyColor);
        _lv.Draw(r);
        float alpha = LorGame.InterpAlpha;
        foreach (var a in _actors) ActorRenderer.Draw(r, a, alpha);
        _worldFx.Draw(r);   // Priority 6: shards are world-space geometry, so they batch and
                            // depth-sort with the level instead of floating over it like the HUD
        r.EndFrame();

        var f = GameServices.Font;
        var sb = GameServices.Sb;
        Ui.Begin(vp);

        DrawNameTags(f, sb, aspect);
        DrawHud(f, sb, aspect);
        DrawTwistBanner(f, sb);

        if (_phase == Phase.Countdown) DrawCountdown(f, sb);
        if (!string.IsNullOrEmpty(_finishBanner) && _phase != Phase.Countdown) DrawFinishBanner(f, sb);
        if (_pause) _pauseMenu.Draw(f, sb);

        _fx.Draw(sb);
        Ui.End();
        _screen.Draw(sb, vp);
    }

    /// <summary>World-to-screen name tags: instant who-is-who; OUT contestants are greyed.</summary>
    void DrawNameTags(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, float aspect)
    {
        var gd = GameServices.Gfx;
        foreach (var a in _actors)
        {
            if (a.RoundOut) continue;
            var ps = gd.Viewport.Project(a.Pos + new Vector3(0, 1.95f, 0), _cam.Projection(aspect), _cam.View, Matrix.Identity);
            if (ps.Z is <= 0 or >= 1) continue;
            var col = a.IsPlayer ? new Color(255, 210, 63) : ColorUtil.Shade(a.Color, 1.12f);
            string label = a.IsPlayer ? "YOU" : a.Name;
            var s = f.Measure(label, 0.4f);
            f.Draw(sb, label, new Vector2(ps.X - s.X / 2, ps.Y), col, 0.4f, 0f, Vector2.Zero, true);
        }
    }

    /// <summary>Twist banner: drops in from the top at round start, holds, fades. Color = severity.</summary>
    void DrawTwistBanner(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb)
    {
        var tw = _season.ActiveTwist;
        if (tw == null || _twistBannerT > 3.4f || _phase == Phase.Countdown) return;

        float t = _twistBannerT;
        float drop = MathHelper.Clamp(t / 0.45f, 0f, 1f);
        drop = 1f - (1f - drop) * (1f - drop);            // ease-out
        float fade = t < 2.6f ? 1f : 1f - (t - 2.6f) / 0.8f;
        float y = MathHelper.Lerp(-70f, 96f, drop);

        string title = $"TWIST — {tw.Name}";
        var ts = f.Measure(title, 0.62f);
        float w = ts.X + 56f;
        var col = _twistSev * fade;

        Ui.Rect(new Vector2(640 - w / 2, y), new Vector2(w, 46), new Color(8, 8, 18, (int)(200 * fade)));
        Ui.Frame(new Rectangle((int)(640 - w / 2), (int)y, (int)w, 46), 2, _twistSev * fade);
        f.Draw(sb, title, new Vector2(640 - ts.X / 2, y + 12), col, 0.62f);
    }

    void DrawHud(BitmapFont f, Microsoft.Xna.Framework.Graphics.SpriteBatch sb, float aspect)
    {
        // timer (top-left) — pulses red under 15 s
        double remain = System.Math.Max(0, _lv.Dto.TimeLimit - _tracker.Time);
        string time = remain.ToString("0.0");
        bool urgent = remain < 15;
        var timeCol = urgent ? new Color(255, 90, 90) : Color.White;
        Ui.Rect(new Vector2(18, 14), new Vector2(180, 64), new Color(8, 8, 18, 190));
        f.Draw(sb, "TIME", new Vector2(34, 20), new Color(150, 160, 190), 0.45f);
        float tScale = urgent ? 1.35f * (1f + 0.05f * MathF.Sin(_tracker.Time * 10f)) : 1.35f;
        f.DrawOutlined(sb, time, new Vector2(34, 36), timeCol, tScale);

        // round banner (top-center)
        string round = $"ROUND {_roundNo}/{_roundTotal} — {_lv.Dto.Name}";
        var rs = f.Measure(round, 0.8f);
        f.DrawOutlined(sb, round, new Vector2(640, 16), Color.White, 0.8f, 0f, new Vector2(rs.X / 2, 0));
        string objective = ObjectiveText();
        var os = f.Measure(objective, 0.5f);
        f.Draw(sb, objective, new Vector2(640, 52), new Color(63, 210, 255), 0.5f, 0f, new Vector2(os.X / 2, 0), true);
        if (_lv.FallbackMode)
        {
            string warn = "UNIMPLEMENTED MODE — RACE RULES APPLY";
            var ws = f.Measure(warn, 0.45f);
            f.DrawOutlined(sb, warn, new Vector2(640, 74), new Color(255, 90, 90), 0.45f, 0f, new Vector2(ws.X / 2, 0));
        }
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

        // live standings (right side): header, player-row highlight, OUT markers, capped
        var sorted = _actors.OrderBy(a => _tracker.LiveRank(a)).ToList();
        float y = 118;
        int shown = System.Math.Min(sorted.Count, 10);
        Ui.Rect(new Vector2(1064, 88), new Vector2(200, shown * 30 + 52), new Color(8, 8, 18, 165));
        Ui.Frame(new Rectangle(1064, 88, 200, shown * 30 + 52), 1, new Color(60, 66, 90));
        f.Draw(sb, "LIVE", new Vector2(1076, 96), new Color(150, 160, 190), 0.42f);
        for (int i = 0; i < shown; i++)
        {
            var a = sorted[i];
            int rank = _tracker.LiveRank(a);
            if (a.IsPlayer) Ui.Rect(new Vector2(1068, y - 4), new Vector2(192, 28), new Color(255, 210, 63, 44));
            var col = a.IsPlayer ? new Color(255, 210, 63) : a.Color;
            string line = $"{rank}  {a.Name}";
            if (a.Finished) line += "  OK";
            if (a.RoundOut) line += "  OUT";
            f.Draw(sb, line, new Vector2(1076, y), a.RoundOut ? new Color(120, 126, 150) : col, 0.52f, 0f, Vector2.Zero, !a.IsPlayer);
            y += 30;
        }
        if (sorted.Count > shown)
            f.Draw(sb, $"+{sorted.Count - shown} MORE", new Vector2(1076, y), new Color(120, 126, 150), 0.42f);

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
        // Priority 6: glass tracker — rows cleared and whether the flawless run is still alive.
        // Shown on any level that has panes, so future glass courses inherit it for free.
        if (_lv.Tiles.Count > 0)
        {
            int rows = 0;
            foreach (var t in _lv.Tiles) if (t.Row + 1 > rows) rows = t.Row + 1;
            int crossed = 0;
            foreach (var t in _lv.Tiles)
                if (t.Pos.Z < pl.Pos.Z && t.Row + 1 > crossed) crossed = t.Row + 1;

            bool flawless = pl.TilesBroken == 0;
            var col = flawless ? new Color(141, 255, 63) : new Color(255, 150, 90);
            Ui.Rect(new Vector2(hx, hy), new Vector2(320, 34), new Color(8, 8, 18, 190));
            string line = flawless
                ? $"GLASS {crossed}/{rows}  ·  FLAWLESS"
                : $"GLASS {crossed}/{rows}  ·  BROKEN {pl.TilesBroken}";
            f.Draw(sb, line, new Vector2(hx + 10, hy + 6), col, 0.6f);
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

        // controls hint: bright during the intro seconds, faded afterwards (always readable)
        if (!_pause)
        {
            string hint = "MOUSE LOOK · WASD MOVE · SPACE JUMP · SHIFT DIVE · ESC PAUSE";
            var hs = f.Measure(hint, 0.42f);
            bool intro = _phase == Phase.Countdown || _tracker.Time < 5;
            f.Draw(sb, hint, new Vector2(1262, 694), intro ? new Color(160, 165, 190) : new Color(120, 126, 150, 130), 0.42f, 0f, new Vector2(hs.X, 0), true);
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
        // Priority 6: a glass course is still a Race, but the read is "pick panes", not "sprint"
        _ when _lv.Tiles.Count > 0 => "ONE PANE IN EACH ROW HOLDS  ·  WATCH WHO FALLS, THEN FOLLOW",
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
}
