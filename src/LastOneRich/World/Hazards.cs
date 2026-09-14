using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>Reusable hazard modules configured by JSON (GDD section 11).</summary>
public sealed class MovingPlatform
{
    public Vector3 A, B, Size, Pos;
    public double Period = 5, Phase;
    public Color Tint = Color.White;
    public Vector3 FrameDelta;

    float _t;

    public MovingPlatform(MoverDTO dto)
    {
        A = dto.A.ToVec3();
        B = dto.B.ToVec3();
        Size = dto.Size.ToVec3();
        Pos = A;
        Period = System.Math.Max(0.5, dto.Period);
        Phase = dto.Phase;
        Tint = ColorUtil.Parse(dto.Color);
    }

    static float PingPong(float u)
    {
        u %= 1f;
        if (u < 0) u += 1f;
        return u <= 0.5f ? u * 2f : (1f - u) * 2f;
    }

    public void Update(float dt)
    {
        _t += dt;
        var prev = Pos;
        Pos = Vector3.Lerp(A, B, PingPong((float)(_t / Period + Phase)));
        FrameDelta = Pos - prev;
    }
}

public sealed class RotatorHammer
{
    public Vector3 Pivot;
    public float ArmLength;
    public float Speed = 1.8f;
    public float Phase;
    public Vector3 HeadSize, ArmSize;
    public Color Tint = Color.Red;
    public float Angle;

    public RotatorHammer(HammerDTO dto)
    {
        Pivot = dto.Pivot.ToVec3();
        ArmLength = (float)dto.ArmLength;
        Speed = (float)dto.Speed;
        Phase = (float)dto.Phase;
        HeadSize = dto.HeadSize.ToVec3();
        ArmSize = dto.ArmSize.ToVec3();
        Tint = ColorUtil.Parse(dto.Color);
    }

    public void ApplyTwist(double mult) => Speed *= (float)mult;

    public Vector3 HeadPos => Pivot + new Vector3(MathF.Sin(Angle), 0, MathF.Cos(Angle)) * ArmLength;

    public void Update(float dt, List<Actor> actors, Level lv)
    {
        Angle = Phase + lv.Time * Speed;
        var head = HeadPos;
        foreach (var a in actors)
        {
            if (a.HammerCd > 0) { a.HammerCd -= dt; continue; }
            if (Vector3.Distance(a.Pos, head) < 0.75f + Actor.HalfXZ + 0.15f)
            {
                var radial = a.Pos - Pivot; radial.Y = 0;
                if (radial.LengthSquared() < 0.001f) radial = new Vector3(0, 0, 1);
                radial.Normalize();
                var tangent = new Vector3(-radial.Z, 0, radial.X) * System.Math.Sign(Speed);
                a.Vel += tangent * 11f + Vector3.Up * 4.5f;
                a.Stagger = 0.6f;
                a.HammerCd = 0.9f;
                lv.Sfx?.Invoke("hammer_hit");
            }
        }
    }

    public void Draw(GeometryRenderer r)
    {
        r.Box(new Vector3(Pivot.X, Pivot.Y * 0.5f, Pivot.Z), new Vector3(0.7f, Pivot.Y, 0.7f), new Color(40, 44, 58));
        var dir = new Vector3(MathF.Sin(Angle), 0, MathF.Cos(Angle));
        r.BoxRotY(Pivot + dir * (ArmLength * 0.5f), ArmSize, Angle, new Color(200, 205, 220));
        r.BoxRotYGlow(HeadPos, HeadSize, Angle, Tint == Color.Red ? ColorPalette.DangerOrange : Tint);
    }
}

public sealed class ConveyorZone
{
    public Vector3 Pos, Size, Dir;
    public double Speed = 6;
    public Color Tint = ColorPalette.ConveyorYellow;

    public ConveyorZone(ConveyorDTO dto)
    {
        Pos = dto.Pos.ToVec3();
        Size = dto.Size.ToVec3();
        Dir = dto.Dir.ToVec3();
        if (Dir.LengthSquared() < 0.001f) Dir = new Vector3(0, 0, 1);
        Dir.Normalize();
        Speed = dto.Speed;
        Tint = ColorUtil.Parse(dto.Color);
    }

    public void ApplyTwist(double mult) => Speed *= mult;

    public void Effect(Actor a, float dt)
    {
        if (!a.OnGround) return;
        var feet = a.Pos - Vector3.UnitY * Actor.HalfY;
        if (CollisionWorld.PointInBox(feet, Pos, Size * 0.5f))
            a.Pos += Dir * (float)Speed * dt;
    }

    public void Draw(GeometryRenderer r, float time)
    {
        // animated belt stripes on top of the (static) base box
        float along = System.MathF.Abs(Size.X * Dir.X) + System.MathF.Abs(Size.Z * Dir.Z);
        if (along < 0.5f) return;
        float step = 2f;
        int count = (int)System.MathF.Ceiling(along / step);
        float offset = (float)(time * Speed) % step;
        var across = new Vector3(System.MathF.Abs(Dir.Z), 0, System.MathF.Abs(Dir.X)); // perpendicular on XZ
        var stripeSize = new Vector3(
            System.MathF.Abs(Dir.X) * 0.9f + across.X * (Size.X - 0.6f),
            0.12f,
            System.MathF.Abs(Dir.Z) * 0.9f + across.Z * (Size.Z - 0.6f));
        for (int i = 0; i < count; i++)
        {
            float s = ((i * step + offset) % along + along) % along;
            var c = Pos - Dir * (along * 0.5f) + Dir * s + Vector3.UnitY * (Size.Y * 0.5f + 0.02f);
            var col = i % 2 == 0 ? Tint : ColorUtil.Shade(Tint, 0.55f);
            r.Box(c, stripeSize, col);
        }
    }
}

public sealed class SlimeZone
{
    public Vector3 Pos, Size;
    public double SpeedMult = 0.3, JumpMult = 0.7;
    public Color Tint = Color.Green;

    public SlimeZone(SlimeDTO dto)
    {
        Pos = dto.Pos.ToVec3();
        Size = dto.Size.ToVec3();
        SpeedMult = dto.SpeedMult;
        JumpMult = dto.JumpMult;
        Tint = ColorUtil.Parse(dto.Color);
    }

    public void ApplyTwist(double mult) => SpeedMult *= mult;

    public bool Contains(Actor a) =>
        CollisionWorld.PointInBox(a.Pos - Vector3.UnitY * (Actor.HalfY - 0.05f), Pos, Size * 0.5f);

    public void Draw(GeometryRenderer r, float time)
    {
        float alpha = 0.30f + 0.06f * MathF.Sin(time * 2.2f);
        r.Zone(Pos, Size + new Vector3(-0.2f, 0.2f, -0.2f), Tint, alpha);
    }
}

public sealed class WindZone
{
    public Vector3 Pos, Size, Dir;
    public double Strength = 10, Period = 4, Duty = 0.5;
    public Color Tint = ColorPalette.WindBlue;

    public WindZone(WindDTO dto)
    {
        Pos = dto.Pos.ToVec3();
        Size = dto.Size.ToVec3();
        Dir = dto.Dir.ToVec3();
        if (Dir.LengthSquared() < 0.001f) Dir = -Vector3.UnitZ;
        Dir.Normalize();
        Strength = dto.Strength;
        Period = System.Math.Max(0.5, dto.Period);
        Duty = MathHelper.Clamp((float)dto.Duty, 0.1f, 1f);
        Tint = ColorUtil.Parse(dto.Color);
    }

    public void ApplyTwist(double mult) => Strength *= mult;

    public bool Active(float time) => (time % (float)Period) / (float)Period < Duty;

    public void Effect(Actor a, float dt, float time)
    {
        if (!Active(time)) return;
        if (CollisionWorld.PointInBox(a.Pos, Pos, Size * 0.5f))
            a.Vel += Dir * (float)Strength * dt;
    }

    public void Draw(GeometryRenderer r, float time)
    {
        float alpha = Active(time) ? 0.14f + 0.05f * MathF.Sin(time * 9f) : 0.04f;
        r.Zone(Pos, Size, Tint, alpha);
        // emitter plate on the upwind face
        var plateCenter = Pos - Dir * (new Vector3(System.MathF.Abs(Size.X * Dir.X), 0, System.MathF.Abs(Size.Z * Dir.Z)) * 0.5f);
        var plateSize = new Vector3(
            System.MathF.Abs(Dir.X) * 0.4f + System.MathF.Abs(Dir.Z) * Size.X,
            Size.Y * 0.8f,
            System.MathF.Abs(Dir.Z) * 0.4f + System.MathF.Abs(Dir.X) * Size.Z);
        if (Active(time)) r.BoxGlow(plateCenter, plateSize, Tint);
        else r.Box(plateCenter, plateSize, ColorUtil.Shade(Tint, 0.4f));
    }
}

public static class DtoExt
{
    public static Vector3 ToVec3(this float[] a) => a != null && a.Length >= 3 ? new Vector3(a[0], a[1], a[2]) : Vector3.Zero;
}
