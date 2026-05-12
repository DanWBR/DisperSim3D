using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Renders a <see cref="View"/> by sampling its pinned <see cref="Simulation"/>'s
    /// OpenFOAM result. Composes existing <see cref="MarchingCubes"/> for isosurfaces and
    /// <see cref="ColorMapHelper"/> for contour-plane textures.
    /// </summary>
    public static class ViewRenderer
    {
        private static readonly string _logPath = Path.Combine(Path.GetTempPath(), "dispersim3d_view.log");
        private static void LogView(string msg)
        {
            try { File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\r\n"); } catch { }
        }

        /// <summary>
        /// Builds a ModelVisual3D for the view, or null when the simulation is unresolved /
        /// not run / its case directory is missing. Caller adds to the viewport.
        /// </summary>
        public static ModelVisual3D BuildVisual(View view, Simulation sim, Scene3D scene)
        {
            if (view == null || sim == null)
            { LogView("[ViewRenderer] null view or sim"); return null; }
            if (string.IsNullOrEmpty(sim.CasePath) || !Directory.Exists(sim.CasePath))
            { LogView($"[ViewRenderer] missing CasePath: '{sim.CasePath}'"); return null; }

            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

            double[,,] field;
            if (FieldTransform.IsAnalytic(view.FieldProperty))
            {
                // Thermal radiation: doesn't sample from the CFD result — computed
                // analytically from every FireSource in the scene at each cell centre.
                field = FieldTransform.BuildRadiationField(scene, nx, ny, nz, half);
                if (field == null) { LogView("[ViewRenderer] analytic field null"); return null; }
                LogView($"[ViewRenderer] analytic radiation field {nx}x{ny}x{nz}");
            }
            else
            {
                string fieldName = ResolveFieldName(view.FieldProperty, sim);
                if (string.IsNullOrEmpty(fieldName))
                { LogView("[ViewRenderer] fieldName null/empty"); return null; }

                var result = OpenFoamResultReader.ReadResults(sim.CasePath, nx, ny, nz, half,
                    scalarFieldName: fieldName);
                if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                {
                    // FluidX3D dispersion writes flat <time>.bin files at the case root rather
                    // than OpenFOAM-style time subdirectories. Detect that layout and rebuild
                    // an OpenFoamResult by hand so the rest of the rendering pipeline works.
                    result = TryLoadFlatBinCase(sim.CasePath, ref nx, ref ny, ref nz, half);
                    if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                    { LogView($"[ViewRenderer] no results at '{sim.CasePath}' (nx={nx})"); return null; }
                    LogView($"[ViewRenderer] flat-bin loaded: {result.TimeSteps.Count} ts, nx={nx} ny={ny} nz={nz}");
                }

                field = SelectField(result, view.TimeMode, view.SpecificTimeS);
                if (field == null)
                { LogView("[ViewRenderer] SelectField returned null"); return null; }

                // If the user picked a derived quantity (ppm, %LFL, mole fraction, …),
                // transform Y_i → target unit using the simulation source's gas. The
                // ContourXY / Isosurface renderers downstream don't know or care which
                // unit they're rendering — just that the values are physically meaningful.
                if (FieldTransform.NeedsSpeciesField(view.FieldProperty))
                {
                    var gas = ResolveGasForSimulation(sim, scene);
                    field = FieldTransform.FromMassFraction(field, view.FieldProperty, gas);
                }
                LogView($"[ViewRenderer] field {field.GetLength(0)}x{field.GetLength(1)}x{field.GetLength(2)}, prop={view.FieldProperty}, building {view.Kind}");
            }

            switch (view.Kind)
            {
                case ViewKind.Isosurface:
                    return BuildIsosurfaceVisual(view, field, half, nx);
                case ViewKind.ContourXY:
                case ViewKind.ContourXZ:
                case ViewKind.ContourYZ:
                    return BuildContourVisual(view, field, half, nx);
                default:
                    return null;
            }
        }

        private static Models.OpenFoamResult TryLoadFlatBinCase(string caseDir, ref int nx, ref int ny, ref int nz, double half)
        {
            if (string.IsNullOrEmpty(caseDir) || !Directory.Exists(caseDir)) return null;
            string controlDict = Path.Combine(caseDir, "system", "controlDict");
            if (File.Exists(controlDict)) return null;
            var binFiles = Directory.GetFiles(caseDir, "*.bin", SearchOption.TopDirectoryOnly);
            if (binFiles.Length == 0) return null;

            // Infer grid from file size — FluidX3D dispersion uses ny=nx, nz=max(8, nx/2).
            // sim.SnapshotGridResolution may be stale if the user reran with a different
            // resolution, so trust the file. Sample the first .bin and search for nx.
            try
            {
                long bytes = new FileInfo(binFiles[0]).Length;
                long doubles = bytes / sizeof(double);
                int bestNx = 0;
                for (int candidate = 8; candidate <= 1024; candidate++)
                {
                    int cnz = Math.Max(8, candidate / 2);
                    if ((long)candidate * candidate * cnz == doubles) { bestNx = candidate; break; }
                }
                if (bestNx > 0)
                {
                    nx = bestNx; ny = bestNx; nz = Math.Max(8, bestNx / 2);
                }
            }
            catch { /* fall back to caller's nx/ny/nz */ }

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
                string name = Path.GetFileNameWithoutExtension(f);
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

        // ── field resolution ──

        /// <summary>
        /// Maps a curated <see cref="ViewFieldProperty"/> to the actual OpenFOAM field
        /// name written by the solver. Concentration auto-resolves via the source's gas.
        /// </summary>
        private static string ResolveFieldName(ViewFieldProperty prop, Simulation sim)
        {
            // All species-derived quantities (mass/mole fraction, kg/m³, ppm/ppb,
            // %LFL/%UFL) read the SAME source field — the released-species mass
            // fraction Y_i. FieldTransform then converts it to the user's unit.
            if (FieldTransform.NeedsSpeciesField(prop))
                return OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource);
            switch (prop)
            {
                case ViewFieldProperty.Temperature:        return "T";
                case ViewFieldProperty.WindSpeed:          return "magU"; // assumes mag(U) FO
                case ViewFieldProperty.Pressure:           return "p_rgh";
                case ViewFieldProperty.TurbulentK:         return "k";
                case ViewFieldProperty.TurbulentEpsilon:   return "epsilon";
                case ViewFieldProperty.TurbulentViscosity: return "nut";
                default: return null;
            }
        }

        /// <summary>Resolve the gas attached to the simulation's snapshotted source —
        /// preferring the linked gas-library entry, then falling back to the inline
        /// <see cref="ReleaseSource3D.Gas"/>. Used for unit conversion (M, LFL, UFL).</summary>
        private static GasProperties ResolveGasForSimulation(Simulation sim, Scene3D scene)
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

        // ── time-mode selection ──

        private static double[,,] SelectField(OpenFoamResult result, ViewTimeMode mode, double specT)
        {
            if (mode == ViewTimeMode.FinalSnapshot || result.TimeSteps.Count == 1)
            {
                double last = result.TimeSteps[result.TimeSteps.Count - 1];
                return result.GetField(last);
            }
            if (mode == ViewTimeMode.SpecificTime)
            {
                double bestT = result.TimeSteps[0];
                double bestDelta = Math.Abs(specT - bestT);
                foreach (var t in result.TimeSteps)
                {
                    double d = Math.Abs(specT - t);
                    if (d < bestDelta) { bestDelta = d; bestT = t; }
                }
                return result.GetField(bestT);
            }
            // PeakOverTime: per-cell maximum across every loaded timestep.
            double[,,] acc = null;
            foreach (var t in result.TimeSteps)
            {
                var f = result.GetField(t);
                if (f == null) continue;
                if (acc == null)
                {
                    int ax = f.GetLength(0), ay = f.GetLength(1), az = f.GetLength(2);
                    acc = new double[ax, ay, az];
                }
                int nx = acc.GetLength(0), ny = acc.GetLength(1), nz = acc.GetLength(2);
                for (int i = 0; i < nx; i++)
                    for (int j = 0; j < ny; j++)
                        for (int k = 0; k < nz; k++)
                            if (f[i, j, k] > acc[i, j, k]) acc[i, j, k] = f[i, j, k];
            }
            return acc;
        }

        // ── isosurface ──

        private static ModelVisual3D BuildIsosurfaceVisual(View view, double[,,] field,
            double half, int nx)
        {
            int fnx = field.GetLength(0);
            double cell = (2 * half) / fnx;
            var origin = new Point3D(-half, -half, 0);
            var mesh = MarchingCubes.GenerateIsosurface(field, view.IsoValue, origin, cell);
            if (mesh == null || mesh.Positions.Count == 0) return null;

            byte alpha = (byte)Math.Max(0, Math.Min(255, view.Opacity * 255));
            var col = Color.FromArgb(alpha, view.IsoColor.R, view.IsoColor.G, view.IsoColor.B);
            var brush = new SolidColorBrush(col);
            brush.Freeze();
            var mat = new DiffuseMaterial(brush);

            var model = new GeometryModel3D
            {
                Geometry = mesh,
                Material = mat,
                BackMaterial = mat
            };
            model.Freeze();
            var group = new Model3DGroup();
            group.Children.Add(model);
            group.Freeze();
            return new ModelVisual3D { Content = group };
        }

        // ── contour plane ──

        private static ModelVisual3D BuildContourVisual(View view, double[,,] field,
            double half, int gridRes)
        {
            int res = view.SampleResolution > 0 ? view.SampleResolution : 80;
            double minV, maxV;
            if (view.MinValue == 0 && view.MaxValue == 0)
            {
                ComputeFieldRange(field, out minV, out maxV);
            }
            else { minV = view.MinValue; maxV = view.MaxValue; }
            double range = Math.Max(maxV - minV, 1e-9);

            var fld = new OpenFoamConcentrationField(field, half, gridRes);
            var bmp = new WriteableBitmap(res, res, 96, 96, PixelFormats.Bgra32, null);
            var pixels = new uint[res * res];

            // Sample on the slicing plane and fill pixels.
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    double u = (i + 0.5) / res; // 0..1 along axis 1
                    double v = (j + 0.5) / res; // 0..1 along axis 2
                    double x, y, z;
                    GetSamplePosition(view.Kind, view.PlanePosition, u, v, half,
                        out x, out y, out z);
                    double val = fld.EvaluateConcentration(x, y, z);
                    double t = (val - minV) / range;
                    if (t < 0) t = 0; else if (t > 1) t = 1;
                    var c = ColorMapHelper.Sample(view.ColorMap, t);
                    byte a = (byte)Math.Max(0, Math.Min(255, view.Opacity * 255));
                    pixels[j * res + i] = (uint)((a << 24) | (c.R << 16) | (c.G << 8) | c.B);
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, res, res), pixels, res * 4, 0);
            bmp.Freeze();

            var brush = new ImageBrush(bmp) { Stretch = Stretch.Fill };
            brush.Freeze();
            var mat = new DiffuseMaterial(brush);

            var planeMesh = BuildPlaneMesh(view.Kind, view.PlanePosition, half);
            var model = new GeometryModel3D
            {
                Geometry = planeMesh,
                Material = mat,
                BackMaterial = mat
            };
            model.Freeze();
            var group = new Model3DGroup();
            group.Children.Add(model);
            group.Freeze();
            return new ModelVisual3D { Content = group };
        }

        private static void ComputeFieldRange(double[,,] field, out double min, out double max)
        {
            min = double.MaxValue; max = double.MinValue;
            int nx = field.GetLength(0), ny = field.GetLength(1), nz = field.GetLength(2);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double v = field[i, j, k];
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
            if (min == max) max = min + 1e-9;
        }

        /// <summary>For a given pixel (u,v) of the contour bitmap, computes the world XYZ to sample.</summary>
        private static void GetSamplePosition(ViewKind kind, double planePos,
            double u, double v, double half, out double x, out double y, out double z)
        {
            // Texcoord origin in WPF ImageBrush is top-left ((0,0)). The plane mesh
            // assigns the TOP-LEFT texcoord to the y=+half (or z=+2half) corner. So we
            // must flip the v parameter when converting back to world coords, otherwise
            // the bitmap renders mirrored along the vertical axis of each plane.
            switch (kind)
            {
                case ViewKind.ContourXY:
                    x = -half + u * 2 * half;
                    y = +half - v * 2 * half;
                    z = planePos;
                    break;
                case ViewKind.ContourXZ:
                    x = -half + u * 2 * half;
                    y = planePos;
                    z = 2 * half * (1 - v); // top of mesh is z=2half (v=0)
                    break;
                case ViewKind.ContourYZ:
                    x = planePos;
                    y = -half + u * 2 * half;
                    z = 2 * half * (1 - v);
                    break;
                default:
                    x = y = z = 0;
                    break;
            }
        }

        /// <summary>Builds a quad mesh oriented per Kind, at the slicing-plane position.</summary>
        private static MeshGeometry3D BuildPlaneMesh(ViewKind kind, double planePos, double half)
        {
            var mesh = new MeshGeometry3D();
            Point3D p00, p10, p01, p11;
            switch (kind)
            {
                case ViewKind.ContourXY:
                    p00 = new Point3D(-half, -half, planePos);
                    p10 = new Point3D(+half, -half, planePos);
                    p01 = new Point3D(-half, +half, planePos);
                    p11 = new Point3D(+half, +half, planePos);
                    break;
                case ViewKind.ContourXZ:
                    p00 = new Point3D(-half, planePos, 0);
                    p10 = new Point3D(+half, planePos, 0);
                    p01 = new Point3D(-half, planePos, 2 * half);
                    p11 = new Point3D(+half, planePos, 2 * half);
                    break;
                case ViewKind.ContourYZ:
                    p00 = new Point3D(planePos, -half, 0);
                    p10 = new Point3D(planePos, +half, 0);
                    p01 = new Point3D(planePos, -half, 2 * half);
                    p11 = new Point3D(planePos, +half, 2 * half);
                    break;
                default:
                    return mesh;
            }
            mesh.Positions.Add(p00); mesh.Positions.Add(p10);
            mesh.Positions.Add(p01); mesh.Positions.Add(p11);
            mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
            mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
            mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));
            mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(3); mesh.TriangleIndices.Add(2);
            return mesh;
        }
    }
}
