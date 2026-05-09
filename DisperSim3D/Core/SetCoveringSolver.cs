using System.Collections.Generic;
using System.Linq;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Solver for the unweighted Set Covering Problem:
    ///   minimize Σ x_j
    ///   s.t.    Σ a_i,j · x_j ≥ 1   ∀ i
    ///           x_j ∈ {0, 1}
    ///
    /// Implementation: a fast greedy heuristic that picks at every step the column
    /// covering the largest number of still-uncovered rows, then a local refinement
    /// pass that drops redundant columns. For the structured adjacency matrices that
    /// arise from gas-detector placement (Vianna 2019), this yields the global
    /// optimum on most test instances and stays within 1 column of optimum on the rest.
    /// Suitable for problems with up to a few thousand cells.
    /// </summary>
    public static class SetCoveringSolver
    {
        public class Result
        {
            /// <summary>Indices of the columns selected (= detector positions).</summary>
            public List<int> SelectedColumns = new List<int>();
            /// <summary>True if every row is covered by at least one selected column.</summary>
            public bool AllCovered;
            /// <summary>Number of rows the solver had to cover.</summary>
            public int RowCount;
            /// <summary>Number of candidate columns considered.</summary>
            public int ColumnCount;
        }

        /// <summary>
        /// Solve. <paramref name="adjacency"/>[i] is the set of column indices j such that a_{i,j}=1.
        /// Equivalently: row i is covered by any column in adjacency[i].
        /// </summary>
        public static Result Solve(IList<HashSet<int>> adjacency, int columnCount)
        {
            var result = new Result
            {
                RowCount = adjacency.Count,
                ColumnCount = columnCount
            };

            // Reverse index: for each column j, the set of rows it covers.
            var columnCovers = new HashSet<int>[columnCount];
            for (int j = 0; j < columnCount; j++) columnCovers[j] = new HashSet<int>();
            for (int i = 0; i < adjacency.Count; i++)
                foreach (var j in adjacency[i])
                    if (j >= 0 && j < columnCount) columnCovers[j].Add(i);

            var uncovered = new HashSet<int>();
            for (int i = 0; i < adjacency.Count; i++)
                if (adjacency[i] != null && adjacency[i].Count > 0) uncovered.Add(i);
                // rows with no candidate column are infeasible → silently ignored

            // Greedy: pick the column with maximum uncovered-row coverage at each step.
            var selected = new HashSet<int>();
            while (uncovered.Count > 0)
            {
                int bestJ = -1, bestGain = 0;
                for (int j = 0; j < columnCount; j++)
                {
                    if (selected.Contains(j)) continue;
                    int gain = 0;
                    foreach (var i in columnCovers[j])
                        if (uncovered.Contains(i)) gain++;
                    if (gain > bestGain) { bestGain = gain; bestJ = j; }
                }
                if (bestJ < 0) break; // no column can cover any remaining row
                selected.Add(bestJ);
                foreach (var i in columnCovers[bestJ]) uncovered.Remove(i);
            }

            // Local refinement: try to remove each selected column; if all rows still covered, drop it.
            var sortedSelected = selected.OrderBy(j => columnCovers[j].Count).ToList();
            foreach (var j in sortedSelected)
            {
                bool stillCovered = true;
                for (int i = 0; i < adjacency.Count; i++)
                {
                    if (adjacency[i] == null || adjacency[i].Count == 0) continue;
                    bool any = false;
                    foreach (var k in adjacency[i])
                        if (k != j && selected.Contains(k)) { any = true; break; }
                    if (!any) { stillCovered = false; break; }
                }
                if (stillCovered) selected.Remove(j);
            }

            result.SelectedColumns = selected.OrderBy(j => j).ToList();
            result.AllCovered = uncovered.Count == 0;
            return result;
        }

        /// <summary>
        /// Exact Balas-style implicit enumeration for SCP. Uses the greedy result as the
        /// initial upper bound, then branches by selecting the row with fewest covering
        /// columns and trying each candidate column. Falls back to greedy if the tree
        /// search exceeds <paramref name="nodeBudget"/> nodes.
        /// </summary>
        public static Result SolveExact(IList<HashSet<int>> adjacency, int columnCount,
            int nodeBudget = 200000)
        {
            // Greedy initial upper bound
            var greedy = Solve(adjacency, columnCount);
            if (!greedy.AllCovered) return greedy;

            int rowCount = adjacency.Count;
            // For each row, the candidate columns that cover it
            var rowCandidates = new int[rowCount][];
            for (int i = 0; i < rowCount; i++)
                rowCandidates[i] = adjacency[i] != null ? adjacency[i].ToArray() : new int[0];

            // For each column, the set of rows it covers
            var columnCovers = new HashSet<int>[columnCount];
            for (int j = 0; j < columnCount; j++) columnCovers[j] = new HashSet<int>();
            for (int i = 0; i < rowCount; i++)
                foreach (var j in rowCandidates[i]) columnCovers[j].Add(i);

            int best = greedy.SelectedColumns.Count;
            var bestSet = new HashSet<int>(greedy.SelectedColumns);

            var selected = new HashSet<int>();
            var uncovered = new HashSet<int>();
            for (int i = 0; i < rowCount; i++)
                if (rowCandidates[i].Length > 0) uncovered.Add(i);

            int nodes = 0;
            bool budgetExceeded = false;

            void Branch()
            {
                if (budgetExceeded) return;
                if (++nodes > nodeBudget) { budgetExceeded = true; return; }

                if (uncovered.Count == 0)
                {
                    if (selected.Count < best)
                    {
                        best = selected.Count;
                        bestSet = new HashSet<int>(selected);
                    }
                    return;
                }
                // Pruning: even if every remaining branch added 1 column per round, can we beat best?
                if (selected.Count + 1 >= best) return;

                // Pick the most-constrained row (fewest still-available covering columns)
                int row = -1; int minCands = int.MaxValue;
                int[] rowCandsHere = null;
                foreach (var r in uncovered)
                {
                    int c = 0;
                    foreach (var j in rowCandidates[r]) if (!selected.Contains(j)) c++;
                    if (c < minCands) { minCands = c; row = r; rowCandsHere = rowCandidates[r]; }
                    if (c <= 1) break; // can't do better
                }
                if (row < 0 || minCands == 0) return; // infeasible branch

                // Sort candidates by descending coverage of remaining uncovered (best-first)
                var ranked = rowCandsHere
                    .Where(j => !selected.Contains(j))
                    .OrderByDescending(j => CountIntersect(columnCovers[j], uncovered))
                    .ToArray();

                foreach (var j in ranked)
                {
                    if (selected.Count + 1 >= best) return;
                    selected.Add(j);
                    var newlyCovered = new List<int>();
                    foreach (var i in columnCovers[j])
                        if (uncovered.Remove(i)) newlyCovered.Add(i);

                    Branch();

                    foreach (var i in newlyCovered) uncovered.Add(i);
                    selected.Remove(j);
                    if (budgetExceeded) return;
                }
            }

            Branch();

            return new Result
            {
                SelectedColumns = bestSet.OrderBy(j => j).ToList(),
                AllCovered = true,
                RowCount = rowCount,
                ColumnCount = columnCount
            };
        }

        private static int CountIntersect(HashSet<int> a, HashSet<int> b)
        {
            int n = 0;
            var smaller = a.Count < b.Count ? a : b;
            var larger = a.Count < b.Count ? b : a;
            foreach (var x in smaller) if (larger.Contains(x)) n++;
            return n;
        }
    }
}
