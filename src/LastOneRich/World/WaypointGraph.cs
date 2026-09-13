using Microsoft.Xna.Framework;

namespace LastOneRich.World;

/// <summary>
/// Waypoint graph navigation — no navmesh (GDD section 10). Dijkstra distance-to-goal;
/// bots pick edges weighted by length, hazard risk and their RouteGreed personality.
/// </summary>
public sealed class WaypointGraph
{
    public Vector3[] Nodes = System.Array.Empty<Vector3>();
    public HashSet<string>[] Flags = System.Array.Empty<HashSet<string>>();
    public List<(int a, int b, float risk)> Edges = new();
    public List<(int node, float risk)>[] Adj = System.Array.Empty<List<(int, float)>>();
    public double[] DistToGoal = System.Array.Empty<double>();
    public readonly Dictionary<int, double[]> ExtraFields = new();
    public int GoalNode { get; private set; } = -1;

    public static WaypointGraph FromDTO(WaypointsDTO dto)
    {
        var g = new WaypointGraph
        {
            Nodes = new Vector3[dto.Nodes.Count],
            Flags = new HashSet<string>[dto.Nodes.Count],
            Adj = new List<(int, float)>[dto.Nodes.Count],
        };
        for (int i = 0; i < dto.Nodes.Count; i++)
        {
            g.Nodes[i] = dto.Nodes[i].Pos.ToVec3();
            g.Flags[i] = new HashSet<string>(dto.Nodes[i].Flags ?? System.Array.Empty<string>());
            g.Adj[i] = new List<(int, float)>();
        }
        foreach (var e in dto.Edges)
        {
            if (e == null || e.Length < 2) continue;
            int a = (int)e[0], b = (int)e[1];
            float risk = e.Length >= 3 ? (float)e[2] : 0f;
            if (a < 0 || b < 0 || a >= g.Nodes.Length || b >= g.Nodes.Length) continue;
            g.Edges.Add((a, b, risk));
            g.Adj[a].Add((b, risk));
            g.Adj[b].Add((a, risk));
        }
        return g;
    }

    public int Nearest(Vector3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int i = 0; i < Nodes.Length; i++)
        {
            var d = Vector3.DistanceSquared(p, Nodes[i]);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    /// <summary>Dijkstra from the goal node over undirected edges; risk adds cost scaled by fixed 0.5 factor.</summary>
    public void ComputeDistances(int goal)
    {
        GoalNode = goal;
        int n = Nodes.Length;
        DistToGoal = new double[n];
        var done = new bool[n];
        Array.Fill(DistToGoal, double.PositiveInfinity);
        DistToGoal[goal] = 0;
        for (int iter = 0; iter < n; iter++)
        {
            int u = -1;
            double best = double.PositiveInfinity;
            for (int i = 0; i < n; i++)
                if (!done[i] && DistToGoal[i] < best) { best = DistToGoal[i]; u = i; }
            if (u < 0) break;
            done[u] = true;
            foreach (var (v, risk) in Adj[u])
            {
                float len = Vector3.Distance(Nodes[u], Nodes[v]);
                double cost = len * (1.0 + 0.5 * risk);
                if (DistToGoal[u] + cost < DistToGoal[v]) DistToGoal[v] = DistToGoal[u] + cost;
            }
        }
    }

    /// <summary>Dijkstra into an extra named field (vault / deposit routes).</summary>
    public void ComputeExtra(int goal)
    {
        if (ExtraFields.ContainsKey(goal) || Nodes.Length == 0) return;
        var dist = new double[Nodes.Length];
        var done = new bool[Nodes.Length];
        Array.Fill(dist, double.PositiveInfinity);
        dist[goal] = 0;
        for (int iter = 0; iter < Nodes.Length; iter++)
        {
            int u = -1; double best = double.PositiveInfinity;
            for (int i = 0; i < Nodes.Length; i++)
                if (!done[i] && dist[i] < best) { best = dist[i]; u = i; }
            if (u < 0) break;
            done[u] = true;
            foreach (var (v, risk) in Adj[u])
            {
                float len = Vector3.Distance(Nodes[u], Nodes[v]);
                double cost = dist[u] + len * (1.0 + 0.5 * risk);
                if (dist[u] + cost < dist[v]) dist[v] = dist[u] + cost;
            }
        }
        ExtraFields[goal] = dist;
    }

    /// <summary>Next hop toward the goal, biased by this bot's route greed (risky-but-short edges).</summary>
    public int BestNext(int from, double routeGreed) => BestNext(from, routeGreed, null);

    public int BestNext(int from, double routeGreed, double[] field)
    {
        field ??= DistToGoal;
        if (field[from] <= 0.0001) return from; // at goal — absorbing
        int best = from;
        double bestCost = double.PositiveInfinity;
        foreach (var (v, risk) in Adj[from])
        {
            if (double.IsInfinity(field[v])) continue;
            float len = Vector3.Distance(Nodes[from], Nodes[v]);
            double cost = field[v] + len * (1.0 + risk * (1.0 - routeGreed));
            if (cost < bestCost) { bestCost = cost; best = v; }
        }
        return best;
    }
}
