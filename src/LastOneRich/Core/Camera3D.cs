using Microsoft.Xna.Framework;

namespace LastOneRich.Core;

/// <summary>Position + look-at + fov camera used for gameplay, orbit backdrops and cutscene beats.</summary>
public class Camera3D
{
    public Vector3 Position = new(0, 8, -20);
    public Vector3 LookAt = Vector3.Zero;
    public float FovDeg = 62f;

    // ---- mouse-look orbit state (gameplay only; cutscenes/backdrops ignore these) ----

    /// <summary>
    /// Horizontal look angle in radians. 0 = facing +Z; increasing rotates the view toward +X.
    /// Note XNA's right-handed CreateLookAt puts world +X on the SCREEN-LEFT when facing +Z,
    /// so a rightward mouse move must DECREASE Yaw (see GameplayState.UpdateLook).
    /// </summary>
    public float Yaw;

    /// <summary>
    /// Camera elevation in radians. Positive = camera raised, looking down on the player.
    /// Clamped by <see cref="PitchMinDeg"/>/<see cref="PitchMaxDeg"/>.
    /// </summary>
    public float Pitch = MathHelper.ToRadians(14f);

    public float PitchMinDeg = -25f;
    public float PitchMaxDeg = 60f;

    public Matrix View => Matrix.CreateLookAt(Position, LookAt, Vector3.Up);

    /// <summary>Camera right axis in world space — the direction that appears as screen-right.</summary>
    public Vector3 RightDir => Vector3.Normalize(new Vector3(View.M11, View.M12, View.M13));

    /// <summary>Camera forward axis in world space — where the screen center points.</summary>
    public Vector3 ForwardDir => Vector3.Normalize(-new Vector3(View.M31, View.M32, View.M33));

    /// <summary>Flattened forward from <see cref="Yaw"/> alone — the direction W should move.</summary>
    public Vector3 YawForward => new(MathF.Sin(Yaw), 0f, MathF.Cos(Yaw));

    /// <summary>
    /// Flattened right from <see cref="Yaw"/> alone — the direction D should strafe.
    /// Matches the View matrix's right axis under XNA's right-handed convention.
    /// </summary>
    public Vector3 YawRight => new(-MathF.Cos(Yaw), 0f, MathF.Sin(Yaw));

    public Matrix Projection(float aspect) =>
        Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(FovDeg), aspect, 0.1f, 700f);

    public void SmoothTo(Vector3 pos, Vector3 look, float stiffness, float dt)
    {
        float k = 1f - MathF.Exp(-stiffness * dt);
        Position += (pos - Position) * k;
        LookAt += (look - LookAt) * k;
    }

    /// <summary>
    /// Apply a look delta (already scaled to radians) and clamp pitch to the configured cone.
    /// </summary>
    public void ApplyLook(float yawDelta, float pitchDelta)
    {
        Yaw += yawDelta;
        if (Yaw > MathHelper.Pi) Yaw -= MathHelper.TwoPi;
        else if (Yaw < -MathHelper.Pi) Yaw += MathHelper.TwoPi;

        Pitch = MathHelper.Clamp(Pitch + pitchDelta,
            MathHelper.ToRadians(PitchMinDeg), MathHelper.ToRadians(PitchMaxDeg));
    }

    /// <summary>
    /// Place the camera on its orbit around <paramref name="target"/> using the current Yaw/Pitch.
    /// <paramref name="sweep"/> (optional) is a ray test returning the free distance along a
    /// direction; when it reports geometry closer than the boom length the camera is pulled in.
    /// Position snaps when pulling in (so walls never clip through) and eases when pushing back out.
    /// </summary>
    public void UpdateOrbit(Vector3 target, float distance, float height, float dt,
                            System.Func<Vector3, Vector3, float, float> sweep = null)
    {
        var pivot = target + new Vector3(0f, height, 0f);

        float cp = MathF.Cos(Pitch), sp = MathF.Sin(Pitch);
        // Direction from the pivot out to the camera (behind + above the player).
        var boom = new Vector3(-cp * MathF.Sin(Yaw), sp, -cp * MathF.Cos(Yaw));
        if (boom.LengthSquared() < 0.0001f) boom = new Vector3(0f, 0f, -1f);
        boom.Normalize();

        float want = MathF.Max(0.6f, distance);
        if (sweep != null)
        {
            float free = sweep(pivot, boom, want);
            if (free < want) want = MathF.Max(0.6f, free);
        }

        var desired = pivot + boom * want;

        // Pull in immediately (avoid clipping), ease back out (avoid popping).
        float curDist = (Position - pivot).Length();
        if (want < curDist - 0.001f)
        {
            Position = desired;
        }
        else
        {
            float k = 1f - MathF.Exp(-10f * dt);
            Position += (desired - Position) * k;
        }

        LookAt = pivot;
    }

    public static Camera3D Orbit(Vector3 center, float radius, float height, float angle, float fov = 55f) => new()
    {
        Position = center + new Vector3(MathF.Sin(angle) * radius, height, MathF.Cos(angle) * radius),
        LookAt = center,
        FovDeg = fov,
    };
}
