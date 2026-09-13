using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>DroneScanner (GDD §11.1): patrols A↔B, detection radius = strike zone.</summary>
public sealed class DroneScanner
{
    public Vector3 A, B;
    public float Speed = 6f;
    public float Radius = 3.2f;
    public float Phase;
    public Vector3 Pos;
    public bool MovingToB;          // current patrol direction (for dodge timing)
    public Color Tint = new(255, 60, 80);

    public DroneScanner(DroneDTO dto)
    {
        A = dto.A.ToVec3();
        B = dto.B.ToVec3();
        Speed = (float)dto.Speed;
        Radius = (float)dto.Radius;
        Phase = (float)dto.Phase;
        Pos = A;
    }

    static float PingPong(float u)
    {
        u %= 1f;
        if (u < 0) u += 1f;
        return u <= 0.5f ? u * 2f : (1f - u) * 2f;
    }

    public void ApplyTwist(double mult) => Speed *= (float)mult;

    public void Update(Level lv)
    {
        float u = Phase + lv.Time * (Speed / System.MathF.Max(0.1f, Vector3.Distance(A, B)));
        Pos = Vector3.Lerp(A, B, PingPong(u));
        float un = u % 1f;
        if (un < 0) un += 1f;
        MovingToB = un <= 0.5f;
    }

    public bool Detects(Vector3 p, float time)
    {
        var d = p - Pos;
        d.Y = 0;
        return d.Length() <= Radius;
    }

    public void Draw(GeometryRenderer r, float time)
    {
        // detection disc
        r.Zone(new Vector3(Pos.X, Pos.Y - 1.6f, Pos.Z), new Vector3(Radius * 2f, 0.15f, Radius * 2f), Tint, 0.16f + 0.05f * MathF.Sin(time * 7f));
        // body + rotor
        r.Box(Pos, new Vector3(1.1f, 0.6f, 1.1f), new Color(230, 235, 245));
        r.BoxRotY(Pos + new Vector3(0, 0.5f, 0), new Vector3(2.2f, 0.1f, 0.25f), time * 22f, Tint);
        r.Box(Pos + new Vector3(0, -0.5f, 0), new Vector3(0.3f, 0.4f, 0.3f), new Color(40, 44, 58));
    }
}

/// <summary>IceZone (GDD §11.1): low-friction floor patch.</summary>
public sealed class IceZone
{
    public Vector3 Pos, Size;
    public float FrictionMult = 0.3f;
    public Color Tint = new(190, 230, 255);

    public IceZone(float[] dto)
    {
        Pos = new Vector3(dto[0], dto[1], dto[2]);
        Size = new Vector3(dto[3], dto[4], dto[5]);
        FrictionMult = dto.Length >= 7 ? dto[6] : 0.3f;
    }

    public void ApplyTwist(double mult) => FrictionMult *= (float)mult;

    public bool Contains(Actor a) => CollisionWorld.PointInBox(a.Pos - Vector3.UnitY * (Actor.HalfY - 0.05f), Pos, Size * 0.5f);

    public void Draw(GeometryRenderer r, float time)
    {
        r.Zone(Pos, Size + new Vector3(-0.2f, 0.2f, -0.2f), Tint, 0.20f + 0.04f * MathF.Sin(time * 1.7f));
    }
}
