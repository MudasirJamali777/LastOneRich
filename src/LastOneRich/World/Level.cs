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
    public Func<Actor, bool> ShieldHook; // auction upgrade: consume to ignore one strike

    public List<MovingPlatform> Movers = new();
    public List<RotatorHammer> Hammers = new();
    public List<ConveyorZone> Conveyors = new();
    public List<SlimeZone> Slimes = new();
    public List<WindZone> Winds = new();
    public List<DroneScanner> Drones = new();
    public List<IceZone> Ices = new();
    public double SlimeSpeedMult = 1, SlimeJumpMult = 1;
    public Vector3 SafeZoneCenter, SafeZoneHalf;
    public Vector3 VaultPos, VaultHalf, DepositPos, DepositHalf;
    public int VaultNode = -1, DepositNode = -1;
    public Vector3 MidPoint;

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

        // twist: time limit (bounded by the validator)
        dto.TimeLimit = Mult("timer", "limit", (float)dto.TimeLimit);

        // --- hazards ---
        foreach (var c in dto.Conveyors) { var z = new ConveyorZone(c); z.Speed = Mult("conveyor", "speed", (float)c.Speed); Conveyors.Add(z); }
        foreach (var s in dto.Slimes) { var z = new SlimeZone(s); z.SpeedMult = Mult("slime", "speedMult", (float)s.SpeedMult); if (Slimes.Count == 0) { SlimeSpeedMult = z.SpeedMult; SlimeJumpMult = (double)s.JumpMult; } Slimes.Add(z); }
        foreach (var w in dto.Winds) { var z = new WindZone(w); z.Strength = Mult("wind", "strength", (float)w.Strength); Winds.Add(z); }
        foreach (var h in dto.Hammers) { var z = new RotatorHammer(h); z.ApplyTwist(Mult("hammer", "speed", 1f)); Hammers.Add(z); }
        foreach (var d in dto.Drones) { var z = new DroneScanner(d); z.ApplyTwist(Mult("drone", "speed", 1f)); Drones.Add(z); }
        foreach (var i in dto.IceZones) { var z = new IceZone(i); z.ApplyTwist(Mult("ice", "friction", 1f)); Ices.Add(z); }

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
        if (dto.SpawnGrid != null && dto.SpawnGrid.Length >= 6)
        {
            // [x,y,z, dx,dz, cols] — auto-fill spawn slots for the full cast
            var grid = new List<Vector3>();
            int cols = (int)dto.SpawnGrid[5];
            int row = 0, col = 0;
            for (int i = 0; i < 32; i++)
            {
                grid.Add(new Vector3(
                    dto.SpawnGrid[0] + (col - (cols - 1) * 0.5f) * dto.SpawnGrid[3],
                    dto.SpawnGrid[1],
                    dto.SpawnGrid[2] - row * dto.SpawnGrid[4]));
                col++;
                if (col >= cols) { col = 0; row++; }
            }
            Spawns = grid.ToArray();
        }
        Checkpoints = dto.Checkpoints.Select(s => s.ToVec3()).ToArray();
        FinishCenter = dto.Finish.Pos.ToVec3();
        FinishHalf = dto.Finish.Size.ToVec3() * 0.5f;

        // --- mode config ---
        float maxZ = 0f;
        foreach (var g in dto.Geometry) maxZ = System.MathF.Max(maxZ, g.Pos[2] + g.Size[2] * 0.5f);
        MidPoint = new Vector3(0, 0, maxZ * 0.45f);
        if (dto.SafeZone != null)
        {
            SafeZoneCenter = dto.SafeZone.Pos.ToVec3();
            SafeZoneHalf = dto.SafeZone.Size.ToVec3() * 0.5f;
        }
        if (dto.Vault != null) { VaultPos = dto.Vault.Pos.ToVec3(); VaultHalf = dto.Vault.Size.ToVec3() * 0.5f; }
        if (dto.Deposit != null) { DepositPos = dto.Deposit.Pos.ToVec3(); DepositHalf = dto.Deposit.Size.ToVec3() * 0.5f; }

        // --- waypoint graph (bots) ---
        Graph = WaypointGraph.FromDTO(dto.Waypoints ?? new WaypointsDTO());
        if (Graph.Nodes.Length > 0)
        {
            GoalNode = Graph.Nearest(FinishCenter);
            Graph.ComputeDistances(GoalNode);
            if (dto.Vault != null && dto.Deposit != null)
            {
                VaultNode = Graph.Nearest(dto.Vault.Pos.ToVec3());
                DepositNode = Graph.Nearest(dto.Deposit.Pos.ToVec3());
                Graph.ComputeExtra(VaultNode);
                Graph.ComputeExtra(DepositNode);
            }
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

    public bool DroneNear(Vector3 p, float margin = 1f)
    {
        for (int i = 0; i < Drones.Count; i++)
        {
            var d = p - Drones[i].Pos; d.Y = 0;
            if (d.Length() <= Drones[i].Radius * margin) return true;
        }
        return false;
    }

    /// <summary>False while a drone is close AND its x-sweep is heading toward the bot's x — dash the moment it passes.</summary>
    public bool DroneSafeToCross(Vector3 p, float margin = 2.2f)
    {
        for (int i = 0; i < Drones.Count; i++)
        {
            var dr = Drones[i];
            var d = p - dr.Pos; d.Y = 0;
            if (d.Length() > dr.Radius * margin) continue;
            float dirX = dr.MovingToB ? dr.B.X - dr.A.X : dr.A.X - dr.B.X;
            if (System.MathF.Abs(dirX) < 0.001f) continue;
            float dx = p.X - dr.Pos.X;
            if (System.MathF.Abs(dx) > dr.Radius * 1.5f) continue; // can't reach my lane in time
            bool approaching = System.MathF.Sign(dirX) == System.MathF.Sign(p.X - dr.Pos.X) && System.MathF.Abs(dx) > 0.05f;
            if (approaching) return false;     // closing on my x — hold
        }
        return true;
    }

    public Vector3 CarryFor(Actor a) =>
        a.OnGround && a.GroundMover >= 0 && a.GroundMover < Movers.Count ? Movers[a.GroundMover].FrameDelta : Vector3.Zero;

    public bool MoverNear(Vector3 p, float radius)
    {
        foreach (var m in Movers)
            if (Vector3.DistanceSquared(m.Pos, p) < radius * radius) return true;
        return false;
    }

    double _shrinkT;

    public void Update(float dt, List<Actor> actors)
    {
        Time += dt;

        foreach (var m in Movers) m.Update(dt);
        for (int i = 0; i < Movers.Count; i++) _moverColliders[i].Center = Movers[i].Pos;

        foreach (var h in Hammers) h.Update(dt, actors, this);
        foreach (var d in Drones) d.Update(this);

        // shrinking safe zone (SurvivalZone)
        var sz = Dto.SafeZone;
        if (sz != null && sz.ShrinkTo > 0)
        {
            _shrinkT = System.Math.Min(_shrinkT + dt, sz.ShrinkSeconds);
            float t = (float)(_shrinkT / System.Math.Max(0.1, sz.ShrinkSeconds));
            float half = MathHelper.Lerp((float)(sz.Size[0] * 0.5), (float)sz.ShrinkTo, t);
            SafeZoneHalf = new Vector3(half, SafeZoneHalf.Y, half);
        }

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

            a.InIce = false;
            foreach (var iz in Ices)
                if (iz.Contains(a)) a.InIce = true;

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
        foreach (var d in Drones) d.Draw(r, Time);
        foreach (var iz in Ices) iz.Draw(r, Time);

        // mode set dressing
        if (Dto.SafeZone != null)
        {
            float zonePulse = 0.18f + 0.07f * MathF.Sin(Time * 3f);
            r.Zone(SafeZoneCenter, SafeZoneHalf * 2f, new Color(255, 210, 63), zonePulse);
            r.Box(SafeZoneCenter + new Vector3(0, -SafeZoneHalf.Y + 0.1f, 0), new Vector3(SafeZoneHalf.X * 2, 0.15f, SafeZoneHalf.Z * 2), new Color(255, 210, 63));
        }
        if (Dto.Vault != null)
        {
            r.Zone(VaultPos, VaultHalf * 2f, new Color(255, 157, 46), 0.16f);
            r.Box(VaultPos + new Vector3(0, VaultHalf.Y + 0.3f, 0), new Vector3(1.2f, 0.6f, 1.2f), new Color(255, 210, 63));
        }
        if (Dto.Deposit != null)
        {
            r.Zone(DepositPos, DepositHalf * 2f, new Color(141, 255, 63), 0.16f + 0.05f * MathF.Sin(Time * 4f));
            r.Box(DepositPos + new Vector3(0, DepositHalf.Y + 0.3f, 0), new Vector3(1.2f, 0.6f, 1.2f), new Color(141, 255, 63));
        }
        if (Dto.Button != null)
        {
            var b = Dto.Button;
            var bp = b.Pos.ToVec3();
            r.Zone(new Vector3(bp.X, bp.Y + 0.05f, bp.Z), new Vector3((float)b.Radius * 2f, 0.2f, (float)b.Radius * 2f), new Color(255, 70, 70), 0.14f + 0.05f * MathF.Sin(Time * 5f));
            r.Box(bp + new Vector3(0, 0.6f, 0), new Vector3(2.4f, 1.2f, 2.4f), new Color(60, 64, 84));
            r.Box(bp + new Vector3(0, 1.35f, 0), new Vector3(1.6f, 0.3f, 1.6f), new Color(255, 70, 70));
        }
    }
}
