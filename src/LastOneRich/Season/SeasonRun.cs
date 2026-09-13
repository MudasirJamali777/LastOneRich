using LastOneRich.Core;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.Season;

/// <summary>BankedCash (safe) vs RiskedCash (prize pot) — GDD section 6.</summary>
public sealed class Wallet
{
    public double Banked { get; set; }
    public double Risked { get; set; }
    public double PendingMult { get; set; } = 1;

    public double Total => Banked + Risked;

    public void AddPayout(double v) => Risked += v;

    /// <summary>Risked pot multiplies once you survive the next round.</summary>
    public void ApplyPending()
    {
        if (PendingMult > 1) Risked *= PendingMult;
        PendingMult = 1;
    }

    public void ChooseBank() { Banked += Risked; Risked = 0; PendingMult = 1; }
    public void ChooseRisk(double mult) { PendingMult = mult; }
    public void ForfeitRisked() { Risked = 0; PendingMult = 1; }
}

public sealed class Contestant
{
    public string Name = "???";
    public bool IsPlayer;
    public Color Color = Color.White;
    public PersonalityDTO Personality = new();
    public bool Eliminated;
    public int LastRank;
}

/// <summary>Full campaign run state: cast, wallet, round index, active twist (GDD 3.1/5/6).</summary>
public sealed class SeasonRun
{
    public SeasonDTO Season;
    public EconomyDTO Economy;
    public BotsDTO Bots;
    public TwistsDTO Twists;
    public List<Contestant> Cast = new();
    public Wallet Wallet = new();
    public int RoundIdx;
    public TwistDTO ActiveTwist;
    public readonly HashSet<string> Upgrades = new();   // auction advantages (GDD L10)
    public bool ConsumeUpgrade(string id) { if (Upgrades.Remove(id)) { System.Console.WriteLine($"[upgrade] consumed {id}"); return true; } return false; }
    public string SabotageFlavor = "";
    public Level CurrentLevel;
    public RaceTracker Tracker;

    public RoundDTO Round => Season.Rounds[RoundIdx];
    public bool IsFinalRound => RoundIdx == Season.Rounds.Count - 1;
    public Contestant Player => Cast[0];

    public static SeasonRun Create()
    {
        var run = new SeasonRun
        {
            Season = Json.Load<SeasonDTO>("data/season.json"),
            Economy = Json.Load<EconomyDTO>("data/economy.json"),
            Bots = Json.Load<BotsDTO>("data/bots.json"),
            Twists = Json.Load<TwistsDTO>("data/twists.json"),
        };
        run.BuildCast();
        return run;
    }

    void BuildCast()
    {
        Cast.Add(new Contestant
        {
            Name = "YOU",
            IsPlayer = true,
            Color = ColorUtil.Parse("#ff4747"),
            Personality = new PersonalityDTO { RiskTolerance = 0.6, Aggression = 0.3, PuzzleSkill = 0.6, RouteGreed = 0.5, Pace = 1.0, SpendStyle = "Buyer" },
        });
        foreach (var b in Bots.Bots)
        {
            Cast.Add(new Contestant
            {
                Name = b.Name,
                Color = ColorUtil.Parse(b.Color),
                Personality = b.Personality,
            });
        }

        // fill bots (bots.json "fillCount"/"fillNames") — seeded, deterministic roster size
        var rng = new Random(20260913);
        Color[] palette = { new(0xff, 0x8a, 0x3d), new(0x4d, 0xff, 0xc3), new(0xf8, 0x4f, 0x4f), new(0x9d, 0x6b, 0xff),
                            new(0x3f, 0xe0, 0xb0), new(0xff, 0x6b, 0xd8), new(0x7f, 0xd4, 0xff), new(0xd4, 0xff, 0x6b) };
        for (int i = 0; i < Bots.FillCount; i++)
        {
            string name = i < Bots.FillNames.Count ? Bots.FillNames[i] : $"R-{i + 2:00}";
            Cast.Add(new Contestant
            {
                Name = name,
                Color = palette[i % palette.Length],
                Personality = new PersonalityDTO
                {
                    RiskTolerance = 0.15 + rng.NextDouble() * 0.8,
                    Aggression = rng.NextDouble() * 0.85,
                    PuzzleSkill = 0.2 + rng.NextDouble() * 0.75,
                    RouteGreed = 0.15 + rng.NextDouble() * 0.75,
                    Pace = 0.94 + rng.NextDouble() * 0.09,
                    SpendStyle = new[] { "Saver", "Buyer", "Saboteur" }[rng.Next(3)],
                },
            });
        }
    }

    public TwistDTO TwistById(string id) => Twists.Twists.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Payout formula (GDD 6): base + placement + top-half + speed bonus.</summary>
    public double ComputePayout(int rank, double time, bool finished, int castCount)
    {
        if (!finished) return Round.BaseReward * 0.4; // survived by progress but didn't finish
        double v = Round.BaseReward;
        if (Economy.PlacementBonus.TryGetValue(rank.ToString(), out var pb)) v += pb;
        if (rank > 3 && rank <= castCount / 2) v += Economy.TopHalfBonus;
        if (time <= Economy.SpeedBonusParSeconds) v += Economy.SpeedBonusAmount;
        return v;
    }
}

/// <summary>GDD section 14: bounded, validator-friendly twists. Nothing runs unless it validates.</summary>
public static class TwistValidator
{
    public static List<string> ValidateSeason(SeasonRun run)
    {
        var errors = new List<string>();

        if (run.Season.Rounds.Count == 0) errors.Add("season has no rounds");

        foreach (var round in run.Season.Rounds)
        {
            if (round.Level == "none" || round.Level == "auction")
            {
                // intermission rounds carry no level payload
                continue;
            }
            try
            {
                var lvl = Json.Load<LevelDTO>($"data/levels/{round.Level}.json");
                if (lvl.TimeLimit < 30) errors.Add($"round {round.Round}: timeLimit {lvl.TimeLimit} < 30");
                if (lvl.Elimination.Percent < 0 || lvl.Elimination.Percent > 50)
                    errors.Add($"round {round.Round}: elimination percent out of bounds");

                var validRules = new[] { "TimeTrialRankCut", "ScoreRankCut", "LastNStanding", "StrikesOut", "TeamCut", "TopNAdvance", "NoElimination" };
                if (!validRules.Contains(lvl.Elimination.Rule))
                    errors.Add($"round {round.Round}: unknown elimination rule '{lvl.Elimination.Rule}'");

                switch (lvl.Type)
                {
                    case "Race":
                        if (lvl.Elimination.Rule != "TimeTrialRankCut" && lvl.Elimination.Rule != "TopNAdvance")
                            errors.Add($"round {round.Round}: elimination rule doesn't match Race");
                        if (lvl.Finish == null) errors.Add($"round {round.Round}: race level has no finish");
                        break;
                    case "SurvivalZone":
                        if (lvl.SafeZone == null) errors.Add($"round {round.Round}: SurvivalZone has no safe zone");
                        if (lvl.Elimination.Rule != "ScoreRankCut") errors.Add($"round {round.Round}: SurvivalZone expects ScoreRankCut");
                        break;
                    case "StrikesOut":
                        if (lvl.Drones.Count == 0) errors.Add($"round {round.Round}: StrikesOut has no drones");
                        break;
                    case "ScoreCollect":
                        if (lvl.Vault == null || lvl.Deposit == null) errors.Add($"round {round.Round}: ScoreCollect missing vault/deposit");
                        break;
                    case "FinaleButton":
                        if (lvl.Button == null) errors.Add($"round {round.Round}: FinaleButton has no button");
                        break;
                    case "Auction":
                        break;
                    default:
                        errors.Add($"round {round.Round}: unknown level type '{lvl.Type}' (falling back to Race at runtime)");
                        break;
                }

                int spawnCount = lvl.Spawns.Length;
                if (lvl.SpawnGrid != null) spawnCount = 32;
                if (spawnCount < run.Cast.Count)
                    errors.Add($"round {round.Round}: level has fewer spawns ({spawnCount}) than contestants ({run.Cast.Count})");
            }
            catch (Exception e)
            {
                errors.Add($"round {round.Round}: level '{round.Level}' failed to load: {e.Message}");
            }

            foreach (var id in round.TwistPool)
                if (run.TwistById(id) == null)
                    errors.Add($"round {round.Round}: twist '{id}' not found");

            if (round.CashOutAfter && !run.Season.CashOutOffers.ContainsKey(round.Round.ToString()))
                errors.Add($"round {round.Round}: CashOutAfter set but no offer amount");
        }

        // at least one solvable path per level: graph must reach the goal
        try
        {
            var lvl = Json.Load<LevelDTO>("data/levels/level01.json");
            var g = WaypointGraph.FromDTO(lvl.Waypoints);
            g.ComputeDistances(g.Nearest(lvl.Finish.Pos.ToVec3()));
            if (g.Nodes.Length == 0) errors.Add("level01: no waypoints");
            else if (double.IsInfinity(g.DistToGoal[0])) errors.Add("level01: start node cannot reach finish");
        }
        catch (Exception e) { errors.Add($"level01: waypoint check failed: {e.Message}"); }

        return errors;
    }

    /// <summary>Validate a twist selection for a round: bounds + conflicts (GDD 14 / Appendix A).</summary>
    public static List<string> ValidateSelection(IEnumerable<TwistDTO> selected, int roundNumber)
    {
        var errors = new List<string>();
        var list = selected.ToList();
        var products = new Dictionary<string, double>();

        foreach (var t in list)
        {
            if (roundNumber < t.MinRound) errors.Add($"'{t.Id}' not allowed before round {t.MinRound}");
            foreach (var e in t.Effects)
            {
                var key = $"{e.Target}.{e.Param}";
                products[key] = products.GetValueOrDefault(key, 1.0) * e.Mult;
            }
        }

        foreach (var t in list)
            foreach (var kv in t.MaxMult)
            {
                if (!products.TryGetValue(kv.Key, out var p)) continue;
                if (kv.Value >= 1.0 && p > kv.Value)
                    errors.Add($"'{t.Id}': {kv.Key} product {p:0.###} exceeds cap {kv.Value:0.###}");
                if (kv.Value < 1.0 && p < kv.Value)
                    errors.Add($"'{t.Id}': {kv.Key} product {p:0.###} below floor {kv.Value:0.###}");
            }

        foreach (var a in list)
            foreach (var b in list)
                if (a != b && a.ConflictsWith.Contains(b.Id))
                    errors.Add($"'{a.Id}' conflicts with '{b.Id}'");

        return errors.Distinct().ToList();
    }
}
