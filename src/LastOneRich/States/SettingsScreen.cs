using LastOneRich.Core;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace LastOneRich.States;

/// <summary>
/// The Settings screen (Priority 3) — ONE implementation, hosted by both the main menu and the
/// pause menu's SETTINGS page, so the two entry points can never drift apart.
///
/// Layout: four tabs (GRAPHICS / AUDIO / CONTROLS / GAMEPLAY) above a list of rows. Each row is a
/// toggle, a picker or a slider, and every one of them writes straight into Keybinds.Settings
/// (the existing ControlsDTO — no shadow copy, no new DTO), then persists the whole merged state
/// to saves/settings.json.
///
/// What "apply immediately" means here:
///   · audio faders   — AudioBank reads its static volumes on every play / PlayMusic call
///   · FOV            — GameplayState pushes cam.FovDeg every Draw
///   · fog            — GeometryRenderer.FogOn, read at the top of BeginFrame
///   · VSync          — the GraphicsDeviceManager property can flip without a device reset
///   · camera distance/height, sensitivity, invert-Y — already read per frame from Keybinds
///   · resolution / fullscreen — QUEUED, never sprung on the player mid-round. The screen shows a
///     warning and an APPLY button; APPLY sets SettingsStore.PendingGraphics, which LorGame acts on
///     at the one point in its Update that is outside any begin/end draw pair. Quitting to the menu
///     or restarting the game applies it too, because the file is already written.
/// </summary>
public sealed class SettingsScreen
{
    /// <summary>What the host should do after an Update.</summary>
    public enum Result { None, Back }

    const int PanelW = 840, PanelH = 468, PanelX = 220, PanelY = 126;
    static readonly string[] TabNames = { "GRAPHICS", "AUDIO", "CONTROLS", "GAMEPLAY" };
    static readonly string[] DifficultyNames = { "CASUAL", "STANDARD", "HARDCORE" };
    static readonly (int w, int h)[] Resolutions =
    {
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160),
    };

    // Slider geometry is shared by Draw and the mouse test so the two can never disagree.
    // Track stops short of the value text on purpose: 700..940 with the read-out ending at 1020.
    const int TrackX = 480, TrackW = 240;
    static int TrackX0 => PanelX + TrackX;
    static int ValueRight => PanelX + PanelW - 40;

    int _tab;
    int _sel;
    float _t;
    bool _dirty;
    bool _applyShown;

    readonly Row[] _rows;
    readonly List<Row>[] _byTab;

    readonly List<Rectangle> _tabRects = new();
    readonly List<Rectangle> _rowRects = new();
    Rectangle _resetRect, _applyRect;

    /// <summary>True while resolution / fullscreen edits are waiting to be pushed to the device.</summary>
    public bool PendingRestartNotice { get; private set; }

    // ---------------- row model ----------------

    sealed class Row
    {
        public enum KindT { Toggle, Slider, Picker }

        public string Label;
        public KindT Kind;
        public int Tab;
        public int Min, Max, Step = 1;
        public string[] Options;
        public string Suffix = "";
        public Func<int> Get;
        public Action<int> Set;

        public static Row Toggle(string label, int tab, Func<bool> get, Action<bool> set) => new()
        {
            Label = label,
            Tab = tab,
            Kind = KindT.Toggle,
            Get = () => get() ? 1 : 0,
            Set = v => set(v != 0),
        };

        public static Row Slider(string label, int tab, int min, int max, int step, Func<int> get, Action<int> set, string suffix = "") => new()
        {
            Label = label,
            Tab = tab,
            Kind = KindT.Slider,
            Min = min,
            Max = max,
            Step = step,
            Suffix = suffix,
            Get = get,
            Set = set,
        };

        public static Row Picker(string label, int tab, string[] options, Func<int> get, Action<int> set) => new()
        {
            Label = label,
            Tab = tab,
            Kind = KindT.Picker,
            Options = options,
            Get = get,
            Set = set,
        };

        public string ValueText => Kind switch
        {
            KindT.Toggle => Get() != 0 ? "ON" : "OFF",
            KindT.Picker => Options[Math.Clamp(Get(), 0, Options.Length - 1)],
            _ => Get().ToString() + Suffix,
        };
    }

    // ---------------- construction ----------------

    public SettingsScreen()
    {
        // Rows close over Keybinds.Settings, which is never null (defaults until EnsureLoaded has
        // read controls.json + settings.json, and the read path can only ever replace it wholesale).
        // So a screen built before LoadContent finishes still shows legal values.
        var s = Keybinds.Settings;
        _byTab = new List<Row>[TabNames.Length];
        for (int i = 0; i < _byTab.Length; i++) _byTab[i] = new List<Row>();

        _rows = new[]
        {
            // ---- GRAPHICS ----
            Row.Picker("RESOLUTION", 0, ResolutionLabels(),
                () => ResolutionIndex(s.ResolutionWidth, s.ResolutionHeight),
                v =>
                {
                    var r = Resolutions[Math.Clamp(v, 0, Resolutions.Length - 1)];
                    s.ResolutionWidth = r.w;
                    s.ResolutionHeight = r.h;
                }),
            Row.Toggle("FULLSCREEN", 0, () => s.Fullscreen, v => s.Fullscreen = v),
            Row.Toggle("VSYNC", 0, () => s.VSync, v => s.VSync = v),
            Row.Slider("FIELD OF VIEW", 0, 50, 100, 1,
                () => (int)Math.Round(s.FovDeg), v => s.FovDeg = v, "°"),
            Row.Toggle("FOG", 0, () => s.FogEnabled, v => s.FogEnabled = v),

            // ---- AUDIO ----
            Row.Slider("MASTER VOLUME", 1, 0, 100, 5, () => s.MasterVolume, v => s.MasterVolume = v, "%"),
            Row.Slider("MUSIC VOLUME", 1, 0, 100, 5, () => s.MusicVolume, v => s.MusicVolume = v, "%"),
            Row.Slider("SFX VOLUME", 1, 0, 100, 5, () => s.SfxVolume, v => s.SfxVolume = v, "%"),

            // ---- CONTROLS ----
            // Sensitivity is stored as the real rad/px number; the slider is the friendly
            // percent-of-default view of it (100% == 0.003). Keybinds still clamps on read.
            Row.Slider("MOUSE SENSITIVITY", 2, 10, 300, 5,
                () => Math.Clamp((int)Math.Round(s.MouseSensitivity / 0.003 * 100.0), 10, 300),
                v => s.MouseSensitivity = Math.Round(v * 0.003 / 100.0, 6)),
            Row.Toggle("INVERT Y", 2, () => s.InvertY, v => s.InvertY = v),
            Row.Slider("CAMERA DISTANCE", 2, 20, 300, 10,
                () => Math.Clamp((int)Math.Round(s.CameraDistance * 10.0), 20, 300),
                v => s.CameraDistance = v / 10.0),

            // ---- GAMEPLAY ----
            Row.Picker("DIFFICULTY", 3, DifficultyNames,
                () => DifficultyIndex(s.Difficulty),
                v => s.Difficulty = DifficultyNames[Math.Clamp(v, 0, DifficultyNames.Length - 1)]),
        };

        // Grouped once, here: Update and Draw run every frame while paused and must not allocate.
        foreach (var r in _rows) _byTab[r.Tab].Add(r);
    }

    // ---------------- open / close ----------------

    /// <summary>Call on every entry (from either host) — resets focus and the pending banner.</summary>
    public void Open()
    {
        _t = 0f;
        _sel = 0;
        _tab = Math.Clamp(_tab, 0, TabNames.Length - 1);
        _dirty = false;
        PendingRestartNotice = false;
        _rowRects.Clear();
        _tabRects.Clear();
    }

    /// <summary>Host calls this on the way out; writes the file only if something actually moved.</summary>
    public void Close()
    {
        if (_dirty) Persist();
        _dirty = false;
    }

    // ---------------- update ----------------

    public Result Update(float dt, Viewport vp)
    {
        _t += dt;
        var m = Input.MouseUi(vp);
        bool moved = Input.MouseMoved;
        bool click = Input.MouseLeftPressed;

        // BACK: ESC or gamepad B. There is deliberately no "OK" button — every edit is written the
        // moment it happens, so leaving the screen *is* committing.
        if (Input.CancelPressed || Input.PausePressed) { Blip(); return Result.Back; }

        // ---- tabs: Q/E edges or the shoulder buttons ----
        // E is NOT used as this screen's confirm key even though controls.json binds it to Confirm:
        // that would flip a tab on every "select" press. Confirm here is Enter / gamepad A only.
        int tabStep = 0;
        if (Input.Pressed(Keys.Q)) tabStep -= 1;
        if (Input.Pressed(Keys.E)) tabStep += 1;
        if (Input.Num1Pressed) tabStep -= 1;      // LeftShoulder (also D1)
        if (Input.Num2Pressed) tabStep += 1;      // RightShoulder (also D2)

        int tabHover = HitIndex(_tabRects, m);
        if (click && tabHover >= 0 && tabHover != _tab) { _tab = tabHover; tabStep = 0; _sel = 0; Blip(); }
        if (tabStep != 0)
        {
            _tab = ((_tab + tabStep) % TabNames.Length + TabNames.Length) % TabNames.Length;
            _sel = 0;
            Blip();
        }

        var rows = RowsForTab();
        if (rows.Count == 0) return Result.None;

        // ---- selection: keys first, then hover (only when the cursor actually moved) ----
        if (Input.MenuUpPressed) { _sel = (_sel + rows.Count - 1) % rows.Count; Blip(); }
        if (Input.MenuDownPressed) { _sel = (_sel + 1) % rows.Count; Blip(); }

        int hover = HitIndex(_rowRects, m);
        if (moved && hover >= 0) _sel = hover;

        _sel = Math.Clamp(_sel, 0, rows.Count - 1);
        var cur = rows[_sel];          // resolved AFTER every selection change — never stale

        // ---- mouse activation ----
        // Sliders are direct manipulation: a click anywhere on the track jumps to that value and a
        // held button drags, because pointing at a position is unambiguous. Toggles and pickers keep
        // PauseMenu's two-stage rule (a click on an unselected row selects it; a click on the
        // selected row activates) so nothing ever fires by accident.
        if (click && hover >= 0)
        {
            var hit = rows[hover];
            // Only a click ON the track edits a slider — otherwise clicking the row's LABEL would
            // slam the value to its minimum, since ValueFromX clamps anything left of the track.
            bool onTrack = m.X >= TrackX0 - 10 && m.X <= TrackX0 + TrackW + 10;
            if (hit.Kind == Row.KindT.Slider && onTrack)
                SetRow(hit, ValueFromX(m.X, hit));
            else if (hover == _sel)
            {
                if (hit.Kind == Row.KindT.Toggle) SetRow(hit, hit.Get() == 0 ? 1 : 0);
                else SetRow(hit, Wrap(hit.Get() + 1, hit));
            }
        }
        else if (cur.Kind == Row.KindT.Slider && Input.MouseLeftHeld && hover == _sel)
        {
            SetRow(cur, ValueFromX(m.X, cur));     // drag
        }

        // ---- keyboard activation ----
        if (Input.Pressed(Keys.Enter) || Input.GpPressed(Buttons.A))
        {
            switch (cur.Kind)
            {
                case Row.KindT.Toggle: SetRow(cur, cur.Get() == 0 ? 1 : 0); break;
                case Row.KindT.Picker: SetRow(cur, Wrap(cur.Get() + 1, cur)); break;
                default: SetRow(cur, Math.Clamp(cur.Get() + cur.Step, cur.Min, cur.Max)); break;
            }
        }

        // ---- value editing ----
        // A/D and the d-pad nudge by one detent, Shift by four, PageUp/PageDown by ten.
        // (Up/Down are taken by row navigation, exactly like every other menu in this game.)
        int stride = Input.Held(Keys.LeftShift) || Input.Held(Keys.RightShift) ? 4 : 1;
        int delta = 0;
        if (Input.LeftPressed) delta -= stride;
        if (Input.RightPressed) delta += stride;
        if (Input.Pressed(Keys.PageUp)) delta += 10;
        if (Input.Pressed(Keys.PageDown)) delta -= 10;
        if (Input.MouseRightPressed && hover == _sel) delta -= stride;
        if (delta != 0) Adjust(cur, delta);

        // ---- buttons ----
        if (click && _applyShown && _applyRect.Contains((int)m.X, (int)m.Y))
        {
            SettingsStore.MarkPending();       // LorGame pushes it to the device at its safe point
            PendingRestartNotice = false;      // queued = handled; the banner was the call to act
            Blip();
        }
        if (click && _resetRect.Contains((int)m.X, (int)m.Y)) ResetAll();
        else if (Input.Pressed(Keys.R)) ResetAll();

        return Result.None;
    }

    static readonly List<Row> NoRows = new();

    /// <summary>Never allocates: the empty case exists only to keep a pre-ctor call from throwing.</summary>
    List<Row> RowsForTab() => _byTab.Length == 0
        ? NoRows
        : _byTab[Math.Clamp(_tab, 0, _byTab.Length - 1)];

    void Adjust(Row r, int delta)
    {
        if (delta == 0) return;
        switch (r.Kind)
        {
            // A toggle has no magnitude — D turns it OFF, A turns it ON. (Without this branch a
            // toggle would be "incremented" against Min==Max==0 and could never be switched on.)
            case Row.KindT.Toggle: SetRow(r, delta > 0 ? 1 : 0); break;
            case Row.KindT.Picker: SetRow(r, Wrap(r.Get() + Math.Sign(delta), r)); break;
            default: SetRow(r, Math.Clamp(r.Get() + delta * r.Step, r.Min, r.Max)); break;
        }
    }

    static int Wrap(int v, Row r) =>
        r.Options == null || r.Options.Length == 0
            ? 0
            : (v % r.Options.Length + r.Options.Length) % r.Options.Length;

    /// <summary>Single choke point for every edit: write, sound, apply, persist.</summary>
    void SetRow(Row r, int value)
    {
        if (r.Get() == value) return;
        r.Set(value);
        _dirty = true;

        // Feedback policy: the house menu blip for everything, dropping to the softer tick when a
        // toggle turns OFF. Both are SFX, so the Audio tab's SFX fader is previewable by ear on
        // every edit — and muting it makes the UI go quiet under the cursor, which is the point.
        GameServices.Audio.Event(r.Kind == Row.KindT.Toggle && value == 0 ? "move" : "blip");

        AudioBank.ApplyVolumes();              // music bed follows the faders without a re-init

        if (r.Tab == 0 && (r.Label == "RESOLUTION" || r.Label == "FULLSCREEN"))
        {
            // Queued, NOT pushed into the device silently: an unannounced resolution flip under a
            // live player is the kind of thing that loses a round. APPLY (or the next launch) does it.
            PendingRestartNotice = true;
        }

        Persist();
    }

    /// <summary>
    /// Write the merged state. Re-resolving Keybinds first means a remap or a re-clamped value is
    /// live this frame, and it also refreshes the derived difficulty multipliers on the next round.
    /// </summary>
    void Persist()
    {
        Keybinds.Apply(Keybinds.Settings);
        SettingsStore.Persist(Keybinds.Settings);
    }

    /// <summary>
    /// Restores the code defaults for exactly the knobs this screen owns — and nothing else.
    /// Keybinds, camera height and the pitch cone are deliberately NOT reset: they are hand-edited
    /// in controls.json, never shown here, and wiping invisible settings behind a visible button
    /// would be the worst kind of surprise.
    /// </summary>
    void ResetAll()
    {
        var d = new ControlsDTO();      // the code defaults — what controls.json itself starts from
        var s = Keybinds.Settings;
        s.ResolutionWidth = d.ResolutionWidth; s.ResolutionHeight = d.ResolutionHeight;
        s.Fullscreen = d.Fullscreen; s.VSync = d.VSync;
        s.FovDeg = d.FovDeg; s.FogEnabled = d.FogEnabled;
        s.MasterVolume = d.MasterVolume; s.MusicVolume = d.MusicVolume; s.SfxVolume = d.SfxVolume;
        s.MouseSensitivity = d.MouseSensitivity; s.InvertY = d.InvertY;
        s.CameraDistance = d.CameraDistance;
        s.Difficulty = d.Difficulty;
        _dirty = true;
        PendingRestartNotice = true;
        GameServices.Audio.Event("cash");
        AudioBank.ApplyVolumes();
        Persist();
    }

    // ---------------- helpers ----------------

    static string[] ResolutionLabels()
    {
        var a = new string[Resolutions.Length];
        for (int i = 0; i < Resolutions.Length; i++) a[i] = $"{Resolutions[i].w}×{Resolutions[i].h}";
        return a;
    }

    /// <summary>Nearest listed mode to what is applied now, so an off-list hand-edit still shows a sane pick.</summary>
    static int ResolutionIndex(int w, int h)
    {
        int best = 0;
        long bestCost = long.MaxValue;
        for (int i = 0; i < Resolutions.Length; i++)
        {
            long dw = Resolutions[i].w - w, dh = Resolutions[i].h - h;
            long cost = dw * dw + dh * dh;
            if (cost < bestCost) { bestCost = cost; best = i; }
        }
        return best;
    }

    static int DifficultyIndex(string s)
    {
        for (int i = 0; i < DifficultyNames.Length; i++)
            if (string.Equals(DifficultyNames[i], (s ?? "").Trim(), StringComparison.OrdinalIgnoreCase))
                return i;
        return 1;   // STANDARD
    }

    static int HitIndex(List<Rectangle> rects, Vector2 m)
    {
        for (int i = 0; i < rects.Count; i++)
            if (rects[i].Contains((int)m.X, (int)m.Y)) return i;
        return -1;
    }

    static void Blip() => GameServices.Audio.Event("blip");

    static int ValueFromX(float mouseX, Row r)
    {
        float u = MathHelper.Clamp((mouseX - TrackX0) / (float)TrackW, 0f, 1f);
        int raw = (int)Math.Round(r.Min + u * (r.Max - r.Min));
        // snap onto the step grid so dragging can never land between detents
        int snapped = r.Min + (int)Math.Round((raw - r.Min) / (double)r.Step) * r.Step;
        return Math.Clamp(snapped, r.Min, r.Max);
    }

    // ---------------- drawing ----------------

    public void Draw(BitmapFont f, SpriteBatch sb)
    {
        Ui.Rect(new Vector2(0, 0), new Vector2(Ui.W, Ui.H), new Color(5, 5, 12, 210));

        float ease = MathHelper.Clamp(_t / 0.16f, 0f, 1f);
        ease = 1f - (1f - ease) * (1f - ease);
        int py = (int)MathHelper.Lerp(140f, PanelY, ease);
        var panel = new Rectangle(PanelX, py, PanelW, PanelH);

        Ui.Rect(panel, new Color(14, 16, 30, 246));
        Ui.Frame(panel, 3, new Color(63, 210, 255));
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(PanelW, 4), new Color(63, 210, 255));

        string title = "SETTINGS";
        var ts = f.Measure(title, 1.25f);
        f.DrawOutlined(sb, title, new Vector2(Ui.W / 2f, panel.Y + 22), Color.White, 1.25f, 0f, new Vector2(ts.X / 2f, 0));

        // ---- tabs ----
        _tabRects.Clear();
        const int tw = 196, gap = 12;
        int total = TabNames.Length * tw + (TabNames.Length - 1) * gap;
        int tx = Ui.W / 2 - total / 2;
        int ty = panel.Y + 72;
        for (int i = 0; i < TabNames.Length; i++)
        {
            var r = new Rectangle(tx + i * (tw + gap), ty, tw, 44);
            _tabRects.Add(r);
            bool on = i == _tab;
            Ui.Rect(r, on ? new Color(63, 210, 255, 46) : new Color(255, 255, 255, 14));
            Ui.Frame(r, 2, on ? new Color(63, 210, 255) : new Color(70, 76, 100));
            var s = f.Measure(TabNames[i], 0.6f);
            f.Draw(sb, TabNames[i], new Vector2(r.X + r.Width / 2f, r.Y + 12),
                on ? Color.White : new Color(160, 168, 195), 0.6f, 0f, new Vector2(s.X / 2f, 0), true);
        }

        // ---- rows ----
        _rowRects.Clear();
        var rows = RowsForTab();
        // Rows run 134..506 inside the panel, leaving 520 for the banner and 548 for the buttons —
        // the graphics tab is the tallest (5 rows) and must not touch either.
        float y = panel.Y + 134;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = new Rectangle(panel.X + 28, (int)y, PanelW - 56, 46);
            _rowRects.Add(row);
            var r = rows[i];
            bool sel = i == _sel;
            if (sel)
            {
                Ui.Rect(row, new Color(255, 210, 63, 30));
                Ui.Frame(row, 1, new Color(255, 210, 63, 150));
            }

            f.Draw(sb, r.Label, new Vector2(row.X + 16, y + 13),
                sel ? new Color(255, 240, 180) : new Color(190, 195, 220), 0.56f);

            switch (r.Kind)
            {
                case Row.KindT.Slider: DrawSlider(f, sb, r, y + 26, sel); break;
                case Row.KindT.Picker: DrawPicker(f, sb, r, y, sel); break;
                default: DrawToggle(f, sb, r, y, sel); break;
            }
            y += 50;
        }

        // ---- pending-graphics banner + buttons ----
        if (PendingRestartNotice)
        {
            string warn = "RESOLUTION / FULLSCREEN CHANGED — APPLY NOW, OR IT APPLIES AT NEXT LAUNCH.";
            var ws = f.Measure(warn, 0.46f);
            f.Draw(sb, warn, new Vector2(Ui.W / 2f, panel.Bottom - 74), new Color(255, 200, 120), 0.46f, 0f, new Vector2(ws.X / 2f, 0), true);
        }

        _applyShown = PendingRestartNotice;
        _applyRect = new Rectangle(panel.X + 28, panel.Bottom - 46, 214, 34);
        DrawButton(f, sb, _applyRect, "APPLY & RESTART", new Color(255, 210, 63), _applyShown);

        _resetRect = new Rectangle(ValueRight - 190, panel.Bottom - 46, 190, 34);
        DrawButton(f, sb, _resetRect, "RESET DEFAULTS", new Color(255, 150, 150), visible: true);

        string hint = "W/S: ROW   A/D: CHANGE   Q/E: TAB   ENTER: APPLY   R: RESET   SAVED AUTOMATICALLY";
        var hs = f.Measure(hint, 0.42f);
        f.Draw(sb, hint, new Vector2(Ui.W / 2f, panel.Bottom + 16), new Color(140, 148, 175), 0.42f, 0f, new Vector2(hs.X / 2f, 0), true);

        // Status line under the hint: safe mode first (it explains why nothing sticks), then a
        // read failure. Both are informational only — neither can block the player.
        if (SettingsStore.IgnoreOverrides)
        {
            string note = "SAFE MODE — CHANGES APPLY FOR THIS SESSION ONLY AND ARE NOT SAVED";
            var ns = f.Measure(note, 0.42f);
            f.Draw(sb, note, new Vector2(Ui.W / 2f, panel.Bottom + 38), new Color(255, 200, 120), 0.42f, 0f, new Vector2(ns.X / 2f, 0), true);
        }
        else if (SettingsStore.LoadFailed)
        {
            string bad = "SETTINGS FILE WAS UNREADABLE — STARTED FROM DEFAULTS";
            var bs = f.Measure(bad, 0.42f);
            f.Draw(sb, bad, new Vector2(Ui.W / 2f, panel.Bottom + 38), new Color(255, 150, 150), 0.42f, 0f, new Vector2(bs.X / 2f, 0), true);
        }
    }

    void DrawButton(BitmapFont f, SpriteBatch sb, Rectangle r, string label, Color accent, bool visible)
    {
        if (!visible) return;
        var mp = Input.MouseUi(GameServices.Gfx.Viewport);
        bool hover = r.Contains((int)mp.X, (int)mp.Y);
        Ui.Rect(r, hover ? accent * 0.22f : new Color(255, 255, 255, 16));
        Ui.Frame(r, 2, hover ? accent : new Color(84, 90, 114));
        var s = f.Measure(label, 0.5f);
        f.Draw(sb, label, new Vector2(r.X + r.Width / 2f, r.Y + 9),
            hover ? Color.White : new Color(205, 210, 232), 0.5f, 0f, new Vector2(s.X / 2f, 0));
    }

    static void DrawSlider(BitmapFont f, SpriteBatch sb, Row r, float midY, bool sel)
    {
        int x0 = TrackX0, w = TrackW;
        int val = Math.Clamp(r.Get(), r.Min, r.Max);
        float u = r.Max > r.Min ? (val - r.Min) / (float)(r.Max - r.Min) : 0f;

        var track = new Rectangle(x0, (int)midY - 6, w, 12);
        Ui.Rect(track, new Color(255, 255, 255, 26));
        Ui.Frame(track, 1, new Color(70, 76, 100));
        if (w * u > 1)
            Ui.Rect(new Rectangle(x0, (int)midY - 6, (int)(w * u), 12), sel ? new Color(255, 210, 63) : new Color(63, 210, 255));

        // detent ticks, so a slider reads as discrete rather than analogue
        int steps = Math.Max(1, (r.Max - r.Min) / r.Step);
        if (steps <= 24)
            for (int i = 0; i <= steps; i++)
                Ui.Rect(new Vector2(x0 + (w * i) / steps - 1, midY + 8), new Vector2(2, 4), new Color(255, 255, 255, 40));

        int hx = x0 + (int)(w * u);
        Ui.Rect(new Vector2(hx - 5, midY - 10), new Vector2(10, 20), Color.White);

        var vs = f.Measure(r.ValueText, 0.56f);
        f.Draw(sb, r.ValueText, new Vector2(ValueRight - vs.X, midY - 9),
            sel ? new Color(255, 240, 180) : Color.White, 0.56f);
    }

    static void DrawPicker(BitmapFont f, SpriteBatch sb, Row r, float y, bool sel)
    {
        string val = r.ValueText;
        var vs = f.Measure(val, 0.56f);
        int vw = (int)vs.X + 8;
        int vx = ValueRight - vw - 44;

        Ui.Rect(new Rectangle(vx - 40, (int)y + 7, 30, 32), new Color(255, 255, 255, 18));
        Ui.Rect(new Rectangle(vx + vw + 10, (int)y + 7, 30, 32), new Color(255, 255, 255, 18));
        f.Draw(sb, "<", new Vector2(vx - 33, y + 13), sel ? new Color(255, 240, 180) : new Color(170, 176, 200), 0.6f);
        f.Draw(sb, ">", new Vector2(vx + vw + 17, y + 13), sel ? new Color(255, 240, 180) : new Color(170, 176, 200), 0.6f);

        Ui.Rect(new Rectangle(vx, (int)y + 7, vw, 32), new Color(16, 18, 32, 220));
        Ui.Frame(new Rectangle(vx, (int)y + 7, vw, 32), 1, sel ? new Color(255, 210, 63) : new Color(70, 76, 100));
        f.Draw(sb, val, new Vector2(vx + 4, y + 14), sel ? new Color(255, 240, 180) : Color.White, 0.56f);
    }

    static void DrawToggle(BitmapFont f, SpriteBatch sb, Row r, float y, bool sel)
    {
        bool on = r.Get() != 0;
        int x = ValueRight - 92;
        var box = new Rectangle(x, (int)y + 9, 74, 28);
        Ui.Rect(box, on ? new Color(141, 255, 63, 56) : new Color(255, 255, 255, 16));
        Ui.Frame(box, 2, on ? new Color(141, 255, 63) : new Color(90, 96, 120));

        // The knob slides to the lit side, so the state is readable at a glance even mid-fade.
        var label = on ? "ON" : "OFF";
        var s = f.Measure(label, 0.5f);
        f.Draw(sb, label, new Vector2(box.X - 34 - s.X, y + 15),
            on ? new Color(141, 255, 63) : new Color(150, 156, 180), 0.5f);

        int knob = on ? box.Right - 24 : box.X + 4;
        Ui.Rect(new Vector2(knob, box.Y + 4), new Vector2(20, 20),
            on ? new Color(141, 255, 63) : new Color(150, 156, 180));
    }
}
