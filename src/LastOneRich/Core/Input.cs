using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace LastOneRich.Core;

/// <summary>
/// Keyboard + gamepad input with edge detection.
/// Keyboard actions are remappable via content/data/controls.json (edit the file, restart).
/// Gamepad mappings are fixed (A=jump/confirm, X=dive, Start=pause, d-pad=menu).
/// </summary>
public static class Input
{
    static KeyboardState _cur, _prev;
    static GamePadState _gp, _gpPrev;
    const float Deadzone = 0.22f;

    public static void Update()
    {
        Keybinds.EnsureLoaded();
        _prev = _cur; _cur = Keyboard.GetState();
        _gpPrev = _gp; _gp = GamePad.GetState(PlayerIndex.One);
    }

    // ---- raw keyboard (rare direct queries) ----
    public static bool Held(params Keys[] keys) { foreach (var k in keys) if (_cur.IsKeyDown(k)) return true; return false; }
    public static bool Pressed(params Keys[] keys) { foreach (var k in keys) if (_cur.IsKeyDown(k) && !_prev.IsKeyDown(k)) return true; return false; }

    // ---- remappable actions ----
    public static bool HeldAction(string action) => Keybinds.Held(_cur, action);
    public static bool PressedAction(string action) => Keybinds.Pressed(_cur, _prev, action);

    static bool GpPressed(Buttons b) => _gp.IsButtonDown(b) && !_gpPrev.IsButtonDown(b);

    /// <summary>
    /// Raw move intent in SCREEN space: +X = screen-right, +Y = screen-forward/away.
    /// GameplayState resolves this into world-space XZ using the camera basis
    /// (see GameplayState.ResolveMove) — never feed this straight into the world.
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
