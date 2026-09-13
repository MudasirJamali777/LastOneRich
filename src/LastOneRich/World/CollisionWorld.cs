using Microsoft.Xna.Framework;

namespace LastOneRich.World;

public sealed class Collider
{
    public Vector3 Center;
    public Vector3 Half;
    public int Mover = -1; // index into Level.Movers, -1 = static
}

public sealed class RampCollider
{
    public Vector3 Min, Max;
    public int DirX, DirZ; // rising direction (one of them ±1)
    public float BaseY, TopY;

    public float HeightAt(float x, float z)
    {
        float t;
        if (DirZ != 0)
        {
            t = (z - Min.Z) / System.MathF.Max(0.001f, Max.Z - Min.Z);
            if (DirZ < 0) t = 1f - t;
        }
        else
        {
            t = (x - Min.X) / System.MathF.Max(0.001f, Max.X - Min.X);
            if (DirX < 0) t = 1f - t;
        }
        t = MathHelper.Clamp(t, 0f, 1f);
        return BaseY + (TopY - BaseY) * t;
    }
}

/// <summary>
/// Custom AABB trigger/step physics (GDD section 0: "simple custom collision, not rigidbody").
/// Axis-separated resolve + step-up ledges + ramp height fields + platform carry.
/// </summary>
public sealed class CollisionWorld
{
    public List<Collider> Colliders = new();
    public List<RampCollider> Ramps = new();

    public void AddBox(Vector3 center, Vector3 size, int moverIdx = -1)
    {
        Colliders.Add(new Collider { Center = center, Half = size * 0.5f, Mover = moverIdx });
    }

    public void AddRamp(Vector3 center, Vector3 size, int dirX, int dirZ)
    {
        var h = size * 0.5f;
        Ramps.Add(new RampCollider
        {
            Min = center - h,
            Max = center + h,
            DirX = dirX,
            DirZ = dirZ,
            BaseY = center.Y - h.Y,
            TopY = center.Y + h.Y,
        });
    }

    public static bool PointInBox(Vector3 p, Vector3 center, Vector3 half) =>
        System.MathF.Abs(p.X - center.X) <= half.X &&
        System.MathF.Abs(p.Y - center.Y) <= half.Y &&
        System.MathF.Abs(p.Z - center.Z) <= half.Z;

    public void Integrate(Actor a, float dt, Vector3 carry)
    {
        a.Pos += carry;

        a.Pos.X += a.Vel.X * dt;
        ResolveHorizontal(a, 0);
        a.Pos.Z += a.Vel.Z * dt;
        ResolveHorizontal(a, 2);

        a.Vel.Y = System.MathF.Max(a.Vel.Y - Phys.Gravity * dt, Phys.Terminal);
        a.Pos.Y += a.Vel.Y * dt;

        bool grounded = false;
        int mover = -1;

        foreach (var c in Colliders)
        {
            float dx = a.Pos.X - c.Center.X, dz = a.Pos.Z - c.Center.Z;
            if (System.MathF.Abs(dx) >= Actor.HalfXZ + c.Half.X || System.MathF.Abs(dz) >= Actor.HalfXZ + c.Half.Z) continue;

            float top = c.Center.Y + c.Half.Y, bottom = c.Center.Y - c.Half.Y;
            float feet = a.Pos.Y - Actor.HalfY, head = a.Pos.Y + Actor.HalfY;
            if (head <= bottom || feet >= top) continue;

            if (a.Vel.Y <= 0f && feet >= top - 0.75f) // > terminal fall per frame (0.63) so nothing clips through
            {
                a.Pos.Y = top + Actor.HalfY;
                a.Vel.Y = 0f;
                grounded = true;
                mover = c.Mover;
            }
            else if (a.Vel.Y > 0f)
            {
                a.Pos.Y = bottom - Actor.HalfY - 0.001f;
                a.Vel.Y = 0f;
            }
        }

        // Ramps: snap to height field when close to the surface from above.
        float? floor = null;
        foreach (var r in Ramps)
        {
            if (a.Pos.X < r.Min.X || a.Pos.X > r.Max.X || a.Pos.Z < r.Min.Z || a.Pos.Z > r.Max.Z) continue;
            float h = r.HeightAt(a.Pos.X, a.Pos.Z);
            float feet = a.Pos.Y - Actor.HalfY;
            if (h <= feet + Phys.StepUp && h >= feet - 0.6f)
                if (floor == null || h > floor.Value) floor = h;
        }
        if (floor != null && a.Vel.Y <= 0.01f)
        {
            a.Pos.Y = floor.Value + Actor.HalfY;
            a.Vel.Y = 0f;
            grounded = true;
        }

        a.OnGround = grounded;
        a.GroundMover = mover;
    }

    void ResolveHorizontal(Actor a, int axis)
    {
        foreach (var c in Colliders)
        {
            float dx = a.Pos.X - c.Center.X, dz = a.Pos.Z - c.Center.Z;
            float ox = Actor.HalfXZ + c.Half.X - System.MathF.Abs(dx);
            float oz = Actor.HalfXZ + c.Half.Z - System.MathF.Abs(dz);
            if (ox <= 0f || oz <= 0f) continue;

            float top = c.Center.Y + c.Half.Y, bottom = c.Center.Y - c.Half.Y;
            float feet = a.Pos.Y - Actor.HalfY, head = a.Pos.Y + Actor.HalfY;
            if (head <= bottom + 0.02f || feet >= top - 0.02f) continue;

            // Min-penetration rule: only eject along this axis if it is the shallower one.
            // Without this, a head-on Z graze ejects the actor sideways across the level.
            if (axis == 0 && ox > oz + 0.0001f) continue;
            if (axis == 2 && oz > ox + 0.0001f) continue;

            // Step-up small ledges while grounded/falling (forgiving movement, GDD 4).
            float rise = top - feet;
            if (rise <= Phys.StepUp && rise > -0.01f && (a.OnGround || a.Vel.Y <= 0.5f))
            {
                a.Pos.Y = top + Actor.HalfY;
                a.Vel.Y = System.MathF.Max(a.Vel.Y, 0f);
                a.OnGround = true;
                continue;
            }

            if (axis == 0)
            {
                a.Pos.X += (dx >= 0 ? 1 : -1) * ox;
                a.Vel.X = 0f;
            }
            else
            {
                a.Pos.Z += (dz >= 0 ? 1 : -1) * oz;
                a.Vel.Z = 0f;
            }
        }
    }
}
