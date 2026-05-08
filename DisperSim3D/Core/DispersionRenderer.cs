using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Renders 3D dispersion visualizations including isosurfaces, particle clouds,
    /// contour planes, wind arrows, vector fields, and streamlines.
    /// </summary>
    public class DispersionRenderer
    {
        private double[,,] _scalarField;
        private bool[,,] _occupancyGrid;
        private int _gridRes;
        private double _domainSize;
        private Point3D _gridOrigin;
        private double _cellSize;
        private readonly Random _rng = new Random(42);

        /// <summary>
        /// Sets up the sampling grid dimensions and origin from the given scenario.
        /// </summary>
        /// <param name="scenario">The dispersion scenario defining grid resolution and domain size.</param>
        public void Initialize(DispersionScenario scenario)
        {
            _gridRes = scenario.GridResolution;
            _domainSize = scenario.DomainSizeM;
            _cellSize = (_domainSize * 2.0) / _gridRes;
            _gridOrigin = new Point3D(-_domainSize, -_domainSize, 0);
            _scalarField = new double[_gridRes, _gridRes, _gridRes / 2 > 0 ? _gridRes / 2 : 1];
        }

        /// <summary>
        /// Samples the concentration field on the grid and generates Marching Cubes isosurfaces for each visible threshold.
        /// </summary>
        /// <param name="engine">Concentration field to sample.</param>
        /// <param name="thresholds">List of concentration thresholds with colors and opacities.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the isosurface meshes.</returns>
        public ModelVisual3D GenerateIsosurfaces(
            IConcentrationField engine, List<DispersionThreshold> thresholds)
        {
            SampleScalarField(engine);

            var visual = new ModelVisual3D();
            var group = new Model3DGroup();

            var sorted = thresholds
                .Where(t => t.Visible)
                .OrderBy(t => t.ConcentrationValue)
                .ToList();

            foreach (var threshold in sorted)
            {
                var mesh = MarchingCubes.GenerateIsosurface(
                    _scalarField, threshold.ConcentrationValue, _gridOrigin, _cellSize);

                if (mesh.Positions.Count == 0) continue;

                var brush = new SolidColorBrush(threshold.Color);
                brush.Opacity = threshold.Opacity;
                brush.Freeze();

                var material = new DiffuseMaterial(brush);
                var model = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = material,
                    BackMaterial = material
                };
                group.Children.Add(model);
            }

            visual.Content = group;
            return visual;
        }

        /// <summary>
        /// Creates a particle cloud visualization by distributing sphere particles within each active puff.
        /// </summary>
        /// <param name="engine">The Gaussian puff engine providing active puff data.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the particle cloud.</returns>
        public ModelVisual3D GenerateParticleCloud(GaussianPuffEngine engine)
        {
            var visual = new ModelVisual3D();
            var group = new Model3DGroup();

            var puffs = engine.ActivePuffs;
            if (puffs.Count == 0) return visual;

            var windVector = new Vector3D(0, 0, 0);
            int particlesPerPuff = Math.Max(5, 100 / Math.Max(1, puffs.Count));
            if (particlesPerPuff > 40) particlesPerPuff = 40;

            var sphereMesh = CreateLowPolySphere(0.5, 4, 4);

            foreach (var puff in puffs)
            {
                double elapsed = engine.CurrentTimeS - puff.EmitTimeS;
                if (elapsed < 0.001) continue;

                double cx = puff.MinBound.X + (puff.MaxBound.X - puff.MinBound.X) * 0.5;
                double cy = puff.MinBound.Y + (puff.MaxBound.Y - puff.MinBound.Y) * 0.5;
                double cz = puff.MinBound.Z + (puff.MaxBound.Z - puff.MinBound.Z) * 0.5;

                double extX = (puff.MaxBound.X - puff.MinBound.X) * 0.5;
                double extY = (puff.MaxBound.Y - puff.MinBound.Y) * 0.5;
                double extZ = (puff.MaxBound.Z - puff.MinBound.Z) * 0.5;

                int seed = puff.GetHashCode();
                var localRng = new Random(seed);

                for (int p = 0; p < particlesPerPuff; p++)
                {
                    double gx = NextGaussian(localRng) * extX * 0.3;
                    double gy = NextGaussian(localRng) * extY * 0.3;
                    double gz = Math.Abs(NextGaussian(localRng) * extZ * 0.3);

                    double px = cx + gx;
                    double py = cy + gy;
                    double pz = Math.Max(0, cz + gz);

                    if (IsOccupied(px, py, pz)) continue;

                    double dist = Math.Sqrt(gx * gx + gy * gy + gz * gz);
                    double maxDist = Math.Sqrt(extX * extX + extY * extY + extZ * extZ) * 0.3;
                    double alpha = Math.Max(0.05, 0.4 * (1.0 - dist / Math.Max(1, maxDist)));

                    var color = Color.FromArgb(
                        (byte)(alpha * 255),
                        200, 200, 200);

                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    var material = new DiffuseMaterial(brush);

                    double scale = 0.3 + extX * 0.02;
                    var transform = new Transform3DGroup();
                    transform.Children.Add(new ScaleTransform3D(scale, scale, scale));
                    transform.Children.Add(new TranslateTransform3D(px, py, pz));

                    var particle = new GeometryModel3D
                    {
                        Geometry = sphereMesh,
                        Material = material,
                        BackMaterial = material,
                        Transform = transform
                    };
                    group.Children.Add(particle);
                }
            }

            visual.Content = group;
            return visual;
        }

        /// <summary>
        /// Creates a single wind direction arrow from a base position.
        /// </summary>
        /// <param name="windVector">The wind velocity vector.</param>
        /// <param name="basePosition">The starting point of the arrow.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the arrow line.</returns>
        public ModelVisual3D GenerateWindArrow(Vector3D windVector, Point3D basePosition)
        {
            var visual = new ModelVisual3D();
            if (windVector.Length < 0.01) return visual;

            var dir = windVector;
            dir.Normalize();
            var arrowLength = 10.0;
            var from = basePosition;
            var to = new Point3D(
                from.X + dir.X * arrowLength,
                from.Y + dir.Y * arrowLength,
                from.Z + dir.Z * arrowLength);

            var lines = new LinesVisual3D
            {
                Color = Colors.DodgerBlue,
                Thickness = 3
            };
            lines.Points.Add(from);
            lines.Points.Add(to);

            visual.Children.Add(lines);
            return visual;
        }

        /// <summary>
        /// Creates a 2D color-mapped contour slice through the concentration field along the configured axis.
        /// </summary>
        /// <param name="engine">Concentration field to evaluate.</param>
        /// <param name="config">Configuration specifying axis, position, color map, and opacity.</param>
        /// <param name="domainMin">Minimum domain coordinate.</param>
        /// <param name="domainMax">Maximum domain coordinate.</param>
        /// <param name="maxConcentration">Maximum concentration value for normalization.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the textured contour plane.</returns>
        public ModelVisual3D GenerateContourPlane(
            IConcentrationField engine, ContourPlaneConfig config,
            double domainMin, double domainMax, double maxConcentration)
        {
            var visual = new ModelVisual3D();
            if (!config.Visible || maxConcentration < 1e-20) return visual;

            int resolution = 80;
            double step = (domainMax - domainMin) / resolution;

            var mesh = new MeshGeometry3D();
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(
                resolution, resolution, 96, 96,
                PixelFormats.Bgra32, null);

            byte[] pixels = new byte[resolution * resolution * 4];

            for (int i = 0; i < resolution; i++)
            {
                for (int j = 0; j < resolution; j++)
                {
                    double u = domainMin + i * step;
                    double v = domainMin + j * step;
                    double x, y, z;

                    switch (config.Axis)
                    {
                        case ContourAxis.XY: x = u; y = v; z = config.Position; break;
                        case ContourAxis.XZ: x = u; y = config.Position; z = v; break;
                        default: x = config.Position; y = u; z = v; break;
                    }

                    double c = engine.EvaluateConcentration(x, y, z);
                    double t = Math.Min(1.0, c / maxConcentration);

                    var color = ColorMapHelper.Sample(config.ColorMap, t);
                    int idx = (j * resolution + i) * 4;
                    pixels[idx + 0] = color.B;
                    pixels[idx + 1] = color.G;
                    pixels[idx + 2] = color.R;
                    pixels[idx + 3] = (byte)(config.Opacity * 255 * (t > 0.001 ? 1.0 : 0.15));
                }
            }

            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, resolution, resolution),
                pixels, resolution * 4, 0);
            bitmap.Freeze();

            Point3D p00, p10, p01, p11;
            switch (config.Axis)
            {
                case ContourAxis.XY:
                    p00 = new Point3D(domainMin, domainMin, config.Position);
                    p10 = new Point3D(domainMax, domainMin, config.Position);
                    p01 = new Point3D(domainMin, domainMax, config.Position);
                    p11 = new Point3D(domainMax, domainMax, config.Position);
                    break;
                case ContourAxis.XZ:
                    p00 = new Point3D(domainMin, config.Position, domainMin);
                    p10 = new Point3D(domainMax, config.Position, domainMin);
                    p01 = new Point3D(domainMin, config.Position, domainMax);
                    p11 = new Point3D(domainMax, config.Position, domainMax);
                    break;
                default:
                    p00 = new Point3D(config.Position, domainMin, domainMin);
                    p10 = new Point3D(config.Position, domainMax, domainMin);
                    p01 = new Point3D(config.Position, domainMin, domainMax);
                    p11 = new Point3D(config.Position, domainMax, domainMax);
                    break;
            }

            mesh.Positions.Add(p00);
            mesh.Positions.Add(p10);
            mesh.Positions.Add(p11);
            mesh.Positions.Add(p01);

            mesh.TextureCoordinates.Add(new System.Windows.Point(0, 1));
            mesh.TextureCoordinates.Add(new System.Windows.Point(1, 1));
            mesh.TextureCoordinates.Add(new System.Windows.Point(1, 0));
            mesh.TextureCoordinates.Add(new System.Windows.Point(0, 0));

            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(1); mesh.TriangleIndices.Add(2);
            mesh.TriangleIndices.Add(0); mesh.TriangleIndices.Add(2); mesh.TriangleIndices.Add(3);

            var brush = new System.Windows.Media.ImageBrush(bitmap);
            brush.Freeze();
            var mat = new DiffuseMaterial(brush);

            var geom = new GeometryModel3D { Geometry = mesh, Material = mat, BackMaterial = mat };
            visual.Content = geom;
            return visual;
        }

        /// <summary>
        /// Assigns a pre-computed 3D scalar field directly, bypassing internal sampling.
        /// </summary>
        /// <param name="field">The 3D array of concentration values.</param>
        public void SetScalarFieldDirect(double[,,] field)
        {
            _scalarField = field;
        }

        /// <summary>
        /// Updates the grid origin and cell size to match the specified domain bounds.
        /// </summary>
        /// <param name="xMin">Minimum X coordinate.</param>
        /// <param name="xMax">Maximum X coordinate.</param>
        /// <param name="yMin">Minimum Y coordinate.</param>
        /// <param name="yMax">Maximum Y coordinate.</param>
        /// <param name="zMax">Maximum Z coordinate.</param>
        public void SetDomainBounds(double xMin, double xMax, double yMin, double yMax, double zMax)
        {
            _gridOrigin = new Point3D(xMin, yMin, 0);
            int nx = _scalarField != null ? _scalarField.GetLength(0) : _gridRes;
            int ny = _scalarField != null ? _scalarField.GetLength(1) : _gridRes;
            _cellSize = Math.Max((xMax - xMin) / nx, (yMax - yMin) / ny);
            _domainSize = Math.Max(xMax - xMin, yMax - yMin) / 2.0;
        }

        /// <summary>
        /// Generates a cloud-like visualization from the pre-computed scalar field using layered isosurfaces
        /// with emissive materials at multiple concentration fractions.
        /// </summary>
        /// <param name="thresholds">Optional user-defined thresholds to render in addition to the built-in layers.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the cloud geometry.</returns>
        public ModelVisual3D GenerateCloudVisual(List<DispersionThreshold> thresholds)
        {
            var visual = new ModelVisual3D();
            if (_scalarField == null) return visual;

            double maxC = GetMaxConcentration();
            if (maxC < 1e-20) return visual;

            var group = new Model3DGroup();

            if (thresholds != null && thresholds.Count > 0)
            {
                var sorted = thresholds.Where(t => t.Visible).OrderByDescending(t => t.ConcentrationValue).ToList();
                foreach (var th in sorted)
                {
                    var mesh = MarchingCubes.GenerateIsosurface(
                        _scalarField, th.ConcentrationValue, _gridOrigin, _cellSize);
                    if (mesh.Positions.Count == 0) continue;
                    group.Children.Add(MakeCloudGeometry(mesh, th.Color, th.Opacity));
                }
            }

            double[] fractions = { 0.50, 0.20, 0.08, 0.03, 0.01, 0.003 };
            double[] opacities = { 0.50, 0.35, 0.22, 0.14, 0.08, 0.04 };
            byte[][] colors = {
                new byte[]{ 255, 60, 20 },
                new byte[]{ 255, 140, 30 },
                new byte[]{ 255, 200, 50 },
                new byte[]{ 220, 220, 200 },
                new byte[]{ 200, 210, 220 },
                new byte[]{ 190, 200, 215 }
            };

            for (int i = 0; i < fractions.Length; i++)
            {
                double isoVal = maxC * fractions[i];
                var mesh = MarchingCubes.GenerateIsosurface(_scalarField, isoVal, _gridOrigin, _cellSize);
                if (mesh.Positions.Count == 0) continue;

                var color = Color.FromRgb(colors[i][0], colors[i][1], colors[i][2]);
                group.Children.Add(MakeCloudGeometry(mesh, color, opacities[i]));
            }

            visual.Content = group;
            return visual;
        }

        private static GeometryModel3D MakeCloudGeometry(MeshGeometry3D mesh, Color color, double opacity)
        {
            var alphaColor = Color.FromArgb((byte)(opacity * 255), color.R, color.G, color.B);
            var brush = new SolidColorBrush(alphaColor);
            brush.Freeze();

            var emissiveColor = Color.FromArgb(
                (byte)(opacity * 80),
                (byte)(color.R * 0.5), (byte)(color.G * 0.5), (byte)(color.B * 0.5));
            var emissiveBrush = new SolidColorBrush(emissiveColor);
            emissiveBrush.Freeze();

            var matGroup = new MaterialGroup();
            matGroup.Children.Add(new DiffuseMaterial(brush));
            matGroup.Children.Add(new EmissiveMaterial(emissiveBrush));

            return new GeometryModel3D
            {
                Geometry = mesh,
                Material = matGroup,
                BackMaterial = matGroup
            };
        }

        /// <summary>
        /// Generates isosurfaces directly from the pre-computed scalar field using emissive materials for each threshold.
        /// </summary>
        /// <param name="thresholds">List of visible thresholds with concentration values, colors, and opacities.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the isosurface meshes.</returns>
        public ModelVisual3D GenerateIsosurfacesDirect(List<DispersionThreshold> thresholds)
        {
            var visual = new ModelVisual3D();
            var group = new Model3DGroup();

            var sorted = thresholds
                .Where(t => t.Visible)
                .OrderBy(t => t.ConcentrationValue)
                .ToList();

            foreach (var threshold in sorted)
            {
                var mesh = MarchingCubes.GenerateIsosurface(
                    _scalarField, threshold.ConcentrationValue, _gridOrigin, _cellSize);
                if (mesh.Positions.Count == 0) continue;

                var color = threshold.Color;
                var alphaColor = Color.FromArgb(
                    (byte)(threshold.Opacity * 255), color.R, color.G, color.B);
                var brush = new SolidColorBrush(alphaColor);
                brush.Freeze();

                var emissiveColor = Color.FromScRgb(
                    (float)(threshold.Opacity * 0.3f),
                    color.ScR * 0.4f, color.ScG * 0.4f, color.ScB * 0.4f);
                var emissiveBrush = new SolidColorBrush(emissiveColor);
                emissiveBrush.Freeze();

                var matGroup = new MaterialGroup();
                matGroup.Children.Add(new DiffuseMaterial(brush));
                matGroup.Children.Add(new EmissiveMaterial(emissiveBrush));

                var model = new GeometryModel3D
                {
                    Geometry = mesh, Material = matGroup, BackMaterial = matGroup
                };
                group.Children.Add(model);
            }

            visual.Content = group;
            return visual;
        }

        /// <summary>
        /// Returns the maximum concentration value in the current scalar field.
        /// </summary>
        /// <returns>The peak concentration value, or 0 if no field is loaded.</returns>
        public double GetMaxConcentration()
        {
            if (_scalarField == null) return 0;
            double max = 0;
            int nx = _scalarField.GetLength(0);
            int ny = _scalarField.GetLength(1);
            int nz = _scalarField.GetLength(2);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                        if (_scalarField[i, j, k] > max) max = _scalarField[i, j, k];
            return max;
        }

        /// <summary>
        /// Gets the half-extent of the simulation domain.
        /// </summary>
        public double DomainSize => _domainSize;

        /// <summary>
        /// Creates a grid of 3D arrows on a horizontal plane, colored by local concentration.
        /// </summary>
        /// <param name="engine">Concentration field to evaluate.</param>
        /// <param name="windVector">Wind direction and magnitude.</param>
        /// <param name="maxConcentration">Maximum concentration for color normalization.</param>
        /// <param name="arrowCount">Number of arrows per axis (default 8).</param>
        /// <param name="planeZ">Z height of the arrow plane (default 2.0).</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the vector field arrows.</returns>
        public ModelVisual3D GenerateVectorField(
            IConcentrationField engine, Vector3D windVector, double maxConcentration,
            int arrowCount = 8, double planeZ = 2.0)
        {
            var visual = new ModelVisual3D();
            if (windVector.Length < 0.01 || maxConcentration < 1e-20) return visual;

            var dir = windVector;
            dir.Normalize();
            double step = (_domainSize * 2.0) / arrowCount;

            var group = new Model3DGroup();

            for (int i = 0; i < arrowCount; i++)
            {
                for (int j = 0; j < arrowCount; j++)
                {
                    double x = -_domainSize + step * (i + 0.5);
                    double y = -_domainSize + step * (j + 0.5);
                    double c = engine.EvaluateConcentration(x, y, planeZ);
                    if (c < maxConcentration * 0.01) continue;

                    double t = Math.Min(1.0, c / maxConcentration);
                    double len = 2.0 + t * step * 0.4;

                    var from = new Point3D(x, y, planeZ);
                    var to = new Point3D(x + dir.X * len, y + dir.Y * len, planeZ + dir.Z * len);

                    var arrowMesh = CreateArrowMesh(from, to, 0.15 + t * 0.2);
                    var color = ColorMapHelper.Sample(ColorMapName.Jet, t);
                    color = Color.FromArgb(180, color.R, color.G, color.B);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();

                    group.Children.Add(new GeometryModel3D
                    {
                        Geometry = arrowMesh,
                        Material = new DiffuseMaterial(brush),
                        BackMaterial = new DiffuseMaterial(brush)
                    });
                }
            }

            visual.Content = group;
            return visual;
        }

        /// <summary>
        /// Generates streamlines by advecting seed points along the wind direction, colored by concentration.
        /// </summary>
        /// <param name="engine">Concentration field to evaluate along each streamline.</param>
        /// <param name="windVector">Wind direction and magnitude.</param>
        /// <param name="seedPoints">Starting positions for the streamlines.</param>
        /// <param name="maxConcentration">Maximum concentration for color normalization.</param>
        /// <param name="steps">Number of integration steps per streamline (default 60).</param>
        /// <param name="stepSize">Distance per integration step (default 1.0).</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the streamline visuals.</returns>
        public ModelVisual3D GenerateStreamlines(
            IConcentrationField engine, Vector3D windVector,
            List<Point3D> seedPoints, double maxConcentration,
            int steps = 60, double stepSize = 1.0)
        {
            var visual = new ModelVisual3D();
            if (windVector.Length < 0.01 || seedPoints == null || seedPoints.Count == 0)
                return visual;

            var dir = windVector;
            dir.Normalize();

            foreach (var seed in seedPoints)
            {
                var lines = new LinesVisual3D { Thickness = 2 };
                var prev = seed;

                for (int s = 0; s < steps; s++)
                {
                    var next = new Point3D(
                        prev.X + dir.X * stepSize,
                        prev.Y + dir.Y * stepSize,
                        Math.Max(0, prev.Z + dir.Z * stepSize));

                    double c = engine.EvaluateConcentration(prev.X, prev.Y, prev.Z);
                    double t = maxConcentration > 1e-20 ? Math.Min(1.0, c / maxConcentration) : 0;
                    var color = ColorMapHelper.Sample(ColorMapName.Jet, t);

                    lines.Points.Add(prev);
                    lines.Points.Add(next);
                    lines.Color = color;

                    prev = next;
                    if (prev.X < -_domainSize || prev.X > _domainSize ||
                        prev.Y < -_domainSize || prev.Y > _domainSize) break;
                }

                visual.Children.Add(lines);
            }

            return visual;
        }

        private static MeshGeometry3D CreateArrowMesh(Point3D from, Point3D to, double radius)
        {
            var mesh = new MeshGeometry3D();
            var dir = to - from;
            double len = dir.Length;
            if (len < 0.01) return mesh;
            dir.Normalize();

            var up = Math.Abs(dir.Z) < 0.99 ? new Vector3D(0, 0, 1) : new Vector3D(1, 0, 0);
            var right = Vector3D.CrossProduct(dir, up);
            right.Normalize();
            up = Vector3D.CrossProduct(right, dir);

            int segments = 4;
            double shaftLen = len * 0.7;
            double headRadius = radius * 2;

            for (int i = 0; i < segments; i++)
            {
                double a1 = 2 * Math.PI * i / segments;
                double a2 = 2 * Math.PI * ((i + 1) % segments) / segments;

                var r1 = right * Math.Cos(a1) * radius + up * Math.Sin(a1) * radius;
                var r2 = right * Math.Cos(a2) * radius + up * Math.Sin(a2) * radius;

                var p0 = from + r1;
                var p1 = from + r2;
                var p2 = from + dir * shaftLen + r1;
                var p3 = from + dir * shaftLen + r2;

                int b = mesh.Positions.Count;
                mesh.Positions.Add(p0); mesh.Positions.Add(p1);
                mesh.Positions.Add(p2); mesh.Positions.Add(p3);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
                mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 3); mesh.TriangleIndices.Add(b + 2);

                var hr1 = right * Math.Cos(a1) * headRadius + up * Math.Sin(a1) * headRadius;
                var hr2 = right * Math.Cos(a2) * headRadius + up * Math.Sin(a2) * headRadius;

                var hp0 = from + dir * shaftLen + hr1;
                var hp1 = from + dir * shaftLen + hr2;

                b = mesh.Positions.Count;
                mesh.Positions.Add(hp0); mesh.Positions.Add(hp1); mesh.Positions.Add(to);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(b + 2);
            }

            return mesh;
        }

        /// <summary>
        /// Changes the grid resolution (clamped to 10-100) and reallocates the scalar field.
        /// </summary>
        /// <param name="newResolution">Desired grid resolution.</param>
        public void UpdateGridResolution(int newResolution)
        {
            if (newResolution < 10) newResolution = 10;
            if (newResolution > 100) newResolution = 100;
            _gridRes = newResolution;
            _cellSize = (_domainSize * 2.0) / _gridRes;
            _scalarField = new double[_gridRes, _gridRes, _gridRes / 2 > 0 ? _gridRes / 2 : 1];
            _occupancyGrid = null;
        }

        /// <summary>
        /// Marks grid cells that are occupied by obstacle bounding boxes in the scene.
        /// </summary>
        /// <param name="scene">The 3D scene containing decorations with bounding boxes.</param>
        public void ComputeOccupancyGrid(Scene3D scene)
        {
            int nz = _gridRes / 2 > 0 ? _gridRes / 2 : 1;
            _occupancyGrid = new bool[_gridRes, _gridRes, nz];

            var boxes = new List<BoundingBox>();

            foreach (var deco in scene.Decorations)
            {
                if (deco.BoundingBox != null)
                    boxes.Add(deco.BoundingBox);
            }

            if (boxes.Count == 0) return;

            double halfCell = _cellSize * 0.5;

            for (int i = 0; i < _gridRes; i++)
            {
                double x = _gridOrigin.X + i * _cellSize + halfCell;
                for (int j = 0; j < _gridRes; j++)
                {
                    double y = _gridOrigin.Y + j * _cellSize + halfCell;
                    for (int k = 0; k < nz; k++)
                    {
                        double z = _gridOrigin.Z + k * _cellSize + halfCell;
                        var pt = new Point3D(x, y, z);

                        for (int b = 0; b < boxes.Count; b++)
                        {
                            if (boxes[b].Contains(pt))
                            {
                                _occupancyGrid[i, j, k] = true;
                                break;
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Checks whether the given world-space point falls inside an occupied grid cell.
        /// </summary>
        /// <param name="x">X coordinate in world space.</param>
        /// <param name="y">Y coordinate in world space.</param>
        /// <param name="z">Z coordinate in world space.</param>
        /// <returns><c>true</c> if the cell is occupied; otherwise <c>false</c>.</returns>
        public bool IsOccupied(double x, double y, double z)
        {
            if (_occupancyGrid == null) return false;

            int i = (int)((x - _gridOrigin.X) / _cellSize);
            int j = (int)((y - _gridOrigin.Y) / _cellSize);
            int k = (int)((z - _gridOrigin.Z) / _cellSize);

            int nz = _occupancyGrid.GetLength(2);
            if (i < 0 || i >= _gridRes || j < 0 || j >= _gridRes || k < 0 || k >= nz)
                return false;

            return _occupancyGrid[i, j, k];
        }

        private void SampleScalarField(IConcentrationField engine)
        {
            int nz = _scalarField.GetLength(2);
            bool hasOccupancy = _occupancyGrid != null;

            for (int i = 0; i < _gridRes; i++)
            {
                double x = _gridOrigin.X + i * _cellSize;
                for (int j = 0; j < _gridRes; j++)
                {
                    double y = _gridOrigin.Y + j * _cellSize;
                    for (int k = 0; k < nz; k++)
                    {
                        if (hasOccupancy && _occupancyGrid[i, j, k])
                        {
                            _scalarField[i, j, k] = 0;
                            continue;
                        }
                        double z = _gridOrigin.Z + k * _cellSize;
                        _scalarField[i, j, k] = engine.EvaluateConcentration(x, y, z);
                    }
                }
            }
        }

        private static MeshGeometry3D CreateLowPolySphere(double radius, int slices, int stacks)
        {
            var mesh = new MeshGeometry3D();

            mesh.Positions.Add(new Point3D(0, 0, radius));

            for (int s = 1; s < stacks; s++)
            {
                double phi = Math.PI * s / stacks;
                double sinPhi = Math.Sin(phi);
                double cosPhi = Math.Cos(phi);

                for (int sl = 0; sl < slices; sl++)
                {
                    double theta = 2 * Math.PI * sl / slices;
                    mesh.Positions.Add(new Point3D(
                        radius * sinPhi * Math.Cos(theta),
                        radius * sinPhi * Math.Sin(theta),
                        radius * cosPhi));
                }
            }

            mesh.Positions.Add(new Point3D(0, 0, -radius));
            int bottomIdx = mesh.Positions.Count - 1;

            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1 + sl);
                mesh.TriangleIndices.Add(1 + next);
            }

            for (int s = 0; s < stacks - 2; s++)
            {
                int row = 1 + s * slices;
                int nextRow = 1 + (s + 1) * slices;
                for (int sl = 0; sl < slices; sl++)
                {
                    int next = (sl + 1) % slices;
                    mesh.TriangleIndices.Add(row + sl);
                    mesh.TriangleIndices.Add(nextRow + sl);
                    mesh.TriangleIndices.Add(nextRow + next);

                    mesh.TriangleIndices.Add(row + sl);
                    mesh.TriangleIndices.Add(nextRow + next);
                    mesh.TriangleIndices.Add(row + next);
                }
            }

            int lastRow = 1 + (stacks - 2) * slices;
            for (int sl = 0; sl < slices; sl++)
            {
                int next = (sl + 1) % slices;
                mesh.TriangleIndices.Add(bottomIdx);
                mesh.TriangleIndices.Add(lastRow + next);
                mesh.TriangleIndices.Add(lastRow + sl);
            }

            mesh.Freeze();
            return mesh;
        }

        /// <summary>
        /// Creates a particle cloud from the pre-computed scalar field (e.g., CFD results),
        /// placing particles in cells above a minimum concentration threshold.
        /// </summary>
        /// <param name="maxConcentration">Maximum concentration for normalizing particle appearance.</param>
        /// <param name="maxParticles">Maximum number of particles to generate (default 2000).</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the CFD particle cloud.</returns>
        public ModelVisual3D GenerateCfdParticleCloud(double maxConcentration, int maxParticles = 2000)
        {
            var visual = new ModelVisual3D();
            if (_scalarField == null || maxConcentration < 1e-20) return visual;

            int nx = _scalarField.GetLength(0);
            int ny = _scalarField.GetLength(1);
            int nz = _scalarField.GetLength(2);

            var candidates = new List<Tuple<double, int, int, int>>();
            double threshold = maxConcentration * 0.005;

            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double c = _scalarField[i, j, k];
                        if (c > threshold)
                            candidates.Add(Tuple.Create(c, i, j, k));
                    }

            if (candidates.Count == 0) return visual;

            var group = new Model3DGroup();
            var sphereMesh = CreateLowPolySphere(0.5, 4, 4);
            double halfCell = _cellSize * 0.5;

            int particlesPlaced = 0;
            double invMax = 1.0 / maxConcentration;

            foreach (var cell in candidates)
            {
                double c = cell.Item1;
                double fraction = Math.Min(1.0, c * invMax);
                int count = (int)(fraction * 4) + 1;
                if (count > 5) count = 5;

                for (int p = 0; p < count && particlesPlaced < maxParticles; p++)
                {
                    double px = _gridOrigin.X + cell.Item2 * _cellSize + halfCell + (NextGaussian(_rng) * halfCell * 0.6);
                    double py = _gridOrigin.Y + cell.Item3 * _cellSize + halfCell + (NextGaussian(_rng) * halfCell * 0.6);
                    double pz = _gridOrigin.Z + cell.Item4 * _cellSize + halfCell + (NextGaussian(_rng) * halfCell * 0.6);
                    if (pz < 0) pz = 0.1;

                    double alpha = 0.08 + 0.35 * fraction;
                    byte a = (byte)(alpha * 255);
                    byte gray = (byte)(220 - 60 * fraction);

                    var color = Color.FromArgb(a, gray, gray, gray);
                    var brush = new SolidColorBrush(color);
                    brush.Freeze();
                    var material = new DiffuseMaterial(brush);

                    double scale = _cellSize * (0.15 + 0.25 * fraction);
                    var transform = new Transform3DGroup();
                    transform.Children.Add(new ScaleTransform3D(scale, scale, scale));
                    transform.Children.Add(new TranslateTransform3D(px, py, pz));

                    group.Children.Add(new GeometryModel3D
                    {
                        Geometry = sphereMesh,
                        Material = material,
                        BackMaterial = material,
                        Transform = transform
                    });
                    particlesPlaced++;
                }
            }

            visual.Content = group;
            return visual;
        }

        private static double NextGaussian(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble();
            double u2 = 1.0 - rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }
    }
}
