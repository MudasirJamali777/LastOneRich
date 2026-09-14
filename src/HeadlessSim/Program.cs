using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.HeadlessSim;

/// <summary>
/// Full-season deterministic harness (GDD 13/14 acceptance gate):
/// plays all 12 rounds with the same physics/AI/rules code as the game —
/// no GPU, no window. Verifies resolution per round, roster shrinking,
/// auction flow, and a finale winner. Exit 0 = ALL CHECKS PASSED.
/// </summary>
public static class Program
{
    static bool DebugTrace;

    public static int Main(string[] args)
    {
        System.Console.WriteLine("=================================================");
        System.Console.WriteLine(" LAST ONE RICH! — full season validation harness");
        System.Console.WriteLine("=================================================");

        SeasonRun season;
        try { season = SeasonRun.Create(); }
        catch (Exception e) { System.Console.WriteLine($"FATAL: content failed to load: {e}"); return 2; }

        var errors = TwistValidator.ValidateSeason(season);
        if (errors.Count == 0) System.Console.WriteLine("[validator] season + 12 levels + twists... OK");
        else foreach (var e in errors) System.Console.WriteLine($"[validator] FAIL: {e}");

        DebugTrace = args.Any(a => a.Contains("debug"));
        int failures = errors.Count;
        var alive = season.Cast.ToList();

        for (int r = 0; r < season.Season.Rounds.Count; r++)
        {
            var round = season.Season.Rounds[r];
            season.RoundIdx = r;
            string mode = "Auction";
            Level lv = null;

            if (round.Level != "none" && round.Level != "auction")
            {
                lv = Level.Load(round.Level, null, null);
                mode = lv.Dto.Type;
            }

            if (mode == "Auction")
            {
                System.Console.WriteLine($"R{round.Round,2} {mode,-13} roster {alive.Count,2}  (intermission — no elimination)");
                continue;
            }

            if (DebugTrace) System.Console.WriteLine(
                $"[trace] R{round.Round}: type={lv.Dto.Type} nodes={lv.Graph.Nodes.Length} vaultNode={lv.VaultNode} depositNode={lv.DepositNode} extraFields={lv.Graph.ExtraFields.Count} vaultDto={(lv.Dto.Vault != null)} carryCap={lv.Dto.CarryCap}");

            Rng.SetSeed(4242 + round.Round * 100);

            // fresh actors for everyone still standing
            var actors = new List<Actor>();
            var controllers = new List<BotController>();
            for (int i = 0; i < alive.Count; i++)
            {
                var c = alive[i];
                var a = new Actor { Name = c.Name, IsPlayer = c.IsPlayer, Color = c.Color };
                var sp = i < lv.Spawns.Length ? lv.Spawns[i] : Vector3.Zero;
                a.Pos = sp + new Vector3(0, Actor.HalfY + 0.06f, 0);
                actors.Add(a);
                controllers.Add(new BotController(lv.Graph, c.Personality, i + 1));
            }

            var tracker = new RaceTracker(actors, lv);
            const float Dt = 1f / 60f;
            bool nan = false;
            string exception = null;
            float nextTrace = 0;

            try
            {
                while (!tracker.Ended && tracker.Time < lv.Dto.TimeLimit + 2.0)
                {
                    lv.Update(Dt, actors);
                    for (int k = 0; k < actors.Count; k++)
                        controllers[k].Update(actors[k], lv, Dt, actors);
                    Modes.UpdateRound(lv, actors, tracker, Dt);
                    tracker.Update(Dt);

                    if (DebugTrace && tracker.Time >= nextTrace)
                    {
                        nextTrace += 5f;
                        System.Console.WriteLine($"--- t={tracker.Time:0}s ---");
                        foreach (var a in actors)
                            System.Console.WriteLine($"  {a.Name,-8} x={a.Pos.X,6:0.0} z={a.Pos.Z,7:0.0} score={a.Score,8:0} strikes={a.Strikes} out={a.RoundOut}");
                    }

                    foreach (var a in actors)
                        if (float.IsNaN(a.Pos.X) || float.IsNaN(a.Pos.Y) || float.IsNaN(a.Pos.Z)) { nan = true; break; }
                    if (nan) break;
                }
            }
            catch (Exception e) { exception = e.Message; }

            // resolve eliminations exactly like the game does
            var ranking = tracker.Ranking();
            var eliminated = Elimination.Resolve(tracker, lv, ranking);
            alive.RemoveAll(c => eliminated.Any(x => x.Name == c.Name));

            // ---- verdicts ----
            var problems = new List<string>();
            if (nan) problems.Add("NaN");
            if (exception != null) problems.Add($"EXC:{exception}");

            if (alive.Count == 0) problems.Add("roster wiped out");

            switch (mode)
            {
                case "Race" when actors.Count > 0:
                {
                    int finishers = actors.Count(a => a.Finished);
                    if (finishers == 0) problems.Add("no finisher");
                    break;
                }
                case "SurvivalZone" when actors.Count > 0:
                    if (actors.All(a => a.Score <= 0)) problems.Add("nobody survived any time");
                    break;
                case "StrikesOut":
                    // either someone struck out, or the fallback cut resolves — both fine
                    break;
                case "ScoreCollect" when actors.Count > 0:
                    if (actors.Sum(a => a.Score) <= 0) problems.Add("nothing was deposited");
                    break;
                case "FinaleButton" when actors.Count > 0:
                    if (actors.Max(a => a.Score) <= 0) problems.Add("no winner score");
                    break;
            }

            if (eliminated.Count == 0 && mode != "FinaleButton" && alive.Count > 3)
                problems.Add("roster did not shrink");
            if (alive.Count == 0) problems.Add("nobody left for the finale");

            string winner = ranking.Count > 0 ? ranking[0].Name : "?";
            double bestScore = ranking.Count > 0 ? ranking[0].Score : 0;
            string note = mode switch
            {
                "Race" => $"{actors.Count(a => a.Finished)}/{actors.Count} finished",
                "SurvivalZone" => $"best {bestScore:0}s safe",
                "StrikesOut" => $"{eliminated.Count} struck out",
                "ScoreCollect" => $"{Ui2.Money(actors.Sum(a => a.Score))} deposited",
                "FinaleButton" => $"{winner} wins ({bestScore:0})",
                _ => "",
            };
            var verdict = problems.Count == 0 ? "PASS" : "FAIL (" + string.Join(", ", problems) + ")";
            System.Console.WriteLine($"R{round.Round,2} {mode,-13} roster {alive.Count,2}  {note,-28} {verdict}");
            if (problems.Count > 0) failures++;

            if (r == season.Season.Rounds.Count - 1 && alive.Count > 0 && ranking.Count > 0)
                System.Console.WriteLine($"\nCHAMPION: {ranking[0].Name}  ·  surviving roster {alive.Count}");
        }

        System.Console.WriteLine();
        System.Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED ✔" : $"{failures} CHECK(S) FAILED ✘");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>Tiny local money formatter (harness has no Ui dependency).</summary>
internal static class Ui2
{
    public static string Money(double v) => "$" + v.ToString("N0");
}
