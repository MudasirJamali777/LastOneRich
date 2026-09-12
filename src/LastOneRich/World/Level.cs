using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// Runtime level: built entirely from JSON (GDD 13.2) — geometry, hazards, spawns,
/// finish trigger, waypoints. Supports twist modifiers and renders with primitives.
/// </summary>
public sealed class Level
{
    public LevelDTO Dto;
    public CollisionWorld World = new();
    public WaypointGraph Graph;
    public Vector3[] Spawns = System.Array.Empty<Vector3>();
    public Vector3[] Checkpoints = System.Array.Empty<Vector3>();
    public Vector3 FinishCenter, FinishHalf;
    public Color SkyColor = new(19, 20, 41);
    public Action<string> Sfx;

    public List<MovingPlatform> Movers = new();
    public List<RotatorHammer> Hammers = new();
    public List<ConveyorZone> Conveyors = new();
    public List<SlimeZone> Slimes = new();
    public List<WindZone> Winds = new();
    public double SlimeSpeedMult = 1, SlimeJumpMult = 1;

    public float Time;
    readonly List<Collider> _moverColliders = new();
    readonly List<(Vector3 c, Vector3 s, Color col)> _crowd = new();
    public int GoalNode;

    public static Level Load(string id, IEnumerable<TwistDTO> twists = null, Action<string> sfx = null) =>
        new(Json.Load<LevelDTO>($"data/levels/{id}.json"), twists, sfx);

    public Level(LevelDTO dto, IEnumerable<TwistDTO> twists = null, Action<string> sfx = null)
    {
        Dto = dto;
        Sfx = sfx;

        float Mult(string target, string param, float baseVal)
        {
            float m = 1f;
            if (twists != null)
                foreach (var t in twists)
                    foreach (var e in t.Effects)
                        if (string.Equals(e.Target, target, StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(e.Param, param, StringComparison.OrdinalIgnoreCase))
                            m *= (float)e.Mult;
            return baseVal * m;
        }

        // --- geometry + colliders ---
        foreach (var g in dto.Geometry)
        {
            var pos = g.Pos.ToVec3();
            var size = g.Size.ToVec3();
            if (string.Equals(g.Kind, "ramp", StringComparison.OrdinalIgnoreCase))
            {
                int dx = 0, dz = 0;
                switch ((g.Dir ?? "z+").ToLowerInvariant())
                {
                    case "x+": dx = 1; break;
                    case "x-": dx = -1; break;
                    case "z-": dz = -1; break;
                    default: dz = 1; break;
                }
                if (g.Collide) World.AddRamp(pos, size, dx, dz);
            }
            else if (g.Collide)
            {
                World.AddBox(pos, size);
            }
        }

        // --- hazards ---
        foreach (var c in dto.Conveyors) { var z = new ConveyorZone(c); z.Speed = Mult("conveyor", "speed", (float)c.Speed); Conveyors.Add(z); }
        foreach (var s in dto.Slimes) { var z = new SlimeZone(s); z.SpeedMult = Mult("slime", "speedMult", (float)s.SpeedMult); if (Slimes.Count == 0) { SlimeSpeedMult = z.SpeedMult; SlimeJumpMult = (double)s.JumpMult; } Slimes.Add(z); }
        foreach (var w in dto.Winds) { var z = new WindZone(w); z.Strength = Mult("wind", "strength", (float)w.Strength); Winds.Add(z); }
        foreach (var h in dto.Hammers) { var z = new RotatorHammer(h); z.ApplyTwist(Mult("hammer", "speed", 1f)); Hammers.Add(z); }

        // --- moving platforms + their dynamic colliders ---
        foreach (var m in dto.Movers)
        {
            var mp = new MovingPlatform(m);
            Movers.Add(mp);
            var col = new Collider { Center = mp.Pos, Half = mp.Size * 0.5f, Mover = Movers.Count - 1 };
            _moverColliders.Add(col);
            World.Colliders.Add(col);
        }

        // --- points of interest ---
        Spawns = dto.Spawns.Select(s => s.ToVec3()).ToArray();
        Checkpoints = dto.Checkpoints.Select(s => s.ToVec3()).ToArray();
        FinishCenter = dto.Finish.Pos.ToVec3();
        FinishHalf = dto.Finish.Size.ToVec3() * 0.5f;

        // --- waypoint graph (bots) ---
        Graph = WaypointGraph.FromDTO(dto.Waypoints ?? new WaypointsDTO());
        if (Graph.Nodes.Length > 0)
        {
            GoalNode = Graph.Nearest(FinishCenter);
            Graph.ComputeDistances(GoalNode);
        }

        // --- cosmetic crowd (seeded, GDD 16: spectacle from primitives) ---
        if (dto.CosmeticCrowd)
        {
            var standCol = new Color(30, 32, 52);
            Color[] fans = { new(255, 79, 216), new(255, 210, 63), new(63, 210, 255), new(141, 255, 63), new(255, 255, 255), new(255, 120, 90) };
            var rng = new Random(987654);
            for (float z = -8; z <= 252; z += 5.5f)
            {
                foreach (int side in new[] { -1, 1 })
                {
                    for (int tier = 0; tier < 3; tier++)
                    {
                        var c = new Vector3(side * (15f + tier * 2.4f), 1.1f + tier * 1.8f, z);
                        _crowd.Add((c, new Vector3(2.2f, 1.8f, 5f), standCol));
                        for (int f = 0; f < 2; f++)
                        {
                            var fc = fans[rng.Next(fans.Length)];
                            _crowd.Add((c + new Vector3((float)(rng.NextDouble() - 0.5) * 1.6f, 1.25f, (float)(rng.NextDouble() - 0.5) * 3.8f),
                                        new Vector3(0.55f, 0.55f, 0.55f), fc));
                        }
                    }
                }
            }
        }
    }

    public Vector3 CarryFor(Actor a) =>
        a.OnGround && a.GroundMover >= 0 && a.GroundMover < Movers.Count ? Movers[a.GroundMover].FrameDelta : Vector3.Zero;

    public bool MoverNear(Vector3 p, float radius)
    {
        foreach (var m in Movers)
            if (Vector3.DistanceSquared(m.Pos, p) < radius * radius) return true;
        return false;
    }

    public void Update(float dt, List<Actor> actors)
    {
        Time += dt;

        foreach (var m in Movers) m.Update(dt);
        for (int i = 0; i < Movers.Count; i++) _moverColliders[i].Center = Movers[i].Pos;

        foreach (var h in Hammers) h.Update(dt, actors, this);

        foreach (var a in actors)
        {
            foreach (var c in Conveyors) c.Effect(a, dt);
            foreach (var w in Winds) w.Effect(a, dt, Time);

            a.InSlime = false;
            foreach (var s in Slimes)
            {
                if (s.Contains(a))
                {
                    a.InSlime = true;
                    SlimeSpeedMult = s.SpeedMult;
                    SlimeJumpMult = s.JumpMult;
                }
            }

            // checkpoints (assumed ordered along the course)
            int next = a.CheckpointIdx + 1;
            if (next < Checkpoints.Length && a.Pos.Z >= Checkpoints[next].Z) a.CheckpointIdx = next;

            // fell off the world → respawn at last checkpoint (forgiving, GDD 4)
            if (a.Pos.Y < (float)Dto.KillY) Respawn(a);
        }
    }

    public static Action<Actor, Vector3> RespawnHook;

    public void Respawn(Actor a)
    {
        RespawnHook?.Invoke(a, a.Pos);
        var cp = Checkpoints.Length > 0 ? Checkpoints[System.Math.Min(a.CheckpointIdx, Checkpoints.Length - 1)] : Spawns[0];
        a.Pos = cp + new Vector3(0, Actor.HalfY + 0.08f, 0);
        a.Vel = Vector3.Zero;
        a.InSlime = false;
        a.Stagger = 0.25f;
        a.PenaltyAccum += Dto.RespawnPenalty;
        Sfx?.Invoke("splash");
    }

    // ------------------------------------------------------------------ draw

    public void Draw(GeometryRenderer r)
    {
        foreach (var g in Dto.Geometry)
        {
            var col = ColorUtil.Parse(g.Color);
            if (string.Equals(g.Kind, "ramp", StringComparison.OrdinalIgnoreCase))
            {
                int dx = 0, dz = 0;
                switch ((g.Dir ?? "z+").ToLowerInvariant())
                {
                    case "x+": dx = 1; break;
                    case "x-": dx = -1; break;
                    case "z-": dz = -1; break;
                    default: dz = 1; break;
                }
                r.Ramp(g.Pos.ToVec3(), g.Size.ToVec3(), dx, dz, col);
            }
            else
            {
                r.Box(g.Pos.ToVec3(), g.Size.ToVec3(), col);
            }
        }

        foreach (var c in _crowd) r.Box(c.c, c.s, c.col);

        foreach (var cp in Checkpoints)
            r.Zone(cp + new Vector3(0, 0.05f, 0), new Vector3(2.8f, 0.12f, 2.8f), new Color(63, 210, 255), 0.35f);

        float pulse = 0.22f + 0.10f * MathF.Sin(Time * 5f);
        r.Zone(FinishCenter, FinishHalf * 2f, new Color(255, 210, 63), pulse);

        foreach (var s in Slimes) s.Draw(r, Time);
        foreach (var w in Winds) w.Draw(r, Time);
        foreach (var c in Conveyors) c.Draw(r, Time);
        foreach (var h in Hammers) h.Draw(r);
        foreach (var m in Movers) r.Box(m.Pos, m.Size, m.Tint);
    }
}
