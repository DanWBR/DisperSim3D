#nullable enable
using System;
using System.Collections.Generic;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Keeps <see cref="Decoration3D.BoundingBox"/> in world coordinates.
    ///
    /// <para>The box is read by everything that asks the scene what its solid
    /// geometry is — <see cref="SceneObstacles"/>, and through it the flash-fire
    /// flood fill, the radiation shading and the detector allocators. Those all
    /// assume world coordinates, which is the convention the WinForms host has
    /// always used via its <c>UpdateBoundingBox()</c> extension.</para>
    ///
    /// <para>This viewport used to fill the same field with the mesh's own
    /// coordinates, ignoring position, rotation and scale. An obstacle sitting
    /// 40 m downwind therefore reached the solvers sitting at the origin, at
    /// whatever size its file happened to use. Everything here exists to make
    /// the Avalonia host agree with the WinForms one.</para>
    ///
    /// <para>The mesh's untransformed box is cached per decoration so a later
    /// edit to position, rotation or scale can rebuild the world box without
    /// re-reading the file.</para>
    /// </summary>
    internal static class DecorationBounds
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, BoundingBox> LocalBoxes =
            new Dictionary<string, BoundingBox>();

        /// <summary>
        /// Records the mesh's untransformed box and sets the decoration's
        /// world-space box from it. Called by the viewport once per mesh, when
        /// the geometry is first uploaded.
        /// </summary>
        public static void SetLocalBox(Decoration3D deco, BoundingBox localBox)
        {
            if (deco == null || localBox == null) return;
            lock (Gate) LocalBoxes[deco.Id] = localBox;
            deco.BoundingBox = GridAutoFit.ToWorld(
                localBox, deco.Position, deco.Rotation, deco.Scale);
        }

        /// <summary>
        /// Rebuilds the decoration's world-space box after its transform changed.
        /// Uses the cached mesh box, falling back to reading the file when the
        /// viewport has not measured this decoration yet.
        /// </summary>
        /// <returns><c>true</c> when a box was produced.</returns>
        public static bool UpdateBoundingBox(Decoration3D deco)
        {
            if (deco == null) return false;

            BoundingBox? local;
            lock (Gate) LocalBoxes.TryGetValue(deco.Id, out local);
            if (local == null)
            {
                local = LocalBoxOf(deco.FilePath, out _);
                if (local == null) return false;
                lock (Gate) LocalBoxes[deco.Id] = local;
            }

            deco.BoundingBox = GridAutoFit.ToWorld(
                local, deco.Position, deco.Rotation, deco.Scale);
            return deco.BoundingBox != null;
        }

        /// <summary>
        /// Drops every cached mesh box. Called when a project is opened or created,
        /// so the entries from the previous scene do not accumulate for the life of
        /// the process.
        /// </summary>
        public static void Clear()
        {
            lock (Gate) LocalBoxes.Clear();
        }

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
            return BoxOfVertices(loaded.Value.verts);
        }

        /// <summary>Axis-aligned box of a vertex array, in the array's own space.</summary>
        public static BoundingBox BoxOfVertices(IReadOnlyList<SolidVertex> verts)
        {
            float xMin = float.MaxValue, yMin = float.MaxValue, zMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue, zMax = float.MinValue;
            for (int i = 0; i < verts.Count; i++)
            {
                var p = verts[i].Position;
                if (p.X < xMin) xMin = p.X;
                if (p.Y < yMin) yMin = p.Y;
                if (p.Z < zMin) zMin = p.Z;
                if (p.X > xMax) xMax = p.X;
                if (p.Y > yMax) yMax = p.Y;
                if (p.Z > zMax) zMax = p.Z;
            }
            return new BoundingBox(
                new DisperSim3D.Geometry.Point3D(xMin, yMin, zMin),
                new DisperSim3D.Geometry.Point3D(xMax, yMax, zMax));
        }
    }
}
