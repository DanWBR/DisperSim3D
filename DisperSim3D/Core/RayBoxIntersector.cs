using System;
using System.Collections.Generic;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Segment / axis-aligned-box intersection by the slab method, and the occlusion
    /// query built on it.
    ///
    /// Used to let plant geometry cast radiation shadows: a panel of flame that a wall
    /// hides from the receiver contributes nothing to the flux there.
    /// </summary>
    public static class RayBoxIntersector
    {
        /// <summary>
        /// True when the segment from <paramref name="origin"/> to
        /// <paramref name="target"/> crosses <paramref name="box"/>.
        ///
        /// Slab method: clip the segment's parameter range against each axis pair of
        /// planes in turn and see whether anything survives. A segment parallel to an
        /// axis is handled by the degenerate branch rather than dividing by zero.
        /// </summary>
        public static bool SegmentHitsBox(Point3D origin, Point3D target, BoundingBox box)
        {
            if (box == null) return false;

            double tmin = 0.0, tmax = 1.0;

            if (!ClipAxis(origin.X, target.X - origin.X, box.Min.X, box.Max.X, ref tmin, ref tmax)) return false;
            if (!ClipAxis(origin.Y, target.Y - origin.Y, box.Min.Y, box.Max.Y, ref tmin, ref tmax)) return false;
            if (!ClipAxis(origin.Z, target.Z - origin.Z, box.Min.Z, box.Max.Z, ref tmin, ref tmax)) return false;

            return true;
        }

        private static bool ClipAxis(double origin, double direction, double min, double max,
            ref double tmin, ref double tmax)
        {
            const double epsilon = 1e-12;

            if (Math.Abs(direction) < epsilon)
                return origin >= min && origin <= max;   // parallel: in or out for good

            double t1 = (min - origin) / direction;
            double t2 = (max - origin) / direction;
            if (t1 > t2) { double swap = t1; t1 = t2; t2 = swap; }

            if (t1 > tmin) tmin = t1;
            if (t2 < tmax) tmax = t2;
            return tmin <= tmax;
        }

        /// <summary>True when any box in <paramref name="obstacles"/> crosses the segment.</summary>
        public static bool SegmentBlocked(Point3D origin, Point3D target, IList<BoundingBox> obstacles)
        {
            if (obstacles == null) return false;
            for (int i = 0; i < obstacles.Count; i++)
                if (SegmentHitsBox(origin, target, obstacles[i])) return true;
            return false;
        }

        /// <summary>
        /// A set of obstacle boxes with their common bounding box, so a segment that
        /// misses the whole set is rejected by one slab test instead of one per box.
        ///
        /// This matters: shading the radiation field costs one query per cell per flame
        /// panel — around 2×10⁷ on a 60³ grid — and in a real plant most of those
        /// segments never come near the geometry.
        /// </summary>
        public sealed class Occluder
        {
            private readonly BoundingBox[] _boxes;
            private readonly BoundingBox _bounds;

            private Occluder(BoundingBox[] boxes, BoundingBox bounds)
            {
                _boxes = boxes;
                _bounds = bounds;
            }

            /// <summary>Number of boxes; zero means every query answers "clear".</summary>
            public int BoxCount => _boxes.Length;

            /// <summary>Builds an occluder, or returns null when there is nothing to
            /// occlude — callers can then skip the shading work entirely.</summary>
            public static Occluder From(IList<BoundingBox> obstacles)
            {
                if (obstacles == null || obstacles.Count == 0) return null;

                var boxes = new List<BoundingBox>(obstacles.Count);
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;

                foreach (var box in obstacles)
                {
                    if (box == null) continue;
                    boxes.Add(box);
                    if (box.Min.X < minX) minX = box.Min.X;
                    if (box.Min.Y < minY) minY = box.Min.Y;
                    if (box.Min.Z < minZ) minZ = box.Min.Z;
                    if (box.Max.X > maxX) maxX = box.Max.X;
                    if (box.Max.Y > maxY) maxY = box.Max.Y;
                    if (box.Max.Z > maxZ) maxZ = box.Max.Z;
                }

                if (boxes.Count == 0) return null;
                return new Occluder(boxes.ToArray(),
                    new BoundingBox(new Point3D(minX, minY, minZ), new Point3D(maxX, maxY, maxZ)));
            }

            /// <summary>True when geometry blocks the line of sight between the two points.</summary>
            public bool Blocks(Point3D origin, Point3D target)
            {
                if (!SegmentHitsBox(origin, target, _bounds)) return false;
                for (int i = 0; i < _boxes.Length; i++)
                    if (SegmentHitsBox(origin, target, _boxes[i])) return true;
                return false;
            }

            /// <summary>True when the point is inside solid geometry.</summary>
            public bool Contains(double x, double y, double z)
            {
                if (x < _bounds.Min.X || x > _bounds.Max.X
                    || y < _bounds.Min.Y || y > _bounds.Max.Y
                    || z < _bounds.Min.Z || z > _bounds.Max.Z) return false;

                for (int i = 0; i < _boxes.Length; i++)
                {
                    var box = _boxes[i];
                    if (x >= box.Min.X && x <= box.Max.X
                        && y >= box.Min.Y && y <= box.Max.Y
                        && z >= box.Min.Z && z <= box.Max.Z) return true;
                }
                return false;
            }
        }
    }
}
