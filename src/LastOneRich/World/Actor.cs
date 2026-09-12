using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>A contestant body: AABB with capsule-ish feel, pure data (no GPU) so sim + game share it.</summary>
public sealed class Actor
{
    public const float HalfY = 0.9f;
    public const float HalfXZ = 0.42f;

    public string Name = "???";
    public bool IsPlayer;
    public Microsoft.Xna.Framework.Color Color = Microsoft.Xna.Framework.Color.White;

    public Vector3 Pos;
    public Vector3 Vel;
    public bool OnGround;
    public int GroundMover = -1;

    public Vector3 FaceDir = new(0, 0, 1);

    public bool InSlime;
    public float Stagger;
    public float HammerCd;
    public float BumpCd;
    public double PenaltyAccum;

    public int CheckpointIdx;
    public bool Finished;
    public double FinishTime = -1;
    public float CelebrateHop;
}
