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

    public LorGame(LaunchArgs launch)
    {
        _gfx = new GraphicsDeviceManager(this);
        _launch = launch;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "LAST ONE RICH! — Volt Dome (vertical slice)";

        // Rendering baseline (rendering task): MSAA on. Reach profile = maximum
        // compatibility (old GPUs / software GL / VMs); slice vertex counts are tiny.
        _gfx.GraphicsProfile = GraphicsProfile.Reach;
        _gfx.PreferMultiSampling = true;
    }

    protected override void Initialize()
    {
        _gfx.PreferredBackBufferWidth = 1280;
        _gfx.PreferredBackBufferHeight = 720;
        _gfx.ApplyChanges();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        var sb = new SpriteBatch(GraphicsDevice);
        GameServices.Init(GraphicsDevice, sb, _launch);
        if (_launch?.Overlay == true) GameServices.DebugOverlay = true;
        _states = new StateMachine();
        _states.Replace(new BootState(_states));
    }

    protected override void Update(GameTime gameTime)
    {
        Input.Update();
        if (Input.PressedAction("DebugOverlay")) GameServices.DebugOverlay = !GameServices.DebugOverlay;

        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        dt = MathHelper.Min(dt, 1f / 20f); // clamp hitches (alt-tab safety)
        _states.Update(dt);
        base.Update(gameTime);
        if (_states.IsEmpty) Exit();
    }

    protected override void Draw(GameTime gameTime)
    {
        _frame++;
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        if (dt > 0.0001f) _fps = _fps <= 0 ? 1f / dt : MathHelper.Lerp(_fps, 1f / dt, 0.05f);

        _states.Draw();
        if (GameServices.DebugOverlay) DrawDebugOverlay();
        base.Draw(gameTime);

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
}
