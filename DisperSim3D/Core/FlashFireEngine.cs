using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Flash fire from an ignited dispersion result.
    ///
    /// Three decisions carry the result:
    ///
    /// <para><b>Only the connected cloud burns.</b> A flood fill from the ignition cell
    /// over the envelope mask separates the pocket that catches fire from pockets that
    /// do not — the difference between summing every flammable cell in the domain and
    /// summing what the ignition actually reaches. Obstacles block the fill, so a cloud
    /// split by a wall does not burn through it.</para>
    ///
    /// <para><b>The envelope is drawn at a fraction of the LFL</b>, 0.5 by default. The
    /// concentration field is time-averaged; the momentary flammable cloud extends past
    /// the averaged LFL contour. The rich core above the UFL is inside the envelope too
    /// — it burns as it entrains air.</para>
    ///
    /// <para><b>Propagation is a constant-speed burn-back.</b> Arrival time is the
    /// geodesic distance from the ignition point, around obstacles, divided by the flame
    /// speed. The point is the duration of the exposure, not the structure of the flame:
    /// nothing here resolves combustion.</para>
    /// </summary>
    public static class FlashFireEngine
    {
        /// <summary>Arrival time stored in cells the flame never reaches. Large and
        /// finite so an isosurface at time <c>t</c> still separates "burnt by t" from
        /// everything else, and so interpolation never produces NaN.</summary>
        public const double UnreachedArrivalS = 1.0e9;

        /// <summary>How far from the given position the engine will look for a
        /// flammable cell before giving up, in cells. Keeps a click that lands one cell
        /// outside the cloud from silently producing an empty result.</summary>
        private const int IgnitionSearchCells = 2;

        public sealed class FlashFireResult
        {
            /// <summary>Time (s) at which the flame reaches each cell;
            /// <see cref="UnreachedArrivalS"/> where it never does.</summary>
            public double[,,] ArrivalTimeS;

            /// <summary>1 inside the burnt envelope, 0 outside. Take the isosurface at
            /// 0.5 to get the hazard envelope.</summary>
            public double[,,] EnvelopeMask;

            /// <summary>Volume of the burnt envelope (m³).</summary>
            public double EnvelopeVolumeM3;

            /// <summary>Volume inside the envelope that is genuinely between LFL and
            /// UFL (m³) — the flammable core, always smaller than the envelope.</summary>
            public double FlammableVolumeM3;

            /// <summary>Fuel mass inside the burnt envelope (kg).</summary>
            public double BurnedMassKg;

            /// <summary>Time for the flame to cross the whole envelope (s) — the
            /// exposure duration a thermal dose calculation needs.</summary>
            public double DurationS;

            /// <summary>Farthest the flame travels from the ignition point (m).</summary>
            public double MaxReachM;

            /// <summary>Number of disconnected pockets in the envelope mask, ignited or
            /// not. More than one means the ignition point chose which cloud burns.</summary>
            public int ConnectedComponents;

            /// <summary>Cells that burned.</summary>
            public int BurnedCells;

            /// <summary>False when the ignition point is not in, or next to, a
            /// flammable cell — nothing burns and every field is empty.</summary>
            public bool Ignited;

            /// <summary>Cell the ignition resolved to, or (-1,-1,-1).</summary>
            public (int I, int J, int K) IgnitionCell = (-1, -1, -1);
        }

        /// <summary>
        /// Burns the cloud in <paramref name="concentrationKgM3"/> from
        /// <paramref name="ignition"/>.
        /// </summary>
        /// <param name="concentrationKgM3">Concentration field [x,y,z] in kg/m³.</param>
        /// <param name="lflKgM3">Lower flammability limit (kg/m³).</param>
        /// <param name="uflKgM3">Upper flammability limit (kg/m³); 0 disables the rich cut
        /// when reporting the flammable core.</param>
        /// <param name="ignition">Ignition point, time and burn parameters.</param>
        /// <param name="originX">World X of cell [0,0,0]'s centre.</param>
        /// <param name="originY">World Y of cell [0,0,0]'s centre.</param>
        /// <param name="originZ">World Z of cell [0,0,0]'s centre.</param>
        /// <param name="cellX">Cell size in X (m).</param>
        /// <param name="cellY">Cell size in Y (m).</param>
        /// <param name="cellZ">Cell size in Z (m).</param>
        /// <param name="obstacles">Solid boxes the flame cannot cross; may be null.</param>
        public static FlashFireResult Compute(
            double[,,] concentrationKgM3, double lflKgM3, double uflKgM3, IgnitionEvent ignition,
            double originX, double originY, double originZ,
            double cellX, double cellY, double cellZ,
            IList<BoundingBox> obstacles = null)
        {
            var result = new FlashFireResult();
            if (concentrationKgM3 == null || ignition == null || lflKgM3 <= 0)
                return Empty(concentrationKgM3, result);

            int nx = concentrationKgM3.GetLength(0);
            int ny = concentrationKgM3.GetLength(1);
            int nz = concentrationKgM3.GetLength(2);

            double fraction = ignition.EnvelopeFraction > 0 ? ignition.EnvelopeFraction : 0.5;
            double envelopeThreshold = fraction * lflKgM3;
            double flameSpeed = ignition.FlameSpeedMS > 0 ? ignition.FlameSpeedMS : 10.0;

            // ── Envelope mask ───────────────────────────────────────────────
            var inEnvelope = new bool[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        if (concentrationKgM3[i, j, k] < envelopeThreshold) continue;
                        if (obstacles != null && IsInsideObstacle(
                                originX + i * cellX, originY + j * cellY, originZ + k * cellZ, obstacles))
                            continue;
                        inEnvelope[i, j, k] = true;
                    }

            result.ConnectedComponents = CountComponents(inEnvelope, nx, ny, nz);

            // ── Ignition cell ───────────────────────────────────────────────
            // The ignition has to land on genuinely flammable gas (>= LFL), not merely
            // inside the half-LFL envelope: the envelope is a fluctuation allowance, not
            // a mixture that lights.
            var cell = FindIgnitionCell(concentrationKgM3, inEnvelope, lflKgM3, ignition.Position,
                originX, originY, originZ, cellX, cellY, cellZ, nx, ny, nz);
            if (cell.I < 0) return Empty(concentrationKgM3, result);

            result.Ignited = true;
            result.IgnitionCell = cell;

            // ── Burn-back ───────────────────────────────────────────────────
            // Dijkstra, not a plain BFS: the distance has to follow the cloud around an
            // obstacle rather than cut through it, and the three cell sizes differ.
            var distance = new double[nx, ny, nz];
            var settled = new bool[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        distance[i, j, k] = double.PositiveInfinity;

            var queue = new PriorityQueue<(int I, int J, int K), double>();
            distance[cell.I, cell.J, cell.K] = 0;
            queue.Enqueue(cell, 0);

            Span<int> di = stackalloc int[] { 1, -1, 0, 0, 0, 0 };
            Span<int> dj = stackalloc int[] { 0, 0, 1, -1, 0, 0 };
            Span<int> dk = stackalloc int[] { 0, 0, 0, 0, 1, -1 };
            var stepLength = new[] { cellX, cellX, cellY, cellY, cellZ, cellZ };

            while (queue.TryDequeue(out var current, out double currentDistance))
            {
                if (settled[current.I, current.J, current.K]) continue;
                settled[current.I, current.J, current.K] = true;
                if (currentDistance > result.MaxReachM) result.MaxReachM = currentDistance;

                for (int n = 0; n < 6; n++)
                {
                    int ni = current.I + di[n], nj = current.J + dj[n], nk = current.K + dk[n];
                    if (ni < 0 || ni >= nx || nj < 0 || nj >= ny || nk < 0 || nk >= nz) continue;
                    if (!inEnvelope[ni, nj, nk] || settled[ni, nj, nk]) continue;

                    double candidate = currentDistance + stepLength[n];
                    if (candidate >= distance[ni, nj, nk]) continue;
                    distance[ni, nj, nk] = candidate;
                    queue.Enqueue((ni, nj, nk), candidate);
                }
            }

            // ── Fields and statistics ───────────────────────────────────────
            double cellVolume = cellX * cellY * cellZ;
            var arrival = new double[nx, ny, nz];
            var mask = new double[nx, ny, nz];

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        if (!settled[i, j, k])
                        {
                            arrival[i, j, k] = UnreachedArrivalS;
                            continue;
                        }

                        double t = distance[i, j, k] / flameSpeed;
                        arrival[i, j, k] = t;
                        mask[i, j, k] = 1.0;
                        if (t > result.DurationS) result.DurationS = t;

                        double c = concentrationKgM3[i, j, k];
                        result.BurnedCells++;
                        result.EnvelopeVolumeM3 += cellVolume;
                        result.BurnedMassKg += c * cellVolume;
                        if (c >= lflKgM3 && (uflKgM3 <= 0 || c <= uflKgM3))
                            result.FlammableVolumeM3 += cellVolume;
                    }

            result.ArrivalTimeS = arrival;
            result.EnvelopeMask = mask;
            return result;
        }

        /// <summary>
        /// Convenience overload for the renderers: derives the grid mapping from the
        /// domain half-width the way the isosurface builders do — cubic cells of
        /// <c>2·half/nx</c> with the origin at <c>(-half, -half, 0)</c> — and pulls the
        /// obstacles out of the scene.
        /// </summary>
        public static double[,,] BuildViewField(Scene3D scene, View view,
            double[,,] concentrationKgM3, GasProperties gas, double halfM)
        {
            if (scene == null || view == null || concentrationKgM3 == null) return null;

            var ignition = FindIgnitionFor(scene, view.SimulationId);
            if (ignition == null) return null;

            double lfl = gas != null && gas.LFL > 0 ? gas.LFL : 0.033;
            double ufl = gas != null && gas.UFL > 0 ? gas.UFL : 0;
            double cell = 2.0 * halfM / concentrationKgM3.GetLength(0);

            var result = Compute(concentrationKgM3, lfl, ufl, ignition,
                -halfM, -halfM, 0, cell, cell, cell, CollectObstacles(scene));

            return view.FieldProperty == ViewFieldProperty.FlashFireArrivalS
                ? result.ArrivalTimeS
                : result.EnvelopeMask;
        }

        /// <summary>The ignition attached to a simulation, or null.</summary>
        public static IgnitionEvent FindIgnitionFor(Scene3D scene, string simulationId)
        {
            if (scene?.Ignitions == null || string.IsNullOrEmpty(simulationId)) return null;
            foreach (var ignition in scene.Ignitions)
                if (ignition != null && ignition.SimulationId == simulationId) return ignition;
            return null;
        }

        /// <summary>Obstacle boxes from the scene decorations. The portable path — the
        /// WPF host can voxelise imported meshes per triangle, but every consumer here
        /// only needs "does the flame get through".</summary>
        public static List<BoundingBox> CollectObstacles(Scene3D scene)
        {
            var boxes = new List<BoundingBox>();
            if (scene?.Decorations == null) return boxes;
            foreach (var deco in scene.Decorations)
                if (deco?.BoundingBox != null) boxes.Add(deco.BoundingBox);
            return boxes;
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static FlashFireResult Empty(double[,,] template, FlashFireResult result)
        {
            int nx = template?.GetLength(0) ?? 1;
            int ny = template?.GetLength(1) ?? 1;
            int nz = template?.GetLength(2) ?? 1;
            var arrival = new double[nx, ny, nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        arrival[i, j, k] = UnreachedArrivalS;
            result.ArrivalTimeS = arrival;
            result.EnvelopeMask = new double[nx, ny, nz];
            return result;
        }

        private static (int I, int J, int K) FindIgnitionCell(
            double[,,] concentration, bool[,,] inEnvelope, double lfl, Point3D position,
            double originX, double originY, double originZ,
            double cellX, double cellY, double cellZ, int nx, int ny, int nz)
        {
            int ci = (int)Math.Round((position.X - originX) / cellX);
            int cj = (int)Math.Round((position.Y - originY) / cellY);
            int ck = (int)Math.Round((position.Z - originZ) / cellZ);

            var best = (I: -1, J: -1, K: -1);
            double bestDistance = double.MaxValue;

            for (int i = ci - IgnitionSearchCells; i <= ci + IgnitionSearchCells; i++)
                for (int j = cj - IgnitionSearchCells; j <= cj + IgnitionSearchCells; j++)
                    for (int k = ck - IgnitionSearchCells; k <= ck + IgnitionSearchCells; k++)
                    {
                        if (i < 0 || i >= nx || j < 0 || j >= ny || k < 0 || k >= nz) continue;
                        if (!inEnvelope[i, j, k] || concentration[i, j, k] < lfl) continue;

                        double dx = (originX + i * cellX) - position.X;
                        double dy = (originY + j * cellY) - position.Y;
                        double dz = (originZ + k * cellZ) - position.Z;
                        double d2 = dx * dx + dy * dy + dz * dz;
                        if (d2 < bestDistance) { bestDistance = d2; best = (i, j, k); }
                    }

            return best;
        }

        private static int CountComponents(bool[,,] mask, int nx, int ny, int nz)
        {
            var seen = new bool[nx, ny, nz];
            var stack = new Stack<(int I, int J, int K)>();
            int components = 0;

            for (int i0 = 0; i0 < nx; i0++)
                for (int j0 = 0; j0 < ny; j0++)
                    for (int k0 = 0; k0 < nz; k0++)
                    {
                        if (!mask[i0, j0, k0] || seen[i0, j0, k0]) continue;
                        components++;
                        seen[i0, j0, k0] = true;
                        stack.Push((i0, j0, k0));

                        while (stack.Count > 0)
                        {
                            var (i, j, k) = stack.Pop();
                            PushNeighbour(mask, seen, stack, i + 1, j, k, nx, ny, nz);
                            PushNeighbour(mask, seen, stack, i - 1, j, k, nx, ny, nz);
                            PushNeighbour(mask, seen, stack, i, j + 1, k, nx, ny, nz);
                            PushNeighbour(mask, seen, stack, i, j - 1, k, nx, ny, nz);
                            PushNeighbour(mask, seen, stack, i, j, k + 1, nx, ny, nz);
                            PushNeighbour(mask, seen, stack, i, j, k - 1, nx, ny, nz);
                        }
                    }

            return components;
        }

        private static void PushNeighbour(bool[,,] mask, bool[,,] seen,
            Stack<(int I, int J, int K)> stack, int i, int j, int k, int nx, int ny, int nz)
        {
            if (i < 0 || i >= nx || j < 0 || j >= ny || k < 0 || k >= nz) return;
            if (!mask[i, j, k] || seen[i, j, k]) return;
            seen[i, j, k] = true;
            stack.Push((i, j, k));
        }

        private static bool IsInsideObstacle(double x, double y, double z, IList<BoundingBox> obstacles)
        {
            for (int b = 0; b < obstacles.Count; b++)
            {
                var box = obstacles[b];
                if (box == null) continue;
                if (x >= box.Min.X && x <= box.Max.X
                    && y >= box.Min.Y && y <= box.Max.Y
                    && z >= box.Min.Z && z <= box.Max.Z)
                    return true;
            }
            return false;
        }
    }
}
