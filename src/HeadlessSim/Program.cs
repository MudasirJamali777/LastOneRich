using LastOneRich.Core;
using LastOneRich.Season;
using LastOneRich.World;
using Microsoft.Xna.Framework;

namespace LastOneRich.HeadlessSim;

/// <summary>
/// GPU-free validation harness (GDD 13/14): loads all JSON, runs the twist validator,
/// then actually simulates full races with the same physics/AI code the game uses.
/// Exit code 0 = all cases pass.
/// </summary>
public static class Program
{
    sealed class CaseResult
    {
        public string Name;
        public int Finishers;
        public int Total;
        public string Winner;
        public string WinnerTime;
        public List<string> Eliminated = new();
        public bool NanFound;
        public bool Pass;
    }

    public static int Main(string[] args)
    {
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine(" LAST ONE RICH! — headless validation harness");
        System.Console.WriteLine("==============================================");

        SeasonRun season;
        try
        {
            season = SeasonRun.Create();
        }
        catch (Exception e)
        {
            System.Console.WriteLine($"FATAL: content failed to load: {e}");
            return 2;
        }

        // ---- 1) season / level / twist validator ----
        var errors = TwistValidator.ValidateSeason(season);
        if (errors.Count == 0)
            System.Console.WriteLine("[validator] season.json, levels, twist pools, cash-out offers... OK");
        else
        {
            System.Console.WriteLine("[validator] FAILURES:");
            foreach (var e in errors) System.Console.WriteLine($"  - {e}");
        }

        // ---- 2) simulate the base race + every twist applied ----
        DebugTrace = args.Any(a => a.Contains("debug"));
        DebugFine = args.Any(a => a.Contains("fine"));
        foreach (var a in args)
        {
            if (a.StartsWith("--who=")) FineName = a.Split("=")[1];
            if (a.StartsWith("--until=")) float.TryParse(a.Split("=")[1], out FineUntil);
        }
        int failures = errors.Count;
        var results = new List<CaseResult>
        {
            Simulate(season, "BASE (no twist)", null),
        };
        foreach (var t in season.Twists.Twists)
            results.Add(Simulate(season, $"TWIST: {t.Id}", t));

        System.Console.WriteLine();
        System.Console.WriteLine($"{"CASE",-26} {"FIN",4} {"WINNER",-24} {"ELIMINATED",-22} VERDICT");
        System.Console.WriteLine(new string('-', 86));
        foreach (var r in results)
        {
            var verdict = r.NanFound ? "FAIL (NaN!)" : r.Pass ? "PASS" : "FAIL (too few finishers)";
            var elim = string.Join(", ", r.Eliminated);
            System.Console.WriteLine($"{r.Name,-26} {r.Finishers,3}/{r.Total} {(r.Winner + " " + r.WinnerTime),-24} {elim,-22} {verdict}");
            if (r.NanFound || !r.Pass) failures++;
        }

        System.Console.WriteLine();
        System.Console.WriteLine(failures == 0 ? "ALL CHECKS PASSED ✔" : $"{failures} CHECK(S) FAILED ✘");
        return failures == 0 ? 0 : 1;
    }

    static bool DebugTrace;
    static bool DebugFine;
    static string FineName = "NOVA";
    static float FineUntil = 30f;

    static CaseResult Simulate(SeasonRun season, string name, TwistDTO twist)
    {
        Rng.SetSeed(42);

        if (twist != null)
        {
            var errs = TwistValidator.ValidateSelection(new List<TwistDTO> { twist }, 2);
            if (errs.Count > 0)
                System.Console.WriteLine($"[validator] {twist.Id}: {string.Join("; ", errs)}");
        }

        var twists = twist != null ? new List<TwistDTO> { twist } : null;
        var lv = Level.Load("level01", twists, null);

        // 7 runners: a "virtual player" (average personality) + the 6 rival bots
        var actors = new List<Actor>();
        var controllers = new List<BotController>();
        int i = 0;
        foreach (var c in season.Cast)
        {
            var a = new Actor { Name = c.Name, IsPlayer = c.IsPlayer, Color = c.Color };
            var sp = i < lv.Spawns.Length ? lv.Spawns[i] : Vector3.Zero;
            a.Pos = sp + new Vector3(0, Actor.HalfY + 0.06f, 0);
            actors.Add(a);
            controllers.Add(new BotController(lv.Graph, c.Personality, i + 1));
            i++;
        }

        var tracker = new RaceTracker(actors, lv);
        if (DebugTrace)
            Level.RespawnHook = (a, pos) => System.Console.WriteLine($"  !! {a.Name} FELL at ({pos.X:0.0},{pos.Y:0.0},{pos.Z:0.0}) t={tracker.Time:0.0}");
        const float Dt = 1f / 60f;
        bool nan = false;
        float nextTrace = 0;

        while (!tracker.Ended && tracker.Time < lv.Dto.TimeLimit + 2.0)
        {
            lv.Update(Dt, actors);
            for (int k = 0; k < actors.Count; k++)
                controllers[k].Update(actors[k], lv, Dt, actors);
            tracker.Update(Dt);

            if (DebugTrace && tracker.Time >= nextTrace)
            {
                nextTrace += DebugFine ? 0.5f : 5f;
                if (DebugFine)
                {
                    var ja = actors.First(x => x.Name == FineName);
                    var jc = controllers[actors.IndexOf(ja)];
                    System.Console.WriteLine($"  t={tracker.Time,6:0.00} {FineName} node={jc._node} pos=({ja.Pos.X:0.00},{ja.Pos.Y:0.00},{ja.Pos.Z:0.00}) vel=({ja.Vel.X:0.0},{ja.Vel.Y:0.0},{ja.Vel.Z:0.0}) gnd={ja.OnGround} mv={ja.GroundMover} stag={ja.Stagger:0.00}");
                    if (tracker.Time > FineUntil) break;
                    continue;
                }
                System.Console.WriteLine($"--- t={tracker.Time:0}s ---");
                foreach (var a in actors)
                    System.Console.WriteLine($"  {a.Name,-6} z={a.Pos.Z,7:0.0} x={a.Pos.X,6:0.0} y={a.Pos.Y,5:0.0} gnd={a.OnGround} fin={a.Finished} pen={a.PenaltyAccum:0}");
            }

            foreach (var a in actors)
                if (float.IsNaN(a.Pos.X) || float.IsNaN(a.Pos.Y) || float.IsNaN(a.Pos.Z)) { nan = true; break; }
            if (nan) break;
        }

        if (DebugTrace)
        {
            System.Console.WriteLine("=== final positions ===");
            foreach (var a in actors)
                System.Console.WriteLine($"  {a.Name,-6} z={a.Pos.Z:0.0} x={a.Pos.X:0.0} finished={a.Finished} time={(a.Finished ? a.FinishTime.ToString("0.0") : "-")}");
        }

        var ranking = tracker.Ranking();
        var eliminated = RaceTracker.Eliminate(ranking, tracker.ElimPercent);

        var res = new CaseResult
        {
            Name = name,
            Total = actors.Count,
            Finishers = actors.Count(a => a.Finished),
            Winner = ranking[0].Name,
            WinnerTime = ranking[0].Finished ? ranking[0].FinishTime.ToString("0.0") + "s" : "DNF",
            NanFound = nan,
        };
        foreach (var e in eliminated) res.Eliminated.Add(e.Name);
        res.Pass = res.Finishers >= actors.Count - 2 && !nan; // at most 2 stragglers allowed

        return res;
    }
}
