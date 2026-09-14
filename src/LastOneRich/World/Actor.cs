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
    public Vector3 PrevPos;      // position before the last physics step (render interpolation)
    public float LastGroundY;    // top of the last surface stood on (drop-shadow anchor)
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

    // --- mode state (SurvivalZone / StrikesOut / ScoreCollect / FinaleButton) ---
    public bool RoundOut;          // eliminated mid-round (strikes / periodic cut)
    public double RoundOutTime = -1;
    public int Strikes;
    public float StrikeCd;
    public double Score;           // survival seconds / deposited value / finale score
    public double WaitTime;        // finale: time off-button (builds multiplier)
    public double Stamina = 100;
    public double ForcedOff;       // finale: forced cooldown after stamina burnout
    public bool OnButton;
    public int Carrying;           // heist bricks
    public float CarryCd;
    public bool InIce;
}
