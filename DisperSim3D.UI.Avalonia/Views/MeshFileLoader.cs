#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;

namespace DisperSim3D.UI.Avalonia.Views
{
    internal sealed class ObjMaterial
    {
        public string Name = "";
        public Vector4 DiffuseColor = Vector4.One;
        public string? DiffuseTexturePath;
        public float Opacity = 1f;
    }

    internal sealed class TexturedSubmesh
    {
        public TexturedVertex[] Vertices = Array.Empty<TexturedVertex>();
        public uint[] Indices = Array.Empty<uint>();
        public Vector4 DiffuseColor = Vector4.One;
        public string? TexturePath;
    }

    internal static class MeshFileLoader
    {
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
                    ".obj" => LoadObjSolid(filePath, color),
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

        public static List<TexturedSubmesh>? LoadTextured(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return null;

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            try
            {
                return ext switch
                {
                    ".obj" => LoadObjTextured(filePath),
                    _ => null
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[MeshFileLoader] Failed to load textured {filePath}: {ex.Message}");
                return null;
            }
        }

        // ── STL loader ─────────────────────────────────────────────────

        private static (SolidVertex[] verts, uint[] indices)? LoadStl(
            string filePath, Vector4 color)
        {
            byte[] header = File.ReadAllBytes(filePath);
            if (header.Length < 84) return null;

            bool isAscii = false;
            if (header.Length > 5)
            {
                string start = System.Text.Encoding.ASCII.GetString(
                    header, 0, Math.Min(300, header.Length));
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
            uint triCount = BitConverter.ToUInt32(data, 80);
            long expected = 84 + (long)triCount * 50;
            if (data.Length < expected || triCount > 10_000_000)
                return null;

            var verts = new SolidVertex[triCount * 3];
            var indices = new uint[triCount * 3];

            int offset = 84;
            for (uint t = 0; t < triCount; t++)
            {
                float nx = BitConverter.ToSingle(data, offset);
                float ny = BitConverter.ToSingle(data, offset + 4);
                float nz = BitConverter.ToSingle(data, offset + 8);
                var normal = new Vector3(nx, ny, nz);
                offset += 12;

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

                offset += 2;
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

        // ── OBJ loader (solid, backward compat) ───────────────────────

        private static (SolidVertex[] verts, uint[] indices)? LoadObjSolid(
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
                    ParseFaceVertex(parts[1], out int vi0, out _, out int ni0);
                    for (int i = 2; i < parts.Length - 1; i++)
                    {
                        ParseFaceVertex(parts[i], out int vi1, out _, out int ni1);
                        ParseFaceVertex(parts[i + 1], out int vi2, out _, out int ni2);

                        var p0 = GetSafe(positions, vi0);
                        var p1 = GetSafe(positions, vi1);
                        var p2 = GetSafe(positions, vi2);

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

        // ── OBJ loader (textured, with MTL support) ───────────────────

        private static List<TexturedSubmesh>? LoadObjTextured(string filePath)
        {
            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var texCoords = new List<Vector2>();
            var inv = CultureInfo.InvariantCulture;
            string? objDir = Path.GetDirectoryName(filePath);

            var materials = new Dictionary<string, ObjMaterial>(StringComparer.OrdinalIgnoreCase);
            string activeMtl = "";

            var faceGroups = new Dictionary<string, List<(int vi, int ti, int ni)[]>>(
                StringComparer.OrdinalIgnoreCase);
            faceGroups[""] = new List<(int, int, int)[]>();

            bool hasAnyUv = false;

            foreach (string rawLine in File.ReadLines(filePath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "mtllib" when parts.Length >= 2:
                        string mtlFile = string.Join(" ", parts, 1, parts.Length - 1);
                        string mtlPath = Path.Combine(objDir ?? ".", mtlFile);
                        ParseMtl(mtlPath, objDir ?? ".", materials);
                        break;

                    case "usemtl" when parts.Length >= 2:
                        activeMtl = string.Join(" ", parts, 1, parts.Length - 1);
                        if (!faceGroups.ContainsKey(activeMtl))
                            faceGroups[activeMtl] = new List<(int, int, int)[]>();
                        break;

                    case "v" when parts.Length >= 4:
                        positions.Add(new Vector3(
                            float.Parse(parts[1], inv),
                            float.Parse(parts[2], inv),
                            float.Parse(parts[3], inv)));
                        break;

                    case "vn" when parts.Length >= 4:
                        normals.Add(new Vector3(
                            float.Parse(parts[1], inv),
                            float.Parse(parts[2], inv),
                            float.Parse(parts[3], inv)));
                        break;

                    case "vt" when parts.Length >= 3:
                        texCoords.Add(new Vector2(
                            float.Parse(parts[1], inv),
                            float.Parse(parts[2], inv)));
                        hasAnyUv = true;
                        break;

                    case "f" when parts.Length >= 4:
                        var face = new (int vi, int ti, int ni)[parts.Length - 1];
                        for (int i = 1; i < parts.Length; i++)
                        {
                            ParseFaceVertex(parts[i], out int vi, out int ti, out int ni);
                            face[i - 1] = (vi, ti, ni);
                        }
                        faceGroups[activeMtl].Add(face);
                        break;
                }
            }

            if (!hasAnyUv && materials.Count == 0)
                return null;

            var result = new List<TexturedSubmesh>();

            foreach (var kv in faceGroups)
            {
                if (kv.Value.Count == 0) continue;

                materials.TryGetValue(kv.Key, out var mtl);
                var diffuse = mtl?.DiffuseColor ?? Vector4.One;

                var outVerts = new List<TexturedVertex>();
                var outIdx = new List<uint>();

                foreach (var face in kv.Value)
                {
                    var v0 = BuildTexturedVertex(face[0], positions, normals, texCoords);
                    for (int i = 1; i < face.Length - 1; i++)
                    {
                        var v1 = BuildTexturedVertex(face[i], positions, normals, texCoords);
                        var v2 = BuildTexturedVertex(face[i + 1], positions, normals, texCoords);

                        if (v0.Normal == Vector3.Zero || v1.Normal == Vector3.Zero || v2.Normal == Vector3.Zero)
                        {
                            var faceN = Vector3.Normalize(
                                Vector3.Cross(v1.Position - v0.Position, v2.Position - v0.Position));
                            if (v0.Normal == Vector3.Zero) v0.Normal = faceN;
                            if (v1.Normal == Vector3.Zero) v1.Normal = faceN;
                            if (v2.Normal == Vector3.Zero) v2.Normal = faceN;
                        }

                        uint bi = (uint)outVerts.Count;
                        outVerts.Add(v0);
                        outVerts.Add(v1);
                        outVerts.Add(v2);
                        outIdx.Add(bi);
                        outIdx.Add(bi + 1);
                        outIdx.Add(bi + 2);
                    }
                }

                if (outVerts.Count == 0) continue;

                result.Add(new TexturedSubmesh
                {
                    Vertices = outVerts.ToArray(),
                    Indices = outIdx.ToArray(),
                    DiffuseColor = diffuse,
                    TexturePath = mtl?.DiffuseTexturePath
                });
            }

            return result.Count > 0 ? result : null;
        }

        private static TexturedVertex BuildTexturedVertex(
            (int vi, int ti, int ni) idx,
            List<Vector3> positions, List<Vector3> normals, List<Vector2> texCoords)
        {
            var pos = GetSafe(positions, idx.vi);
            var norm = idx.ni != 0 ? GetSafe(normals, idx.ni) : Vector3.Zero;
            var uv = idx.ti != 0 && texCoords.Count > 0
                ? GetSafe2(texCoords, idx.ti)
                : Vector2.Zero;
            return new TexturedVertex(pos, norm, uv);
        }

        // ── MTL parser ─────────────────────────────────────────────────

        private static void ParseMtl(
            string mtlPath, string baseDir,
            Dictionary<string, ObjMaterial> materials)
        {
            if (!File.Exists(mtlPath)) return;
            var inv = CultureInfo.InvariantCulture;
            ObjMaterial? current = null;

            foreach (string rawLine in File.ReadLines(mtlPath))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split((char[])null!, StringSplitOptions.RemoveEmptyEntries);

                switch (parts[0])
                {
                    case "newmtl" when parts.Length >= 2:
                        current = new ObjMaterial
                        {
                            Name = string.Join(" ", parts, 1, parts.Length - 1)
                        };
                        materials[current.Name] = current;
                        break;

                    case "Kd" when current != null && parts.Length >= 4:
                        float r = float.Parse(parts[1], inv);
                        float g = float.Parse(parts[2], inv);
                        float b = float.Parse(parts[3], inv);
                        current.DiffuseColor = new Vector4(r, g, b, current.Opacity);
                        break;

                    case "d" when current != null && parts.Length >= 2:
                        current.Opacity = float.Parse(parts[1], inv);
                        current.DiffuseColor = new Vector4(
                            current.DiffuseColor.X, current.DiffuseColor.Y,
                            current.DiffuseColor.Z, current.Opacity);
                        break;

                    case "map_Kd" when current != null && parts.Length >= 2:
                        string texFile = string.Join(" ", parts, 1, parts.Length - 1);
                        string texPath = Path.IsPathRooted(texFile)
                            ? texFile
                            : Path.Combine(baseDir, texFile);
                        if (File.Exists(texPath))
                            current.DiffuseTexturePath = texPath;
                        break;
                }
            }
        }

        // ── Utilities ──────────────────────────────────────────────────

        private static void ParseFaceVertex(string token, out int vi, out int ti, out int ni)
        {
            vi = 0; ti = 0; ni = 0;
            var parts = token.Split('/');
            if (parts.Length >= 1 && int.TryParse(parts[0], out int v)) vi = v;
            if (parts.Length >= 2 && int.TryParse(parts[1], out int t)) ti = t;
            if (parts.Length >= 3 && int.TryParse(parts[2], out int n)) ni = n;
        }

        private static Vector3 GetSafe(List<Vector3> list, int oneBasedIdx)
        {
            int idx = oneBasedIdx > 0 ? oneBasedIdx - 1 : list.Count + oneBasedIdx;
            if (idx >= 0 && idx < list.Count) return list[idx];
            return Vector3.Zero;
        }

        private static Vector2 GetSafe2(List<Vector2> list, int oneBasedIdx)
        {
            int idx = oneBasedIdx > 0 ? oneBasedIdx - 1 : list.Count + oneBasedIdx;
            if (idx >= 0 && idx < list.Count) return list[idx];
            return Vector2.Zero;
        }
    }
}
