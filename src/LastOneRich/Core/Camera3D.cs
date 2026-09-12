using Microsoft.Xna.Framework;

namespace LastOneRich.Core;

/// <summary>Position + look-at + fov camera used for gameplay, orbit backdrops and cutscene beats.</summary>
public class Camera3D
{
    public Vector3 Position = new(0, 8, -20);
    public Vector3 LookAt = Vector3.Zero;
    public float FovDeg = 62f;

    public Matrix View => Matrix.CreateLookAt(Position, LookAt, Vector3.Up);
    public Matrix Projection(float aspect) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(FovDeg), aspect, 0.1f, 700f);

    public void SmoothTo(Vector3 pos, Vector3 look, float stiffness, float dt)
    {
        float k = 1f - MathF.Exp(-stiffness * dt);
        Position += (pos - Position) * k;
        LookAt += (look - LookAt) * k;
    }

    public static Camera3D Orbit(Vector3 center, float radius, float height, float angle, float fov = 55f) => new()
    {
        Position = center + new Vector3(MathF.Sin(angle) * radius, height, MathF.Cos(angle) * radius),
        LookAt = center,
        FovDeg = fov,
    };
}
