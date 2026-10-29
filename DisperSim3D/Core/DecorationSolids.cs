using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// The decorations of a scene as solid geometry for the grid solvers, read
    /// from their mesh files so it needs no UI: the triangles of every STL, OBJ
    /// and RVM decoration in world coordinates, placed as both UIs place them
    /// (scale, then rotations about Z, Y and X, then the position), plus the
    /// bounding box of any decoration whose mesh cannot be read (procedural
    /// primitives, missing files).
    ///
    /// <see cref="Rasterize"/> turns that into blocked cells on a solver grid:
    /// every cell a triangle passes through, and every cell the surfaces close
    /// off from the open boundaries. A bounding box per decoration blocks far
    /// more than the object — a whole plant imported as one mesh becomes one
    /// block, with the release inside it.
    /// </summary>
    public sealed class DecorationSolids
    {
        private readonly HashSet<BoundingBox> _represented = new HashSet<BoundingBox>();

        private DecorationSolids(TriangleBundle triangles, List<BoundingBox> boxes, int meshes)
        {
            Triangles = triangles;
            Boxes = boxes;
            MeshCount = meshes;
        }

        /// <summary>World-space triangles of the decorations whose meshes were read.</summary>
        public TriangleBundle Triangles { get; }

        /// <summary>Bounding boxes standing in for decorations with no readable mesh.</summary>
        public IReadOnlyList<BoundingBox> Boxes { get; }

        /// <summary>How many decorations contributed triangles.</summary>
        public int MeshCount { get; }

        /// <summary>Solids from triangles already in world coordinates.</summary>
        public static DecorationSolids FromTriangles(TriangleBundle triangles) =>
            new DecorationSolids(triangles ?? new TriangleBundle { P0 = new float[0], P1 = new float[0], P2 = new float[0] },
                new List<BoundingBox>(), triangles != null && triangles.TriangleCount > 0 ? 1 : 0);

        /// <summary>
        /// True when this already accounts for the box, because it is the bounding
        /// box of one of the decorations it was read from.
        /// </summary>
        public bool Represents(BoundingBox box) => box != null && _represented.Contains(box);

        /// <summary>Reads every decoration's mesh. Never throws: an unreadable mesh falls back to its box.</summary>
        public static DecorationSolids FromDecorations(IEnumerable<Decoration3D> decorations)
        {
            var p0 = new List<float>();
            var p1 = new List<float>();
            var p2 = new List<float>();
            var boxes = new List<BoundingBox>();
            var represented = new List<BoundingBox>();
            int meshes = 0;

            if (decorations != null)
            {
                foreach (var deco in decorations)
                {
                    if (deco == null) continue;
                    float[] local = null;
                    try { local = ReadMesh(deco.FilePath); }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine("[DecorationSolids] " + deco.FilePath + ": " + ex.Message);
                    }

                    if (local == null || local.Length < 9)
                    {
                        if (deco.BoundingBox != null)
                        {
                            boxes.Add(deco.BoundingBox);
                            represented.Add(deco.BoundingBox);
                        }
                        continue;
                    }

                    AddWorldTriangles(deco, local, p0, p1, p2);
                    if (deco.BoundingBox != null) represented.Add(deco.BoundingBox);
                    meshes++;
                }
            }

            var solids = new DecorationSolids(new TriangleBundle
            {
                P0 = p0.ToArray(),
                P1 = p1.ToArray(),
                P2 = p2.ToArray(),
                TriangleCount = p0.Count / 3
            }, boxes, meshes);
            foreach (var b in represented) solids._represented.Add(b);
            return solids;
        }

        /// <summary>
        /// Blocked cells on a grid over [−half, half]² × [0, height]: cells the
        /// triangles pass through, cells they enclose, and the stand-in boxes.
        /// Surfaces lower than half a cell layer are the ground itself (an
        /// imported site's paving, say) and block nothing.
        /// </summary>
        public bool[,,] Rasterize(double domainHalfM, double heightM, int nx, int ny, int nz) =>
            Rasterize(-domainHalfM, -domainHalfM, 0, 2.0 * domainHalfM / nx, 2.0 * domainHalfM / ny, heightM / nz, nx, ny, nz);

        /// <summary>
        /// The same on any grid: cell (i, j, k) spans x0 + i·dx to x0 + (i+1)·dx,
        /// and likewise in y and z.
        /// </summary>
        public bool[,,] Rasterize(double x0, double y0, double z0, double dx, double dy, double dz,
            int nx, int ny, int nz)
        {
            var blocked = new bool[nx, ny, nz];
            var surface = new bool[nx, ny, nz];
            bool any = false;
            double step = 0.5 * Math.Min(dx, Math.Min(dy, dz));
            var t = Triangles;
            for (int n = 0; n < t.TriangleCount; n++)
            {
                int b = 3 * n;
                var a = new Vector3(t.P0[b], t.P0[b + 1], t.P0[b + 2]);
                var e1 = new Vector3(t.P1[b], t.P1[b + 1], t.P1[b + 2]) - a;
                var e2 = new Vector3(t.P2[b], t.P2[b + 1], t.P2[b + 2]) - a;
                if (Math.Max(a.Z, Math.Max(a.Z + e1.Z, a.Z + e2.Z)) < 0.5 * dz) continue;

                // Points over the triangle no further apart than half a cell.
                double longest = Math.Max(e1.Length(), Math.Max(e2.Length(), (e2 - e1).Length()));
                int m = Math.Max(1, Math.Min(4000, (int)Math.Ceiling(longest / step)));
                for (int u = 0; u <= m; u++)
                    for (int v = 0; v <= m - u; v++)
                    {
                        var p = a + e1 * ((float)u / m) + e2 * ((float)v / m);
                        int i = (int)Math.Floor((p.X - x0) / dx);
                        int j = (int)Math.Floor((p.Y - y0) / dy);
                        int k = (int)Math.Floor((p.Z - z0) / dz);
                        if (i < 0 || j < 0 || k < 0 || i >= nx || j >= ny || k >= nz) continue;
                        surface[i, j, k] = true;
                        any = true;
                    }
            }

            if (any)
            {
                // What the open faces (the sides and the top; the bottom is the
                // ground) reach without crossing a surface is outside.
                var outside = new bool[nx, ny, nz];
                var queue = new Queue<(int, int, int)>();
                void Seed(int i, int j, int k)
                {
                    if (surface[i, j, k] || outside[i, j, k]) return;
                    outside[i, j, k] = true;
                    queue.Enqueue((i, j, k));
                }
                for (int k = 0; k < nz; k++)
                    for (int j = 0; j < ny; j++) { Seed(0, j, k); Seed(nx - 1, j, k); }
                for (int k = 0; k < nz; k++)
                    for (int i = 0; i < nx; i++) { Seed(i, 0, k); Seed(i, ny - 1, k); }
                for (int j = 0; j < ny; j++)
                    for (int i = 0; i < nx; i++) Seed(i, j, nz - 1);

                while (queue.Count > 0)
                {
                    var (i, j, k) = queue.Dequeue();
                    if (i > 0) Seed(i - 1, j, k);
                    if (i < nx - 1) Seed(i + 1, j, k);
                    if (j > 0) Seed(i, j - 1, k);
                    if (j < ny - 1) Seed(i, j + 1, k);
                    if (k > 0) Seed(i, j, k - 1);
                    if (k < nz - 1) Seed(i, j, k + 1);
                }

                for (int k = 0; k < nz; k++)
                    for (int j = 0; j < ny; j++)
                        for (int i = 0; i < nx; i++)
                            blocked[i, j, k] = !outside[i, j, k];
            }

            foreach (var box in Boxes)
                BlockBox(blocked, box, x0, y0, z0, dx, dy, dz);
            return blocked;
        }

        /// <summary>
        /// The blocked cells a tracer engine uses: the decorations' solids when
        /// given, plus every box they do not already account for. Without solids,
        /// every box, as the engines have always done.
        /// </summary>
        public static bool[,,] ObstacleMask(IList<BoundingBox> obstacles, DecorationSolids solids,
            double domainHalfM, double heightM, int nx, int ny, int nz)
        {
            var blocked = solids != null
                ? solids.Rasterize(domainHalfM, heightM, nx, ny, nz)
                : new bool[nx, ny, nz];
            if (obstacles != null)
            {
                double dx = 2.0 * domainHalfM / nx, dy = 2.0 * domainHalfM / ny, dz = heightM / nz;
                foreach (var box in obstacles)
                    if (box != null && (solids == null || !solids.Represents(box)))
                        BlockBox(blocked, box, -domainHalfM, -domainHalfM, 0, dx, dy, dz);
            }
            return blocked;
        }

        /// <summary>Blocks every cell the box touches, rounding outward.</summary>
        private static void BlockBox(bool[,,] blocked, BoundingBox bb, double x0, double y0, double z0,
            double dx, double dy, double dz)
        {
            int nx = blocked.GetLength(0), ny = blocked.GetLength(1), nz = blocked.GetLength(2);
            int i0 = Math.Max(0, (int)Math.Floor((bb.Min.X - x0) / dx));
            int i1 = Math.Min(nx - 1, (int)Math.Ceiling((bb.Max.X - x0) / dx));
            int j0 = Math.Max(0, (int)Math.Floor((bb.Min.Y - y0) / dy));
            int j1 = Math.Min(ny - 1, (int)Math.Ceiling((bb.Max.Y - y0) / dy));
            int k0 = Math.Max(0, (int)Math.Floor((Math.Max(0, bb.Min.Z) - z0) / dz));
            int k1 = Math.Min(nz - 1, (int)Math.Ceiling((Math.Max(0, bb.Max.Z) - z0) / dz));
            for (int k = k0; k <= k1; k++)
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                        blocked[i, j, k] = true;
        }

        private static void AddWorldTriangles(Decoration3D deco, float[] local,
            List<float> p0, List<float> p1, List<float> p2)
        {
            float rx = (float)(deco.Rotation.X * Math.PI / 180.0);
            float ry = (float)(deco.Rotation.Y * Math.PI / 180.0);
            float rz = (float)(deco.Rotation.Z * Math.PI / 180.0);
            var world =
                Matrix4x4.CreateScale((float)(deco.Scale > 0 ? deco.Scale : 1.0)) *
                Matrix4x4.CreateRotationZ(rz) *
                Matrix4x4.CreateRotationY(ry) *
                Matrix4x4.CreateRotationX(rx) *
                Matrix4x4.CreateTranslation((float)deco.Position.X, (float)deco.Position.Y, (float)deco.Position.Z);

            // A clipped decoration keeps the triangles with a corner on its kept side,
            // in model coordinates, as the clipper cuts the model before placing it.
            int axis = deco.ClipAxis == ClipAxis.X ? 0 : deco.ClipAxis == ClipAxis.Y ? 1 : 2;
            bool Kept(int at) => !deco.ClipEnabled ||
                (deco.ClipAbove ? local[at + axis] >= deco.ClipValue : local[at + axis] <= deco.ClipValue);

            for (int at = 0; at + 8 < local.Length; at += 9)
            {
                if (!Kept(at) && !Kept(at + 3) && !Kept(at + 6)) continue;
                Emit(p0, Vector3.Transform(new Vector3(local[at], local[at + 1], local[at + 2]), world));
                Emit(p1, Vector3.Transform(new Vector3(local[at + 3], local[at + 4], local[at + 5]), world));
                Emit(p2, Vector3.Transform(new Vector3(local[at + 6], local[at + 7], local[at + 8]), world));
            }
        }

        private static void Emit(List<float> to, Vector3 v)
        {
            to.Add(v.X); to.Add(v.Y); to.Add(v.Z);
        }

        /// <summary>
        /// A mesh file's triangles as flat corner triples in model coordinates
        /// (x0 y0 z0 x1 … z2 per triangle), or null when the path is not a mesh
        /// this can read.
        /// </summary>
        internal static float[] ReadMesh(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".stl": return ReadStl(path);
                case ".obj": return ReadObj(path);
                case ".rvm":
                    {
                        var rvm = RvmMeshLoader.Load(path);
                        if (rvm == null) return null;
                        var tris = new float[rvm.Indices.Length * 3];
                        for (int n = 0; n < rvm.Indices.Length; n++)
                        {
                            var v = rvm.Vertices[rvm.Indices[n]];
                            tris[3 * n] = v.X; tris[3 * n + 1] = v.Y; tris[3 * n + 2] = v.Z;
                        }
                        return tris;
                    }
                default: return null;
            }
        }

        private static float[] ReadStl(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 84) return null;
            string head = System.Text.Encoding.ASCII.GetString(data, 0, Math.Min(300, data.Length));
            bool ascii = head.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase)
                         && head.IndexOf("facet", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!ascii)
            {
                uint count = BitConverter.ToUInt32(data, 80);
                if (data.Length < 84 + (long)count * 50) return null;
                var tris = new float[count * 9];
                int offset = 84;
                for (uint t = 0; t < count; t++)
                {
                    offset += 12; // normal
                    for (int c = 0; c < 9; c++, offset += 4)
                        tris[t * 9 + c] = BitConverter.ToSingle(data, offset);
                    offset += 2;
                }
                return tris;
            }

            var list = new List<float>();
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (!line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase)) continue;
                var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) continue;
                for (int c = 1; c <= 3; c++)
                    list.Add(float.Parse(parts[c], CultureInfo.InvariantCulture));
            }
            return list.ToArray();
        }

        private static float[] ReadObj(string path)
        {
            var positions = new List<Vector3>();
            var tris = new List<float>();
            foreach (string raw in File.ReadLines(path))
            {
                string line = raw.Trim();
                if (line.Length < 2) continue;
                if (line[0] == 'v' && line[1] == ' ')
                {
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                        positions.Add(new Vector3(
                            float.Parse(parts[1], CultureInfo.InvariantCulture),
                            float.Parse(parts[2], CultureInfo.InvariantCulture),
                            float.Parse(parts[3], CultureInfo.InvariantCulture)));
                }
                else if (line[0] == 'f' && line[1] == ' ')
                {
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 4) continue;
                    // A polygon becomes a fan around its first corner.
                    var first = Corner(positions, parts[1]);
                    for (int c = 2; c + 1 < parts.Length; c++)
                    {
                        var b = Corner(positions, parts[c]);
                        var d = Corner(positions, parts[c + 1]);
                        tris.Add(first.X); tris.Add(first.Y); tris.Add(first.Z);
                        tris.Add(b.X); tris.Add(b.Y); tris.Add(b.Z);
                        tris.Add(d.X); tris.Add(d.Y); tris.Add(d.Z);
                    }
                }
            }
            return tris.ToArray();
        }

        /// <summary>The position an OBJ face corner ("7", "7/2", "7//3", "-1") refers to.</summary>
        private static Vector3 Corner(List<Vector3> positions, string token)
        {
            int slash = token.IndexOf('/');
            int index = int.Parse(slash >= 0 ? token.Substring(0, slash) : token, CultureInfo.InvariantCulture);
            int at = index > 0 ? index - 1 : positions.Count + index;
            return at >= 0 && at < positions.Count ? positions[at] : Vector3.Zero;
        }
    }
}
