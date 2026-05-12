using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Renders a 3D wind rose diagram as a set of colored wedge segments on a horizontal plane.
    /// Each wedge direction and length encodes the wind direction and relative frequency from the supplied data.
    /// </summary>
    public static class WindRoseRenderer
    {
        /// <summary>
        /// Generates a 3D wind rose visual from the given wind frequency data.
        /// Wedge length is proportional to the bin frequency relative to the maximum, and color
        /// interpolates from light blue (low frequency) to dark blue (high frequency).
        /// </summary>
        /// <param name="data">The wind rose data containing directional frequency bins. Returns an empty visual if null or empty.</param>
        /// <param name="baseRadius">The radius in scene units corresponding to the maximum frequency wedge. Defaults to 30.0.</param>
        /// <param name="z">The Z-coordinate of the horizontal plane on which the wind rose is drawn. Defaults to 0.1.</param>
        /// <returns>A <see cref="ModelVisual3D"/> containing the wind rose wedge geometry, or an empty visual if no valid data is provided.</returns>
        public static ModelVisual3D Generate(WindRoseData data, double baseRadius = 30.0, double z = 0.1)
        {
            var visual = new ModelVisual3D();
            if (data == null || data.Bins.Count == 0) return visual;

            var group = new Model3DGroup();
            double maxFreq = 0;
            foreach (var bin in data.Bins)
                if (bin.Frequency > maxFreq) maxFreq = bin.Frequency;
            if (maxFreq < 0.01) return visual;

            double wedgeHalf = data.Bins.Count > 1 ? Math.PI / data.Bins.Count : Math.PI / 12;

            foreach (var bin in data.Bins)
            {
                double dirRad = bin.DirectionDeg * Math.PI / 180.0;
                double len = (bin.Frequency / maxFreq) * baseRadius;

                var mesh = new MeshGeometry3D();
                mesh.Positions.Add(new Point3D(0, 0, z));
                mesh.Positions.Add(new Point3D(
                    Math.Sin(dirRad - wedgeHalf) * len,
                    Math.Cos(dirRad - wedgeHalf) * len, z));
                mesh.Positions.Add(new Point3D(
                    Math.Sin(dirRad + wedgeHalf) * len,
                    Math.Cos(dirRad + wedgeHalf) * len, z));

                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(1);
                mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(0);
                mesh.TriangleIndices.Add(2);
                mesh.TriangleIndices.Add(1);

                double t = bin.Frequency / maxFreq;
                var color = ColorMapHelper.Lerp(
                    Color.FromArgb(180, 100, 160, 220),
                    Color.FromArgb(200, 20, 60, 160), t);
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                var material = new DiffuseMaterial(brush);

                group.Children.Add(new GeometryModel3D
                {
                    Geometry = mesh,
                    Material = material,
                    BackMaterial = material
                });
            }

            visual.Content = group;
            return visual;
        }
    }
}
