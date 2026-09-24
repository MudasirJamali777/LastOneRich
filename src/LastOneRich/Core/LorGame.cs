using LastOneRich.States;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

public sealed class LorGame : Game
{
    readonly GraphicsDeviceManager _gfx;
    StateMachine _states;
    int _frame;
    float _fps;
    readonly LaunchArgs _launch;
    readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    double _lastUpdate;

    /// <summary>0..1 factor for rendering between the previous and current physics step.</summary>
    public static float InterpAlpha;

    public LorGame(LaunchArgs launch)
    {
        _gfx = new GraphicsDeviceManager(this);
        _launch = launch;

        // Boot safety, decided before anything reads the display settings:
        //   --safe            → opt out by hand (debug builds only)
        //   stale boot probe  → the previous launch applied a mode and never reached a frame,
        //                       so ignore the overrides ONCE (settings.json is left intact and
        //                       retried next time, which is when the player can fix it in-game)
        bool rescue = false;
#if DEBUG
        rescue = launch?.SafeMode == true;
#endif
        if (!rescue && SettingsStore.BootProbeStale())
        {
            rescue = true;
            SettingsStore.ClearStaleProbe();
        }
        SettingsStore.IgnoreOverrides = rescue;

        // File IO + reflection only, so it is legal this early; Initialize reuses the cache.
        Keybinds.EnsureLoaded();
        if (!rescue && SettingsStore.GraphicsDifferFromSafeDefaults())
            SettingsStore.ArmBootProbe();

        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "LAST ONE RICH! — Volt Dome (Season 1)";

        // Rendering baseline (rendering task): MSAA on. Reach profile = maximum
        // compatibility (old GPUs / software GL / VMs); slice vertex counts are tiny.
        _gfx.GraphicsProfile = GraphicsProfile.Reach;
        _gfx.PreferMultiSampling = true;

        // Fixed 60 Hz simulation + vsync: physics runs at the exact rate the render
        // loop interpolates over (jitter fix 3A/3B). dt is always 1/60 in Update.
        // VSync here is only the default — the real value is taken from settings below,
        // because IsFixedTimeStep = true keeps simulating at 60 Hz regardless.
        IsFixedTimeStep = true;
        TargetElapsedTime = System.TimeSpan.FromSeconds(1.0 / 60.0);
    }

    protected override void Initialize()
    {
        // Player settings win over the 1280x720 default BEFORE the device is created, so the
        // window opens at the saved size/state (EnsureLoaded reads saves/settings.json).
        Keybinds.EnsureLoaded();
        _gfx.PreferredBackBufferWidth = Keybinds.ResolutionWidth;
        _gfx.PreferredBackBufferHeight = Keybinds.ResolutionHeight;
        _gfx.IsFullScreen = Keybinds.Fullscreen;
        _gfx.SynchronizeWithVerticalRetrace = Keybinds.VSync;

        // base.Initialize() creates the device from the manager's pending parameters, so the
        // saved resolution is already baked in by the time LoadContent runs.
        base.Initialize();
    }

    protected override void LoadContent()
    {
        var sb = new SpriteBatch(GraphicsDevice);
        Input.AttachHost(this); // enables mouse capture for gameplay mouse-look
        GameServices.Init(GraphicsDevice, sb, _launch);
        AudioBank.ApplyVolumes();                 // settings ▸ Audio takes effect before the first sound
        _gfx.SynchronizeWithVerticalRetrace = Keybinds.VSync; // live VSync (no device reset needed)
#if DEBUG
        if (_launch?.Overlay == true) GameServices.DebugOverlay = true;
#endif
        _states = new StateMachine();
        _states.Replace(new BootState(_states));
    }

    protected override void Update(GameTime gameTime)
    {
        Input.Update();
#if DEBUG
        if (Input.PressedAction("DebugOverlay")) GameServices.DebugOverlay = !GameServices.DebugOverlay;
#endif

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        dt = MathHelper.Min(dt, 1f / 20f); // clamp hitches (alt-tab safety)
        _states.Update(dt);
        Achievements.UpdateToasts(dt);   // Priority 5: unlock toasts tick in every state
        GameServices.Audio?.Tick(dt);    // Priority 7: advances any PlayMusicFadeIn in progress

        // Safe point for resolution / fullscreen: after the state's Update, long before BeginDraw,
        // so resetting the device can never land between a begin/end pair.
        SettingsStore.ApplyPendingGraphics(_gfx);

        _lastUpdate = _clock.Elapsed.TotalSeconds;
        base.Update(gameTime);
        if (_states.IsEmpty) Exit();
    }

    protected override void Draw(GameTime gameTime)
    {
        _frame++;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (dt > 0.0001f) _fps = _fps <= 0 ? 1f / dt : MathHelper.Lerp(_fps, 1f / dt, 0.05f);

        // Render interpolation factor: how far we are into the current fixed step.
        double step = TargetElapsedTime.TotalSeconds;
        InterpAlpha = (float)System.Math.Clamp((_clock.Elapsed.TotalSeconds - _lastUpdate) / step, 0.0, 1.0);

        // Settings ▸ Graphics ▸ FOG, applied once for every state so the menu backdrop and the
        // cutscenes respect the same switch the player just flipped (default true = as before).
        GeometryRenderer.FogOn = Keybinds.FogEnabled;

        _states.Draw();
#if DEBUG
        if (GameServices.DebugOverlay) DrawDebugOverlay();
#endif
        DrawAchievementToasts();   // Priority 5: ride above whatever the state drew
        base.Draw(gameTime);

        // A frame survived a present, so the saved display mode is good: disarm the boot probe
        // (no-op on every later frame, and no disk traffic at all when nothing was armed).
        SettingsStore.ClearBootProbe();

        if (_launch?.ShotFrame >= 0 && _frame >= _launch.ShotFrame && !string.IsNullOrEmpty(_launch.ShotPath))
        {
            try
            {
                var pp = GraphicsDevice.PresentationParameters;
                var data = new Color[pp.BackBufferWidth * pp.BackBufferHeight];
                GraphicsDevice.GetBackBufferData(data);
                using var tex = new Texture2D(GraphicsDevice, pp.BackBufferWidth, pp.BackBufferHeight);
                tex.SetData(data);
                using var fs = File.Create(_launch.ShotPath);
                tex.SaveAsPng(fs, pp.BackBufferWidth, pp.BackBufferHeight);
            }
            catch { /* screenshot is best-effort */ }
            Exit();
        }
    }

    /// <summary>Achievement unlock toasts — one extra UI pass, drawn above every state.</summary>
    void DrawAchievementToasts()
    {
        if (!Achievements.HasToasts) return;
        var vp = GraphicsDevice.Viewport;
        Ui.Begin(vp);
        Achievements.DrawToasts(GameServices.Font, GameServices.Sb);
        Ui.End();
    }

    /// <summary>Best-effort career flush on exit. Store is atomic, so quitting mid-frame
    /// can never tear save.json (Priority 4).</summary>
    protected override void OnExiting(object sender, System.EventArgs args)
    {
        if (GameServices.Save != null) SaveSystem.Store(GameServices.Save);
        base.OnExiting(sender, args);
    }

#if DEBUG
    /// <summary>F3 overlay: FPS + move-vector indicators (acceptance: A/D/W/S must match these).</summary>
    void DrawDebugOverlay()
    {
        var vp = GraphicsDevice.Viewport;
        var f = GameServices.Font;
        var sb = GameServices.Sb;

        Ui.Begin(vp);
        var panel = new Rectangle(16, 96, 300, 170);
        Ui.Rect(new Vector2(panel.X, panel.Y), new Vector2(panel.Width, panel.Height), new Color(8, 8, 18, 205));
        Ui.Frame(panel, 2, new Color(63, 210, 255));

        f.Draw(sb, "F3 DEBUG", new Vector2(panel.X + 14, panel.Y + 10), new Color(63, 210, 255), 0.55f);
        f.Draw(sb, $"FPS {_fps:0}", new Vector2(panel.X + 14, panel.Y + 34), _fps > 55 ? new Color(141, 255, 63) : new Color(255, 210, 63), 0.55f);

        var raw = GameplayState.DebugMoveRaw;
        var xz = GameplayState.DebugMoveXZ;
        f.Draw(sb, $"RAW  X {raw.X,5:0.00}  Y {raw.Y,5:0.00}", new Vector2(panel.X + 14, panel.Y + 58), new Color(200, 208, 235), 0.55f);
        f.Draw(sb, $"WORLD ({xz.X,5:0.00},{xz.Y,5:0.00})", new Vector2(panel.X + 14, panel.Y + 82), new Color(200, 208, 235), 0.55f);

        // WASD indicators: lit exactly when that screen direction is requested.
        // Press D -> D lights up and the runner moves screen-right. Always.
        void KeyLabel(string label, bool lit, float x, float y)
        {
            var col = lit ? new Color(255, 235, 150) : new Color(90, 96, 120);
            Ui.Rect(new Vector2(x, y), new Vector2(56, 30), lit ? new Color(255, 210, 63, 60) : new Color(16, 18, 32, 220));
            Ui.Frame(new Rectangle((int)x, (int)y, 56, 30), 1, lit ? new Color(255, 210, 63) : new Color(60, 66, 90));
            var s = f.Measure(label, 0.6f);
            f.Draw(sb, label, new Vector2(x + 28 - s.X / 2, y + 7), col, 0.6f);
        }
        float bx = panel.X + 14, by = panel.Y + 112;
        KeyLabel("W", raw.Y > 0.3f, bx + 62, by);
        KeyLabel("A", raw.X < -0.3f, bx, by + 34);
        KeyLabel("S", raw.Y < -0.3f, bx + 62, by + 34);
        KeyLabel("D", raw.X > 0.3f, bx + 124, by + 34);

        Ui.End();
    }
#endif
}
