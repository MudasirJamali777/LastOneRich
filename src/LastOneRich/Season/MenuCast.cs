using LastOneRich.Core;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.Season;

/// <summary>
/// Priority 7: the idle "cast" of bots milling around behind the main menu title while the
/// player reads it — six familiar faces from the roster, standing on the actual level01
/// geometry (real gravity + collision against the level's <see cref="CollisionWorld"/>), so
/// they read as contestants waiting around rather than floating cosmetic props.
///
/// Seeded with a PRIVATE <see cref="System.Random"/> instance, deliberately never
/// <see cref="Rng"/> (Rng.Shared): that shared stream feeds HeadlessSim's deterministic season
/// simulation and every bot's per-round stumble timing. The menu never runs inside a season —
/// but it DOES run before one starts, and after one ends — so if this class ever drew from
/// Rng.Shared, idle time spent sitting on the main menu would silently shift the RNG draws
/// consumed by the next season, breaking HeadlessSim's reproducibility guarantee. A private RNG
/// means the menu cast can hop and glance as much as it likes with zero effect on gameplay.
/// </summary>
public sealed class MenuCast
{
    /// <summary>Live actors, ready for <see cref="States.ActorRenderer"/> to draw directly.</summary>
    public readonly List<Actor> Actors = new();

    readonly Level _level;
    readonly System.Random _rng;
    readonly float[] _nextHop;
    readonly float[] _nextGlance;

    static readonly (string Name, Color Color)[] Cast =
    {
        ("NOVA", ColorPalette.Bots["NOVA"]),
        ("JAX", ColorPalette.Bots["JAX"]),
        ("MIRA", ColorPalette.Bots["MIRA"]),
        ("TANK", ColorPalette.Bots["TANK"]),
        ("LUXE", ColorPalette.Bots["LUXE"]),
        ("PIXEL", ColorPalette.Bots["PIXEL"]),
    };

    /// <param name="level">Backdrop level the cast stands on; null is tolerated (no cast draws/updates).</param>
    /// <param name="seed">Fixed by default so the same six contestants strike the same idle beats
    /// on every boot — reads as "in character" rather than random jitter.</param>
    public MenuCast(Level level, int seed = 20260923)
    {
        _level = level;
        _rng = new System.Random(seed);

        int n = Cast.Length;
        _nextHop = new float[n];
        _nextGlance = new float[n];

        for (int i = 0; i < n; i++)
        {
            var (name, color) = Cast[i];
            var spawn = PickSpawn(i);
            var a = new Actor
            {
                Name = name,
                Color = color,
                Pos = spawn + new Vector3(0, Actor.HalfY + 0.1f, 0),
            };
            a.PrevPos = a.Pos;                 // no interpolation pop on the very first frame
            a.LastGroundY = spawn.Y;
            float facing = Range(-3.14f, 3.14f);
            a.FaceDir = new Vector3(MathF.Sin(facing), 0f, MathF.Cos(facing));
            Actors.Add(a);

            // staggered timers so all six don't hop or glance in lockstep
            _nextHop[i] = Range(2.0f, 6.0f);
            _nextGlance[i] = Range(0.6f, 3.0f);
        }
    }

    Vector3 PickSpawn(int i)
    {
        if (_level?.Spawns != null && _level.Spawns.Length > 0)
            return _level.Spawns[i % _level.Spawns.Length];
        return new Vector3((i - 2.5f) * 2.2f, 0f, -4f); // fallback line-up if the level has none
    }

    float Range(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    bool Chance(float p) => (float)_rng.NextDouble() < p;

    /// <summary>No-op when there is no backdrop level (auction/intermission void) — nothing to stand on.</summary>
    public void Update(float dt)
    {
        if (_level == null) return;

        for (int i = 0; i < Actors.Count; i++)
        {
            var a = Actors[i];

            // idle hop: a small vertical pop, well under a real jump, read as restless waiting
            _nextHop[i] -= dt;
            if (_nextHop[i] <= 0f && a.OnGround)
            {
                a.Vel.Y = Phys.JumpVel * Range(0.4f, 0.6f);
                _nextHop[i] = Range(4.0f, 9.0f);
            }

            // idle glance: turn to face a new direction now and then, no locomotion attached
            _nextGlance[i] -= dt;
            if (_nextGlance[i] <= 0f)
            {
                float ang = Range(-1.3f, 1.3f);
                a.FaceDir = Vector3.Normalize(new Vector3(MathF.Sin(ang), 0f, MathF.Cos(ang)));
                _nextGlance[i] = Chance(0.3f) ? Range(0.8f, 1.6f) : Range(2.5f, 6.0f);
            }

            // bleed off any horizontal drift from the hop/landing so nobody wanders off the set
            Phys.Approach(ref a.Vel.X, 0f, Phys.Friction * 0.5f, dt);
            Phys.Approach(ref a.Vel.Z, 0f, Phys.Friction * 0.5f, dt);

            _level.World.Integrate(a, dt, Vector3.Zero);
        }
    }
}
