#nullable enable
using System;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    internal static class ShadowRenderer
    {
        private static readonly DiffuseMaterial ShadowMaterial;

        static ShadowRenderer()
        {
            var brush = new SolidColorBrush(Color.FromArgb(80, 0, 0, 0));
            brush.Freeze();
            ShadowMaterial = new DiffuseMaterial(brush);
            ShadowMaterial.Freeze();
        }

        public static Vector3D GetSunDirection(EnvironmentSettings env)
        {
            double azDeg = env.SunAzimuthDeg;
            double elDeg = env.SunElevationDeg;

            if (env.UseSolarClock)
            {
                var (az, el) = env.ComputeSolarPosition();
                azDeg = az;
                elDeg = el;
            }

            double azRad = azDeg * Math.PI / 180.0;
            double elRad = elDeg * Math.PI / 180.0;
            double cosEl = Math.Cos(elRad);

            return new Vector3D(
                cosEl * Math.Sin(azRad),
                cosEl * Math.Cos(azRad),
                Math.Sin(elRad));
        }

        public static ModelVisual3D? ProjectShadow(
            Model3DGroup model,
            Transform3D worldTransform,
            Vector3D sunDir,
            double groundZ)
        {
            if (sunDir.Z <= 0.05) return null;

            var shadowGroup = new Model3DGroup();

            foreach (var child in model.Children)
            {
                if (child is GeometryModel3D gm &&
                    gm.Geometry is MeshGeometry3D srcMesh &&
                    srcMesh.Positions.Count >= 3)
                {
                    var shadowMesh = ProjectMesh(srcMesh, gm.Transform, worldTransform, sunDir, groundZ);
                    if (shadowMesh != null)
                    {
                        shadowGroup.Children.Add(new GeometryModel3D
                        {
                            Geometry = shadowMesh,
                            Material = ShadowMaterial,
                            BackMaterial = ShadowMaterial
                        });
                    }
                }
            }

            if (shadowGroup.Children.Count == 0) return null;

            return new ModelVisual3D { Content = shadowGroup };
        }

        public static ModelVisual3D? ProjectSphereShadow(
            Point3D center, double radius,
            Vector3D sunDir, double groundZ)
        {
            if (sunDir.Z <= 0.05) return null;

            double t = (groundZ - center.Z) / sunDir.Z;
            var projCenter = new Point3D(
                center.X - sunDir.X * t,
                center.Y - sunDir.Y * t,
                groundZ + 0.02);

            double stretch = Math.Max(1.0, 1.0 / Math.Max(0.2, sunDir.Z / sunDir.Length));
            double rx = radius * stretch;
            double ry = radius;

            double azRad = Math.Atan2(sunDir.X, sunDir.Y);

            int segments = 24;
            var mesh = new MeshGeometry3D();
            mesh.Positions.Add(projCenter);
            for (int i = 0; i < segments; i++)
            {
                double angle = 2.0 * Math.PI * i / segments;
                double lx = rx * Math.Cos(angle);
                double ly = ry * Math.Sin(angle);
                double wx = lx * Math.Cos(azRad) - ly * Math.Sin(azRad);
                double wy = lx * Math.Sin(azRad) + ly * Math.Cos(azRad);
                mesh.Positions.Add(new Point3D(
                    projCenter.X + wx,
                    projCenter.Y + wy,
                    groundZ + 0.02));
            }
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1 + i);
                mesh.TriangleIndices.Add(1 + next);
            }

            return new ModelVisual3D
            {
                Content = new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = ShadowMaterial,
                    BackMaterial = ShadowMaterial
                }
            };
        }

        private static MeshGeometry3D? ProjectMesh(
            MeshGeometry3D src,
            Transform3D? localTransform,
            Transform3D worldTransform,
            Vector3D sunDir,
            double groundZ)
        {
            var positions = src.Positions;
            var indices = src.TriangleIndices;
            if (positions.Count < 3 || indices.Count < 3) return null;

            var combinedTransform = localTransform != null
                ? new Transform3DGroup
                {
                    Children = { localTransform, worldTransform }
                }
                : worldTransform;

            var projected = new Point3DCollection(positions.Count);
            for (int i = 0; i < positions.Count; i++)
            {
                var wp = combinedTransform.Transform(positions[i]);
                double t = (groundZ - wp.Z) / sunDir.Z;
                projected.Add(new Point3D(
                    wp.X - sunDir.X * t,
                    wp.Y - sunDir.Y * t,
                    groundZ + 0.02));
            }

            var result = new MeshGeometry3D
            {
                Positions = projected,
                TriangleIndices = new Int32Collection(indices)
            };
            return result;
        }
    }
}
