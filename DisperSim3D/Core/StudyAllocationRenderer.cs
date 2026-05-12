using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Renders <see cref="DispersionStudy"/> cloud envelopes and
    /// <see cref="DetectorAllocation"/> detector markers into the editor viewport.
    /// Each study contributes one semi-transparent isosurface per member cloud,
    /// colour-coded by simulation; each allocation contributes a sphere per
    /// allocated position plus a translucent ball of the detection radius.
    /// </summary>
    public static class StudyAllocationRenderer
    {
        // Stable palette so the SAME simulation always gets the SAME colour across
        // studies — picks by hashing the simulation Id.
        private static readonly Color[] Palette =
        {
            Color.FromRgb(255, 99, 71),    // tomato
            Color.FromRgb(70, 130, 180),   // steel blue
            Color.FromRgb(60, 179, 113),   // medium sea green
            Color.FromRgb(255, 165, 0),    // orange
            Color.FromRgb(186, 85, 211),   // medium orchid
            Color.FromRgb(255, 215, 0),    // gold
            Color.FromRgb(0, 191, 255),    // deep sky blue
            Color.FromRgb(255, 105, 180),  // hot pink
            Color.FromRgb(124, 252, 0),    // lawn green
            Color.FromRgb(220, 20, 60)     // crimson
        };

        private static Color ColorFor(string simId)
        {
            int h = (simId ?? "").GetHashCode();
            int idx = (h & 0x7FFFFFFF) % Palette.Length;
            return Palette[idx];
        }

        /// <summary>Builds a Model3DGroup containing one isosurface per cloud in the
        /// study. Heavy operation — caller should cache the result and only rebuild
        /// when the study's membership/threshold changes. Returns null on empty.</summary>
        public static ModelVisual3D BuildStudyVisual(DispersionStudy study, Scene3D scene)
        {
            if (study == null || scene == null || study.SimulationIds.Count == 0) return null;

            var clouds = DispersionStudyEngine.LoadClouds(study, scene)
                .Where(c => c.IsValid).ToList();
            if (clouds.Count == 0) return null;

            var group = new Model3DGroup();
            foreach (var cs in clouds)
            {
                // Reload the actual continuous field for marching-cubes (the mask is binary).
                // Simpler: render an iso of "threshold" against the same transformed field.
                // We can rebuild the transformed field here directly.
                var sim = scene.Simulations.FirstOrDefault(s => s.Id == cs.SimulationId);
                if (sim == null) continue;
                var field = LoadTransformedField(sim, study, scene);
                if (field == null) continue;

                int fnx = field.GetLength(0);
                double cell = (2 * cs.DomainHalfM) / fnx;
                var origin = new Point3D(-cs.DomainHalfM, -cs.DomainHalfM, 0);
                var mesh = MarchingCubes.GenerateIsosurface(field, study.DetectionThreshold, origin, cell);
                if (mesh == null || mesh.Positions.Count == 0) continue;

                var color = ColorFor(cs.SimulationId);
                var brush = new SolidColorBrush(Color.FromArgb(110, color.R, color.G, color.B));
                brush.Freeze();
                var mat = new DiffuseMaterial(brush);
                group.Children.Add(new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = mat,
                    BackMaterial = mat
                });
            }
            if (group.Children.Count == 0) return null;
            return new ModelVisual3D { Content = group };
        }

        private static double[,,] LoadTransformedField(Simulation sim, DispersionStudy study, Scene3D scene)
        {
            // Same loader path as DispersionStudyEngine; pulled in here so we have
            // the continuous field (not the mask) for marching cubes.
            if (string.IsNullOrEmpty(sim.CasePath) || !System.IO.Directory.Exists(sim.CasePath))
                return null;
            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;
            string speciesName = OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource);

            Models.OpenFoamResult result = null;
            try
            {
                result = OpenFoamResultReader.ReadResults(sim.CasePath, nx, ny, nz, half,
                    scalarFieldName: speciesName);
            }
            catch { result = null; }
            if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                result = TryLoadFlatBin(sim.CasePath, ref nx, ref ny, ref nz, half);
            if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                return null;

            double lastT = result.TimeSteps[result.TimeSteps.Count - 1];
            var raw = result.GetField(lastT);
            if (raw == null) return null;

            var gas = ResolveGas(sim, scene);
            if (FieldTransform.NeedsSpeciesField(study.DetectionQuantity))
                return FieldTransform.FromMassFraction(raw, study.DetectionQuantity, gas);
            return raw;
        }

        private static GasProperties ResolveGas(Simulation sim, Scene3D scene)
        {
            if (sim?.SnapshotSource == null) return null;
            var src = sim.SnapshotSource;
            if (!string.IsNullOrEmpty(src.GasRefId) && scene?.GasLibrary != null)
            {
                var lib = scene.GasLibrary.Find(g => g.Id == src.GasRefId);
                if (lib != null) return lib.AsGasProperties();
            }
            return src.Gas;
        }

        private static Models.OpenFoamResult TryLoadFlatBin(string caseDir,
            ref int nx, ref int ny, ref int nz, double half)
        {
            if (string.IsNullOrEmpty(caseDir) || !System.IO.Directory.Exists(caseDir)) return null;
            if (System.IO.File.Exists(System.IO.Path.Combine(caseDir, "system", "controlDict"))) return null;
            var binFiles = System.IO.Directory.GetFiles(caseDir, "*.bin",
                System.IO.SearchOption.TopDirectoryOnly)
                .Where(p => !System.IO.Path.GetFileNameWithoutExtension(p)
                    .EndsWith("_T", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (binFiles.Length == 0) return null;
            try
            {
                long bytes = new System.IO.FileInfo(binFiles[0]).Length;
                long doubles = bytes / sizeof(double);
                int bestNx = 0;
                for (int c = 8; c <= 1024; c++)
                {
                    int cnz = Math.Max(8, c / 2);
                    if ((long)c * c * cnz == doubles) { bestNx = c; break; }
                }
                if (bestNx > 0) { nx = bestNx; ny = bestNx; nz = Math.Max(8, bestNx / 2); }
            }
            catch { }
            var result = new Models.OpenFoamResult
            {
                GridNx = nx, GridNy = ny, GridNz = nz,
                DomainSizeM = half,
                DomainXMin = -half, DomainXMax = half,
                DomainYMin = -half, DomainYMax = half,
                DomainZMax = half,
                CaseDir = caseDir
            };
            foreach (var f in binFiles)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(f);
                if (double.TryParse(name, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double t))
                {
                    result.TimeSteps.Add(t);
                    result.TimeStepPaths[t] = f;
                }
            }
            result.TimeSteps.Sort();
            result.IsLoaded = result.TimeSteps.Count > 0;
            return result;
        }

        /// <summary>Builds the visual for an allocation: spheres at the allocated
        /// positions (orange) plus a translucent detection-radius ball around each.
        /// Per-cloud coverage status colours the sphere green/red when available.</summary>
        public static ModelVisual3D BuildAllocationVisual(DetectorAllocation alloc, Scene3D scene)
        {
            if (alloc == null || alloc.AllocatedPositions == null
                || alloc.AllocatedPositions.Count == 0) return null;
            var group = new Model3DGroup();
            double r = Math.Max(0.05, alloc.DetectionRadiusM);

            // Solid centre sphere (small, distinct colour).
            var centreBrush = new SolidColorBrush(Color.FromRgb(255, 140, 0));
            centreBrush.Freeze();
            var centreMatGroup = new MaterialGroup();
            centreMatGroup.Children.Add(new DiffuseMaterial(centreBrush));
            centreMatGroup.Children.Add(new EmissiveMaterial(
                new SolidColorBrush(Color.FromArgb(180, 255, 140, 0))));

            // Translucent ball of detection radius.
            var ballBrush = new SolidColorBrush(Color.FromArgb(35, 0, 191, 255));
            ballBrush.Freeze();
            var ballMat = new DiffuseMaterial(ballBrush);

            foreach (var p in alloc.AllocatedPositions)
            {
                var centre = BuildSphere(0.4, 10, 8);
                group.Children.Add(new GeometryModel3D
                {
                    Geometry = centre,
                    Material = centreMatGroup,
                    BackMaterial = centreMatGroup,
                    Transform = new TranslateTransform3D(p.X, p.Y, p.Z)
                });

                var ball = BuildSphere(r, 18, 12);
                group.Children.Add(new GeometryModel3D
                {
                    Geometry = ball,
                    Material = ballMat,
                    BackMaterial = ballMat,
                    Transform = new TranslateTransform3D(p.X, p.Y, p.Z)
                });
            }
            return new ModelVisual3D { Content = group };
        }

        private static MeshGeometry3D BuildSphere(double r, int slices, int stacks)
        {
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(new Point3D(0, 0, r));
            for (int s = 1; s < stacks; s++)
            {
                double phi = Math.PI * s / stacks;
                double sinP = Math.Sin(phi), cosP = Math.Cos(phi);
                for (int sl = 0; sl < slices; sl++)
                {
                    double theta = 2 * Math.PI * sl / slices;
                    mesh.Positions.Add(new Point3D(r * sinP * Math.Cos(theta),
                        r * sinP * Math.Sin(theta), r * cosP));
                }
            }
            mesh.Positions.Add(new Point3D(0, 0, -r));
            int bottom = mesh.Positions.Count - 1;
            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1 + sl);
                mesh.TriangleIndices.Add(1 + next);
            }
            for (int s = 0; s < stacks - 2; s++)
            {
                int row2 = 1 + s * slices, nextRow = 1 + (s + 1) * slices;
                for (int sl = 0; sl < slices; sl++)
                {
                    int next = (sl + 1) % slices;
                    mesh.TriangleIndices.Add(row2 + sl);
                    mesh.TriangleIndices.Add(nextRow + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row2 + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row2 + next);
                }
            }
            int lastRow2 = 1 + (stacks - 2) * slices;
            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(bottom);
                mesh.TriangleIndices.Add(lastRow2 + next);
                mesh.TriangleIndices.Add(lastRow2 + sl);
            }
            return mesh;
        }
    }
}
