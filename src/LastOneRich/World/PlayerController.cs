using Microsoft.Xna.Framework;

namespace LastOneRich.World;

public struct InputState
{
    public Vector2 Move;
    public bool Jump;
    public bool Dive;
}

/// <summary>Arcade player controller (GDD 4): run, jump, air control, dive, forgiving coyote/buffer.</summary>
public sealed class PlayerController
{
    float _coyote, _jumpBuf, _diveT, _diveCd;
    Vector3 _diveDir = new(0, 0, 1);

    public void Update(Actor a, in InputState inp, Level lv, float dt)
    {
        _coyote = a.OnGround ? Phys.Coyote : System.MathF.Max(0f, _coyote - dt);
        _jumpBuf = inp.Jump ? Phys.JumpBuffer : System.MathF.Max(0f, _jumpBuf - dt);
        _diveCd = System.MathF.Max(0f, _diveCd - dt);
        a.Stagger = System.MathF.Max(0f, a.Stagger - dt);
        a.BumpCd = System.MathF.Max(0f, a.BumpCd - dt);

        var mv = a.Stagger > 0 ? Vector2.Zero : inp.Move;

        float maxS = Phys.MaxSpeed * (a.InSlime ? (float)lv.SlimeSpeedMult : 1f);
        var wish = new Vector3(mv.X, 0, mv.Y) * maxS;
        float accel = (a.OnGround ? Phys.Accel : Phys.AirAccel) * (a.InSlime ? 0.5f : 1f);

        if (_diveT > 0)
        {
            _diveT -= dt;
            Phys.Approach(ref a.Vel.X, _diveDir.X * Phys.DiveSpeed, 8f, dt);
            Phys.Approach(ref a.Vel.Z, _diveDir.Z * Phys.DiveSpeed, 8f, dt);
        }
        else
        {
            Phys.Approach(ref a.Vel.X, wish.X, accel, dt);
            Phys.Approach(ref a.Vel.Z, wish.Z, accel, dt);
            if (a.OnGround && mv.LengthSquared() < 0.001f)
            {
                Phys.Approach(ref a.Vel.X, 0f, Phys.Friction, dt);
                Phys.Approach(ref a.Vel.Z, 0f, Phys.Friction, dt);
            }
        }

        // facing follows intent
        var f = wish;
        if (f.LengthSquared() < 0.01f) f = a.Vel;
        f.Y = 0;
        if (f.LengthSquared() > 0.01f) a.FaceDir = Vector3.Normalize(f);

        // jump (buffered + coyote)
        if (_jumpBuf > 0 && (_coyote > 0 || a.OnGround))
        {
            a.Vel.Y = Phys.JumpVel * (a.InSlime ? (float)lv.SlimeJumpMult : 1f);
            _jumpBuf = 0; _coyote = 0;
            if (_diveT > 0) _diveT = 0;
            lv.Sfx?.Invoke("jump");
        }

        // dive burst
        if (inp.Dive && _diveCd <= 0 && a.Stagger <= 0)
        {
            var dir = a.FaceDir;
            if (mv.LengthSquared() > 0.001f) dir = Vector3.Normalize(new Vector3(mv.X, 0, mv.Y));
            _diveDir = dir;
            _diveT = Phys.DiveTime;
            _diveCd = Phys.DiveCooldown;
            a.Vel.X = dir.X * Phys.DiveSpeed;
            a.Vel.Z = dir.Z * Phys.DiveSpeed;
            if (!a.OnGround) a.Vel.Y = System.MathF.Max(a.Vel.Y, 1.5f);
            lv.Sfx?.Invoke("jump");
        }

        lv.World.Integrate(a, dt, lv.CarryFor(a));
    }
}
