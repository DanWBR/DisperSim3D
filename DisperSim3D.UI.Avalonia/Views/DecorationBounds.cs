#nullable enable
using System;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Measures a decoration's mesh from disk, ahead of the renderer.
    ///
    /// <para><see cref="GlViewport"/> fills in <see cref="Decoration3D.BoundingBox"/>
    /// the first time it uploads a mesh, which is a frame or more after the user
    /// added the object. Both the import dialog and the grid need the size at the
    /// moment of the drop, so this reads the file once at that point instead.</para>
    /// </summary>
    internal static class DecorationBounds
    {
        /// <summary>
        /// Raw size of the mesh in the units its file happens to use, plus its
        /// triangle count. Returns <c>null</c> for a file that does not load, or
        /// for a spec that is not a mesh at all (the parametric <c>primitive:</c>
        /// forms).
        /// </summary>
        public static ImportModelDialog.ModelInfo? MeasureFile(string? filePath)
        {
            var local = LocalBoxOf(filePath, out int triangles);
            if (local == null) return null;

            return new ImportModelDialog.ModelInfo(
                local.Min.X, local.Min.Y, local.Min.Z,
                local.Max.X - local.Min.X,
                local.Max.Y - local.Min.Y,
                local.Max.Z - local.Min.Z,
                triangles);
        }

        /// <summary>
        /// World-space box of a decoration's mesh once placed by its own transform.
        /// Returns <c>null</c> when the mesh cannot be measured.
        /// </summary>
        public static BoundingBox? WorldBoxOf(Decoration3D deco)
        {
            if (deco == null) return null;
            var local = LocalBoxOf(deco.FilePath, out _);
            if (local == null) return null;
            return GridAutoFit.ToWorld(local, deco.Position, deco.Rotation, deco.Scale);
        }

        /// <summary>
        /// Grows the scene's grid so a just-added decoration fits on it.
        /// </summary>
        /// <returns><c>true</c> when the grid changed.</returns>
        public static bool FitGrid(Scene3D scene, Decoration3D deco)
        {
            var world = WorldBoxOf(deco);
            return world != null && GridAutoFit.Fit(scene, world);
        }

        /// <summary>Box in the mesh file's own coordinates, before any transform.</summary>
        private static BoundingBox? LocalBoxOf(string? filePath, out int triangles)
        {
            triangles = 0;
            if (string.IsNullOrEmpty(filePath)) return null;
            if (filePath.StartsWith("primitive:", StringComparison.OrdinalIgnoreCase))
                return null;
            if (!System.IO.File.Exists(filePath)) return null;

            var loaded = MeshFileLoader.Load(filePath, System.Numerics.Vector4.One);
            if (loaded == null || loaded.Value.verts.Length == 0) return null;

            triangles = loaded.Value.indices.Length / 3;

            float xMin = float.MaxValue, yMin = float.MaxValue, zMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue, zMax = float.MinValue;
            foreach (var v in loaded.Value.verts)
            {
                if (v.Position.X < xMin) xMin = v.Position.X;
                if (v.Position.Y < yMin) yMin = v.Position.Y;
                if (v.Position.Z < zMin) zMin = v.Position.Z;
                if (v.Position.X > xMax) xMax = v.Position.X;
                if (v.Position.Y > yMax) yMax = v.Position.Y;
                if (v.Position.Z > zMax) zMax = v.Position.Z;
            }

            return new BoundingBox(
                new DisperSim3D.Geometry.Point3D(xMin, yMin, zMin),
                new DisperSim3D.Geometry.Point3D(xMax, yMax, zMax));
        }
    }
}
