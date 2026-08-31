using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using RvmSharp;
using RvmSharp.Containers;
using RvmSharp.Operations;
using RvmSharp.Primitives;
using RvmSharp.Tessellation;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Reads AVEVA PDMS / E3D <c>.rvm</c> models and tessellates them into one
    /// triangle soup.
    ///
    /// <para>RVM is the plant-design format that actually circulates between
    /// engineering contractors, and it is the only one of the proprietary review
    /// formats with a usable open reader. Parsing and tessellation come from
    /// <see href="https://github.com/equinor/rvmsharp">Equinor.RvmSharp</see>
    /// (MIT); this class reduces its output to the one thing the rest of the
    /// program wants — triangles in world coordinates.</para>
    ///
    /// <para>An RVM node tree carries paramteric primitives (box, cylinder,
    /// snout, pyramid, rectangular and circular torus, dishes) alongside explicit
    /// facet groups. Everything is tessellated here rather than mapped onto the
    /// project's own primitives, because a plant model mixes the two freely and a
    /// single mesh is what both the renderer and the obstacle voxeliser consume.</para>
    ///
    /// <para><b>Units.</b> RVM carries no unit and PDMS conventionally exports in
    /// millimetres, so a model will usually arrive a thousand times too large.
    /// That is the importer's unit prompt to resolve, not this class's:
    /// <see cref="ModelUnits.Guess"/> reads the extent reported here and the user
    /// confirms it.</para>
    /// </summary>
    public static class RvmMeshLoader
    {
        /// <summary>Triangles from an RVM file, in the file's own units.</summary>
        public sealed class Result
        {
            /// <summary>Vertex positions.</summary>
            public Vector3[] Vertices { get; }
            /// <summary>Per-vertex normals, same length as <see cref="Vertices"/>.</summary>
            public Vector3[] Normals { get; }
            /// <summary>Triangle indices into <see cref="Vertices"/>.</summary>
            public uint[] Indices { get; }
            /// <summary>How many RVM primitives were tessellated.</summary>
            public int PrimitiveCount { get; }
            /// <summary>Chord tolerance actually used, in file units.</summary>
            public float ToleranceUsed { get; }

            public Result(Vector3[] vertices, Vector3[] normals, uint[] indices,
                int primitiveCount, float toleranceUsed)
            {
                Vertices = vertices;
                Normals = normals;
                Indices = indices;
                PrimitiveCount = primitiveCount;
                ToleranceUsed = toleranceUsed;
            }

            /// <summary>Triangle count.</summary>
            public int TriangleCount => Indices.Length / 3;
        }

        /// <summary>True when the path looks like an RVM file.</summary>
        public static bool IsRvmPath(string filePath) =>
            !string.IsNullOrEmpty(filePath) &&
            string.Equals(Path.GetExtension(filePath), ".rvm", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Reads and tessellates an RVM file.
        /// </summary>
        /// <param name="filePath">Path to the <c>.rvm</c> file.</param>
        /// <param name="toleranceOverride">Chord tolerance in file units. Leave at
        /// zero to size it from the model itself, which is what keeps a millimetre
        /// model from tessellating into tens of millions of triangles.</param>
        /// <returns>The triangles, or <c>null</c> when the file cannot be read or
        /// holds no geometry.</returns>
        public static Result Load(string filePath, float toleranceOverride = 0f)
        {
            if (!IsRvmPath(filePath) || !File.Exists(filePath)) return null;

            RvmFile file;
            using (var stream = File.OpenRead(filePath))
                file = RvmParser.ReadRvm(stream);
            if (file?.Model == null) return null;

            // Connect and align before tessellating. RVM stores a cylinder and the
            // snout bolted to it as independent primitives; these two passes find
            // those junctions and rotate the parts into agreement, which is what
            // stops a pipe run from showing visible seams at every fitting.
            var store = new RvmStore();
            store.RvmFiles.Add(file);
            RvmConnect.Connect(store);
            RvmAlign.Align(store);

            var primitives = new List<RvmPrimitive>();
            foreach (var root in file.Model.Children)
                Collect(root, primitives);
            if (primitives.Count == 0) return null;

            float tolerance = toleranceOverride > 0
                ? toleranceOverride
                : ChooseTolerance(primitives);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<uint>();

            foreach (var primitive in primitives)
            {
                RvmMesh mesh;
                try
                {
                    mesh = TessellatorBridge.Tessellate(primitive, tolerance);
                }
                catch (Exception)
                {
                    // One malformed primitive in a plant model of half a million
                    // must not lose the other 499 999.
                    continue;
                }
                if (mesh == null || mesh.Vertices.Length == 0) continue;

                uint offset = (uint)vertices.Count;
                vertices.AddRange(mesh.Vertices);

                if (mesh.Normals != null && mesh.Normals.Length == mesh.Vertices.Length)
                    normals.AddRange(mesh.Normals);
                else
                    for (int i = 0; i < mesh.Vertices.Length; i++)
                        normals.Add(Vector3.UnitZ);

                foreach (uint t in mesh.Triangles)
                    indices.Add(t + offset);
            }

            if (vertices.Count == 0) return null;

            return new Result(vertices.ToArray(), normals.ToArray(), indices.ToArray(),
                primitives.Count, tolerance);
        }

        /// <summary>Depth-first walk collecting every primitive under a node.</summary>
        private static void Collect(RvmGroup group, List<RvmPrimitive> into)
        {
            switch (group)
            {
                case RvmPrimitive primitive:
                    into.Add(primitive);
                    break;
                case RvmNode node:
                    foreach (var child in node.Children)
                        Collect(child, into);
                    break;
            }
        }

        /// <summary>
        /// Chord tolerance scaled to the model, because RVM carries no unit.
        ///
        /// <para>A fixed tolerance cannot work here: 1 mm is sensible for a
        /// millimetre model and absurdly coarse for one authored in metres, and
        /// 0.001 is the reverse. Sizing it against the model's own extent gives
        /// curved surfaces roughly the same number of facets whatever the unit.</para>
        /// </summary>
        private static float ChooseTolerance(List<RvmPrimitive> primitives)
        {
            // Per axis, not pooled across all three. A plant model sits at its
            // survey coordinates rather than at the origin, so pooling the axes
            // measures the distance to that origin instead of the model, and a
            // 22 m module 250 m out reads as 280 m across.
            float minX = float.MaxValue, minY = float.MaxValue, minZ = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue, maxZ = float.MinValue;
            foreach (var p in primitives)
            {
                RvmBoundingBox box;
                try { box = p.CalculateAxisAlignedBoundingBox(); }
                catch (Exception) { continue; }
                if (box == null) continue;
                minX = Math.Min(minX, box.Min.X);
                minY = Math.Min(minY, box.Min.Y);
                minZ = Math.Min(minZ, box.Min.Z);
                maxX = Math.Max(maxX, box.Max.X);
                maxY = Math.Max(maxY, box.Max.Y);
                maxZ = Math.Max(maxZ, box.Max.Z);
            }

            float extent = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
            if (extent <= 0 || float.IsNaN(extent) || float.IsInfinity(extent))
                return 1.0f;

            // A thousandth of the model's own span. Fine enough that a handrail
            // still reads as round, coarse enough that a refinery does not blow
            // past the vertex budget.
            float tolerance = extent / 1000f;
            return tolerance <= 0 ? 1.0f : tolerance;
        }
    }
}
