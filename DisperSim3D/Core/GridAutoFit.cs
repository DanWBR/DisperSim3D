using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Grows the ground grid so a newly added object fits on it.
    ///
    /// <para>The grid is the working area the scene is laid out on. It spans
    /// <c>[-GridHalfSize, +GridHalfSize]</c> in X and Y and starts at a fixed
    /// 100 m. Dropping in a model bigger than that leaves it hanging over the
    /// edge with nothing underneath, so the grid opens up to the model's
    /// footprint plus a margin.</para>
    ///
    /// <para>It only ever grows. Deleting the object that forced the growth
    /// leaves the larger grid, which keeps a layout from resizing under the
    /// user while they work.</para>
    ///
    /// <para>The simulation domain is a different quantity, sized later from
    /// its own bounding box. Nothing here touches
    /// <see cref="DispersionScenario.DomainSizeM"/>.</para>
    /// </summary>
    public static class GridAutoFit
    {
        /// <summary>Extra fraction of the object's own reach left around it.</summary>
        public const double DefaultMargin = 0.2;

        /// <summary>
        /// Half-extent the grid needs in order to hold <paramref name="worldBox"/>,
        /// margin included. Only the horizontal footprint counts: the grid is a
        /// ground plane and has no height.
        /// </summary>
        /// <param name="worldBox">Bounding box in world coordinates.</param>
        /// <param name="margin">Fraction to add on top. 0.2 leaves 20%.</param>
        /// <returns>The required half-extent in metres, or 0 for an unusable box.</returns>
        public static double RequiredHalfSize(BoundingBox worldBox, double margin = DefaultMargin)
        {
            if (worldBox == null) return 0;

            // The grid is centred on the origin, so what matters is how far the
            // box reaches from it — not how wide the box is.
            double reach = Math.Max(
                Math.Max(Math.Abs(worldBox.Min.X), Math.Abs(worldBox.Max.X)),
                Math.Max(Math.Abs(worldBox.Min.Y), Math.Abs(worldBox.Max.Y)));

            if (double.IsNaN(reach) || double.IsInfinity(reach) || reach <= 0) return 0;
            return reach * (1.0 + margin);
        }

        /// <summary>
        /// Grows the scene's grid so <paramref name="worldBox"/> fits on it.
        /// </summary>
        /// <returns><c>true</c> when the grid actually changed, so the caller can
        /// mark the project dirty and tell the user.</returns>
        public static bool Fit(Scene3D scene, BoundingBox worldBox,
            double margin = DefaultMargin)
        {
            if (scene?.Environment == null) return false;

            double required = RequiredHalfSize(worldBox, margin);
            if (required <= scene.Environment.GridHalfSize) return false;

            scene.Environment.GridHalfSize = required;
            return true;
        }

        /// <summary>
        /// Grows the grid so every box in <paramref name="worldBoxes"/> fits. Used
        /// where objects arrive together rather than one at a time.
        /// </summary>
        /// <returns><c>true</c> when the grid changed.</returns>
        public static bool Fit(Scene3D scene, IEnumerable<BoundingBox> worldBoxes,
            double margin = DefaultMargin)
        {
            if (scene?.Environment == null || worldBoxes == null) return false;

            double required = 0;
            foreach (var box in worldBoxes)
            {
                double r = RequiredHalfSize(box, margin);
                if (r > required) required = r;
            }

            if (required <= scene.Environment.GridHalfSize) return false;

            scene.Environment.GridHalfSize = required;
            return true;
        }

        /// <summary>
        /// World-space axis-aligned box of a model-space box carried through a
        /// decoration's transform: scale, then rotation Z → Y → X, then translation.
        /// That is the order <c>Decoration3D.GetWorldTransform()</c> composes.
        ///
        /// <para>Callers whose <see cref="Decoration3D.BoundingBox"/> is already in
        /// world coordinates must not use this — they would apply the transform
        /// twice. It exists for the renderers that measure raw mesh vertices.</para>
        /// </summary>
        /// <param name="localBox">Box in the model's own coordinates.</param>
        /// <param name="position">Decoration position in world coordinates.</param>
        /// <param name="rotationDeg">Euler angles in degrees, per axis.</param>
        /// <param name="scale">Uniform scale factor.</param>
        public static BoundingBox ToWorld(BoundingBox localBox, Point3D position,
            Vector3D rotationDeg, double scale)
        {
            if (localBox == null) return null;

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

            // Rotating a box tilts it, so the enclosing axis-aligned box has to be
            // rebuilt from the eight transformed corners rather than the two corners.
            for (int i = 0; i < 8; i++)
            {
                double x = ((i & 1) == 0 ? localBox.Min.X : localBox.Max.X) * scale;
                double y = ((i & 2) == 0 ? localBox.Min.Y : localBox.Max.Y) * scale;
                double z = ((i & 4) == 0 ? localBox.Min.Z : localBox.Max.Z) * scale;

                Rotate(ref x, ref y, rotationDeg.Z);   // about Z
                Rotate(ref z, ref x, rotationDeg.Y);   // about Y
                Rotate(ref y, ref z, rotationDeg.X);   // about X

                x += position.X; y += position.Y; z += position.Z;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (z < minZ) minZ = z;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
                if (z > maxZ) maxZ = z;
            }

            return new BoundingBox(new Point3D(minX, minY, minZ),
                                   new Point3D(maxX, maxY, maxZ));
        }

        /// <summary>Rotates the pair (a, b) by <paramref name="degrees"/> in their own plane.</summary>
        private static void Rotate(ref double a, ref double b, double degrees)
        {
            if (degrees == 0) return;
            double rad = degrees * Math.PI / 180.0;
            double cos = Math.Cos(rad), sin = Math.Sin(rad);
            double na = a * cos - b * sin;
            double nb = a * sin + b * cos;
            a = na; b = nb;
        }
    }
}
