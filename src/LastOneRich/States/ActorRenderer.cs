using LastOneRich.Core;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.States;

/// <summary>Contestants drawn as stylized capsule-ish primitives (GDD 16 MVP approach).</summary>
public static class ActorRenderer
{
    public static void Draw(GeometryRenderer r, Actor a)
    {
        var body = a.Color;
        var dark = ColorUtil.Shade(body, 0.72f);
        var head = ColorUtil.Shade(body, 1.08f);

        // body
        r.Box(a.Pos + new Vector3(0, -0.32f, 0), new Vector3(0.84f, 1.12f, 0.6f), body);
        // head
        r.Box(a.Pos + new Vector3(0, 0.62f, 0), new Vector3(0.58f, 0.52f, 0.56f), head);
        // visor on the facing side
        var visorPos = a.Pos + new Vector3(0, 0.66f, 0) + a.FaceDir * 0.30f;
        r.Box(visorPos, new Vector3(0.34f, 0.18f, 0.10f) + new Vector3(System.MathF.Abs(a.FaceDir.X) * 0.06f, 0, System.MathF.Abs(a.FaceDir.Z) * 0.06f), new Color(18, 20, 30));
        // feet stripe
        r.Box(a.Pos + new Vector3(0, -0.80f, 0), new Vector3(0.7f, 0.12f, 0.52f), dark);
    }
}

/// <summary>Shared slow-orbit arena backdrop for ceremonies and menus.</summary>
public sealed class ArenaBackdrop
{
    public readonly Level Level;
    readonly Camera3D _cam = new();
    float _t;
    float _speed = 0.08f;

    public ArenaBackdrop(Level level) => Level = level;

    public Camera3D Cam => _cam;

    public void Update(float dt)
    {
        _t += dt * _speed;
        _cam.Position = new Vector3(MathF.Sin(_t) * 34f, 14f, 95f + MathF.Cos(_t) * 34f);
        _cam.LookAt = new Vector3(0, 0, 95);
        _cam.FovDeg = 55f;
    }

    public void Draw()
    {
        var vp = GameServices.Gfx.Viewport;
        if (Level == null)
        {
            // auction intermission (and any level-less round): simple dome void backdrop
            GameServices.Gfx.Clear(new Color(16, 12, 34));
            return;
        }
        GameServices.Gfx.Clear(Level.SkyColor);
        var r = GameServices.Renderer;
        r.BeginFrame(_cam, vp.Width / (float)vp.Height, Level.SkyColor);
        Level.Draw(r);
        r.EndFrame();
    }
}
