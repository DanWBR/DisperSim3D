using System.Windows.Media.Media3D;
using DisperSim3D.Core;

namespace DisperSim3D.Models
{
    /// <summary>
    /// WPF-coupled operations on <see cref="Decoration3D"/> that live outside
    /// the cross-platform engine. They're exposed as extension methods so the
    /// engine's <c>Decoration3D</c> stays a pure data class while UI code can
    /// still call <c>decoration.ApplyClip()</c>, <c>decoration.GetWorldTransform()</c>
    /// and <c>decoration.UpdateBoundingBox()</c> exactly as before.
    /// </summary>
    public static class Decoration3DWpfExtensions
    {
        /// <summary>Applies the current clipping configuration to the model.
        /// Restores the original model when clipping is disabled.</summary>
        public static void ApplyClip(this Decoration3D deco)
        {
            if (deco?.OriginalModel3D is not Model3DGroup original) return;

            if (!deco.ClipEnabled)
            {
                deco.Model3D = original;
                return;
            }

            deco.Model3D = MeshClipper.ClipModel(
                original, deco.ClipAxis, deco.ClipValue, deco.ClipAbove);
        }

        /// <summary>Composite world transform (scale → rotation Z → Y → X →
        /// translation) used both for rendering and triangle voxelization.</summary>
        public static Transform3D GetWorldTransform(this Decoration3D deco)
        {
            var group = new Transform3DGroup();

            group.Children.Add(new ScaleTransform3D(deco.Scale, deco.Scale, deco.Scale));

            if (deco.Rotation.Z != 0)
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(0, 0, 1), deco.Rotation.Z)));
            if (deco.Rotation.Y != 0)
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(0, 1, 0), deco.Rotation.Y)));
            if (deco.Rotation.X != 0)
                group.Children.Add(new RotateTransform3D(
                    new AxisAngleRotation3D(new Vector3D(1, 0, 0), deco.Rotation.X)));

            group.Children.Add(new TranslateTransform3D(
                deco.Position.X, deco.Position.Y, deco.Position.Z));
            return group;
        }

        /// <summary>Recalculates the world-space bounding box from the current
        /// model geometry and transform.</summary>
        public static void UpdateBoundingBox(this Decoration3D deco)
        {
            if (deco == null) return;

            if (deco.Model3D is not Model3DGroup model)
            {
                var size = 1.0 * deco.Scale;
                deco.BoundingBox = new BoundingBox(
                    new Point3D(deco.Position.X - size / 2, deco.Position.Y - size / 2, deco.Position.Z - size / 2),
                    new Point3D(deco.Position.X + size / 2, deco.Position.Y + size / 2, deco.Position.Z + size / 2));
            }
            else
            {
                var bounds = model.Bounds;
                var transform = deco.GetWorldTransform();
                var min = new Point3D(bounds.X, bounds.Y, bounds.Z);
                var max = new Point3D(bounds.X + bounds.SizeX, bounds.Y + bounds.SizeY, bounds.Z + bounds.SizeZ);
                deco.BoundingBox = new BoundingBox(min, max).Transform(transform);
            }
        }
    }
}
