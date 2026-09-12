using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>Tuned arcade feel constants (GDD section 4: readable + forgiving).</summary>
public static class Phys
{
    public const float Gravity = 25f;
    public const float Terminal = -38f;
    public const float MaxSpeed = 10f;
    public const float Accel = 44f;
    public const float AirAccel = 15f;
    public const float Friction = 36f;
    public const float JumpVel = 9.2f;
    public const float Coyote = 0.10f;
    public const float JumpBuffer = 0.12f;
    public const float DiveSpeed = 16f;
    public const float DiveTime = 0.28f;
    public const float DiveCooldown = 1.1f;
    public const float StepUp = 0.55f;
    public const float BotBaseFactor = 0.92f;

    public static void Approach(ref float value, float target, float rate, float dt)
    {
        float delta = target - value;
        float max = rate * dt;
        value += MathHelper.Clamp(delta, -max, max);
    }
}
