using LastOneRich.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.States;

/// <summary>
/// Self-contained pause overlay: root menu, a Settings placeholder (Priority 3 fills it in)
/// and a confirmation dialog for quitting a run.
///
/// It owns no game state and never ticks the world — GameplayState freezes everything and
/// forwards only this Update. Keyboard, gamepad and mouse all drive the same selection index,
/// so the highlight always matches what Confirm will activate.
/// </summary>
public sealed class PauseMenu
{
    /// <summary>What the owning state should do after an Update.</summary>
    public enum Result { None, Resume, RestartRound, QuitToMenu, QuitToDesktop }

    enum Page { Root, Settings, ConfirmQuit }

    static readonly string[] RootItems = { "RESUME", "RESTART ROUND", "SETTINGS", "QUIT TO MENU", "QUIT TO DESKTOP" };
    static readonly string[] ConfirmItems = { "NO, KEEP PLAYING", "YES, QUIT" };

    Page _page = Page.Root;
    int _sel;
    int _confirmSel;            // defaults to NO — the safe option
    float _t;                   // animation clock for the panel slide/fade

    // Hit rectangles are rebuilt every Draw so mouse tests always match what is on screen.
    readonly List<Rectangle> _rootRects = new();
    readonly List<Rectangle> _confirmRects = new();
    Rectangle _settingsBackRect;

    /// <summary>Reset to a clean root menu. Called each time the game is paused.</summary>
    public void Open()
    {
        _page = Page.Root;
        _sel = 0;
        _confirmSel = 0;
        _t = 0f;
        _rootRects.Clear();
        _confirmRects.Clear();
    }

    public Result Update(float dt, Viewport vp)
    {
        _t += dt;
        var m = Input.MouseUi(vp);
        bool moved = Input.MouseMoved;
        bool click = Input.MouseLeftPressed;

        switch (_page)
        {
            case Page.Root: return UpdateRoot(m, moved, click);
            case Page.Settings: return UpdateSettings(m, moved, click);
            case Page.ConfirmQuit: return UpdateConfirm(m, moved, click);
        }
        return Result.None;
    }

    Result UpdateRoot(Vector2 m, bool moved, bool click)
    {
        // ESC / B on the root page resumes — never quits.
        if (Input.PausePressed || Input.CancelPressed) { Blip(); return Result.Resume; }

        if (Input.MenuUpPressed) { _sel = (_sel + RootItems.Length - 1) % RootItems.Length; Blip(); }
        if (Input.MenuDownPressed) { _sel = (_sel + 1) % RootItems.Length; Blip(); }

        // Mouse hover takes over the highlight only when the cursor actually moves,
        // so it never fights keyboard/gamepad navigation.
        int hover = HitIndex(_rootRects, m);
        if (moved && hover >= 0 && hover != _sel) { _sel = hover; Blip(); }

        bool activate = Input.ConfirmPressed || (click && hover >= 0 && hover == _sel);
        if (!activate) return Result.None;

        Blip();
        switch (_sel)
        {
            case 0: return Result.Resume;
            case 1: return Result.RestartRound;
            case 2: _page = Page.Settings; return Result.None;
            case 3: _page = Page.ConfirmQuit; _confirmSel = 0; return Result.None;
            case 4: return Result.QuitToDesktop;
        }
        return Result.None;
    }

    Result UpdateSettings(Vector2 m, bool moved, bool click)
    {
        bool back = Input.PausePressed || Input.CancelPressed || Input.ConfirmPressed
                    || (click && _settingsBackRect.Contains((int)m.X, (int)m.Y));
        if (back) { Blip(); _page = Page.Root; _sel = 2; }
        return Result.None;
    }

    Result UpdateConfirm(Vector2 m, bool moved, bool click)
    {
        // ESC / B cancels the dialog and returns to the pause root — it does not quit.
        if (Input.PausePressed || Input.CancelPressed) { Blip(); _page = Page.Root; _sel = 3; return Result.None; }

        if (Input.MenuUpPressed || Input.MenuDownPressed
            || Input.LeftPressed || Input.RightPressed)
        {
            _confirmSel = 1 - _confirmSel;
            Blip();
        }

        int hover = HitIndex(_confirmRects, m);
        if (moved && hover >= 0 && hover != _confirmSel) { _confirmSel = hover; Blip(); }

        bool activate = Input.ConfirmPressed || (click && hover >= 0 && hover == _confirmSel);
        if (!activate) return Result.None;

        Blip();
        if (_confirmSel == 1) return Result.QuitToMenu;
        _page = Page.Root;
        _sel = 3;
        return Result.None;
    }

    static int HitIndex(List<Rectangle> rects, Vector2 m)
    {
        for (int i = 0; i < rects.Count; i++)
            if (rects[i].Contains((int)m.X, (int)m.Y)) return i;
        return -1;
    }

    static void Blip() => GameServices.Audio.Event("blip");

    // ---------------- drawing ----------------

    public void Draw(BitmapFont f, SpriteBatch sb)
    {
        // Dim the frozen frame behind the overlay.
        Ui.Rect(new Vector2(0, 0), new Vector2(Ui.W, Ui.H), new Color(5, 5, 12, 200));

        switch (_page)
        {
            case Page.Root: DrawRoot(f, sb); break;
            case Page.Settings: DrawSettings(f, sb); break;
            case Page.ConfirmQuit: DrawRoot(f, sb, dimmed: true); DrawConfirm(f, sb); break;
        }
    }

    void DrawRoot(BitmapFont f, SpriteBatch sb, bool dimmed = false)
    {
        float ease = MathHelper.Clamp(_t / 0.18f, 0f, 1f);
        ease = 1f - (1f - ease) * (1f - ease);
        float alpha = dimmed ? 0.35f : 1f;

        const int PanelW = 520, PanelH = 456;
        int px = (Ui.W - PanelW) / 2;
        int py = (int)MathHelper.Lerp(150f, 132f, ease);
        var panel = new Rectangle(px, py, PanelW, PanelH);

        Ui.Rect(panel, new Color(14, 16, 30, (int)(238 * alpha)));
        Ui.Frame(panel, 3, new Color(255, 210, 63) * alpha);
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(PanelW, 4), new Color(255, 210, 63) * alpha);

        string title = "PAUSED";
        var ts = f.Measure(title, 1.5f);
        f.DrawOutlined(sb, title, new Vector2(Ui.W / 2f, panel.Y + 30), Color.White * alpha, 1.5f, 0f, new Vector2(ts.X / 2f, 0));

        _rootRects.Clear();
        float y = panel.Y + 110;
        for (int i = 0; i < RootItems.Length; i++)
        {
            var row = new Rectangle(panel.X + 40, (int)y, PanelW - 80, 52);
            _rootRects.Add(row);

            bool sel = i == _sel && !dimmed;
            if (sel)
            {
                Ui.Rect(row, new Color(255, 210, 63, 46));
                Ui.Frame(row, 2, new Color(255, 210, 63, 210));
            }

            // Quit to desktop is the destructive option — tinted so it reads differently.
            var baseCol = i == 4 ? new Color(255, 150, 150) : new Color(190, 195, 220);
            var col = (sel ? new Color(255, 240, 180) : baseCol) * alpha;

            var size = f.Measure(RootItems[i], 0.8f);
            f.DrawOutlined(sb, RootItems[i], new Vector2(Ui.W / 2f, y + 12), col, 0.8f, 0f, new Vector2(size.X / 2f, 0));

            if (sel)
                f.Draw(sb, ">", new Vector2(row.X + 18, y + 12), new Color(255, 210, 63), 0.8f);

            y += 62;
        }

        if (!dimmed)
        {
            string hint = "MOUSE / WASD / D-PAD  ·  ENTER OR A: SELECT  ·  ESC OR B: RESUME";
            var hs = f.Measure(hint, 0.42f);
            f.Draw(sb, hint, new Vector2(Ui.W / 2f, panel.Bottom + 18), new Color(140, 148, 175), 0.42f, 0f, new Vector2(hs.X / 2f, 0), true);
        }
    }

    void DrawSettings(BitmapFont f, SpriteBatch sb)
    {
        var panel = new Rectangle(300, 180, 680, 360);
        Ui.Rect(panel, new Color(14, 16, 30, 245));
        Ui.Frame(panel, 3, new Color(63, 210, 255));

        string title = "SETTINGS";
        var ts = f.Measure(title, 1.3f);
        f.DrawOutlined(sb, title, new Vector2(Ui.W / 2f, panel.Y + 34), new Color(63, 210, 255), 1.3f, 0f, new Vector2(ts.X / 2f, 0));

        string msg = "COMING SOON";
        var ms = f.Measure(msg, 0.9f);
        f.Draw(sb, msg, new Vector2(Ui.W / 2f, panel.Y + 140), new Color(215, 220, 240), 0.9f, 0f, new Vector2(ms.X / 2f, 0), true);

        string sub = "GRAPHICS · AUDIO · CONTROLS · GAMEPLAY";
        var ss = f.Measure(sub, 0.5f);
        f.Draw(sb, sub, new Vector2(Ui.W / 2f, panel.Y + 190), new Color(130, 138, 165), 0.5f, 0f, new Vector2(ss.X / 2f, 0), true);

        _settingsBackRect = new Rectangle(Ui.W / 2 - 110, panel.Bottom - 84, 220, 52);
        var mp = Input.MouseUi(GameServices.Gfx.Viewport);
        bool hover = _settingsBackRect.Contains((int)mp.X, (int)mp.Y);
        Ui.Rect(_settingsBackRect, hover ? new Color(63, 210, 255, 60) : new Color(255, 255, 255, 22));
        Ui.Frame(_settingsBackRect, 2, hover ? new Color(63, 210, 255) : new Color(90, 96, 120));
        var bs = f.Measure("BACK", 0.8f);
        f.DrawOutlined(sb, "BACK", new Vector2(Ui.W / 2f, _settingsBackRect.Y + 12),
            hover ? new Color(200, 240, 255) : new Color(200, 205, 230), 0.8f, 0f, new Vector2(bs.X / 2f, 0));
    }

    void DrawConfirm(BitmapFont f, SpriteBatch sb)
    {
        Ui.Rect(new Vector2(0, 0), new Vector2(Ui.W, Ui.H), new Color(5, 5, 12, 150));

        var panel = new Rectangle(304, 232, 672, 256);
        Ui.Rect(panel, new Color(20, 14, 18, 248));
        Ui.Frame(panel, 3, new Color(255, 90, 90));

        string title = "QUIT TO MENU?";
        var ts = f.Measure(title, 1.05f);
        f.DrawOutlined(sb, title, new Vector2(Ui.W / 2f, panel.Y + 26), new Color(255, 120, 120), 1.05f, 0f, new Vector2(ts.X / 2f, 0));

        string l1 = "YOU WILL LOSE YOUR CURRENT RUN.";
        string l2 = "ARE YOU SURE?";
        var s1 = f.Measure(l1, 0.58f);
        var s2 = f.Measure(l2, 0.58f);
        f.Draw(sb, l1, new Vector2(Ui.W / 2f, panel.Y + 86), new Color(225, 228, 245), 0.58f, 0f, new Vector2(s1.X / 2f, 0), true);
        f.Draw(sb, l2, new Vector2(Ui.W / 2f, panel.Y + 116), new Color(225, 228, 245), 0.58f, 0f, new Vector2(s2.X / 2f, 0), true);

        _confirmRects.Clear();
        int bw = 268, bh = 56, gap = 24;
        int totalW = bw * 2 + gap;
        int bx = Ui.W / 2 - totalW / 2;
        int by = panel.Bottom - bh - 26;

        for (int i = 0; i < ConfirmItems.Length; i++)
        {
            var r = new Rectangle(bx + i * (bw + gap), by, bw, bh);
            _confirmRects.Add(r);

            bool sel = i == _confirmSel;
            // NO is green/safe, YES is red/destructive.
            var accent = i == 0 ? new Color(141, 255, 63) : new Color(255, 90, 90);
            Ui.Rect(r, sel ? accent * 0.22f : new Color(255, 255, 255, 18));
            Ui.Frame(r, 2, sel ? accent : new Color(90, 96, 120));

            var size = f.Measure(ConfirmItems[i], 0.62f);
            f.DrawOutlined(sb, ConfirmItems[i], new Vector2(r.X + r.Width / 2f, r.Y + 16),
                sel ? Color.White : new Color(180, 186, 210), 0.62f, 0f, new Vector2(size.X / 2f, 0));
        }

        string hint = "ESC OR B: CANCEL";
        var hs = f.Measure(hint, 0.42f);
        f.Draw(sb, hint, new Vector2(Ui.W / 2f, panel.Bottom + 16), new Color(140, 148, 175), 0.42f, 0f, new Vector2(hs.X / 2f, 0), true);
    }
}
