using LastOneRich.Core;
using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// BreakTile (GDD §11.1, Priority 6): a glass pane that shatters a beat after it takes weight.
///
/// Lifecycle — Solid → Cracking → Shattered → (optional) Reforming → Solid:
///
///   Solid       collidable; the safe/fake identity is hidden until someone commits to it
///   Cracking    triggered by an actor standing on it; <see cref="CrackTime"/> grace before it drops
///   Shattered   collider removed; anyone on top falls through to the kill plane
///   Reforming   only when <see cref="ReformTime"/> &gt; 0 — the pane fades back in and re-solidifies
///
/// Only FAKE tiles ever break; SAFE tiles absorb the weight and merely glint. Which is which is
/// decided by the level JSON (<c>safe</c>) or, when absent, by a seeded shuffle per row so the
/// "memory" fantasy holds: the pattern is fixed for the whole round and identical for every
/// contestant, and — because it is seeded off the level id — identical between the game and
/// HeadlessSim. That determinism is what lets bots be told the answer (see <see cref="Level.TileAt"/>)
/// without desyncing the simulation.
///
/// The collider is OWNED by this class: it is added to the CollisionWorld at load and physically
/// removed on shatter, so falling through needs no special-casing inside the physics integrator.
/// </summary>
public sealed class BreakTile
{
    public enum TileState { Solid, Cracking, Shattered, Reforming }

    public Vector3 Pos;          // center of the pane
    public Vector3 Size;         // full extents
    public bool Safe;            // true = never breaks (the correct stepping stone)
    public int Row;              // which rank of the path this pane belongs to (memory grouping)

    public TileState State = TileState.Solid;
    public float CrackTime = 0.45f;   // grace between first contact and the drop
    public float ReformTime;          // 0 = gone for the rest of the round
    public float Timer;               // counts within the current state

    /// <summary>True once anything has stood on this pane (drives the cracked look + bot memory).</summary>
    public bool Touched;

    /// <summary>Set for one frame on the transition into Shattered, so the level can spawn shards once.</summary>
    public bool JustShattered;

    /// <summary>Set for one frame when the pane finishes re-forming (cue + sparkle, once).</summary>
    public bool JustReformed;

    public Color Tint = ColorPalette.SafeTileTeal;

    Collider _collider;           // owned; pulled out of the world while shattered
    CollisionWorld _world;

    public BreakTile(BreakTileDTO dto, bool safe, int row)
    {
        Pos = dto.Pos.ToVec3();
        Size = dto.Size.ToVec3();
        Safe = safe;
        Row = row;
        CrackTime = (float)System.Math.Max(0.05, dto.CrackTime);
        ReformTime = (float)System.Math.Max(0.0, dto.ReformTime);
        Tint = ColorUtil.Parse(dto.Color);
    }

    /// <summary>Twist hook: scales the grace period (harsher = shatters sooner). Clamped so it stays playable.</summary>
    public void ApplyTwist(double mult) => CrackTime = MathHelper.Clamp(CrackTime * (float)mult, 0.12f, 2.0f);

    /// <summary>Register this pane's collider with the level's collision world (called once, at load).</summary>
    public void Attach(CollisionWorld world)
    {
        _world = world;
        _collider = new Collider { Center = Pos, Half = Size * 0.5f };
        _world.Colliders.Add(_collider);
    }

    public bool IsSolid => State is TileState.Solid or TileState.Cracking;

    /// <summary>The pane's top surface — what an actor actually stands on.</summary>
    public float TopY => Pos.Y + Size.Y * 0.5f;

    /// <summary>Far edge of the pane along Z — the lip a runner launches from.</summary>
    public float FarEdgeZ => Pos.Z + Size.Z * 0.5f;

    /// <summary>True when this actor is standing on (or landing squarely onto) the pane.</summary>
    public bool Supports(Actor a)
    {
        float feet = a.Pos.Y - Actor.HalfY;
        if (feet > TopY + 0.30f || feet < TopY - 0.65f) return false;
        return System.MathF.Abs(a.Pos.X - Pos.X) <= Size.X * 0.5f + Actor.HalfXZ * 0.60f
            && System.MathF.Abs(a.Pos.Z - Pos.Z) <= Size.Z * 0.5f + Actor.HalfXZ * 0.60f;
    }

    void Detach()
    {
        if (_collider != null && _world != null) _world.Colliders.Remove(_collider);
        _collider = null;
    }

    void Reattach()
    {
        if (_collider != null || _world == null) return;
        _collider = new Collider { Center = Pos, Half = Size * 0.5f };
        _world.Colliders.Add(_collider);
    }

    /// <summary>
    /// Weight from an actor. Safe panes never crack; a pane already past Solid keeps its own clock
    /// (re-stepping never resets a crack in progress — that would make the hazard dodgeable by hopping).
    /// </summary>
    public void Press(Actor a)
    {
        Touched = true;
        if (Safe || State != TileState.Solid) return;
        State = TileState.Cracking;
        Timer = 0f;
    }

    /// <summary>Advance the pane's own clock. Returns true on the frame it shatters.</summary>
    public bool Update(float dt)
    {
        JustShattered = false;
        JustReformed = false;
        switch (State)
        {
            case TileState.Cracking:
                Timer += dt;
                if (Timer >= CrackTime)
                {
                    State = TileState.Shattered;
                    Timer = 0f;
                    JustShattered = true;
                    Detach();
                    return true;
                }
                break;

            case TileState.Shattered:
                if (ReformTime <= 0f) break;      // gone for good
                Timer += dt;
                if (Timer >= ReformTime)
                {
                    State = TileState.Reforming;
                    Timer = 0f;
                }
                break;

            case TileState.Reforming:
                Timer += dt;
                if (Timer >= 0.6f)                // fade-in beat, then it bears weight again
                {
                    State = TileState.Solid;
                    Timer = 0f;
                    Touched = false;              // a fresh pane carries no scuff to read
                    Reattach();
                    JustReformed = true;
                }
                break;
        }
        return false;
    }

    /// <summary>Reset to the pristine pane (round restart).</summary>
    public void Reset()
    {
        State = TileState.Solid;
        Timer = 0f;
        Touched = false;
        JustShattered = false;
        Reattach();
    }

    // ------------------------------------------------------------------ draw

    /// <summary>
    /// Glass look: a translucent slab plus a bright rim so the pane reads as an edge-lit pane
    /// rather than a floating box. Cracking panes strobe amber (the tell that you must move NOW),
    /// reforming panes fade in from nothing. Shattered panes draw nothing at all.
    /// </summary>
    public void Draw(GeometryRenderer r, float time)
    {
        switch (State)
        {
            case TileState.Shattered:
                // faint frame left behind so the gap is still readable against the void
                r.Zone(new Vector3(Pos.X, TopY, Pos.Z), new Vector3(Size.X, 0.04f, Size.Z), new Color(70, 80, 105), 0.10f);
                return;

            case TileState.Reforming:
            {
                float k = MathHelper.Clamp(Timer / 0.6f, 0f, 1f);
                r.Zone(Pos, Size, Tint, 0.10f + 0.28f * k);
                return;
            }

            case TileState.Cracking:
            {
                // urgency strobe: faster and hotter the closer it is to dropping
                float k = MathHelper.Clamp(Timer / System.MathF.Max(0.001f, CrackTime), 0f, 1f);
                float blink = 0.5f + 0.5f * MathF.Sin(time * (26f + 40f * k));
                var hot = Color.Lerp(ColorPalette.DangerOrange, ColorPalette.DangerRed, k);
                r.Zone(Pos, Size, hot, 0.30f + 0.35f * blink);
                r.BoxGlow(new Vector3(Pos.X, TopY + 0.03f, Pos.Z), new Vector3(Size.X, 0.05f, Size.Z), hot);
                return;
            }

            default:
            {
                // Solid: uniform glass for everyone. A touched-but-held pane keeps a dim scuff so
                // players can read the route others already proved — that is the "memory" payoff.
                float shimmer = 0.16f + 0.04f * MathF.Sin(time * 1.6f + Pos.Z * 0.35f);
                r.Zone(Pos, Size, Tint, Touched ? shimmer + 0.06f : shimmer);
                var rim = Touched ? ColorUtil.Shade(Tint, 1.25f) : Tint;
                r.BoxGlow(new Vector3(Pos.X, TopY + 0.02f, Pos.Z), new Vector3(Size.X, 0.035f, Size.Z), rim);
                return;
            }
        }
    }
}
