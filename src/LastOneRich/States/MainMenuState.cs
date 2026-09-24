using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

public sealed class MainMenuState : IGameState
{
    // Priority 7: CREDITS added ahead of QUIT — every switch arm below is renumbered to match.
    static readonly string[] Items = { "NEW SEASON", "HOW TO PLAY", "ACHIEVEMENTS", "SETTINGS", "CREDITS", "QUIT" };

    readonly StateMachine _sm;
    Level _level;
    ArenaBackdrop _bg;
    MenuCast _cast;       // Priority 7: idle bots milling around behind the menu
    int _sel;
    bool _howTo;
    bool _achievements;   // Priority 5: trophy case page
    bool _settingsOpen;
    readonly SettingsScreen _settings = new();
    float _t;

    // Priority 7: per-row hover/select "heat" — eases toward 1 while hot, decays back to 0
    // otherwise, driving the glow strength, gold frame alpha and text scale together so a
    // selection change reads as a smooth pulse-up rather than an instant snap.
    readonly float[] _heat = new float[Items.Length];
    readonly System.Collections.Generic.List<Rectangle> _itemRects = new();

    public MainMenuState(StateMachine sm) => _sm = sm;

    public void Enter()
    {
        _level = Level.Load("level01");
        _bg = new ArenaBackdrop(_level);
        _cast = new MenuCast(_level);
        _t = 0f;
        System.Array.Clear(_heat, 0, _heat.Length);
        // Priority 7: bed swells in over 1.2s instead of snapping to full volume on entry —
        // most noticeable the very first time the menu appears, right after the boot splash.
        GameServices.Audio.PlayMusicFadeIn("music_loop", 1.2f);
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;
        _bg.Update(dt);
        _cast.Update(dt);

        if (_howTo)
        {
            if (Input.ConfirmPressed || Input.BackPressed) { _howTo = false; GameServices.Audio.Event("blip"); }
            return;
        }

        if (_achievements)
        {
            if (Input.ConfirmPressed || Input.BackPressed) { _achievements = false; GameServices.Audio.Event("blip"); }
            return;
        }

        // Settings overlay: the screen owns its own navigation, and backs out with ESC / B.
        if (_settingsOpen)
        {
            if (_settings.Update(dt, GameServices.Gfx.Viewport) == SettingsScreen.Result.Back)
            {
                _settings.Close();        // flushes edits to saves/settings.json
                _settingsOpen = false;
                _sel = 3;                 // land back on SETTINGS
                GameServices.Audio.Event("blip");
            }
            return;                       // the menu list itself is inert while settings is up
        }

        if (Input.UpPressed) { _sel = (_sel + Items.Length - 1) % Items.Length; GameServices.Audio.Event("blip"); }
        if (Input.DownPressed) { _sel = (_sel + 1) % Items.Length; GameServices.Audio.Event("blip"); }

        // Mouse hover moves the highlight (only when the cursor actually moved, so it never
        // fights keyboard/gamepad navigation), and a click on the highlighted row activates it —
        // the same two-stage rule PauseMenu and SettingsScreen already use.
        var m = Input.MouseUi(GameServices.Gfx.Viewport);
        int hover = HitIndex(m);
        bool click = Input.MouseLeftPressed;
        if (Input.MouseMoved && hover >= 0 && hover != _sel) { _sel = hover; GameServices.Audio.Event("blip"); }

        bool activate = Input.ConfirmPressed || (click && hover >= 0 && hover == _sel);

        // Heat: ease every row toward 1 (selected) or 0 (not) — smoother than a binary highlight.
        for (int i = 0; i < Items.Length; i++)
        {
            float target = i == _sel ? 1f : 0f;
            _heat[i] += (target - _heat[i]) * MathHelper.Clamp(dt * 10f, 0f, 1f);
        }

        if (activate)
        {
            GameServices.Audio.Event("cash");
            switch (_sel)
            {
                case 0:
                    var run = SeasonRun.Create();
                    var errors = TwistValidator.ValidateSeason(run);
                    foreach (var e in errors)
                        GameLog.Log($"[validator] {e}");
                    // QA pass: the validator's findings were logged and then thrown away, so a
                    // broken content edit started a season anyway and failed later, somewhere
                    // less legible. ContentErrorState exists precisely for this (GDD §14) and
                    // was reachable from nowhere — it is the season's front door now.
                    if (errors.Count > 0)
                        _sm.Replace(new ContentErrorState(_sm, errors));
                    else
                        _sm.Replace(new IntroCutsceneState(_sm, run));
                    break;
                case 1:
                    _howTo = true;
                    break;
                case 2:
                    _achievements = true;
                    break;
                case 3:
                    _settings.Open();
                    _settingsOpen = true;
                    break;
                case 4:
                    _sm.Replace(new CreditsState(_sm));
                    break;
                case 5:
                    _sm.Quit();
                    break;
            }
        }
    }

    int HitIndex(Vector2 m)
    {
        for (int i = 0; i < _itemRects.Count; i++)
            if (_itemRects[i].Contains((int)m.X, (int)m.Y)) return i;
        return -1;
    }

    public void Draw()
    {
        _bg.Draw(_cast);
        var vp = GameServices.Gfx.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        // dark gradient panel top
        Ui.Begin(vp);
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 190), new Color(8, 8, 18, 160));
        Ui.Rect(new Vector2(0, 186), new Vector2(1280, 4), new Color(255, 210, 63, 220));

        // ---- Priority 7: title breathing scale + gold glow ----
        // A slow (0.6 Hz) breathing scale on top of the old faster pulse, plus a soft oversized
        // glow copy drawn first so the crisp outlined title sits on a warm halo instead of the
        // flat backdrop panel.
        float fastPulse = 0.85f + 0.15f * MathF.Sin(_t * 3f);
        float breathe = 1f + 0.035f * MathF.Sin(_t * 1.9f);
        string title = "LAST ONE RICH!";
        float baseScale = 2.6f * breathe;
        var ts = f.Measure(title, baseScale);
        float glowAlpha = 0.28f + 0.12f * MathF.Sin(_t * 1.9f + 1.2f);
        var glowScale = baseScale * 1.04f;
        var gs = f.Measure(title, glowScale);
        f.Draw(sb, title, new Vector2(640, 46), new Color(255, 210, 63) * glowAlpha, glowScale, 0f, new Vector2(gs.X / 2, 0));
        f.DrawOutlined(sb, title, new Vector2(640, 46), new Color(255, 210, 63) * fastPulse, baseScale, 0f, new Vector2(ts.X / 2, 0));

        // Priority 7: replaces the old generic subtitle with the tagline the spec asks for.
        string sub = "COMPETE. SURVIVE. CASH OUT... OR RISK IT ALL.";
        var ss = f.Measure(sub, 0.62f);
        f.Draw(sb, sub, new Vector2(640, 140), new Color(200, 210, 255), 0.62f, 0f, new Vector2(ss.X / 2, 0), true);

        // menu items
        bool overlay = _howTo || _achievements || _settingsOpen;
        _itemRects.Clear();
        for (int i = 0; i < Items.Length; i++)
        {
            bool selected = i == _sel && !overlay;
            float heat = overlay ? 0f : _heat[i];

            // Entry rise-in: each row eases up from below into its resting spot, staggered by
            // index so the list reads as cascading in rather than popping all at once.
            float delay = i * 0.05f;
            float rise = MathHelper.Clamp((_t - delay) / 0.35f, 0f, 1f);
            rise = 1f - (1f - rise) * (1f - rise); // ease-out
            float yOffset = (1f - rise) * 40f;

            float scale = 1.05f + heat * 0.08f;
            var size = f.Measure(Items[i], scale);
            float baseY = 300 + i * 74;
            var pos = new Vector2(640, baseY + yOffset);

            var rowRect = new Rectangle((int)(640 - size.X / 2 - 34), (int)(pos.Y - 12), (int)(size.X + 68), (int)(size.Y + 22));
            _itemRects.Add(rowRect);

            if (heat > 0.01f)
            {
                Ui.Rect(rowRect, new Color(255, 210, 63, (int)(40 * heat)));
                Ui.Frame(rowRect, 2, new Color(255, 210, 63, (int)(200 * heat)));
            }

            var col = Color.Lerp(new Color(190, 195, 220), new Color(255, 240, 180), heat) * MathHelper.Clamp(rise, 0.001f, 1f);
            f.DrawOutlined(sb, Items[i], pos, col, scale, 0f, new Vector2(size.X / 2, 0));

            if (heat > 0.01f)
            {
                // Bobbing marker: gentle horizontal drift so the arrow reads as alive, not static.
                float bob = MathF.Sin(_t * 5f) * 4f;
                f.Draw(sb, ">", new Vector2(640 - size.X / 2 - 26 + bob, pos.Y), new Color(255, 210, 63) * heat, scale, 0f, new Vector2(f.Measure(">", scale).X, 0));
            }
        }

        // career footer (Priority 4 stats + Priority 5 trophy count)
        var save = GameServices.Save;
        string career = $"CAREER  BANKED {Ui.Money(save.TotalBanked)}   SEASONS {save.SeasonsPlayed}   WINS {save.Championships}   " +
                        $"TROPHIES {Achievements.UnlockedCount()}/{Achievements.All.Length}";
        var cs = f.Measure(career, 0.55f);
        f.Draw(sb, career, new Vector2(640, 668), new Color(150, 160, 190), 0.55f, 0f, new Vector2(cs.X / 2, 0), true);

        if (_howTo) DrawHowTo(f);
        if (_achievements) DrawAchievements(f);
        if (_settingsOpen) _settings.Draw(f, sb);

        Ui.End();
    }

    void DrawHowTo(BitmapFont f)
    {
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(5, 5, 12, 215));
        var panel = new Rectangle(240, 90, 800, 540);
        Ui.Rect(panel, new Color(18, 20, 36, 245));
        Ui.Frame(panel, 3, new Color(255, 210, 63));
        float y = 122;
        f.Draw(GameServices.Sb, "HOW TO PLAY", new Vector2(640, y), new Color(255, 210, 63), 1.2f, 0f, new Vector2(f.Measure("HOW TO PLAY", 1.2f).X / 2, 0));
        y += 70;
        string[] lines =
        {
            "MOVE .......... WASD / ARROWS / LEFT STICK",
            "JUMP .......... SPACE / A",
            "DIVE .......... SHIFT / X  (burst dash with cooldown)",
            "F3 ............ DEBUG OVERLAY (FPS, move vector)",
            "",
            "Survive each episode. The BOTTOM 20% of the field",
            "is ELIMINATED at the results ceremony.",
            "",
            "Every round you earn CASH. Choose to BANK it (safe)",
            "or RISK it in the pot — survive the next round and",
            "the pot DOUBLES. Get eliminated and it's gone.",
            "",
            "At some rounds MAX VOLT offers cold, hard cash to",
            "walk away... or you chase the $1,000,000 finale.",
            "",
            "Watch for TWISTS between rounds. RINA approves them.",
        };
        foreach (var line in lines)
        {
            f.Draw(GameServices.Sb, line, new Vector2(280, y), line.StartsWith("MOVE") || line.StartsWith("JUMP") || line.StartsWith("DIVE") ? new Color(63, 210, 255) : new Color(215, 220, 240), 0.58f);
            y += 30;
        }
        f.Draw(GameServices.Sb, "ENTER / A: BACK", new Vector2(640, 596), new Color(150, 160, 190), 0.55f, 0f, new Vector2(f.Measure("ENTER / A: BACK", 0.55f).X / 2, 0));
    }

    /// <summary>Priority 5: trophy case — every achievement, earned (gold) or locked (grey).</summary>
    void DrawAchievements(BitmapFont f)
    {
        var sb = GameServices.Sb;
        Ui.Rect(new Vector2(0, 0), new Vector2(1280, 720), new Color(5, 5, 12, 215));
        var panel = new Rectangle(240, 60, 800, 600);
        Ui.Rect(panel, new Color(18, 20, 36, 245));
        Ui.Frame(panel, 3, new Color(255, 210, 63));

        int earned = Achievements.UnlockedCount();
        string title = $"ACHIEVEMENTS — {earned}/{Achievements.All.Length}";
        f.Draw(sb, title, new Vector2(640, panel.Y + 22), new Color(255, 210, 63), 1.1f, 0f,
            new Vector2(f.Measure(title, 1.1f).X / 2, 0));

        float y = panel.Y + 92;
        var gold = new Color(255, 210, 63);
        foreach (var a in Achievements.All)
        {
            bool got = Achievements.Has(a.Id);
            var nameCol = got ? gold : new Color(110, 116, 140);
            var descCol = got ? new Color(200, 208, 235) : new Color(90, 96, 120);
            string name = got ? a.Name : "???";

            if (got) Ui.Rect(new Vector2(panel.X + 24, y - 6), new Vector2(8, 46), gold);
            f.DrawOutlined(sb, name, new Vector2(panel.X + 46, y), nameCol, 0.68f);
            f.Draw(sb, a.Desc, new Vector2(panel.X + 46, y + 30), descCol, 0.46f, 0f, Vector2.Zero, true);
            if (got)
            {
                string tag = "EARNED";
                f.Draw(sb, tag, new Vector2(panel.Right - 40 - f.Measure(tag, 0.5f).X, y + 8), gold, 0.5f);
            }
            y += 52;
        }

        string hint = "ENTER / A OR ESC: BACK";
        f.Draw(sb, hint, new Vector2(640, panel.Bottom - 48), new Color(150, 160, 190), 0.55f, 0f,
            new Vector2(f.Measure(hint, 0.55f).X / 2, 0));
    }
}
