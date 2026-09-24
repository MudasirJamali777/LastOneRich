using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>
/// Priority 7: the very first thing a brand-new player (or a returning one whose save predates
/// this screen) sees — a one-page controls summary plus a CASUAL / STANDARD / HARDCORE pick,
/// so nobody's first round starts on a difficulty they never chose.
///
/// Shown by <see cref="BootState"/> exactly when <see cref="SaveSystem.HasSave"/> is false, or
/// the loaded save has never flipped <see cref="SaveData.WelcomeSeen"/>. Confirming here writes
/// the difficulty straight through <see cref="SettingsStore"/> (the same path the Settings menu
/// itself uses, so GAMEPLAY ▸ DIFFICULTY shows the exact choice made here) and flips
/// WelcomeSeen so the screen never appears again for this career.
/// </summary>
public sealed class WelcomeState : IGameState
{
    static readonly string[] Difficulties = { "CASUAL", "STANDARD", "HARDCORE" };
    static readonly string[] Blurbs =
    {
        "SLOWER RIVALS, GENTLER HAZARDS. LEARN THE ROPES.",
        "THE INTENDED EXPERIENCE. FAIR FIGHT, FAIR PRIZE.",
        "FASTER RIVALS, MEANER HAZARDS. NO TRAINING WHEELS.",
    };

    readonly StateMachine _sm;
    int _sel = 1;   // STANDARD by default — matches Keybinds' own fallback
    float _t;

    public WelcomeState(StateMachine sm) => _sm = sm;

    public void Enter()
    {
        // Seed the selector from whatever difficulty is already resolved (a returning player who
        // customized controls.json by hand but never played, for instance) rather than always
        // forcing STANDARD on screen.
        for (int i = 0; i < Difficulties.Length; i++)
            if (Difficulties[i] == Keybinds.Difficulty) _sel = i;

        GameServices.Audio.PlayMusic();
    }

    public void Exit() { }

    public void Update(float dt)
    {
        _t += dt;

        if (Input.LeftPressed) { _sel = (_sel + Difficulties.Length - 1) % Difficulties.Length; GameServices.Audio.Event("blip"); }
        if (Input.RightPressed) { _sel = (_sel + 1) % Difficulties.Length; GameServices.Audio.Event("blip"); }
        if (Input.UpPressed || Input.DownPressed) { _sel = Difficulties.Length - 1 - _sel; GameServices.Audio.Event("blip"); } // symmetric d-pad nav

        if (Input.ConfirmPressed)
        {
            GameServices.Audio.Event("cash");
            Commit();
            _sm.Replace(new MainMenuState(_sm));
        }
    }

    /// <summary>Persist the chosen difficulty (same file + path the Settings screen writes) and
    /// mark the career as having seen this screen, so it never shows again.</summary>
    void Commit()
    {
        Keybinds.Settings.Difficulty = Difficulties[_sel];
        Keybinds.Apply(Keybinds.Settings);
        SettingsStore.Persist(Keybinds.Settings);

        var save = GameServices.Save ?? new SaveData();
        save.WelcomeSeen = true;
        GameServices.Save = save;
        SaveSystem.Store(save);
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        GameServices.Gfx.Clear(new Color(10, 10, 20));
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);

        float ease = MathHelper.Clamp(_t / 0.3f, 0f, 1f);
        ease = 1f - (1f - ease) * (1f - ease);

        const int PanelW = 760, PanelH = 500;
        int px = (Ui.W - PanelW) / 2;
        int py = (int)MathHelper.Lerp(80f, 60f, ease);
        var panel = new Rectangle(px, py, PanelW, PanelH);

        Ui.Rect(panel, new Color(14, 16, 30, 246));
        Ui.Frame(panel, 3, new Color(255, 210, 63));
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(PanelW, 4), new Color(255, 210, 63));

        string title = "WELCOME TO THE VOLT DOME";
        var ts = f.Measure(title, 1.15f);
        f.DrawOutlined(sb, title, new Vector2(Ui.W / 2f, panel.Y + 26), new Color(255, 210, 63), 1.15f, 0f, new Vector2(ts.X / 2f, 0));

        // ---- controls summary ----
        float y = panel.Y + 90;
        string sub = "CONTROLS";
        f.Draw(sb, sub, new Vector2(panel.X + 48, y), new Color(63, 210, 255), 0.62f);
        y += 36;
        string[] lines =
        {
            "MOVE .......... WASD / ARROWS / LEFT STICK",
            "JUMP .......... SPACE / A",
            "DIVE .......... SHIFT / X  (burst dash with cooldown)",
            "LOOK .......... MOUSE / RIGHT STICK",
            "PAUSE ......... ESC / START",
        };
        foreach (var line in lines)
        {
            f.Draw(sb, line, new Vector2(panel.X + 48, y), new Color(210, 215, 238), 0.54f);
            y += 30;
        }

        // ---- difficulty selector ----
        y += 20;
        string dsub = "CHOOSE YOUR DIFFICULTY  (CHANGE ANYTIME IN SETTINGS)";
        f.Draw(sb, dsub, new Vector2(panel.X + 48, y), new Color(63, 210, 255), 0.54f);
        y += 44;

        int cw = (PanelW - 96 - 2 * 16) / 3;
        for (int i = 0; i < Difficulties.Length; i++)
        {
            var r = new Rectangle(panel.X + 48 + i * (cw + 16), (int)y, cw, 96);
            bool sel = i == _sel;
            Ui.Rect(r, sel ? new Color(255, 210, 63, 40) : new Color(255, 255, 255, 14));
            Ui.Frame(r, sel ? 3 : 1, sel ? new Color(255, 210, 63) : new Color(70, 76, 100));

            var ls = f.Measure(Difficulties[i], 0.68f);
            f.DrawOutlined(sb, Difficulties[i], new Vector2(r.X + r.Width / 2f, r.Y + 14),
                sel ? new Color(255, 240, 180) : new Color(190, 195, 220), 0.68f, 0f, new Vector2(ls.X / 2f, 0));
        }
        y += 112;

        string blurb = Blurbs[_sel];
        var bs = f.Measure(blurb, 0.48f);
        f.Draw(sb, blurb, new Vector2(Ui.W / 2f, y), new Color(200, 208, 235), 0.48f, 0f, new Vector2(bs.X / 2f, 0), true);

        string hint = "A / D OR ARROWS: CHOOSE   ENTER: START YOUR SEASON";
        var hs = f.Measure(hint, 0.46f);
        f.Draw(sb, hint, new Vector2(Ui.W / 2f, panel.Bottom - 34), new Color(150, 160, 190), 0.46f, 0f, new Vector2(hs.X / 2f, 0), true);

        Ui.End();
    }
}
