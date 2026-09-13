using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// Round timing, ranking and finish detection (GDD 11.2: rules are pure code + data).
/// Race mode: finish trigger + time-based ranking. Other modes: scores / strikes.
/// </summary>
public sealed class RaceTracker
{
    public readonly List<Actor> Actors;
    public readonly float TimeLimit;
    public float Time;
    public bool Ended { get; private set; }
    public string Mode;

    readonly Vector3 _finC, _finH;
    readonly Level _lv;
    readonly bool _hasFinish;

    public RaceTracker(List<Actor> actors, Level lv)
    {
        Actors = actors;
        _lv = lv;
        Mode = lv.Dto.Type;
        TimeLimit = (float)lv.Dto.TimeLimit;
        _hasFinish = Modes.HasFinish(Mode);
        _finC = lv.FinishCenter;
        _finH = lv.FinishHalf;
    }

    public double ElimPercent => _lv.Dto.Elimination.Percent;

    public void MarkOut(Actor a, float time)
    {
        if (a.Finished) return;
        a.Finished = true;
        a.FinishTime = time;
        a.RoundOut = true;
        a.RoundOutTime = time;
    }

    public void Update(float dt)
    {
        if (Ended) return;
        Time += dt;

        if (_hasFinish)
        {
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
        }

        if (Actors.All(a => a.Finished) || Time >= TimeLimit) Ended = true;
    }

    /// <summary>Final ranking — mode-aware ordering (finish time / score / strikes).</summary>
    public List<Actor> Ranking()
    {
        var list = new List<Actor>(Actors);
        switch (Mode)
        {
            case "SurvivalZone":
            case "ScoreCollect":
            case "FinaleButton":
                list.Sort((x, y) =>
                {
                    int c = y.Score.CompareTo(x.Score);           // higher score first
                    if (c != 0) return c;
                    return y.Pos.Z.CompareTo(x.Pos.Z);            // progress tiebreak
                });
                break;

            case "StrikesOut":
                list.Sort((x, y) =>
                {
                    int c = x.Strikes.CompareTo(y.Strikes);       // fewer strikes first
                    if (c != 0) return c;
                    if (x.RoundOut && y.RoundOut) return x.RoundOutTime.CompareTo(y.RoundOutTime);
                    return y.Pos.Z.CompareTo(x.Pos.Z);
                });
                break;

            default:
                var finished = Actors.Where(a => a.Finished).OrderBy(a => a.FinishTime).ToList();
                var dnf = Actors.Where(a => !a.Finished).OrderByDescending(a => a.Pos.Z).ToList();
                list.Clear();
                list.AddRange(finished);
                list.AddRange(dnf);
                break;
        }
        return list;
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

    bool Better(Actor x, Actor y)
    {
        switch (Mode)
        {
            case "SurvivalZone":
            case "ScoreCollect":
            case "FinaleButton":
                if (x.Score != y.Score) return x.Score > y.Score;
                return x.Pos.Z > y.Pos.Z;
            case "StrikesOut":
                if (x.Strikes != y.Strikes) return x.Strikes < y.Strikes;
                return x.Pos.Z > y.Pos.Z;
            default:
                if (x.Finished && y.Finished) return x.FinishTime < y.FinishTime;
                if (x.Finished) return true;
                if (y.Finished) return false;
                return x.Pos.Z > y.Pos.Z;
        }
    }
}
