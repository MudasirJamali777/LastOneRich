using LastOneRich.States;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

public sealed class LorGame : Game
{
    readonly GraphicsDeviceManager _gfx;
    StateMachine _states;
    int _frame;

    public LorGame(LaunchArgs launch)
    {
        _gfx = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "LAST ONE RICH! — Volt Dome (vertical slice)";
        _launch = launch;
    }

    readonly LaunchArgs _launch;

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
        _states = new StateMachine();
        _states.Replace(new BootState(_states));
    }

    protected override void Update(GameTime gameTime)
    {
        Input.Update();
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        dt = MathHelper.Min(dt, 1f / 20f); // clamp hitches (alt-tab safety)
        _states.Update(dt);
        base.Update(gameTime);
        if (_states.IsEmpty) Exit();
    }

    protected override void Draw(GameTime gameTime)
    {
        _frame++;
        _states.Draw();
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
}
