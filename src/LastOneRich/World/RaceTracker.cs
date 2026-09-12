using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// Race timing, live ranking and elimination rule (GDD 11.2: rules are pure code + data).
/// </summary>
public sealed class RaceTracker
{
    public readonly List<Actor> Actors;
    public readonly float TimeLimit;
    public float Time;
    public bool Ended { get; private set; }

    readonly Vector3 _finC, _finH;
    readonly Level _lv;

    public RaceTracker(List<Actor> actors, Level lv)
    {
        Actors = actors;
        _lv = lv;
        TimeLimit = (float)lv.Dto.TimeLimit;
        _finC = lv.FinishCenter;
        _finH = lv.FinishHalf;
    }

    public double ElimPercent => _lv.Dto.Elimination.Percent;

    public void Update(float dt)
    {
        if (Ended) return;
        Time += dt;
        foreach (var a in Actors)
        {
            if (a.Finished) continue;
            var rel = a.Pos - _finC;
            if (System.MathF.Abs(rel.X) < _finH.X && System.MathF.Abs(rel.Y) < _finH.Y && System.MathF.Abs(rel.Z) < _finH.Z)
            {
                a.Finished = true;
                a.FinishTime = Time + a.PenaltyAccum;
            }
        }
        if (Actors.All(a => a.Finished) || Time >= TimeLimit) Ended = true;
    }

    /// <summary>Final ranking: finishers by time, then DNFs by progress along the course.</summary>
    public List<Actor> Ranking()
    {
        var finished = Actors.Where(a => a.Finished).OrderBy(a => a.FinishTime).ToList();
        var dnf = Actors.Where(a => !a.Finished).OrderByDescending(a => a.Pos.Z).ToList();
        finished.AddRange(dnf);
        return finished;
    }

    public int LiveRank(Actor a)
    {
        int rank = 1;
        foreach (var o in Actors)
        {
            if (o == a) continue;
            if (Better(o, a)) rank++;
        }
        return rank;
    }

    static bool Better(Actor x, Actor y)
    {
        if (x.Finished && y.Finished) return x.FinishTime < y.FinishTime;
        if (x.Finished) return true;
        if (y.Finished) return false;
        return x.Pos.Z > y.Pos.Z;
    }

    /// <summary>The bottom N% of the ranking are eliminated (rule: TimeTrialRankCut).</summary>
    public static List<Actor> Eliminate(List<Actor> ranking, double percent)
    {
        int cut = (int)System.Math.Ceiling(ranking.Count * percent / 100.0);
        cut = System.Math.Clamp(cut, 0, System.Math.Max(0, ranking.Count - 1));
        return ranking.Skip(ranking.Count - cut).ToList();
    }
}
