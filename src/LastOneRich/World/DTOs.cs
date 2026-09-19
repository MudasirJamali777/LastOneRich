namespace LastOneRich.World;

// ---------------------------------------------------------------------------
// Data Transfer Objects for ALL data-driven content (GDD section 13).
// Pure POCOs — usable from the game and from the headless simulator.
// ---------------------------------------------------------------------------

public sealed class SeasonDTO
{
    public string SeasonName { get; set; } = "SEASON 1";
    public double GrandPrize { get; set; } = 1000000;
    public int Contestants { get; set; } = 7;
    public List<RoundDTO> Rounds { get; set; } = new();
    public Dictionary<string, double> CashOutOffers { get; set; } = new();
}

public sealed class RoundDTO
{
    public int Round { get; set; }
    public string Level { get; set; } = "level01";
    public double BaseReward { get; set; }
    public string[] TwistPool { get; set; } = System.Array.Empty<string>();
    public bool CashOutAfter { get; set; }
}

public sealed class EconomyDTO
{
    public double RiskMultiplier { get; set; } = 2.0;
    public double SpeedBonusParSeconds { get; set; } = 75;
    public double SpeedBonusAmount { get; set; } = 1500;
    public Dictionary<string, double> PlacementBonus { get; set; } = new();
    public double TopHalfBonus { get; set; }
    public List<AuctionItemDTO> AuctionItems { get; set; } = new();
}

public sealed class AuctionItemDTO
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public double Price { get; set; }
}

public sealed class BotsDTO
{
    public List<BotDTO> Bots { get; set; } = new();
    public int FillCount { get; set; }                 // fill bots to reach season size
    public List<string> FillNames { get; set; } = new();
}

public sealed class BotDTO
{
    public string Name { get; set; } = "BOT";
    public string Color { get; set; } = "#ffffff";
    public PersonalityDTO Personality { get; set; } = new();
}

public sealed class PersonalityDTO
{
    public double RiskTolerance { get; set; } = 0.5;
    public double Aggression { get; set; } = 0.4;
    public double PuzzleSkill { get; set; } = 0.5;
    public double RouteGreed { get; set; } = 0.5;
    public double Pace { get; set; } = 1.0;
    public string SpendStyle { get; set; } = "Saver";
}

public sealed class TwistsDTO
{
    public List<TwistDTO> Twists { get; set; } = new();
}

public sealed class TwistDTO
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int MinRound { get; set; } = 2;
    public string[] ConflictsWith { get; set; } = System.Array.Empty<string>();
    public List<TwistEffectDTO> Effects { get; set; } = new();
    public Dictionary<string, double> MaxMult { get; set; } = new();
}

public sealed class TwistEffectDTO
{
    public string Target { get; set; } = "";
    public string Param { get; set; } = "";
    public double Mult { get; set; } = 1.0;
}

public sealed class LevelDTO
{
    public string Id { get; set; } = "level01";
    public string Name { get; set; } = "LEVEL";
    public string Type { get; set; } = "Race";
    public double TimeLimit { get; set; } = 120;
    public double RespawnPenalty { get; set; } = 3;
    public double KillY { get; set; } = -10;
    public double ParTime { get; set; } = 75;
    public bool CosmeticCrowd { get; set; }
    public float[][] Spawns { get; set; } = System.Array.Empty<float[]>();
    public float[][] Checkpoints { get; set; } = System.Array.Empty<float[]>();
    public ZoneDTO Finish { get; set; } = new();
    public EliminationDTO Elimination { get; set; } = new();
    public List<GeoDTO> Geometry { get; set; } = new();
    public List<ConveyorDTO> Conveyors { get; set; } = new();
    public List<SlimeDTO> Slimes { get; set; } = new();
    public List<WindDTO> Winds { get; set; } = new();
    public List<HammerDTO> Hammers { get; set; } = new();
    public List<MoverDTO> Movers { get; set; } = new();
    public WaypointsDTO Waypoints { get; set; } = new();
    // --- non-race mode config ---
    public SafeZoneDTO SafeZone { get; set; }
    public List<DroneDTO> Drones { get; set; } = new();
    public float[][] IceZones { get; set; } = System.Array.Empty<float[]>(); // [x,y,z,sx,sy,sz,friction]
    public ZoneDTO Vault { get; set; }
    public ZoneDTO Deposit { get; set; }
    public double BrickValue { get; set; } = 10000;
    public int CarryCap { get; set; } = 3;
    public ButtonDTO Button { get; set; }
    public float[] SpawnGrid { get; set; } // [x,y,z, dx,dz, cols] — fills up to contestant count
    // --- Priority 6: breakable glass path ---
    public List<BreakTileDTO> BreakTiles { get; set; } = new();
    public int GlassSeed { get; set; } = 6061;   // seeds the safe-pane shuffle (per level, fixed)
}

/// <summary>
/// One breakable glass pane (Priority 6). <c>Safe</c> is deliberately a NULLABLE tri-state:
/// null = "let the seeded shuffle decide", true/false = authored by hand. That keeps hand-placed
/// puzzle rows possible without forcing every pane in a 22-row course to be spelled out.
/// </summary>
public sealed class BreakTileDTO
{
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[] { 3.2f, 0.5f, 3.6f };
    public int Row { get; set; }                  // panes sharing a row form one choice
    public bool? Safe { get; set; }               // null = seeded shuffle picks the safe pane
    public double CrackTime { get; set; } = 0.45; // grace before a fake pane drops
    public double ReformTime { get; set; }        // 0 = stays broken for the round
    public string Color { get; set; } = "#8FE6FF";
}

public sealed class SafeZoneDTO
{
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[] { 12, 4, 12 };
    public double ShrinkTo { get; set; }   // final XZ half-size (0 = no shrink)
    public double ShrinkSeconds { get; set; } = 45;
}

public sealed class DroneDTO
{
    public float[] A { get; set; } = new float[3];
    public float[] B { get; set; } = new float[3];
    public double Speed { get; set; } = 6;
    public double Radius { get; set; } = 3.2;
    public double Phase { get; set; }
}

public sealed class ButtonDTO
{
    public float[] Pos { get; set; } = new float[3];
    public double Radius { get; set; } = 6;
    public double StaminaDrain { get; set; } = 9;   // per second while holding
    public double ForcedOffSeconds { get; set; } = 5;
}

public sealed class GeoDTO
{
    public string Kind { get; set; } = "box";
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[3];
    public string Color { get; set; } = "#ffffff";
    public string Dir { get; set; } = "z+";
    public bool Collide { get; set; } = true;
}

public sealed class ZoneDTO
{
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[3];
}

public sealed class EliminationDTO
{
    public string Rule { get; set; } = "TimeTrialRankCut";
    public double Percent { get; set; } = 20;
    public int TopN { get; set; } = 6;          // TopNAdvance
}

public sealed class ConveyorDTO
{
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[3];
    public float[] Dir { get; set; } = new float[] { 0, 0, 1 };
    public double Speed { get; set; } = 6;
    public string Color { get; set; } = "#ffd23f";
}

public sealed class SlimeDTO
{
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[3];
    public double SpeedMult { get; set; } = 0.3;
    public double JumpMult { get; set; } = 0.7;
    public string Color { get; set; } = "#7ad12e";
}

public sealed class WindDTO
{
    public float[] Pos { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[3];
    public float[] Dir { get; set; } = new float[] { 0, 0, -1 };
    public double Strength { get; set; } = 10;
    public double Period { get; set; } = 4;
    public double Duty { get; set; } = 0.5;
    public string Color { get; set; } = "#9fd8ff";
}

public sealed class HammerDTO
{
    public float[] Pivot { get; set; } = new float[3];
    public double ArmLength { get; set; } = 5;
    public double Speed { get; set; } = 1.8;
    public double Phase { get; set; }
    public float[] HeadSize { get; set; } = new float[] { 1.5f, 1.5f, 1.5f };
    public float[] ArmSize { get; set; } = new float[] { 0.5f, 0.5f, 5 };
    public string Color { get; set; } = "#ff3355";
}

public sealed class MoverDTO
{
    public float[] A { get; set; } = new float[3];
    public float[] B { get; set; } = new float[3];
    public float[] Size { get; set; } = new float[] { 4, 0.6f, 4 };
    public double Period { get; set; } = 5;
    public double Phase { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public sealed class WaypointsDTO
{
    public List<NodeDTO> Nodes { get; set; } = new();
    public double[][] Edges { get; set; } = System.Array.Empty<double[]>(); // [from, to, risk?]
}

public sealed class NodeDTO
{
    public float[] Pos { get; set; } = new float[3];
    public string[] Flags { get; set; } = System.Array.Empty<string>();
}

public sealed class CutsceneDTO
{
    public string Id { get; set; } = "";
    public string Music { get; set; } = "music_loop";
    public double Duration { get; set; } = 10;
    public List<CutsceneBeatDTO> Beats { get; set; } = new();
}

public sealed class CutsceneBeatDTO
{
    public double T { get; set; }
    public string Type { get; set; } = "";
    public float[] Pos { get; set; }
    public float[] LookAt { get; set; }
    public double Fov { get; set; } = 60;
    public double Dur { get; set; } = 2;
    public string Speaker { get; set; }
    public string Text { get; set; }
    public string Graphic { get; set; }
    public double Amount { get; set; }
    public string Sfx { get; set; }
    public string Name { get; set; }
    // cameraOrbit beats (code-driven, level-agnostic)
    public double CenterZ { get; set; } = -1;   // -1 = auto (level mid)
    public double Radius { get; set; } = 30;
    public double Height { get; set; } = 12;
    public double Speed { get; set; } = 0.25;   // rad/s
}
