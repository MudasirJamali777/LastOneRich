using Microsoft.Xna.Framework;

namespace LastOneRich.Core;

/// <summary>
/// 3D particle system for world events (Priority 6) — glass shards, dust puffs, landing sparks.
///
/// Deliberately NOT part of <see cref="Particles"/>: that one lives in 2D screen space for
/// ceremonies and HUD confetti, whereas these exist in the world, are occluded by geometry and
/// are drawn as real boxes through <see cref="GeometryRenderer"/>. Keeping them apart means the
/// UI pass and the 3D pass never fight over blend/depth state.
///
/// Budgeted and allocation-free in the steady state: the pool is fixed at <see cref="Max"/>,
/// dead slots are recycled, and an exhausted pool drops new requests rather than growing
/// (a frame with 300 shards must never stall the round).
/// </summary>
public sealed class WorldParticles
{
    public const int Max = 512;

    struct P
    {
        public Vector3 Pos, Vel;
        public float Life, MaxLife, Size, Spin, Yaw;
        public Color Color;
        public bool Glow;
        public bool Alive;
    }

    readonly P[] _ps = new P[Max];
    int _cursor;

    /// <summary>Live particle count — surfaced for the F3 debug overlay.</summary>
    public int Count
    {
        get
        {
            int n = 0;
            for (int i = 0; i < Max; i++) if (_ps[i].Alive) n++;
            return n;
        }
    }

    int NextFree()
    {
        for (int i = 0; i < Max; i++)
        {
            int idx = (_cursor + i) % Max;
            if (!_ps[idx].Alive) { _cursor = (idx + 1) % Max; return idx; }
        }
        return -1;   // pool exhausted — drop the request (never grow mid-round)
    }

    void Spawn(Vector3 pos, Vector3 vel, float life, float size, Color color, bool glow)
    {
        int i = NextFree();
        if (i < 0) return;
        _ps[i] = new P
        {
            Pos = pos,
            Vel = vel,
            Life = life,
            MaxLife = life,
            Size = size,
            Spin = Rng.Range(-9f, 9f),
            Yaw = Rng.Range(0f, 6.28f),
            Color = color,
            Glow = glow,
            Alive = true,
        };
#if DEBUG
        System.Diagnostics.Debug.Assert(Count <= Max, "World particle pool capacity exceeded.");
#endif
    }

    /// <summary>
    /// A pane of glass giving way: shards burst outward and downward from the pane's footprint,
    /// tinted from the pane's own color so the break reads as that tile and not a generic puff.
    /// </summary>
    public void GlassShatter(Vector3 center, Vector3 size, Color tint, int count = 26)
    {
        for (int i = 0; i < count; i++)
        {
            var p = new Vector3(
                center.X + Rng.Range(-size.X * 0.5f, size.X * 0.5f),
                center.Y + Rng.Range(-size.Y * 0.4f, size.Y * 0.5f),
                center.Z + Rng.Range(-size.Z * 0.5f, size.Z * 0.5f));
            var v = new Vector3(Rng.Range(-3.4f, 3.4f), Rng.Range(-1.2f, 3.2f), Rng.Range(-3.4f, 3.4f));
            var c = Rng.Chance(0.35f) ? Color.Lerp(tint, Color.White, 0.65f) : tint;
            Spawn(p, v, Rng.Range(0.8f, 1.6f), Rng.Range(0.12f, 0.34f), c, glow: Rng.Chance(0.3f));
        }
    }

    /// <summary>Small dust kick — landings, respawns, tile first-contact.</summary>
    public void Dust(Vector3 center, int count = 8, Color? color = null)
    {
        var c = color ?? new Color(150, 158, 185);
        for (int i = 0; i < count; i++)
        {
            var v = new Vector3(Rng.Range(-1.5f, 1.5f), Rng.Range(0.4f, 2.0f), Rng.Range(-1.5f, 1.5f));
            Spawn(center + new Vector3(Rng.Range(-0.3f, 0.3f), 0.05f, Rng.Range(-0.3f, 0.3f)),
                  v, Rng.Range(0.35f, 0.7f), Rng.Range(0.10f, 0.20f), c, glow: false);
        }
    }

    /// <summary>Bright omnidirectional spark pop (checkpoints, finish, impacts).</summary>
    public void Sparks(Vector3 center, int count = 14, Color? color = null)
    {
        var c = color ?? new Color(255, 220, 120);
        for (int i = 0; i < count; i++)
        {
            var v = new Vector3(Rng.Range(-4f, 4f), Rng.Range(1f, 5f), Rng.Range(-4f, 4f));
            Spawn(center, v, Rng.Range(0.3f, 0.75f), Rng.Range(0.08f, 0.18f), c, glow: true);
        }
    }

    public void Update(float dt)
    {
        for (int i = 0; i < Max; i++)
        {
            if (!_ps[i].Alive) continue;
            _ps[i].Life -= dt;
            if (_ps[i].Life <= 0f) { _ps[i].Alive = false; continue; }

            _ps[i].Vel.Y -= 14f * dt;                 // lighter than actor gravity: debris floats a touch
            _ps[i].Vel *= 1f - 0.9f * dt;             // air drag
            _ps[i].Pos += _ps[i].Vel * dt;
            _ps[i].Yaw += _ps[i].Spin * dt;
        }
    }

    public void Draw(GeometryRenderer r)
    {
        for (int i = 0; i < Max; i++)
        {
            if (!_ps[i].Alive) continue;
            float k = MathHelper.Clamp(_ps[i].Life / System.MathF.Max(0.001f, _ps[i].MaxLife), 0f, 1f);
            float s = _ps[i].Size * (0.4f + 0.6f * k);          // shrink as they die
            var size = new Vector3(s, s * 0.55f, s);
            if (_ps[i].Glow) r.BoxRotYGlow(_ps[i].Pos, size, _ps[i].Yaw, _ps[i].Color);
            else r.BoxRotY(_ps[i].Pos, size, _ps[i].Yaw, ColorUtil.Shade(_ps[i].Color, 0.35f + 0.65f * k));
        }
    }

    public void Clear()
    {
        for (int i = 0; i < Max; i++) _ps[i].Alive = false;
        _cursor = 0;
    }
}
