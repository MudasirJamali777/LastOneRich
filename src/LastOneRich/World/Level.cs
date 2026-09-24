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
    public Color SkyColor = ColorPalette.Sky;
    public Action<string> Sfx;
    public Func<Actor, bool> ShieldHook; // auction upgrade: consume to ignore one strike

    public List<MovingPlatform> Movers = new();
    public List<RotatorHammer> Hammers = new();
    public List<ConveyorZone> Conveyors = new();
    public List<SlimeZone> Slimes = new();
    public List<WindZone> Winds = new();
    public List<DroneScanner> Drones = new();
    public List<IceZone> Ices = new();
    public List<BreakTile> Tiles = new();

    /// <summary>
    /// Priority 6: fired when a pane shatters (center, size, tint) so the presentation layer can
    /// throw shards without the World layer ever touching the renderer. GameplayState subscribes;
    /// HeadlessSim leaves it null and runs the identical simulation silently.
    /// </summary>
    public Action<Vector3, Vector3, Color> ShatterFx;
    public double SlimeSpeedMult = 1, SlimeJumpMult = 1;
    public Vector3 SafeZoneCenter, SafeZoneHalf;
    public Vector3 VaultPos, VaultHalf, DepositPos, DepositHalf;
    public int VaultNode = -1, DepositNode = -1;
    public Vector3 MidPoint;

    // graphics pass: load-baked static mesh + mode fallback + button occupancy (for the glow)
    public bool FallbackMode;
    public bool ButtonDown;
    List<VertexPositionNormalColor> _staticVerts;
    BakedMesh _staticMesh;
    static readonly System.Collections.Generic.HashSet<string> KnownTypes = new()
    { "Race", "SurvivalZone", "StrikesOut", "ScoreCollect", "FinaleButton" };

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

        // Finalization guard: unimplemented/unknown modes fall back to Race rules
        // with a warning HUD instead of misbehaving (data errors degrade, never crash).
        if (!KnownTypes.Contains(Dto.Type))
        {
            GameLog.Log($"[level] '{Dto.Name}': unknown mode '{Dto.Type}' — using Race rules");
            FallbackMode = true;
            Dto.Type = "Race";
        }

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

        // --- breakable glass panes (Priority 6) ---
        BuildBreakTiles(dto, Mult("glass", "crackTime", 1f));

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

        BakeStaticGeometry();
    }

    // ---------- Priority 6: breakable glass path ----------

    /// <summary>
    /// Build the glass panes and decide which one of each row is safe.
    ///
    /// The shuffle is seeded from <c>glassSeed</c> (level JSON) and NOT from the shared
    /// <see cref="Rng"/>, on purpose: the pattern must be identical in the game, in HeadlessSim
    /// and across a mid-round restart, and must not consume draws from the gameplay RNG stream
    /// (which would shift every bot stumble downstream of it). Hand-authored <c>safe</c> values
    /// always win; the shuffle only fills in the rows that left it null.
    ///
    /// Every row is guaranteed at least one safe pane — a row of pure fakes would be an
    /// unwinnable course, so the invariant is enforced here rather than trusted to the data.
    /// </summary>
    void BuildBreakTiles(LevelDTO dto, float crackMult)
    {
        if (dto.BreakTiles == null || dto.BreakTiles.Count == 0) return;

        // group pane indices by row
        var rows = new Dictionary<int, List<int>>();
        for (int i = 0; i < dto.BreakTiles.Count; i++)
        {
            int row = dto.BreakTiles[i].Row;
            if (!rows.TryGetValue(row, out var list)) rows[row] = list = new List<int>();
            list.Add(i);
        }

        var safe = new bool[dto.BreakTiles.Count];
        var rng = new Random(dto.GlassSeed);

        foreach (var kv in rows.OrderBy(k => k.Key))
        {
            var idx = kv.Value;
            bool anyAuthored = idx.Any(i => dto.BreakTiles[i].Safe.HasValue);

            if (anyAuthored)
            {
                foreach (int i in idx) safe[i] = dto.BreakTiles[i].Safe ?? false;
                // invariant: never ship a row nobody can cross
                if (!idx.Any(i => safe[i])) safe[idx[0]] = true;
            }
            else
            {
                safe[idx[rng.Next(idx.Count)]] = true;
            }
        }

        for (int i = 0; i < dto.BreakTiles.Count; i++)
        {
            var t = new BreakTile(dto.BreakTiles[i], safe[i], dto.BreakTiles[i].Row);
            t.ApplyTwist(crackMult);
            t.Attach(World);
            Tiles.Add(t);
        }
    }

    /// <summary>Index of the pane supporting this actor, or -1. Linear scan: courses hold ~50 panes.</summary>
    public int TileAt(Actor a)
    {
        for (int i = 0; i < Tiles.Count; i++)
            if (Tiles[i].IsSolid && Tiles[i].Supports(a)) return i;
        return -1;
    }

    /// <summary>
    /// Bot oracle: is the pane under/ahead of this position a safe one? Bots consult this through
    /// their PuzzleSkill so smart rivals "remember" the route and dim ones guess — the knowledge
    /// gate lives in <see cref="BotController"/>, not here.
    /// </summary>
    public bool TileSafeAt(Vector3 p, float lookAheadZ)
    {
        for (int i = 0; i < Tiles.Count; i++)
        {
            var t = Tiles[i];
            if (!t.IsSolid) continue;
            if (System.MathF.Abs(p.X - t.Pos.X) > t.Size.X * 0.5f) continue;
            float dz = t.Pos.Z - p.Z;
            if (dz < -t.Size.Z * 0.5f || dz > lookAheadZ) continue;
            return t.Safe;
        }
        return true;   // nothing ahead to judge — don't stall the bot
    }

    /// <summary>
    /// Tick every pane and apply weight. Runs inside <see cref="Update"/> BEFORE the fall-out check,
    /// so a pane that shatters this frame drops its rider on this very frame rather than the next.
    /// </summary>
    void UpdateBreakTiles(float dt, List<Actor> actors)
    {
        if (Tiles.Count == 0) return;

        // 1) weight: who is standing on what
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            a.TileIdx = -1;
            if (a.RoundOut) continue;
            if (!a.OnGround) continue;

            for (int t = 0; t < Tiles.Count; t++)
            {
                var tile = Tiles[t];
                if (!tile.IsSolid || !tile.Supports(a)) continue;
                a.TileIdx = t;
                bool firstTouch = !tile.Touched;
                bool wasSolid = tile.State == BreakTile.TileState.Solid;
                tile.Press(a);
                // One cue per pane, not per frame: a creak when a fake pane starts to go, a clean
                // ring when a safe pane takes the weight. Only the PLAYER's own steps ring, or a
                // pack of bots crossing behind you would drown the level in chimes.
                if (wasSolid && !tile.Safe) Sfx?.Invoke("glass_crack");
                else if (firstTouch && tile.Safe && a.IsPlayer) Sfx?.Invoke("glass_land");
                break;
            }
        }

        // 2) advance each pane's clock; attribute the break to whoever was standing on it
        for (int t = 0; t < Tiles.Count; t++)
        {
            var tile = Tiles[t];
            bool shattered = tile.Update(dt);

            if (tile.JustReformed) Sfx?.Invoke("glass_reform");
            if (!shattered) continue;

            for (int i = 0; i < actors.Count; i++)
                if (actors[i].TileIdx == t) actors[i].TilesBroken++;

            Sfx?.Invoke("glass_break");
            ShatterFx?.Invoke(tile.Pos, tile.Size, tile.Tint);
        }
    }

    // ---------- static bake (graphics pass): geometry, crowd, floor grid, edge curbs, hazard decals ----------
    // Built once at load as pure vertex data; the renderer uploads it on first draw.

    void BakeStaticGeometry()
    {
        _staticVerts = new List<VertexPositionNormalColor>(1 << 14);

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
                GeometryRenderer.EmitRamp(_staticVerts, g.Pos.ToVec3(), g.Size.ToVec3(), dx, dz, col);
            }
            else
            {
                GeometryRenderer.EmitBox(_staticVerts, g.Pos.ToVec3(), g.Size.ToVec3(), col);
            }
        }

        foreach (var c in _crowd) GeometryRenderer.EmitBox(_staticVerts, c.c, c.s, c.col);

        BakeFloorGridAndCurbs();
        BakeHazardDecals();
    }

    /// <summary>Subtle 4-unit floor grid + amber edge curbs on every big walkable slab (depth cues).</summary>
    void BakeFloorGridAndCurbs()
    {
        foreach (var g in Dto.Geometry)
        {
            if (g.Collide == false) continue;
            if (string.Equals(g.Kind, "ramp", StringComparison.OrdinalIgnoreCase)) continue;
            var s = g.Size.ToVec3(); var c = g.Pos.ToVec3();
            if (s.X < 8 || s.Z < 8) continue;                    // only real floor slabs
            float top = c.Y + s.Y * 0.5f;
            if (top > 2.5f || top < -5f) continue;               // walkable height band only

            // grid lines (both axes), just above the surface
            var grid = ColorPalette.GridLine;
            for (float x = c.X - s.X * 0.5f + 4f; x < c.X + s.X * 0.5f - 0.2f; x += 4f)
                GeometryRenderer.EmitBox(_staticVerts, new Vector3(x, top + 0.012f, c.Z), new Vector3(0.07f, 0.02f, s.Z - 0.2f), grid);
            for (float z = c.Z - s.Z * 0.5f + 4f; z < c.Z + s.Z * 0.5f - 0.2f; z += 4f)
                GeometryRenderer.EmitBox(_staticVerts, new Vector3(c.X, top + 0.012f, z), new Vector3(s.X - 0.2f, 0.02f, 0.07f), grid);

            // edge-warning curbs on the slab perimeter
            var curb = ColorPalette.EdgeWarn;
            float cw = 0.34f, ch = 0.05f;
            GeometryRenderer.EmitBox(_staticVerts, new Vector3(c.X, top + ch * 0.5f, c.Z - s.Z * 0.5f + cw * 0.5f), new Vector3(s.X, ch, cw), curb);
            GeometryRenderer.EmitBox(_staticVerts, new Vector3(c.X, top + ch * 0.5f, c.Z + s.Z * 0.5f - cw * 0.5f), new Vector3(s.X, ch, cw), curb);
            GeometryRenderer.EmitBox(_staticVerts, new Vector3(c.X - s.X * 0.5f + cw * 0.5f, top + ch * 0.5f, c.Z), new Vector3(cw, ch, s.Z - cw * 2f), curb);
            GeometryRenderer.EmitBox(_staticVerts, new Vector3(c.X + s.X * 0.5f - cw * 0.5f, top + ch * 0.5f, c.Z), new Vector3(cw, ch, s.Z - cw * 2f), curb);
        }
    }

    /// <summary>Static hazard decals: red warning pads under hammers, blue flow arrows in wind corridors.</summary>
    void BakeHazardDecals()
    {
        foreach (var h in Hammers)
        {
            float reach = h.ArmLength + 1.2f;
            GeometryRenderer.EmitBox(_staticVerts, new Vector3(h.Pivot.X, 0.03f, h.Pivot.Z),
                new Vector3(reach * 2f, 0.05f, reach * 2f), new Color(96, 18, 18));
        }
        foreach (var w in Winds)
        {
            var d = w.Dir; d.Y = 0;
            if (d.LengthSquared() < 0.001f) continue;
            d.Normalize();
            float yaw = MathF.Atan2(d.X, d.Z);                  // heading of the flow
            for (int k = -1; k <= 1; k++)
            {
                var baseP = new Vector3(w.Pos.X, 0.045f, w.Pos.Z) + d * (k * 5f);
                GeometryRenderer.EmitBoxRotY(_staticVerts, baseP + new Vector3(d.Z, 0, -d.X) * 0.45f + d * 0.35f,
                    new Vector3(0.34f, 0.02f, 1.5f), yaw + 2.55f, ColorPalette.WindBlue);
                GeometryRenderer.EmitBoxRotY(_staticVerts, baseP - new Vector3(d.Z, 0, -d.X) * 0.45f + d * 0.35f,
                    new Vector3(0.34f, 0.02f, 1.5f), yaw - 2.55f, ColorPalette.WindBlue);
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

        // Priority 6: panes take weight and shatter BEFORE the fall-out sweep below, so an actor
        // whose pane just vanished starts falling on this frame instead of hovering for one tick.
        UpdateBreakTiles(dt, actors);

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
        a.PrevPos = a.Pos; // teleport: keep render interpolation seamless
        a.LastGroundY = cp.Y;
        a.Vel = Vector3.Zero;
        a.InSlime = false;
        a.Stagger = 0.25f;
        a.PenaltyAccum += Dto.RespawnPenalty;
        Sfx?.Invoke("splash");
    }

    // ------------------------------------------------------------------ draw

    public void Draw(GeometryRenderer r)
    {
        // static world: one baked vertex buffer (geometry + crowd + grid + curbs + decals)
        if (_staticMesh == null && _staticVerts != null)
        {
            _staticMesh = r.Bake(_staticVerts);
            _staticVerts = null; // CPU copy no longer needed
        }
        r.DrawBaked(_staticMesh);

        foreach (var cp in Checkpoints)
            r.Zone(cp + new Vector3(0, 0.05f, 0), new Vector3(2.8f, 0.12f, 2.8f), new Color(63, 210, 255), 0.35f);

        float pulse = 0.22f + 0.10f * MathF.Sin(Time * 5f);
        r.Zone(FinishCenter, FinishHalf * 2f, ColorPalette.Safe, pulse); // finish = green = safe

        foreach (var s in Slimes) s.Draw(r, Time);
        foreach (var w in Winds) w.Draw(r, Time);
        foreach (var c in Conveyors) c.Draw(r, Time);
        foreach (var h in Hammers) h.Draw(r);
        foreach (var m in Movers) r.Box(m.Pos, m.Size, m.Tint);
        foreach (var d in Drones) d.Draw(r, Time);
        foreach (var iz in Ices) iz.Draw(r, Time);
        foreach (var t in Tiles) t.Draw(r, Time);

        // mode set dressing
        if (Dto.SafeZone != null)
        {
            float zonePulse = 0.18f + 0.07f * MathF.Sin(Time * 3f);
            r.Zone(SafeZoneCenter, SafeZoneHalf * 2f, ColorPalette.Safe, zonePulse);
            r.Box(SafeZoneCenter + new Vector3(0, -SafeZoneHalf.Y + 0.1f, 0), new Vector3(SafeZoneHalf.X * 2, 0.15f, SafeZoneHalf.Z * 2), ColorPalette.Safe);
        }
        if (Dto.Vault != null)
        {
            r.Zone(VaultPos, VaultHalf * 2f, new Color(255, 157, 46), 0.16f);
            r.BoxGlow(VaultPos + new Vector3(0, VaultHalf.Y + 0.3f, 0), new Vector3(1.2f, 0.6f, 1.2f), ColorPalette.CashGold);
        }
        if (Dto.Deposit != null)
        {
            r.Zone(DepositPos, DepositHalf * 2f, ColorPalette.Safe, 0.16f + 0.05f * MathF.Sin(Time * 4f));
            r.BoxGlow(DepositPos + new Vector3(0, DepositHalf.Y + 0.3f, 0), new Vector3(1.2f, 0.6f, 1.2f), ColorPalette.Safe);
        }
        if (Dto.Button != null)
        {
            var b = Dto.Button;
            var bp = b.Pos.ToVec3();
            r.Zone(new Vector3(bp.X, bp.Y + 0.05f, bp.Z), new Vector3((float)b.Radius * 2f, 0.2f, (float)b.Radius * 2f), new Color(255, 70, 70), 0.14f + 0.05f * MathF.Sin(Time * 5f));
            r.Box(bp + new Vector3(0, 0.6f, 0), new Vector3(2.4f, 1.2f, 2.4f), new Color(60, 64, 84));
            // bright green when free, hot red while pressed — readable state at a glance
            r.BoxGlow(bp + new Vector3(0, 1.35f, 0), new Vector3(1.6f, 0.3f, 1.6f), ButtonDown ? new Color(255, 60, 60) : new Color(0, 255, 68));
        }
    }
}
