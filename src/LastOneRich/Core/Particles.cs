using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>2D confetti + spark particles for ceremonies, cutscene beats and finish lines.</summary>
public sealed class Particles
{
    sealed class P
    {
        public Vector2 Pos, Vel;
        public float Rot, RotV, Size, Life, MaxLife;
        public Color Color;
        public bool Spark;
    }

    readonly List<P> _ps = new(1024);
    static readonly Color[] ConfettiColors =
    {
        new(255, 79, 216), new(255, 210, 63), new(63, 210, 255),
        new(141, 255, 63), new(255, 92, 92), new(176, 107, 255), Color.White,
    };

    public void ConfettiBurst(Vector2 center, int count, float spreadX = 700f, float spreadY = 420f)
    {
        for (int i = 0; i < count; i++)
        {
            _ps.Add(new P
            {
                Pos = center + new Vector2(Rng.Range(-spreadX * 0.5f, spreadX * 0.5f), Rng.Range(-30, 30)),
                Vel = new Vector2(Rng.Range(-140, 140), Rng.Range(-460, -160)),
                Rot = Rng.Range(0, 6.28f),
                RotV = Rng.Range(-9, 9),
                Size = Rng.Range(5, 11),
                Life = Rng.Range(1.6f, 2.9f),
                MaxLife = 2.9f,
                Color = ConfettiColors[Rng.Int(ConfettiColors.Length)],
                Spark = false,
            });
        }
    }

    public void SparkBurst(Vector2 center, int count, Color? color = null)
    {
        for (int i = 0; i < count; i++)
        {
            var ang = Rng.Range(0, 6.28f);
            var spd = Rng.Range(80, 420);
            _ps.Add(new P
            {
                Pos = center,
                Vel = new Vector2(MathF.Cos(ang) * spd, MathF.Sin(ang) * spd),
                Rot = ang,
                RotV = 0,
                Size = Rng.Range(8, 26),
                Life = Rng.Range(0.25f, 0.6f),
                MaxLife = 0.6f,
                Color = color ?? new Color(255, 220, 120),
                Spark = true,
            });
        }
    }

    public void Update(float dt)
    {
        for (int i = _ps.Count - 1; i >= 0; i--)
        {
            var p = _ps[i];
            p.Life -= dt;
            if (p.Life <= 0) { _ps.RemoveAt(i); continue; }
            p.Pos += p.Vel * dt;
            if (p.Spark) p.Vel *= 1f - 3.2f * dt;
            else
            {
                p.Vel.Y += 560f * dt;
                p.Vel.X += MathF.Sin(p.Pos.Y * 0.03f + p.Rot) * 26f * dt;
            }
            p.Rot += p.RotV * dt;
        }
    }

    public void Draw(SpriteBatch sb)
    {
        foreach (var p in _ps)
        {
            float a = MathHelper.Clamp(p.Life / (p.MaxLife * 0.4f), 0f, 1f);
            var c = p.Color * a;
            var tex = p.Spark ? GameServices.Particle : GameServices.Pixel;
            var origin = p.Spark ? new Vector2(tex.Width / 2f, tex.Height / 2f) : new Vector2(2f, 2f);
            sb.Draw(tex, p.Pos, null, c, p.Rot, origin, p.Size / (p.Spark ? tex.Width : 4f), SpriteEffects.None, 0f);
        }
    }

    public void Clear() => _ps.Clear();
}
