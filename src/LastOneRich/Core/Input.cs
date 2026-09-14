using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace LastOneRich.Core;

/// <summary>
/// Keyboard + mouse + gamepad input with edge detection.
/// Keyboard actions are remappable via content/data/controls.json (edit the file, restart).
/// Gamepad mappings are fixed (A=jump/confirm, X=dive, Start=pause, d-pad=menu).
///
/// Mouse-look: gameplay captures the cursor (hidden + re-centred every frame) and reads
/// <see cref="MouseDelta"/> in pixels. Menus/pause release it. Capture is a no-op when the
/// host window has not been attached (headless/CI), so nothing here can crash a sim run.
/// </summary>
public static class Input
{
    static KeyboardState _cur, _prev;
    static GamePadState _gp, _gpPrev;
    static MouseState _mouse, _mousePrev;
    const float Deadzone = 0.22f;

    // ---- mouse capture ----
    static Game _host;
    static bool _captured;
    static bool _warmup;              // skip the first delta after capturing (avoids a huge jump)
    static Vector2 _delta;

    /// <summary>
    /// Attach the MonoGame host so capture can hide the cursor. Called once from LorGame.
    /// Optional: if never called, capture silently does nothing and MouseDelta stays zero.
    /// </summary>
    public static void AttachHost(Game game) => _host = game;

    /// <summary>True while the cursor is hidden and locked to the window centre.</summary>
    public static bool MouseCaptured => _captured;

    /// <summary>Mouse movement in pixels since the previous Update. Zero unless captured.</summary>
    public static Vector2 MouseDelta => _delta;

    /// <summary>Hide + lock the cursor to the window centre, or release it back to the OS.</summary>
    public static void SetMouseCapture(bool on)
    {
        if (_captured == on) return;
        _captured = on;
        _delta = Vector2.Zero;
        if (_host == null) return;
        try
        {
            _host.IsMouseVisible = !on;
            if (on)
            {
                _warmup = true;
                CenterCursor();
            }
        }
        catch { _captured = false; } // no window / no mouse device — stay uncaptured
    }

    static void CenterCursor()
    {
        try
        {
            var b = _host.Window.ClientBounds;
            if (b.Width > 1 && b.Height > 1) Mouse.SetPosition(b.Width / 2, b.Height / 2);
        }
        catch { }
    }

    public static void Update()
    {
        Keybinds.EnsureLoaded();
        _prev = _cur; _cur = Keyboard.GetState();
        _gpPrev = _gp; _gp = GamePad.GetState(PlayerIndex.One);
        _mousePrev = _mouse;

        try { _mouse = Mouse.GetState(); } catch { _mouse = _mousePrev; }

        if (_captured && _host != null)
        {
            try
            {
                var b = _host.Window.ClientBounds;
                int cx = b.Width / 2, cy = b.Height / 2;
                _delta = _warmup ? Vector2.Zero : new Vector2(_mouse.X - cx, _mouse.Y - cy);
                _warmup = false;
                CenterCursor();
            }
            catch { _delta = Vector2.Zero; }
        }
        else
        {
            _delta = Vector2.Zero;
        }
    }

    // ---- raw keyboard (rare direct queries) ----
    public static bool Held(params Keys[] keys) { foreach (var k in keys) if (_cur.IsKeyDown(k)) return true; return false; }
    public static bool Pressed(params Keys[] keys) { foreach (var k in keys) if (_cur.IsKeyDown(k) && !_prev.IsKeyDown(k)) return true; return false; }

    // ---- remappable actions ----
    public static bool HeldAction(string action) => Keybinds.Held(_cur, action);
    public static bool PressedAction(string action) => Keybinds.Pressed(_cur, _prev, action);

    static bool GpPressed(Buttons b) => _gp.IsButtonDown(b) && !_gpPrev.IsButtonDown(b);

    /// <summary>
    /// Raw move intent in LOCAL space: +X = strafe right, +Y = forward.
    /// GameplayState rotates this by the camera yaw (see GameplayState.ResolveMove) —
    /// never feed this straight into the world.
    /// </summary>
    public static Vector2 Move
    {
        get
        {
            float x = 0, y = 0;
            if (HeldAction("MoveLeft")) x -= 1;
            if (HeldAction("MoveRight")) x += 1;
            if (HeldAction("MoveForward")) y += 1;
            if (HeldAction("MoveBack")) y -= 1;
            var st = _gp.ThumbSticks.Left;
            if (System.MathF.Abs(st.X) > Deadzone) x += st.X;
            if (System.MathF.Abs(st.Y) > Deadzone) y += st.Y;
            var v = new Vector2(x, y);
            if (v.LengthSquared() > 1f) v.Normalize();
            return v;
        }
    }

    /// <summary>Right stick look intent (-1..1 per axis) — added to the mouse delta for pad players.</summary>
    public static Vector2 LookStick
    {
        get
        {
            var st = _gp.ThumbSticks.Right;
            float x = System.MathF.Abs(st.X) > Deadzone ? st.X : 0f;
            float y = System.MathF.Abs(st.Y) > Deadzone ? st.Y : 0f;
            return new Vector2(x, y);
        }
    }

    public static bool JumpPressed => PressedAction("Jump") || GpPressed(Buttons.A);
    public static bool DivePressed => PressedAction("Dive") || GpPressed(Buttons.X);
    public static bool ConfirmPressed => PressedAction("Confirm") || GpPressed(Buttons.A);
    public static bool BackPressed => PressedAction("Pause") || GpPressed(Buttons.B);
    public static bool PausePressed => PressedAction("Pause") || GpPressed(Buttons.Start);
    public static bool UpPressed => PressedAction("MoveForward") || GpPressed(Buttons.DPadUp);
    public static bool DownPressed => PressedAction("MoveBack") || GpPressed(Buttons.DPadDown);
    public static bool LeftPressed => PressedAction("MoveLeft") || GpPressed(Buttons.DPadLeft);
    public static bool RightPressed => PressedAction("MoveRight") || GpPressed(Buttons.DPadRight);
    public static bool Num1Pressed => Pressed(Keys.D1, Keys.NumPad1) || GpPressed(Buttons.LeftShoulder);
    public static bool Num2Pressed => Pressed(Keys.D2, Keys.NumPad2) || GpPressed(Buttons.RightShoulder);
    public static bool SkipPressed => PressedAction("Confirm") || GpPressed(Buttons.A);
}
