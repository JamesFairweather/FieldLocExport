using System;
using System.Collections.Generic;
using System.Linq;

public class RGame
{
    public string GameId { get; set; } = "";
    public Dictionary<string, List<string>> Requests { get; set; } = new();
    // Expected keys: "A" and "B"
}

public class RefereeAssigner
{
    private class Edge
    {
        public int To;
        public int Rev;
        public int Capacity;
        public int Cost;

        public Edge(int to, int rev, int capacity, int cost)
        {
            To = to;
            Rev = rev;
            Capacity = capacity;
            Cost = cost;
        }
    }

    private class MinCostMaxFlow
    {
        private readonly List<Edge>[] _graph;

        public MinCostMaxFlow(int n)
        {
            _graph = new List<Edge>[n];
            for (int i = 0; i < n; i++)
            {
                _graph[i] = new List<Edge>();
            }
        }

        public void AddEdge(int from, int to, int capacity, int cost)
        {
            var forward = new Edge(to, _graph[to].Count, capacity, cost);
            var backward = new Edge(from, _graph[from].Count, 0, -cost);
            _graph[from].Add(forward);
            _graph[to].Add(backward);
        }

        public (int Flow, int Cost) GetMinCostFlow(int source, int sink, int maxFlow)
        {
            int n = _graph.Length;
            int flow = 0;
            int cost = 0;
            int[] potential = new int[n];

            while (flow < maxFlow)
            {
                int[] dist = Enumerable.Repeat(int.MaxValue, n).ToArray();
                int[] parentNode = Enumerable.Repeat(-1, n).ToArray();
                int[] parentEdge = Enumerable.Repeat(-1, n).ToArray();

                dist[source] = 0;

                var pq = new PriorityQueue<(int Node, int Dist), int>();
                pq.Enqueue((source, 0), 0);

                while (pq.Count > 0)
                {
                    var current = pq.Dequeue();
                    int u = current.Node;
                    int d = current.Dist;

                    if (d != dist[u])
                        continue;

                    for (int ei = 0; ei < _graph[u].Count; ei++)
                    {
                        Edge e = _graph[u][ei];
                        if (e.Capacity <= 0)
                            continue;

                        int nd = dist[u] + e.Cost + potential[u] - potential[e.To];
                        if (nd < dist[e.To])
                        {
                            dist[e.To] = nd;
                            parentNode[e.To] = u;
                            parentEdge[e.To] = ei;
                            pq.Enqueue((e.To, nd), nd);
                        }
                    }
                }

                if (dist[sink] == int.MaxValue)
                    break;

                for (int i = 0; i < n; i++)
                {
                    if (dist[i] != int.MaxValue)
                        potential[i] += dist[i];
                }

                int addFlow = maxFlow - flow;
                for (int v = sink; v != source; v = parentNode[v])
                {
                    int u = parentNode[v];
                    int ei = parentEdge[v];
                    addFlow = Math.Min(addFlow, _graph[u][ei].Capacity);
                }

                for (int v = sink; v != source; v = parentNode[v])
                {
                    int u = parentNode[v];
                    int ei = parentEdge[v];
                    Edge forward = _graph[u][ei];
                    Edge backward = _graph[v][forward.Rev];

                    forward.Capacity -= addFlow;
                    backward.Capacity += addFlow;
                    cost += forward.Cost * addFlow;
                }

                flow += addFlow;
            }

            return (flow, cost);
        }

        public List<Edge>[] Graph => _graph;
    }

    public static Dictionary<(string GameId, string Position), string?> AssignReferees(
        List<RGame> games,
        Dictionary<string, int> pastAssignmentCounts)
    {
        string[] positions = { "A", "B" };

        // Collect all referee IDs seen in either requests or historical counts.
        var referees = new HashSet<string>(pastAssignmentCounts.Keys);
        foreach (var game in games)
        {
            foreach (var pos in positions)
            {
                if (game.Requests.TryGetValue(pos, out var refs))
                {
                    foreach (var r in refs)
                        referees.Add(r);
                }
            }
        }

        var refereeList = referees.OrderBy(r => r).ToList();

        // Build slot list: one slot for each game/position
        var slots = new List<(string GameId, string Position)>();
        foreach (var game in games)
        {
            foreach (var pos in positions)
            {
                slots.Add((game.GameId, pos));
            }
        }

        // Build (referee, game) pairs only where the referee requested at least one position in the game.
        var refGamePairs = new List<(string Referee, string GameId)>();
        var requestedTriples = new HashSet<(string Referee, string GameId, string Position)>();

        foreach (var game in games)
        {
            foreach (var referee in refereeList)
            {
                bool wantsThisGame = false;
                foreach (var pos in positions)
                {
                    if (game.Requests.TryGetValue(pos, out var refs) && refs.Contains(referee))
                    {
                        wantsThisGame = true;
                        requestedTriples.Add((referee, game.GameId, pos));
                    }
                }

                if (wantsThisGame)
                {
                    refGamePairs.Add((referee, game.GameId));
                }
            }
        }

        // Node layout:
        // source
        // for each referee:
        //   refFirst
        //   refExtra
        //   refMerge
        // for each (ref,game):
        //   refGame
        // for each slot:
        //   slot
        // sink
        int nextNode = 0;
        int source = nextNode++;

        var refFirstNode = new Dictionary<string, int>();
        var refExtraNode = new Dictionary<string, int>();
        var refMergeNode = new Dictionary<string, int>();

        foreach (var referee in refereeList)
        {
            refFirstNode[referee] = nextNode++;
            refExtraNode[referee] = nextNode++;
            refMergeNode[referee] = nextNode++;
        }

        var refGameNode = new Dictionary<(string Referee, string GameId), int>();
        foreach (var pair in refGamePairs)
        {
            refGameNode[pair] = nextNode++;
        }

        var slotNode = new Dictionary<(string GameId, string Position), int>();
        foreach (var slot in slots)
        {
            slotNode[slot] = nextNode++;
        }

        int sink = nextNode++;
        var mcmf = new MinCostMaxFlow(nextNode);

        int totalSlots = slots.Count;
        int maxPast = pastAssignmentCounts.Count == 0 ? 0 : pastAssignmentCounts.Values.Max();

        // Make this large enough that using a referee for a first assignment
        // is always preferred over giving an extra assignment to someone else.
        int bigDistinct = totalSlots * (maxPast + 1) + 1;

        // Source -> referee supply edges
        foreach (var referee in refereeList)
        {
            mcmf.AddEdge(source, refFirstNode[referee], 1, -bigDistinct);
            mcmf.AddEdge(source, refExtraNode[referee], totalSlots, 0);
            mcmf.AddEdge(refFirstNode[referee], refMergeNode[referee], 1, 0);
            mcmf.AddEdge(refExtraNode[referee], refMergeNode[referee], totalSlots, 0);
        }

        // Ref merge -> (ref, game) cap 1 prevents same referee from taking both positions in one game.
        foreach (var pair in refGamePairs)
        {
            mcmf.AddEdge(refMergeNode[pair.Referee], refGameNode[pair], 1, 0);
        }

        // (ref, game) -> slot if requested
        foreach (var game in games)
        {
            foreach (var pos in positions)
            {
                var slot = (game.GameId, pos);

                if (!game.Requests.TryGetValue(pos, out var refs))
                    continue;

                foreach (var referee in refs)
                {
                    int past = pastAssignmentCounts.TryGetValue(referee, out int count) ? count : 0;
                    mcmf.AddEdge(refGameNode[(referee, game.GameId)], slotNode[slot], 1, past);
                }
            }
        }

        // slot -> sink
        foreach (var slot in slots)
        {
            mcmf.AddEdge(slotNode[slot], sink, 1, 0);
        }

        // Maximize filled slots, then minimize total cost.
        mcmf.GetMinCostFlow(source, sink, totalSlots);

        // Read back assignments.
        var result = new Dictionary<(string GameId, string Position), string?>();
        foreach (var slot in slots)
            result[slot] = null;

        foreach (var game in games)
        {
            foreach (var pos in positions)
            {
                var slot = (game.GameId, pos);
                int slotN = slotNode[slot];

                foreach (var referee in refereeList)
                {
                    var pair = (referee, game.GameId);
                    if (!refGameNode.ContainsKey(pair))
                        continue;

                    int rgNode = refGameNode[pair];

                    foreach (var edge in mcmf.Graph[rgNode])
                    {
                        if (edge.To != slotN)
                            continue;

                        // If reverse edge capacity > 0, then flow was sent.
                        var reverse = mcmf.Graph[slotN][edge.Rev];
                        if (reverse.Capacity > 0)
                        {
                            result[slot] = referee;
                            break;
                        }
                    }

                    if (result[slot] != null)
                        break;
                }
            }
        }

        return result;
    }
}
