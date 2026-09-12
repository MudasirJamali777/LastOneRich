using Microsoft.Xna.Framework.Input;

namespace LastOneRich.Core;

/// <summary>Keyboard + gamepad input with edge detection (GDD section 4/12: controller-friendly).</summary>
public static class Input
{
    static KeyboardState _cur, _prev;
    static GamePadState _gp, _gpPrev;
    const float Deadzone = 0.22f;

    public static void Update()
    {
        _prev = _cur; _cur = Keyboard.GetState();
        _gpPrev = _gp; _gp = Microsoft.Xna.Framework.Input.GamePad.GetState(Microsoft.Xna.Framework.PlayerIndex.One);
    }

    public static bool Held(params Keys[] keys) { foreach (var k in keys) if (_cur.IsKeyDown(k)) return true; return false; }
    public static bool Pressed(params Keys[] keys) { foreach (var k in keys) if (_cur.IsKeyDown(k) && !_prev.IsKeyDown(k)) return true; return false; }

    static bool GpPressed(Buttons b) => _gp.IsButtonDown(b) && !_gpPrev.IsButtonDown(b);
    static bool GpHeld(Buttons b) => _gp.IsButtonDown(b);

    public static Microsoft.Xna.Framework.Vector2 Move
    {
        get
        {
            float x = 0, y = 0;
            if (Held(Keys.A, Keys.Left)) x -= 1;
            if (Held(Keys.D, Keys.Right)) x += 1;
            if (Held(Keys.W, Keys.Up)) y += 1;
            if (Held(Keys.S, Keys.Down)) y -= 1;
            var st = _gp.ThumbSticks.Left;
            if (System.MathF.Abs(st.X) > Deadzone) x += st.X;
            if (System.MathF.Abs(st.Y) > Deadzone) y += st.Y;
            var v = new Microsoft.Xna.Framework.Vector2(x, y);
            if (v.LengthSquared() > 1f) v.Normalize();
            return v;
        }
    }

    public static bool JumpPressed => Pressed(Keys.Space) || GpPressed(Buttons.A);
    public static bool DivePressed => Pressed(Keys.LeftShift, Keys.RightShift) || GpPressed(Buttons.X);
    public static bool ConfirmPressed => Pressed(Keys.Enter, Keys.Space) || GpPressed(Buttons.A);
    public static bool BackPressed => Pressed(Keys.Escape) || GpPressed(Buttons.B);
    public static bool PausePressed => Pressed(Keys.Escape) || GpPressed(Buttons.Start);
    public static bool UpPressed => Pressed(Keys.W, Keys.Up) || GpPressed(Buttons.DPadUp);
    public static bool DownPressed => Pressed(Keys.S, Keys.Down) || GpPressed(Buttons.DPadDown);
    public static bool LeftPressed => Pressed(Keys.A, Keys.Left) || GpPressed(Buttons.DPadLeft);
    public static bool RightPressed => Pressed(Keys.D, Keys.Right) || GpPressed(Buttons.DPadRight);
    public static bool Num1Pressed => Pressed(Keys.D1, Keys.NumPad1) || GpPressed(Buttons.LeftShoulder);
    public static bool Num2Pressed => Pressed(Keys.D2, Keys.NumPad2) || GpPressed(Buttons.RightShoulder);
    public static bool SkipPressed => Pressed(Keys.Enter, Keys.Space, Keys.E) || GpPressed(Buttons.A);
}
