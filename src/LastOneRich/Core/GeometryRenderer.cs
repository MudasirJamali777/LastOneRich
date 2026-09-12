using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace LastOneRich.Core;

/// <summary>
/// Immediate-style 3D batcher for the MVP "primitives only" art style (GDD 13.2/16):
/// boxes + ramps as vertex-color triangles drawn with BasicEffect. No models, no pipeline.
/// </summary>
public sealed class GeometryRenderer
{
    const int InitialVerts = 32768;

    readonly GraphicsDevice _gd;
    readonly BasicEffect _fx;
    readonly RasterizerState _raster = new() { CullMode = CullMode.None };
    readonly List<VertexPositionColor> _opaque = new(InitialVerts);
    readonly List<VertexPositionColor> _alpha = new(4096);
    VertexBuffer _vb;
    int _vbCap;

    public GeometryRenderer(GraphicsDevice gd)
    {
        _gd = gd;
        _fx = new BasicEffect(gd)
        {
            World = Matrix.Identity,
            VertexColorEnabled = true,
            LightingEnabled = false,
            TextureEnabled = false,
            FogEnabled = true,
            FogColor = new Vector3(0.075f, 0.08f, 0.16f),
            FogStart = 150f,
            FogEnd = 460f,
        };
    }

    public void BeginFrame(Camera3D cam, float aspect, Color sky)
    {
        _opaque.Clear();
        _alpha.Clear();
        _fx.View = cam.View;
        _fx.Projection = cam.Projection(aspect);
        _fx.FogColor = sky.ToVector3();
    }

    // ---------- emit helpers ----------

    static void Face(List<VertexPositionColor> list, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        list.Add(new VertexPositionColor(a, col));
        list.Add(new VertexPositionColor(b, col));
        list.Add(new VertexPositionColor(c, col));
        list.Add(new VertexPositionColor(a, col));
        list.Add(new VertexPositionColor(c, col));
        list.Add(new VertexPositionColor(d, col));
    }

    /// <summary>Axis-aligned box. Sides get slight shade variance for the toy look.</summary>
    public void Box(Vector3 center, Vector3 size, Color c)
    {
        var h = size * 0.5f;
        var l = new Vector3(center.X - h.X, center.Y - h.Y, center.Z - h.Z);
        var u = new Vector3(center.X + h.X, center.Y + h.Y, center.Z + h.Z);
        EmitCuboid(_opaque, l, u, c);
    }

    /// <summary>Box rotated around Y (used by hammer arms/heads).</summary>
    public void BoxRotY(Vector3 center, Vector3 size, float rotRad, Color c)
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

        var fT = ColorUtil.Shade(c, 1.0f); var fB = ColorUtil.Shade(c, 0.55f);
        var fX = ColorUtil.Shade(c, 0.82f); var fZ = ColorUtil.Shade(c, 0.72f);
        var L = _opaque;
        // corners: 0:(-x,-y,-z) 1:(x,-y,-z) 2:(-x,y,-z) 3:(x,y,-z) 4:(-x,-y,z) 5:(x,-y,z) 6:(-x,y,z) 7:(x,y,z)
        Face(L, p[4], p[5], p[7], p[6], fZ);       // +z
        Face(L, p[1], p[0], p[2], p[3], fZ);       // -z
        Face(L, p[5], p[1], p[3], p[7], fX);       // +x
        Face(L, p[0], p[4], p[6], p[2], fX);       // -x
        Face(L, p[6], p[7], p[3], p[2], fT);       // +y
        Face(L, p[0], p[1], p[5], p[4], fB);       // -y
    }

    static void EmitCuboid(List<VertexPositionColor> L, Vector3 l, Vector3 u, Color c)
    {
        var a = new Vector3(l.X, l.Y, l.Z);
        var b = new Vector3(u.X, l.Y, l.Z);
        var d = new Vector3(l.X, l.Y, u.Z);
        var e = new Vector3(u.X, l.Y, u.Z);
        var f = new Vector3(l.X, u.Y, l.Z);
        var g = new Vector3(u.X, u.Y, l.Z);
        var h = new Vector3(l.X, u.Y, u.Z);
        var i2 = new Vector3(u.X, u.Y, u.Z);
        var fT = ColorUtil.Shade(c, 1.0f); var fB = ColorUtil.Shade(c, 0.55f);
        var fX = ColorUtil.Shade(c, 0.82f); var fZ = ColorUtil.Shade(c, 0.72f);
        Face(L, d, e, i2, h, fZ);
        Face(L, b, a, f, g, fZ);
        Face(L, e, b, g, i2, fX);
        Face(L, a, d, h, f, fX);
        Face(L, f, h, i2, g, fT);
        Face(L, a, b, e, d, fB);
    }

    /// <summary>Wedge ramp rising toward the given direction (collision approximates it as a height field).</summary>
    public void Ramp(Vector3 center, Vector3 size, int dirX, int dirZ, Color c)
    {
        var h = size * 0.5f;
        float x0 = center.X - h.X, x1 = center.X + h.X;
        float z0 = center.Z - h.Z, z1 = center.Z + h.Z;
        float y0 = center.Y - h.Y, y1 = center.Y + h.Y;

        if (dirZ != 0) // rise along Z
        {
            if (dirZ < 0) { (z0, z1) = (z1, z0); }
            var slope = ColorUtil.Shade(c, 0.95f);
            var side = ColorUtil.Shade(c, 0.7f);
            var back = ColorUtil.Shade(c, 0.6f);
            var A = new Vector3(x0, y0, z0);
            var B = new Vector3(x1, y0, z0);
            var C = new Vector3(x1, y1, z1);
            var D = new Vector3(x0, y1, z1);
            var E = new Vector3(x0, y0, z1);
            var F = new Vector3(x1, y0, z1);
            Face(_opaque, A, B, C, D, slope);
            Face(_opaque, A, D, E, E, side); // left tri
            _opaque.RemoveAt(_opaque.Count - 1);
            Face(_opaque, B, F, C, C, side); // right tri
            _opaque.RemoveAt(_opaque.Count - 1);
            Face(_opaque, E, F, C, D, back);
        }
        else // rise along X
        {
            if (dirX < 0) { (x0, x1) = (x1, x0); }
            var slope = ColorUtil.Shade(c, 0.9f);
            var side = ColorUtil.Shade(c, 0.7f);
            var back = ColorUtil.Shade(c, 0.6f);
            var A = new Vector3(x0, y0, z0);
            var B = new Vector3(x0, y0, z1);
            var C = new Vector3(x1, y1, z1);
            var D = new Vector3(x1, y1, z0);
            var E = new Vector3(x1, y0, z0);
            var F = new Vector3(x1, y0, z1);
            Face(_opaque, A, B, C, D, slope);
            Face(_opaque, A, D, E, E, side);
            _opaque.RemoveAt(_opaque.Count - 1);
            Face(_opaque, B, F, C, C, side);
            _opaque.RemoveAt(_opaque.Count - 1);
            Face(_opaque, E, D, C, F, back);
        }
    }

    /// <summary>Translucent trigger/hazard zone volume (drawn premultiplied, no depth write).</summary>
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

    // ---------- flush ----------

    public void EndFrame()
    {
        if (_opaque.Count > 0) Flush(_opaque, BlendState.Opaque, DepthStencilState.Default);
        if (_alpha.Count > 0) Flush(_alpha, BlendState.AlphaBlend, DepthStencilState.DepthRead);
    }

    void Flush(List<VertexPositionColor> verts, BlendState blend, DepthStencilState depth)
    {
        int count = verts.Count;
        int tris = count / 3;
        if (_vbCap < count)
        {
            _vbCap = System.Math.Max(InitialVerts, count * 2);
            _vb?.Dispose();
            _vb = new VertexBuffer(_gd, VertexPositionColor.VertexDeclaration, _vbCap, BufferUsage.WriteOnly);
        }
        var arr = verts.ToArray();
        _vb.SetData(arr, 0, count);
        var oldBlend = _gd.BlendState; var oldDepth = _gd.DepthStencilState; var oldRaster = _gd.RasterizerState;
        _gd.BlendState = blend; _gd.DepthStencilState = depth; _gd.RasterizerState = _raster;
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
