#nullable enable
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using static Avalonia.OpenGL.GlConsts;

namespace DisperSim3D.UI.Avalonia.Views
{
    // ── Vertex types ────────────────────────────────────────────────────

    /// <summary>
    /// Per-vertex data for lit solid meshes.
    /// Position (vec3) + Normal (vec3) + Color (vec4) = 10 floats = 40 bytes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SolidVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector4 Color;

        public SolidVertex(Vector3 pos, Vector3 normal, Vector4 color)
        {
            Position = pos;
            Normal = normal;
            Color = color;
        }
    }

    // ── Mesh buffer ─────────────────────────────────────────────────────

    /// <summary>
    /// GPU-resident mesh buffer (VAO + VBO + optional IBO).
    /// Upload once with <see cref="Upload"/>, draw many times with
    /// <see cref="Draw"/>.  Used for source markers, monitors, detectors,
    /// decorations, isosurfaces, etc.
    /// </summary>
    internal sealed class GlMeshBuffer
    {
        private const int GL_ELEMENT_ARRAY = 0x8893; // GL_ELEMENT_ARRAY_BUFFER
        private const int GL_UNSIGNED_INT  = 0x1405;
        private const int GL_TRIANGLES_VAL = 0x0004;

        private int _vao;
        private int _vbo;
        private int _ibo;
        private int _indexCount;
        private int _vertexCount;
        private bool _hasIndices;

        /// <summary>Number of triangles in this mesh.</summary>
        public int TriangleCount => _hasIndices
            ? _indexCount / 3
            : _vertexCount / 3;

        /// <summary>
        /// Upload vertex data (and optional index data) to the GPU.
        /// </summary>
        public unsafe void Upload(
            GlInterface gl,
            ReadOnlySpan<SolidVertex> vertices,
            ReadOnlySpan<uint> indices = default)
        {
            Cleanup(gl); // release any previous resources

            _vertexCount = vertices.Length;
            _indexCount  = indices.Length;
            _hasIndices  = indices.Length > 0;

            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            // ── VBO ─────────────────────────────────────────────────────
            _vbo = gl.GenBuffer();
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            fixed (SolidVertex* ptr = vertices)
                gl.BufferData(GL_ARRAY_BUFFER,
                    (IntPtr)(vertices.Length * sizeof(SolidVertex)),
                    (IntPtr)ptr,
                    GL_STATIC_DRAW);

            const int stride = 10 * sizeof(float); // 40 bytes

            // Attribute 0: position (vec3)
            gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, stride, IntPtr.Zero);
            gl.EnableVertexAttribArray(0);

            // Attribute 1: normal (vec3)
            gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, stride,
                (IntPtr)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);

            // Attribute 2: color (vec4)
            gl.VertexAttribPointer(2, 4, GL_FLOAT, 0, stride,
                (IntPtr)(6 * sizeof(float)));
            gl.EnableVertexAttribArray(2);

            // ── IBO (optional) ──────────────────────────────────────────
            if (_hasIndices)
            {
                _ibo = gl.GenBuffer();
                gl.BindBuffer(GL_ELEMENT_ARRAY, _ibo);
                fixed (uint* ptr = indices)
                    gl.BufferData(GL_ELEMENT_ARRAY,
                        (IntPtr)(indices.Length * sizeof(uint)),
                        (IntPtr)ptr,
                        GL_STATIC_DRAW);
            }

            gl.BindVertexArray(0);
            gl.BindBuffer(GL_ARRAY_BUFFER, 0);
            if (_hasIndices) gl.BindBuffer(GL_ELEMENT_ARRAY, 0);
        }

        /// <summary>
        /// Draw the mesh using the currently bound shader program.
        /// The caller must set all uniforms (MVP, lighting, etc.) first.
        /// </summary>
        public void Draw(GlInterface gl)
        {
            if (_vao == 0) return;
            gl.BindVertexArray(_vao);

            if (_hasIndices)
                gl.DrawElements(GL_TRIANGLES_VAL, _indexCount,
                    GL_UNSIGNED_INT, IntPtr.Zero);
            else
                gl.DrawArrays(GL_TRIANGLES_VAL, 0, (IntPtr)_vertexCount);

            gl.BindVertexArray(0);
        }

        /// <summary>Release GPU resources.</summary>
        public void Cleanup(GlInterface gl)
        {
            if (_ibo != 0) { gl.DeleteBuffer(_ibo); _ibo = 0; }
            if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
            if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
            _indexCount = 0;
            _vertexCount = 0;
            _hasIndices = false;
        }

        // ── Primitive generators ────────────────────────────────────────

        /// <summary>
        /// Generate a UV sphere (position + normals + color).
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateSphere(
            Vector3 center, float radius, Vector4 color,
            int slices = 16, int stacks = 12)
        {
            int vertCount = (stacks + 1) * (slices + 1);
            int idxCount  = stacks * slices * 6;
            var verts   = new SolidVertex[vertCount];
            var indices = new uint[idxCount];

            int vi = 0;
            for (int st = 0; st <= stacks; st++)
            {
                float phi = MathF.PI * st / stacks;
                float sp  = MathF.Sin(phi);
                float cp  = MathF.Cos(phi);

                for (int sl = 0; sl <= slices; sl++)
                {
                    float theta = 2f * MathF.PI * sl / slices;
                    float st2   = MathF.Sin(theta);
                    float ct    = MathF.Cos(theta);

                    var n = new Vector3(sp * ct, sp * st2, cp);
                    verts[vi++] = new SolidVertex(center + n * radius, n, color);
                }
            }

            int ii = 0;
            for (int st = 0; st < stacks; st++)
            {
                for (int sl = 0; sl < slices; sl++)
                {
                    uint a = (uint)(st * (slices + 1) + sl);
                    uint b = a + 1;
                    uint c = (uint)((st + 1) * (slices + 1) + sl);
                    uint d = c + 1;

                    indices[ii++] = a; indices[ii++] = c; indices[ii++] = b;
                    indices[ii++] = b; indices[ii++] = c; indices[ii++] = d;
                }
            }

            return (verts, indices);
        }

        /// <summary>
        /// Generate a vertical cylinder (for source markers).
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateCylinder(
            Vector3 baseCenter, float radius, float height, Vector4 color,
            int slices = 16)
        {
            // Side vertices: 2 rings
            int sideVerts = (slices + 1) * 2;
            // Cap vertices: center + ring for top and bottom
            int capVerts  = (slices + 1 + 1) * 2;
            var verts   = new SolidVertex[sideVerts + capVerts];
            var indices = new System.Collections.Generic.List<uint>(
                slices * 6 + slices * 3 * 2);

            int vi = 0;

            // ── Side ────────────────────────────────────────────────────
            for (int i = 0; i <= slices; i++)
            {
                float theta = 2f * MathF.PI * i / slices;
                float nx = MathF.Cos(theta);
                float ny = MathF.Sin(theta);
                var n = new Vector3(nx, ny, 0);

                // Bottom ring
                verts[vi++] = new SolidVertex(
                    baseCenter + new Vector3(nx * radius, ny * radius, 0),
                    n, color);
                // Top ring
                verts[vi++] = new SolidVertex(
                    baseCenter + new Vector3(nx * radius, ny * radius, height),
                    n, color);
            }

            for (int i = 0; i < slices; i++)
            {
                uint bl = (uint)(i * 2);
                uint tl = bl + 1;
                uint br = (uint)((i + 1) * 2);
                uint tr = br + 1;
                indices.Add(bl); indices.Add(br); indices.Add(tl);
                indices.Add(tl); indices.Add(br); indices.Add(tr);
            }

            // ── Bottom cap ──────────────────────────────────────────────
            uint baseIdx = (uint)vi;
            var downN = -Vector3.UnitZ;
            verts[vi++] = new SolidVertex(baseCenter, downN, color); // center
            for (int i = 0; i <= slices; i++)
            {
                float theta = 2f * MathF.PI * i / slices;
                verts[vi++] = new SolidVertex(
                    baseCenter + new Vector3(
                        MathF.Cos(theta) * radius,
                        MathF.Sin(theta) * radius, 0),
                    downN, color);
            }
            for (int i = 0; i < slices; i++)
            {
                indices.Add(baseIdx);
                indices.Add(baseIdx + (uint)i + 2);
                indices.Add(baseIdx + (uint)i + 1);
            }

            // ── Top cap ─────────────────────────────────────────────────
            baseIdx = (uint)vi;
            var topCenter = baseCenter + new Vector3(0, 0, height);
            var upN = Vector3.UnitZ;
            verts[vi++] = new SolidVertex(topCenter, upN, color);
            for (int i = 0; i <= slices; i++)
            {
                float theta = 2f * MathF.PI * i / slices;
                verts[vi++] = new SolidVertex(
                    topCenter + new Vector3(
                        MathF.Cos(theta) * radius,
                        MathF.Sin(theta) * radius, 0),
                    upN, color);
            }
            for (int i = 0; i < slices; i++)
            {
                indices.Add(baseIdx);
                indices.Add(baseIdx + (uint)i + 1);
                indices.Add(baseIdx + (uint)i + 2);
            }

            return (verts, indices.ToArray());
        }

        /// <summary>
        /// Generate an axis-aligned box (for detector markers).
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateBox(
            Vector3 center, Vector3 halfExtents, Vector4 color)
        {
            float hx = halfExtents.X, hy = halfExtents.Y, hz = halfExtents.Z;

            // 8 corners
            Vector3 c0 = center + new Vector3(-hx, -hy, -hz);
            Vector3 c1 = center + new Vector3(+hx, -hy, -hz);
            Vector3 c2 = center + new Vector3(+hx, +hy, -hz);
            Vector3 c3 = center + new Vector3(-hx, +hy, -hz);
            Vector3 c4 = center + new Vector3(-hx, -hy, +hz);
            Vector3 c5 = center + new Vector3(+hx, -hy, +hz);
            Vector3 c6 = center + new Vector3(+hx, +hy, +hz);
            Vector3 c7 = center + new Vector3(-hx, +hy, +hz);

            // 6 faces × 4 verts = 24 verts (separate normals per face)
            var verts = new SolidVertex[24];
            var indices = new uint[36];

            // Front face (+Y)
            var n = Vector3.UnitY;
            verts[0]  = new SolidVertex(c2, n, color);
            verts[1]  = new SolidVertex(c3, n, color);
            verts[2]  = new SolidVertex(c7, n, color);
            verts[3]  = new SolidVertex(c6, n, color);

            // Back face (-Y)
            n = -Vector3.UnitY;
            verts[4]  = new SolidVertex(c0, n, color);
            verts[5]  = new SolidVertex(c1, n, color);
            verts[6]  = new SolidVertex(c5, n, color);
            verts[7]  = new SolidVertex(c4, n, color);

            // Right face (+X)
            n = Vector3.UnitX;
            verts[8]  = new SolidVertex(c1, n, color);
            verts[9]  = new SolidVertex(c2, n, color);
            verts[10] = new SolidVertex(c6, n, color);
            verts[11] = new SolidVertex(c5, n, color);

            // Left face (-X)
            n = -Vector3.UnitX;
            verts[12] = new SolidVertex(c3, n, color);
            verts[13] = new SolidVertex(c0, n, color);
            verts[14] = new SolidVertex(c4, n, color);
            verts[15] = new SolidVertex(c7, n, color);

            // Top face (+Z)
            n = Vector3.UnitZ;
            verts[16] = new SolidVertex(c4, n, color);
            verts[17] = new SolidVertex(c5, n, color);
            verts[18] = new SolidVertex(c6, n, color);
            verts[19] = new SolidVertex(c7, n, color);

            // Bottom face (-Z)
            n = -Vector3.UnitZ;
            verts[20] = new SolidVertex(c0, n, color);
            verts[21] = new SolidVertex(c3, n, color);
            verts[22] = new SolidVertex(c2, n, color);
            verts[23] = new SolidVertex(c1, n, color);

            // 6 faces × 2 triangles × 3 indices
            for (int face = 0; face < 6; face++)
            {
                uint b = (uint)(face * 4);
                int  i = face * 6;
                indices[i + 0] = b + 0; indices[i + 1] = b + 1; indices[i + 2] = b + 2;
                indices[i + 3] = b + 0; indices[i + 4] = b + 2; indices[i + 5] = b + 3;
            }

            return (verts, indices);
        }

        /// <summary>
        /// Generate a diamond / octahedron shape (for fire source markers).
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateDiamond(
            Vector3 center, float radius, float height, Vector4 color)
        {
            // 6 vertices: top, bottom, +X, -X, +Y, -Y
            var top    = center + new Vector3(0, 0, height * 0.5f);
            var bottom = center - new Vector3(0, 0, height * 0.5f);
            var px     = center + new Vector3(radius, 0, 0);
            var nx     = center - new Vector3(radius, 0, 0);
            var py     = center + new Vector3(0, radius, 0);
            var ny     = center - new Vector3(0, radius, 0);

            // 8 triangular faces, each with its own normal
            var verts   = new SolidVertex[24]; // 8 faces × 3 verts
            var indices = new uint[24];
            int vi = 0, ii = 0;

            void AddFace(Vector3 a, Vector3 b, Vector3 c)
            {
                var n = Vector3.Normalize(Vector3.Cross(b - a, c - a));
                uint baseIdx = (uint)vi;
                verts[vi++] = new SolidVertex(a, n, color);
                verts[vi++] = new SolidVertex(b, n, color);
                verts[vi++] = new SolidVertex(c, n, color);
                indices[ii++] = baseIdx;
                indices[ii++] = baseIdx + 1;
                indices[ii++] = baseIdx + 2;
            }

            // Upper 4 faces
            AddFace(top, px, py);
            AddFace(top, py, nx);
            AddFace(top, nx, ny);
            AddFace(top, ny, px);
            // Lower 4 faces
            AddFace(bottom, py, px);
            AddFace(bottom, nx, py);
            AddFace(bottom, ny, nx);
            AddFace(bottom, px, ny);

            return (verts, indices);
        }
    }
}
