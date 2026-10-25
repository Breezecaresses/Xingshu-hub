using System.Collections.Generic;

namespace XingShuRuntime.Targeting
{
    /// <summary>User reference colours aligned to official scene node IDs (2026-10-24).
    /// Each profile is enabled only when every fork and its exits match the current graph.
    /// Neighbour array order is never interpreted as a colour.</summary>
    public static class MonsterBranchProfiles
    {
        private static readonly ulong[] GraphFingerprints = { 6209272535928044769UL, 16641731110874414805UL, 14087354453114690593UL, 17936387095202784223UL, 2537957558524615831UL, 17221437286049177429UL, 3651727366087614363UL, 5110597661036164795UL, 9958749297396515688UL };

        private static readonly int[][][] Profiles = {
            // Map_007, 星趴·梦想号
            new[] { new[] {5,4,6,38}, new[] {11,10,12,32}, new[] {17,16,18,55}, new[] {25,24,26,45},
                new[] {29,28,30,44}, new[] {33,32,34,52}, new[] {35,34,36,39}, new[] {51,50,52,53} },
            // Map_008, 御魂庆典
            new[] { new[] {4,3,5,53}, new[] {12,11,13,56}, new[] {28,27,29,64}, new[] {36,35,37,61},
                new[] {50,51,54,49}, new[] {58,57,62,59} },
            // Map_010, 水乡古镇
            new[] { new[] {1,0,2,47}, new[] {4,3,5,46}, new[] {6,7,5,38}, new[] {9,8,10,20},
                new[] {11,10,12,28}, new[] {14,13,15,29}, new[] {16,15,17,37}, new[] {19,18,0,55} },
            // Map_012, 魔法学院外
            new[] { new[] {7,8,22,6}, new[] {19,20,18,23}, new[] {26,25,27,37}, new[] {30,29,47,31} },
            // Map_013 / Map_014, 魔法学院内，两种场景节点布局
            new[] { new[] {0,1,20,19}, new[] {9,11,8,47}, new[] {14,13,32,15}, new[] {28,30,27,33} },
            new[] { new[] {0,1,20,19}, new[] {11,12,8,47}, new[] {14,13,32,15}, new[] {30,31,27,33} },
            // Map_015, 龙宫游乐园
            new[] { new[] {6,7,37,5}, new[] {15,18,14,16}, new[] {19,20,55,18}, new[] {36,35,37,38} },
            // Map_016, 幽魂暗巷
            new[] { new[] {12,11,36,13}, new[] {22,41,23,21}, new[] {32,31,33,34}, new[] {35,36,34,37} },
            // Map_017, 园林中庭；按参考图的连通路径对齐，不按画面朝向猜节点
            new[] { new[] {17,46,35,0}, new[] {18,45,36,3}, new[] {21,33,29,37}, new[] {22,34,30,38} }
        };

        // Full canonical topology, independent of dictionary/neighbor iteration order.
        private static ulong Fingerprint(IReadOnlyDictionary<int, List<int>> graph)
        {
            ulong hash = 14695981039346656037UL;
            var nodes = new List<int>(graph.Keys); nodes.Sort();
            unchecked
            {
                foreach (int node in nodes)
                {
                    var exits = graph[node] == null ? new List<int>() : new List<int>(graph[node]); exits.Sort();
                    hash = (hash ^ (uint)node) * 1099511628211UL;
                    hash = (hash ^ (uint)exits.Count) * 1099511628211UL;
                    foreach (int exit in exits) hash = (hash ^ (uint)exit) * 1099511628211UL;
                }
            }
            return hash;
        }

        public static Dictionary<int, BranchPriorityEntry> Match(IReadOnlyDictionary<int, List<int>> graph)
        {
            if (graph == null) return new Dictionary<int, BranchPriorityEntry>();
            int forks = 0;
            foreach (var pair in graph) if (pair.Value != null && pair.Value.Count > 2) forks++;
            ulong fingerprint = Fingerprint(graph);
            for (int profileIndex = 0; profileIndex < Profiles.Length; profileIndex++)
            {
                var profile = Profiles[profileIndex];
                if (fingerprint != GraphFingerprints[profileIndex]) continue;
                if (forks != profile.Length) continue;
                bool matches = true;
                foreach (var entry in profile)
                {
                    List<int> exits;
                    if (!graph.TryGetValue(entry[0], out exits) || exits == null || exits.Count != 3 ||
                        !exits.Contains(entry[1]) || !exits.Contains(entry[2]) || !exits.Contains(entry[3]))
                    { matches = false; break; }
                }
                if (!matches) continue;
                var result = new Dictionary<int, BranchPriorityEntry>();
                foreach (var entry in profile) result.Add(entry[0], new BranchPriorityEntry(entry[1],entry[2],entry[3]));
                return result;
            }
            return new Dictionary<int, BranchPriorityEntry>();
        }
    }
}
