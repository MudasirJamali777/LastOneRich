using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// Waypoint-following bot (GDD 10): shortest path + risk greed by personality,
/// ranged jump timing, platform waits and centering, stumbles, bumps, stuck recovery.
/// </summary>
public sealed class BotController
{
    public readonly PersonalityDTO P;
    readonly WaypointGraph _graph;
    readonly float _lane;
    float _bandCrossX;      // latched crossing lane for the current drone band
    bool _bandLatched;

    public int _node = -1;
    float _jumpCd, _stumbleT, _stuckT, _lastZ;
    bool _stuckInit;

    public BotController(WaypointGraph graph, PersonalityDTO p, int laneSeed = 0)
    {
        _graph = graph;
        P = p;
        _lane = ((laneSeed % 3) - 1) * 1.2f;
    }

    /// <summary>
    /// Rival top speed. Phys.BotPaceMult is the difficulty knob (Settings ▸ Gameplay): 1.0 at
    /// STANDARD, so the default path is bit-identical to before, and HeadlessSim — which never
    /// assigns it — still validates exactly the same season.
    /// </summary>
    float RunSpeed(Level lv, Actor a) =>
        Phys.MaxSpeed * Phys.BotBaseFactor * (float)P.Pace * (float)Modes.SpeedMultiplierFor(a, lv) * Phys.BotPaceMult;

    /// <summary>Shared steering integration: approach wish velocity, then collide+carry.</summary>
    void IntegrateWish(Actor a, Level lv, Vector2 wish, float dt)
    {
        float accel = a.OnGround ? Phys.Accel : Phys.AirAccel * 0.7f;
        if (a.Stagger > 0) wish = Vector2.Zero;
        if (a.InIce) accel *= 0.4f;
        Phys.Approach(ref a.Vel.X, wish.X, accel, dt);
        Phys.Approach(ref a.Vel.Z, wish.Y, accel, dt);
        if (a.OnGround && wish == Vector2.Zero)
        {
            float fr = Phys.Friction * (a.InIce ? 0.25f : 1f);
            Phys.Approach(ref a.Vel.X, 0f, fr, dt);
            Phys.Approach(ref a.Vel.Z, 0f, fr, dt);
        }
        if (wish != Vector2.Zero)
            a.FaceDir = Vector3.Normalize(new Vector3(wish.X, 0, wish.Y));
        lv.World.Integrate(a, dt, lv.CarryFor(a));
    }

    void UpdateCollectBrain(Actor a, Level lv, float dt)
    {
        a.Stagger = System.MathF.Max(0f, a.Stagger - dt);
        _jumpCd = System.MathF.Max(0f, _jumpCd - dt);
        if (_node < 0) _node = _graph.Nearest(a.Pos);

        bool needBricks = a.Carrying < lv.Dto.CarryCap;
        int goal = needBricks ? lv.VaultNode : lv.DepositNode;
        double[] field = needBricks
            ? _graph.ExtraFields.GetValueOrDefault(lv.VaultNode)
            : _graph.ExtraFields.GetValueOrDefault(lv.DepositNode);

        var nodePos = _graph.Nodes[_node];
        float distH = Vector2.Distance(new Vector2(a.Pos.X, a.Pos.Z), new Vector2(nodePos.X, nodePos.Z));
        if (distH < 1.6f) _node = _graph.BestNext(_node, P.RouteGreed, field);

        // walk directly toward the goal node when adjacent to it
        var target = _graph.Nodes[_node];
        var to = new Vector2(target.X - a.Pos.X, target.Z - a.Pos.Z);
        var wish = to.LengthSquared() < 0.2f ? Vector2.Zero : to / to.Length() * RunSpeed(lv, a);
        IntegrateWish(a, lv, wish, dt);
    }

    void UpdateButtonBrain(Actor a, Level lv, float dt)
    {
        a.Stagger = System.MathF.Max(0f, a.Stagger - dt);
        var b = lv.Dto.Button;
        var bp = b == null ? Vector3.Zero : b.Pos.ToVec3();
        float pressThreshold = 30f + (float)(1.0 - P.RiskTolerance) * 30f; // cautious bots recover sooner
        bool wantPress = a.ForcedOff <= 0 && a.Stamina >= pressThreshold;

        Vector2 wish;
        if (wantPress)
        {
            var to = new Vector2(bp.X - a.Pos.X, bp.Z - a.Pos.Z);
            wish = to.Length() < 1.5f ? Vector2.Zero : to / to.Length() * RunSpeed(lv, a);
        }
        else
        {
            var away = new Vector2(a.Pos.X - bp.X, a.Pos.Z - bp.Z);
            if (away.LengthSquared() < 0.1f) away = new Vector2(0, -1);
            away = away / away.Length() * 10f; // retreat ring around the button
            var spot = new Vector2(bp.X + away.X, bp.Z + away.Y);
            var to = spot - new Vector2(a.Pos.X, a.Pos.Z);
            wish = to.Length() < 1.2f ? Vector2.Zero : to / to.Length() * RunSpeed(lv, a) * 0.9f;
        }
        IntegrateWish(a, lv, wish, dt);
    }

    public void Update(Actor a, Level lv, float dt, List<Actor> others)
    {
        string mode = lv.Dto.Type;
        a.Stagger = System.MathF.Max(0f, a.Stagger - dt);
        a.BumpCd = System.MathF.Max(0f, a.BumpCd - dt);
        _jumpCd = System.MathF.Max(0f, _jumpCd - dt);

        if (mode == "StrikesOut")
        {
            // dodge brain: cross each scan band on the far side of the drone's sweep
            DroneScanner nd = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < lv.Drones.Count; i++)
            {
                var dd = a.Pos - lv.Drones[i].Pos; dd.Y = 0;
                float len = dd.Length();
                if (len < bestD) { bestD = len; nd = lv.Drones[i]; }
            }
            if (nd != null)
            {
                // advance waypoints while in this branch so the march stays on-track
                if (_node < 0) _node = _graph.Nearest(a.Pos);
                var np = _graph.Nodes[_node];
                if (Vector2.Distance(new Vector2(a.Pos.X, a.Pos.Z), new Vector2(np.X, np.Z)) < 1.6f)
                    _node = _graph.BestNext(_node, P.RouteGreed);

                float dz = nd.Pos.Z - a.Pos.Z;
                float dirX = nd.MovingToB ? 1f : -1f;
                float dx = a.Pos.X - nd.Pos.X;
                bool approaching = System.MathF.Abs(dx) > 0.05f
                                   && System.MathF.Abs(dx) < nd.Radius * 1.6f
                                   && System.MathF.Sign(dirX) == System.MathF.Sign(dx);
                bool insideBand = System.MathF.Abs(dz) < nd.Radius + 0.6f;

                if (!insideBand && approaching && System.MathF.Abs(dz) < 9f)
                {
                    IntegrateWish(a, lv, Vector2.Zero, dt);   // let the sweep pass, then cross behind it
                    return;
                }
                if (System.MathF.Abs(dz) >= 12f) _bandLatched = false;   // left the band zone — re-latch next time
                if (System.MathF.Abs(dz) < 9f)
                {
                    if (!_bandLatched)
                    {
                        _bandCrossX = MathHelper.Clamp(nd.Pos.X >= 0 ? -3.2f : 3.2f, -7.4f, 7.4f);
                        _bandLatched = true;
                    }
                    float crossX = _bandCrossX;
                    var np2 = _graph.Nodes[_node];
                    float fwd = System.MathF.Sign(np2.Z - a.Pos.Z);
                    if (fwd == 0) fwd = 1f;
                    float lat = MathHelper.Clamp((crossX - a.Pos.X) * 0.6f, -1f, 1f);
                    var bandWish = new Vector2(lat, fwd);
                    if (bandWish.LengthSquared() > 0.001f)
                    {
                        bandWish.Normalize();
                        IntegrateWish(a, lv, bandWish * RunSpeed(lv, a), dt);
                    }
                    else IntegrateWish(a, lv, Vector2.Zero, dt);
                    return;
                }
            }
        }

        if (a.Finished)
        {
            a.CelebrateHop -= dt;
            if (a.OnGround && a.CelebrateHop <= 0) { a.Vel.Y = 5.5f; a.CelebrateHop = 1.1f; }
            Phys.Approach(ref a.Vel.X, 0f, 10f, dt);
            Phys.Approach(ref a.Vel.Z, 0f, 10f, dt);
            lv.World.Integrate(a, dt, lv.CarryFor(a));
            return;
        }

        // ---------- mode brains (GDD §10: behavior per level type) ----------
        if (mode == "SurvivalZone")
        {
            a.Stagger = System.MathF.Max(0f, a.Stagger - dt);
            var to = new Vector2(lv.SafeZoneCenter.X - a.Pos.X, lv.SafeZoneCenter.Z - a.Pos.Z);
            var szWish = to.LengthSquared() < 0.3f
                ? Vector2.Zero
                : to / to.Length() * (float)(Phys.MaxSpeed * Phys.BotBaseFactor * P.Pace * Modes.SpeedMultiplierFor(a, lv) * Phys.BotPaceMult);
            IntegrateWish(a, lv, szWish, dt);
            return;
        }
        if (mode == "FinaleButton")
        {
            UpdateButtonBrain(a, lv, dt);
            return;
        }
        if (mode == "ScoreCollect")
        {
            UpdateCollectBrain(a, lv, dt);
            return;
        }

        if (_node < 0) _node = _graph.Nearest(a.Pos);

        var nodePos = _graph.Nodes[_node];
        var flags = _graph.Flags[_node];
        float distH = Vector2.Distance(new Vector2(a.Pos.X, a.Pos.Z), new Vector2(nodePos.X, nodePos.Z));
        if (distH > 30f) _node = _graph.Nearest(a.Pos); // respawned far away — re-anchor
        nodePos = _graph.Nodes[_node];
        flags = _graph.Flags[_node];
        distH = Vector2.Distance(new Vector2(a.Pos.X, a.Pos.Z), new Vector2(nodePos.X, nodePos.Z));
        bool arrived = distH < 1.5f && System.MathF.Abs(nodePos.Y - (a.Pos.Y - Actor.HalfY)) < 1.8f;

        float speed = RunSpeed(lv, a);
        float jumpRange = speed * (2f * Phys.JumpVel / Phys.Gravity) * 0.95f; // jump reach (5% safety; AABB tolerance covers the rest)

        Vector2 wish;
        bool jumpNow = false;
        bool hold = false;

        if (arrived)
        {
            // choose the next hop
            if (flags.Contains("waitPlat") && !lv.MoverNear(a.Pos, 4.5f))
            {
                wish = HoldWish(a, lv, nodePos);
                hold = true;
            }
            else
            {
                _node = _graph.BestNext(_node, P.RouteGreed);
                nodePos = _graph.Nodes[_node];
                flags = _graph.Flags[_node];
                var d = SeekWish(a, nodePos, flags, speed, jumpRange, lv, ref jumpNow, ref hold);
                wish = d;
            }
        }
        else
        {
            var d = SeekWish(a, nodePos, flags, speed, jumpRange, lv, ref jumpNow, ref hold);
            wish = d;
        }

        // jump execution
        if (jumpNow && a.OnGround && _jumpCd <= 0 && a.Stagger <= 0)
        {
            a.Vel.Y = Phys.JumpVel * (a.InSlime ? (float)lv.SlimeJumpMult : 1f);
            _jumpCd = 0.35f;
        }

        // integrate steering
        float accel = a.OnGround ? Phys.Accel : Phys.AirAccel * 0.7f;
        if (a.Stagger > 0) wish = Vector2.Zero;
        // holding position on a moving platform: kill momentum fast so receding edges
        // can't slide out from under us (landing coast + stumble coast both covered)
        if (hold && a.OnGround && a.GroundMover >= 0) accel = 90f;
        Phys.Approach(ref a.Vel.X, wish.X, accel, dt);
        Phys.Approach(ref a.Vel.Z, wish.Y, accel, dt);
        if (a.OnGround && wish == Vector2.Zero)
        {
            Phys.Approach(ref a.Vel.X, 0f, Phys.Friction, dt);
            Phys.Approach(ref a.Vel.Z, 0f, Phys.Friction, dt);
        }
        if (wish != Vector2.Zero)
            a.FaceDir = Vector3.Normalize(new Vector3(wish.X, 0, wish.Y));

        // personality stumble
        _stumbleT -= dt * (float)System.Math.Max(0.6, P.Pace);
        if (_stumbleT <= 0)
        {
            _stumbleT = Rng.Range(11f, 24f);
            if (Rng.Chance(0.75f)) a.Stagger = Rng.Range(0.35f, 0.8f);
        }

        // aggression: shoulder-bump whoever is just ahead (kept gentle so it stays comedic)
        if (P.Aggression > 0.5 && a.BumpCd <= 0)
        {
            foreach (var o in others)
            {
                if (o == a || o.Finished) continue;
                var d = o.Pos - a.Pos;
                if (d.LengthSquared() < 1.5f * 1.5f && d.Z > 0.2f)
                {
                    o.Vel += Vector3.Normalize(new Vector3(d.X, 0, d.Z)) * 1.8f;
                    o.Stagger = System.MathF.Max(o.Stagger, 0.12f);
                    a.BumpCd = 4f + Rng.Float() * 2f;
                    break;
                }
            }
        }

        // stuck recovery — never while riding a platform (it carries us, progress oscillates)
        if (a.OnGround && a.GroundMover < 0)
        {
            if (!_stuckInit) { _stuckInit = true; _lastZ = a.Pos.Z; _stuckT = 0; }
            if (a.Pos.Z - _lastZ > 0.4f) { _lastZ = a.Pos.Z; _stuckT = 0; }
            else _stuckT += dt;
            if (_stuckT > 1.5f)
            {
                _node = _graph.Nearest(a.Pos);
                a.Pos = _graph.Nodes[_node] + new Vector3(0, Actor.HalfY + 0.1f, 0);
                a.Vel = Vector3.Zero;
                _stuckT = 0;
                _lastZ = a.Pos.Z;
            }
        }

        lv.World.Integrate(a, dt, lv.CarryFor(a));
    }

    /// <summary>Steer toward the current node. Jump when a flagged node enters honest jump range;
    /// hold position (or re-center on the platform we're riding) while it's out of range.</summary>
    Vector2 SeekWish(Actor a, Vector3 nodePos, HashSet<string> flags, float speed, float jumpRange, Level lv, ref bool jumpNow, ref bool hold)
    {
        var to = new Vector2(nodePos.X - a.Pos.X, nodePos.Z - a.Pos.Z);
        float dist = to.Length();
        bool isJumpNode = flags.Contains("j");

        if (isJumpNode)
        {
            if (dist <= jumpRange * 0.98f)
            {
                jumpNow = true;
                return dist > 0.05f ? to / dist * speed : Vector2.Zero;
            }
            // too far to clear — wait; on a platform, drift toward its target side
            hold = true;
            return HoldWish(a, lv, nodePos);
        }

        var t2 = nodePos;
        t2.X += _lane;
        t2.X = MathHelper.Clamp(t2.X, -12f, 12f);
        to = new Vector2(t2.X - a.Pos.X, t2.Z - a.Pos.Z);
        dist = to.Length();
        if (dist < 0.05f) return Vector2.Zero;
        return to / dist * speed;
    }

    /// <summary>Hold: aboard a moving platform, drift to its target-facing side so the next
    /// jump can reach. On solid ground, creep toward the target until jump range is met.</summary>
    Vector2 HoldWish(Actor a, Level lv, Vector3 targetPos)
    {
        var toT = new Vector2(targetPos.X - a.Pos.X, targetPos.Z - a.Pos.Z);
        if (a.OnGround && a.GroundMover >= 0 && a.GroundMover < lv.Movers.Count)
        {
            var m = lv.Movers[a.GroundMover];
            var toTm = new Vector2(targetPos.X - m.Pos.X, targetPos.Z - m.Pos.Z);
            if (toTm != Vector2.Zero)
            {
                toTm.Normalize();
                float half = (m.Size.X + m.Size.Z) * 0.5f * 0.85f;
                var spot = new Vector2(m.Pos.X + toTm.X * half, m.Pos.Z + toTm.Y * half);
                var to = spot - new Vector2(a.Pos.X, a.Pos.Z);
                if (to.Length() > 0.4f) return to / to.Length() * Phys.MaxSpeed * 0.4f;
                return Vector2.Zero;
            }
        }
        if (toT.Length() > 0.5f) return toT / toT.Length() * Phys.MaxSpeed * Phys.BotBaseFactor * 0.5f;
        return Vector2.Zero;
    }
}
