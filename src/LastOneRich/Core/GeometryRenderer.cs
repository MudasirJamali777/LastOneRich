using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>Position + normal + color vertex: lets BasicEffect do real directional lighting.</summary>
public struct VertexPositionNormalColor : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;

    public static readonly VertexDeclaration VertexDeclaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0));

    VertexDeclaration IVertexType.VertexDeclaration => VertexDeclaration;

    public VertexPositionNormalColor(Vector3 position, Vector3 normal, Color color)
    {
        Position = position;
        Normal = normal;
        Color = color;
    }
}

/// <summary>A static, load-time baked vertex mesh (level geometry, crowd, grid, decals).</summary>
public sealed class BakedMesh
{
    public VertexBuffer Buffer;
    public int Triangles;
}

/// <summary>
/// Batched 3D renderer for the "primitives only" art style (GDD 13.2/16).
/// Lighting rig: 3 directional lights (warm key + cool fill + rim) on BasicEffect,
/// per-face normals, depth fog matched to the sky clear color. Emissive/hazard
/// shapes are batched into a separate unlit "glow" pass so they pop even in shadow.
/// Static level geometry is baked once at load into a VertexBuffer (no per-frame
/// mesh rebuilds); dynamic shapes batch through grow-only vertex buffers.
/// All render states are set explicitly per pass — nothing leaks between 3D and UI.
/// </summary>
public sealed class GeometryRenderer
{
    const int InitialVerts = 32768;

    readonly GraphicsDevice _gd;
    readonly BasicEffect _fx;
    readonly RasterizerState _raster = new() { CullMode = CullMode.None }; // hand-wound faces
    readonly List<VertexPositionNormalColor> _opaque = new(InitialVerts);
    readonly List<VertexPositionNormalColor> _glow = new(2048);
    readonly List<VertexPositionNormalColor> _alpha = new(4096);
    VertexBuffer _vb;
    int _vbCap;

    /// <summary>
    /// Depth-fog switch (Settings ▸ Graphics ▸ FOG). Applied at the top of every BeginFrame, so
    /// toggling it is visible immediately — including while the pause menu is on screen.
    /// The instance-level BasicEffect state stays in one place on purpose: nothing else may
    /// touch FogEnabled or the lit/unlit passes would disagree.
    /// </summary>
    public static bool FogOn = true;

    public GeometryRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        _fx = new BasicEffect(gd)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true,
            TextureEnabled = false,
            FogEnabled = true,
            FogColor = new Vector3(0.06f, 0.06f, 0.10f),
            // Course arenas run 150-300 units long; fog must not swallow the set.
            // (Prompt's 60/150 assumed a small arena — tuned to keep the far course visible.)
            FogStart = 90f,
            FogEnd = 240f,
        };
        // Three-light rig: soft warm ambient, warm key, cool fill, back rim.
        _fx.LightingEnabled = true;
        _fx.AmbientLightColor = new Vector3(0.28f, 0.26f, 0.30f);
        _fx.DirectionalLight0.Enabled = true;
        _fx.DirectionalLight0.Direction = Vector3.Normalize(new Vector3(-0.55f, -1f, -0.35f));
        _fx.DirectionalLight0.DiffuseColor = new Vector3(1.0f, 0.95f, 0.85f);
        _fx.DirectionalLight0.SpecularColor = new Vector3(0.3f, 0.3f, 0.3f);
        _fx.DirectionalLight1.Enabled = true;
        _fx.DirectionalLight1.Direction = Vector3.Normalize(new Vector3(0.5f, -0.4f, 0.6f));
        _fx.DirectionalLight1.DiffuseColor = new Vector3(0.25f, 0.30f, 0.40f);
        _fx.DirectionalLight1.SpecularColor = Vector3.Zero;
        _fx.DirectionalLight2.Enabled = true;
        _fx.DirectionalLight2.Direction = Vector3.Normalize(new Vector3(0.1f, 0.5f, 0.8f));
        _fx.DirectionalLight2.DiffuseColor = new Vector3(0.15f, 0.15f, 0.20f);
        _fx.DirectionalLight2.SpecularColor = Vector3.Zero;
    }

    public void BeginFrame(Camera3D cam, float aspect, Color sky)
    {
        _opaque.Clear();
        _glow.Clear();
        _alpha.Clear();
        _fx.View = cam.View;
        _fx.Projection = cam.Projection(aspect);
        _fx.FogEnabled = FogOn;
        _fx.FogColor = sky.ToVector3();
    }

    // ---------- emit helpers (shared by dynamic batching and load-time baking) ----------

    static void Face(List<VertexPositionNormalColor> list, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        var n = Vector3.Cross(b - a, c - a);
        if (n.LengthSquared() < 0.000001f) n = new Vector3(0, 1, 0);
        else n.Normalize();
        var v0 = new VertexPositionNormalColor(a, n, col);
        var v1 = new VertexPositionNormalColor(b, n, col);
        var v2 = new VertexPositionNormalColor(c, n, col);
        var v3 = new VertexPositionNormalColor(d, n, col);
        list.Add(v0); list.Add(v1); list.Add(v2);
        list.Add(v0); list.Add(v2); list.Add(v3);
    }

    /// <summary>Emit an axis-aligned box into an arbitrary list (used for baking).</summary>
    public static void EmitBox(List<VertexPositionNormalColor> list, Vector3 center, Vector3 size, Color c)
    {
        var h = size * 0.5f;
        EmitCuboid(list,
            new Vector3(center.X - h.X, center.Y - h.Y, center.Z - h.Z),
            new Vector3(center.X + h.X, center.Y + h.Y, center.Z + h.Z), c);
    }

    /// <summary>Emit a Y-rotated box into an arbitrary list (used for baking decals).</summary>
    public static void EmitBoxRotY(List<VertexPositionNormalColor> list, Vector3 center, Vector3 size, float rotRad, Color c)
    {
        var h = size * 0.5f;
        float cs = MathF.Cos(rotRad), sn = MathF.Sin(rotRad);
        Vector3 P(float x, float y, float z) => new(
            center.X + x * cs + z * sn,
            center.Y + y,
            center.Z - x * sn + z * cs);

        var p = new Vector3[8];
        int i = 0;
        for (int sz = 0; sz < 2; sz++)
            for (int sy = 0; sy < 2; sy++)
                for (int sx = 0; sx < 2; sx++)
                    p[i++] = P(sx == 0 ? -h.X : h.X, sy == 0 ? -h.Y : h.Y, sz == 0 ? -h.Z : h.Z);

        var fT = ColorUtil.Shade(c, 1.0f); var fB = ColorUtil.Shade(c, 0.62f);
        var fX = ColorUtil.Shade(c, 0.92f); var fZ = ColorUtil.Shade(c, 0.84f);
        // corners: 0:(-x,-y,-z) 1:(x,-y,-z) 2:(-x,y,-z) 3:(x,y,-z) 4:(-x,-y,z) 5:(x,-y,z) 6:(-x,y,z) 7:(x,y,z)
        Face(list, p[4], p[5], p[7], p[6], fZ);       // +z
        Face(list, p[1], p[0], p[2], p[3], fZ);       // -z
        Face(list, p[5], p[1], p[3], p[7], fX);       // +x
        Face(list, p[0], p[4], p[6], p[2], fX);       // -x
        Face(list, p[6], p[7], p[3], p[2], fT);       // +y
        Face(list, p[0], p[1], p[5], p[4], fB);       // -y
    }

    /// <summary>Emit a wedge ramp (rising toward dirX/dirZ) into an arbitrary list.</summary>
    public static void EmitRamp(List<VertexPositionNormalColor> list, Vector3 center, Vector3 size, int dirX, int dirZ, Color c)
    {
        var h = size * 0.5f;
        float x0 = center.X - h.X, x1 = center.X + h.X;
        float z0 = center.Z - h.Z, z1 = center.Z + h.Z;
        float y0 = center.Y - h.Y, y1 = center.Y + h.Y;

        if (dirZ != 0) // rise along Z
        {
            if (dirZ < 0) { (z0, z1) = (z1, z0); }
            var slope = ColorUtil.Shade(c, 0.97f);
            var side = ColorUtil.Shade(c, 0.8f);
            var back = ColorUtil.Shade(c, 0.72f);
            var A = new Vector3(x0, y0, z0);
            var B = new Vector3(x1, y0, z0);
            var C = new Vector3(x1, y1, z1);
            var D = new Vector3(x0, y1, z1);
            var E = new Vector3(x0, y0, z1);
            var F = new Vector3(x1, y0, z1);
            Face(list, A, B, C, D, slope);
            Face(list, A, D, E, E, side); // left tri
            list.RemoveAt(list.Count - 1);
            Face(list, B, F, C, C, side); // right tri
            list.RemoveAt(list.Count - 1);
            Face(list, E, F, C, D, back);
        }
        else // rise along X
        {
            if (dirX < 0) { (x0, x1) = (x1, x0); }
            var slope = ColorUtil.Shade(c, 0.9f);
            var side = ColorUtil.Shade(c, 0.8f);
            var back = ColorUtil.Shade(c, 0.72f);
            var A = new Vector3(x0, y0, z0);
            var B = new Vector3(x0, y0, z1);
            var C = new Vector3(x1, y1, z1);
            var D = new Vector3(x1, y1, z0);
            var E = new Vector3(x1, y0, z0);
            var F = new Vector3(x1, y0, z1);
            Face(list, A, B, C, D, slope);
            Face(list, A, D, E, E, side);
            list.RemoveAt(list.Count - 1);
            Face(list, B, F, C, C, side);
            list.RemoveAt(list.Count - 1);
            Face(list, E, D, C, F, back);
        }
    }

    static void EmitCuboid(List<VertexPositionNormalColor> L, Vector3 l, Vector3 u, Color c)
    {
        var a = new Vector3(l.X, l.Y, l.Z);
        var b = new Vector3(u.X, l.Y, l.Z);
        var d = new Vector3(l.X, l.Y, u.Z);
        var e = new Vector3(u.X, l.Y, u.Z);
        var f = new Vector3(l.X, u.Y, l.Z);
        var g = new Vector3(u.X, u.Y, l.Z);
        var h = new Vector3(l.X, u.Y, u.Z);
        var i2 = new Vector3(u.X, u.Y, u.Z);
        var fT = ColorUtil.Shade(c, 1.0f); var fB = ColorUtil.Shade(c, 0.62f);
        var fX = ColorUtil.Shade(c, 0.92f); var fZ = ColorUtil.Shade(c, 0.84f);
        Face(L, d, e, i2, h, fZ);
        Face(L, b, a, f, g, fZ);
        Face(L, e, b, g, i2, fX);
        Face(L, a, d, h, f, fX);
        Face(L, f, h, i2, g, fT);
        Face(L, a, b, e, d, fB);
    }

    // ---------- dynamic emit (routed to buckets) ----------

    /// <summary>Lit axis-aligned box with mild per-side tint; real shading comes from the light rig.</summary>
    public void Box(Vector3 center, Vector3 size, Color c) => EmitBox(_opaque, center, size, c);

    /// <summary>Unlit full-bright box — emissive look for hazards/interactives (pops in shadow).</summary>
    public void BoxGlow(Vector3 center, Vector3 size, Color c) => EmitBox(_glow, center, size, c);

    /// <summary>Unlit full-bright Y-rotated box (hammer heads, glow decals).</summary>
    public void BoxRotYGlow(Vector3 center, Vector3 size, float rotRad, Color c) => EmitBoxRotY(_glow, center, size, rotRad, c);

    /// <summary>Box rotated around Y (used by hammer arms).</summary>
    public void BoxRotY(Vector3 center, Vector3 size, float rotRad, Color c) => EmitBoxRotY(_opaque, center, size, rotRad, c);

    /// <summary>Wedge ramp rising toward the given direction (collision approximates it as a height field).</summary>
    public void Ramp(Vector3 center, Vector3 size, int dirX, int dirZ, Color c)
        => EmitRamp(_opaque, center, size, dirX, dirZ, c);

    /// <summary>Translucent trigger/hazard zone volume (drawn premultiplied, depth-read, no depth write).</summary>
    public void Zone(Vector3 center, Vector3 size, Color c, float alpha)
    {
        var prem = ColorUtil.Premult(c, alpha);
        var h = size * 0.5f;
        var l = center - h;
        var u = center + h;
        var a = new Vector3(l.X, l.Y, l.Z);
        var b = new Vector3(u.X, l.Y, l.Z);
        var d = new Vector3(l.X, l.Y, u.Z);
        var e = new Vector3(u.X, l.Y, u.Z);
        var f = new Vector3(l.X, u.Y, l.Z);
        var g = new Vector3(u.X, u.Y, l.Z);
        var hh = new Vector3(l.X, u.Y, u.Z);
        var i2 = new Vector3(u.X, u.Y, u.Z);
        Face(_alpha, d, e, i2, hh, prem);
        Face(_alpha, b, a, f, g, prem);
        Face(_alpha, e, b, g, i2, prem);
        Face(_alpha, a, d, hh, f, prem);
        Face(_alpha, f, hh, i2, g, prem);
        Face(_alpha, a, b, e, d, prem);
    }

    /// <summary>Fake soft drop shadow: flat dark quad on the ground (readability cue, no real shadows).</summary>
    public void Shadow(Vector3 center, float half, float alpha)
    {
        var prem = ColorUtil.Premult(new Color(0, 0, 0), alpha);
        float y = center.Y;
        var a = new Vector3(center.X - half, y, center.Z - half);
        var b = new Vector3(center.X + half, y, center.Z - half);
        var c = new Vector3(center.X + half, y, center.Z + half);
        var d = new Vector3(center.X - half, y, center.Z + half);
        Face(_alpha, a, b, c, d, prem);
    }

    // ---------- static baking ----------

    /// <summary>Bake an emitted vertex list into a GPU buffer once (load-time; never rebuilt per frame).</summary>
    public BakedMesh Bake(List<VertexPositionNormalColor> verts)
    {
        if (verts.Count == 0) return null;
        var mesh = new BakedMesh { Triangles = verts.Count / 3 };
        mesh.Buffer = new VertexBuffer(_gd, VertexPositionNormalColor.VertexDeclaration, verts.Count, BufferUsage.WriteOnly);
        mesh.Buffer.SetData(verts.ToArray());
        return mesh;
    }

    public void DrawBaked(BakedMesh mesh)
    {
        if (mesh?.Buffer == null) return;
        _gd.BlendState = BlendState.Opaque;
        _gd.DepthStencilState = DepthStencilState.Default;
        _gd.RasterizerState = _raster;
        _gd.SamplerStates[0] = SamplerState.LinearClamp;
        _fx.LightingEnabled = true;
        _gd.SetVertexBuffer(mesh.Buffer);
        foreach (var pass in _fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawPrimitives(PrimitiveType.TriangleList, 0, mesh.Triangles);
        }
        _gd.SetVertexBuffer(null);
    }

    // ---------- flush ----------

    public void EndFrame()
    {
        if (_opaque.Count > 0) Flush(_opaque, BlendState.Opaque, DepthStencilState.Default, lit: true);
        if (_glow.Count > 0) Flush(_glow, BlendState.Opaque, DepthStencilState.Default, lit: false);
        if (_alpha.Count > 0) Flush(_alpha, BlendState.AlphaBlend, DepthStencilState.DepthRead, lit: false);
    }

    void Flush(List<VertexPositionNormalColor> verts, BlendState blend, DepthStencilState depth, bool lit)
    {
        int count = verts.Count;
        int tris = count / 3;
        if (_vbCap < count)
        {
            _vbCap = System.Math.Max(InitialVerts, count * 2);
            _vb?.Dispose();
            _vb = new VertexBuffer(_gd, VertexPositionNormalColor.VertexDeclaration, _vbCap, BufferUsage.WriteOnly);
        }
        var arr = verts.ToArray();
        _vb.SetData(arr, 0, count);
        // Render state isolation: every pass sets ALL states explicitly, then restores.
        var oldBlend = _gd.BlendState; var oldDepth = _gd.DepthStencilState; var oldRaster = _gd.RasterizerState;
        _gd.BlendState = blend; _gd.DepthStencilState = depth; _gd.RasterizerState = _raster;
        _gd.SamplerStates[0] = SamplerState.LinearClamp;
        _fx.LightingEnabled = lit;
        _gd.SetVertexBuffer(_vb);
        foreach (var pass in _fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawPrimitives(PrimitiveType.TriangleList, 0, tris);
        }
        _gd.SetVertexBuffer(null);
        _gd.BlendState = oldBlend; _gd.DepthStencilState = oldDepth; _gd.RasterizerState = oldRaster;
    }
}
