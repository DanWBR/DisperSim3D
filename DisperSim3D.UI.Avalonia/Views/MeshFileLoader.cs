#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Portable 3D mesh file loader — reads STL (binary + ASCII) and
    /// Wavefront OBJ files into <see cref="SolidVertex"/> arrays ready
    /// for <see cref="GlMeshBuffer.Upload"/>.  No external dependencies
    /// (replaces HelixToolkit's <c>ModelImporter</c> for cross-platform).
    /// </summary>
    internal static class MeshFileLoader
    {
        /// <summary>
        /// Load a 3D mesh file (.stl or .obj) and return vertex + index
        /// arrays suitable for GPU upload.
        /// </summary>
        /// <param name="filePath">Absolute path to the model file.</param>
        /// <param name="color">Per-vertex color to apply (RGBA).</param>
        /// <returns>Vertex and index arrays, or null if the file can't be loaded.</returns>
        public static (SolidVertex[] verts, uint[] indices)? Load(
            string filePath, Vector4 color)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            try
            {
                return ext switch
                {
                    ".stl" => LoadStl(filePath, color),
                    ".obj" => LoadObj(filePath, color),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MeshFileLoader] Failed to load {filePath}: {ex.Message}");
                return null;
            }
        }

        // ── STL loader ─────────────────────────────────────────────────

        private static (SolidVertex[] verts, uint[] indices)? LoadStl(
            string filePath, Vector4 color)
        {
            // Detect binary vs ASCII: binary STL has an 80-byte header
            // followed by a 4-byte triangle count. ASCII starts with "solid".
            byte[] header = File.ReadAllBytes(filePath);
            if (header.Length < 84) return null;

            // Heuristic: if file starts with "solid" AND contains "facet"
            // somewhere in the first 300 bytes, treat as ASCII.
            bool isAscii = false;
            if (header.Length > 5)
            {
                string start = System.Text.Encoding.ASCII.GetString(header, 0, Math.Min(300, header.Length));
                if (start.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase) &&
                    start.Contains("facet", StringComparison.OrdinalIgnoreCase))
                    isAscii = true;
            }

            return isAscii
                ? LoadStlAscii(filePath, color)
                : LoadStlBinary(header, color);
        }

        private static (SolidVertex[] verts, uint[] indices)? LoadStlBinary(
            byte[] data, Vector4 color)
        {
            // Binary STL format:
            //   80 bytes header
            //   4 bytes uint32 triangle count
            //   For each triangle (50 bytes):
            //     12 bytes normal (3 × float)
            //     12 bytes vertex1 (3 × float)
            //     12 bytes vertex2 (3 × float)
            //     12 bytes vertex3 (3 × float)
            //     2 bytes attribute byte count
            uint triCount = BitConverter.ToUInt32(data, 80);

            // Sanity check
            long expected = 84 + (long)triCount * 50;
            if (data.Length < expected || triCount > 10_000_000)
                return null;

            var verts = new SolidVertex[triCount * 3];
            var indices = new uint[triCount * 3];

            int offset = 84;
            for (uint t = 0; t < triCount; t++)
            {
                // Normal
                float nx = BitConverter.ToSingle(data, offset);
                float ny = BitConverter.ToSingle(data, offset + 4);
                float nz = BitConverter.ToSingle(data, offset + 8);
                var normal = new Vector3(nx, ny, nz);
                offset += 12;

                // Three vertices
                for (int v = 0; v < 3; v++)
                {
                    float px = BitConverter.ToSingle(data, offset);
                    float py = BitConverter.ToSingle(data, offset + 4);
                    float pz = BitConverter.ToSingle(data, offset + 8);
                    offset += 12;

                    uint idx = t * 3 + (uint)v;
                    verts[idx] = new SolidVertex(new Vector3(px, py, pz), normal, color);
                    indices[idx] = idx;
                }

                offset += 2; // attribute byte count (skip)
            }

            return (verts, indices);
        }

        private static (SolidVertex[] verts, uint[] indices)? LoadStlAscii(
            string filePath, Vector4 color)
        {
            var verts = new List<SolidVertex>();
            var indices = new List<uint>();
            var inv = CultureInfo.InvariantCulture;

            Vector3 currentNormal = Vector3.UnitZ;
            uint vertIdx = 0;

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();

                if (line.StartsWith("facet normal", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        currentNormal = new Vector3(
                            float.Parse(parts[2], inv),
                            float.Parse(parts[3], inv),
                            float.Parse(parts[4], inv));
                    }
                }
                else if (line.StartsWith("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4)
                    {
                        var pos = new Vector3(
                            float.Parse(parts[1], inv),
                            float.Parse(parts[2], inv),
                            float.Parse(parts[3], inv));
                        verts.Add(new SolidVertex(pos, currentNormal, color));
                        indices.Add(vertIdx++);
                    }
                }
            }

            if (verts.Count == 0) return null;
            return (verts.ToArray(), indices.ToArray());
        }

        // ── OBJ loader ─────────────────────────────────────────────────

        private static (SolidVertex[] verts, uint[] indices)? LoadObj(
            string filePath, Vector4 color)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var outVerts = new List<SolidVertex>();
            var outIndices = new List<uint>();
            var inv = CultureInfo.InvariantCulture;

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

                if (parts[0] == "v" && parts.Length >= 4)
                {
                    positions.Add(new Vector3(
                        float.Parse(parts[1], inv),
                        float.Parse(parts[2], inv),
                        float.Parse(parts[3], inv)));
                }
                else if (parts[0] == "vn" && parts.Length >= 4)
                {
                    normals.Add(new Vector3(
                        float.Parse(parts[1], inv),
                        float.Parse(parts[2], inv),
                        float.Parse(parts[3], inv)));
                }
                else if (parts[0] == "f" && parts.Length >= 4)
                {
                    // Triangulate face (fan from first vertex)
                    // Face format: v, v/vt, v/vt/vn, v//vn
                    ParseFaceVertex(parts[1], out int vi0, out int ni0);
                    for (int i = 2; i < parts.Length - 1; i++)
                    {
                        ParseFaceVertex(parts[i], out int vi1, out int ni1);
                        ParseFaceVertex(parts[i + 1], out int vi2, out int ni2);

                        var p0 = GetSafe(positions, vi0);
                        var p1 = GetSafe(positions, vi1);
                        var p2 = GetSafe(positions, vi2);

                        // Use normals from file, or compute from face
                        Vector3 n;
                        if (ni0 > 0 && ni0 <= normals.Count)
                            n = GetSafe(normals, ni0);
                        else
                            n = Vector3.Normalize(Vector3.Cross(p1 - p0, p2 - p0));

                        uint baseIdx = (uint)outVerts.Count;
                        outVerts.Add(new SolidVertex(p0,
                            ni0 > 0 ? GetSafe(normals, ni0) : n, color));
                        outVerts.Add(new SolidVertex(p1,
                            ni1 > 0 ? GetSafe(normals, ni1) : n, color));
                        outVerts.Add(new SolidVertex(p2,
                            ni2 > 0 ? GetSafe(normals, ni2) : n, color));

                        outIndices.Add(baseIdx);
                        outIndices.Add(baseIdx + 1);
                        outIndices.Add(baseIdx + 2);
                    }
                }
            }

            if (outVerts.Count == 0) return null;
            return (outVerts.ToArray(), outIndices.ToArray());
        }

        private static void ParseFaceVertex(string token, out int vi, out int ni)
        {
            // Formats: "v", "v/vt", "v/vt/vn", "v//vn"
            vi = 0; ni = 0;
            var parts = token.Split('/');
            if (parts.Length >= 1 && int.TryParse(parts[0], out int v)) vi = v;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int n)) ni = n;
        }

        private static Vector3 GetSafe(List<Vector3> list, int oneBasedIdx)
        {
            // OBJ indices are 1-based; negative = relative to end
            int idx = oneBasedIdx > 0 ? oneBasedIdx - 1 : list.Count + oneBasedIdx;
            if (idx >= 0 && idx < list.Count) return list[idx];
            return Vector3.Zero;
        }
    }
}
