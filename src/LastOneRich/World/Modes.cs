using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// Round logic per level type (GDD §8/§11.2: rules are pure code + data).
/// GPU-free — GameplayState and HeadlessSim drive the exact same functions.
/// </summary>
public static class Modes
{
    public static bool HasFinish(string mode) => mode == "Race";

    /// <summary>Per-frame mode logic. Call after controllers, before tracker.Update.</summary>
    public static void UpdateRound(Level lv, List<Actor> actors, RaceTracker tr, float dt)
    {
        switch (lv.Dto.Type)
        {
            case "SurvivalZone": UpdateSurvival(lv, actors, dt); break;
            case "StrikesOut": UpdateStrikes(lv, actors, tr, dt); break;
            case "ScoreCollect": UpdateCollect(lv, actors, dt); break;
            case "FinaleButton": UpdateButton(lv, actors, dt); break;
        }
    }

    // ---- SurvivalZone: score = seconds inside the (shrinking) safe zone ----
    static void UpdateSurvival(Level lv, List<Actor> actors, float dt)
    {
        var c = lv.SafeZoneCenter; var h = lv.SafeZoneHalf;
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            if (a.RoundOut) continue;
            bool inside = CollisionWorld.PointInBox(a.Pos, c, h);
            a.OnButton = inside; // reuse for HUD "safe" tint
            if (inside) a.Score += dt;
        }
    }

    // ---- StrikesOut: drone detection = strikes; 3 = out ----
    static void UpdateStrikes(Level lv, List<Actor> actors, RaceTracker tr, float dt)
    {
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            a.StrikeCd = System.MathF.Max(0f, a.StrikeCd - dt);
            if (a.RoundOut) continue;
            for (int d = 0; d < lv.Drones.Count; d++)
            {
                if (lv.Drones[d].Detects(a.Pos, lv.Time) && a.StrikeCd <= 0)
                {
                    if (lv.ShieldHook != null && lv.ShieldHook(a))
                    {
                        a.StrikeCd = 3.5f; // shield ate the scan
                        lv.Sfx?.Invoke("blip");
                        break;
                    }
                    a.Strikes++;
                    a.StrikeCd = 6f;
                    a.Stagger = 0.25f;
                    lv.Sfx?.Invoke("stinger_twist");
                    if (a.Strikes >= 3)
                        tr?.MarkOut(a, lv.Time);
                    break;
                }
            }
        }
    }

    // ---- ScoreCollect: grab at vault, deposit at pad, carrying slows ----
    static void UpdateCollect(Level lv, List<Actor> actors, float dt)
    {
        var v = lv.VaultPos; var vh = lv.VaultHalf;
        var dp = lv.DepositPos; var dh = lv.DepositHalf;
        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            a.CarryCd = System.MathF.Max(0f, a.CarryCd - dt);
            if (a.RoundOut) continue;

            if (a.Carrying < lv.Dto.CarryCap && CollisionWorld.PointInBox(a.Pos, v, vh) && a.CarryCd <= 0)
            {
                a.Carrying++;
                a.CarryCd = 0.45f;
                lv.Sfx?.Invoke("blip");
            }
            if (a.Carrying > 0 && CollisionWorld.PointInBox(a.Pos, dp, dh))
            {
                a.Score += a.Carrying * lv.Dto.BrickValue;
                a.Carrying = 0;
                lv.Sfx?.Invoke("cash");
            }
        }
    }

    // ---- FinaleButton: hold the button to drain rivals; waiting builds your rate ----
    static void UpdateButton(Level lv, List<Actor> actors, float dt)
    {
        var b = lv.Dto.Button;
        if (b == null) return;
        var bp = b.Pos.ToVec3();
        float radius = (float)b.Radius;
        var drain = new List<Actor>(actors.Count);

        for (int i = 0; i < actors.Count; i++)
        {
            var a = actors[i];
            if (a.RoundOut) continue;
            a.ForcedOff = System.Math.Max(0, a.ForcedOff - dt);

            var flat = new Vector2(a.Pos.X - bp.X, a.Pos.Z - bp.Z);
            bool on = flat.Length() <= radius && a.ForcedOff <= 0;
            a.OnButton = on;

            if (on)
            {
                a.Stamina -= b.StaminaDrain * dt;
                a.WaitTime = 0;
                if (a.Stamina <= 0)
                {
                    a.Stamina = 0;
                    a.ForcedOff = b.ForcedOffSeconds;
                    a.OnButton = false;
                }
                else drain.Add(a);
            }
            else
            {
                a.Stamina = System.Math.Min(100, a.Stamina + 14 * dt);
                a.WaitTime += dt;
                a.Score += dt * (2.0 + a.WaitTime * 0.08); // waiting builds multiplier
            }
        }

        lv.ButtonDown = drain.Count > 0; // drives the button's red/green glow

        // the button drains everyone else's score — flat total, so multiple
        // pressers can never outpace the waiting accrual (finale must resolve)
        if (drain.Count > 0)
            for (int i = 0; i < actors.Count; i++)
            {
                var o = actors[i];
                if (!o.OnButton && !o.RoundOut)
                    o.Score = System.Math.Max(0, o.Score - 1.5 * dt);
            }
    }

    // ---- player helpers (shared with sim dummy) ----
    public static double SpeedMultiplierFor(Actor a, Level lv)
    {
        float m = 1f;
        if (a.InSlime) m *= (float)lv.SlimeSpeedMult;
        if (a.InIce) m *= 0.62f;
        if (a.Carrying > 0) m *= 0.62f;
        return m;
    }
}

/// <summary>Rule-aware elimination resolution (GDD §11.2).</summary>
public static class Elimination
{
    public static List<Actor> Resolve(RaceTracker tr, Level lv, List<Actor> ranking)
    {
        var rule = lv.Dto.Elimination.Rule;
        switch (rule)
        {
            case "NoElimination":
                return new List<Actor>();

            case "StrikesOut":
            {
                var outActors = ranking.Where(a => a.Strikes >= 3).ToList();
                if (outActors.Count == 0) outActors = Cut(ranking, lv.Dto.Elimination.Percent); // safety net
                return outActors;
            }

            case "ScoreRankCut":
                return Cut(ranking, lv.Dto.Elimination.Percent);

            case "TopNAdvance":
            {
                int n = lv.Dto.Elimination.TopN;
                return ranking.Count > n ? ranking.Skip(n).ToList() : new List<Actor>();
            }

            case "TimeTrialRankCut":
            default:
                return Cut(ranking, lv.Dto.Elimination.Percent);
        }
    }

    static List<Actor> Cut(List<Actor> ranking, double percent)
    {
        int cut = (int)System.Math.Ceiling(ranking.Count * percent / 100.0);
        cut = System.Math.Clamp(cut, 0, System.Math.Max(0, ranking.Count - 1));
        return ranking.Skip(ranking.Count - cut).ToList();
    }

    public static string StatusFor(Actor a, Level lv)
    {
        switch (lv.Dto.Type)
        {
            case "SurvivalZone": return a.RoundOut ? "OUT" : $"{a.Score:0}s safe";
            case "StrikesOut": return a.Strikes >= 3 ? "3 STRIKES" : $"{a.Strikes} strike" + (a.Strikes == 1 ? "" : "s");
            case "ScoreCollect": return "$" + a.Score.ToString("N0") + " banked";
            case "FinaleButton": return $"score {a.Score:0}";
            default: return a.Finished ? a.FinishTime.ToString("0.0") + "s" : "DNF";
        }
    }
}
