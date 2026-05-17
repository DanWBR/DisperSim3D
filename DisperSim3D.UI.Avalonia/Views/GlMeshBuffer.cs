#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Avalonia.OpenGL;
using DisperSim3D.Core;
using DisperSim3D.Models;
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
    [StructLayout(LayoutKind.Sequential)]
    public struct TexturedVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 TexCoord;

        public TexturedVertex(Vector3 pos, Vector3 normal, Vector2 uv)
        {
            Position = pos;
            Normal = normal;
            TexCoord = uv;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct LineVertex
    {
        public Vector3 Position;
        public Vector4 Color;
        public float ArcT;

        public LineVertex(Vector3 pos, Vector4 color, float arcT = 0f)
        {
            Position = pos;
            Color = color;
            ArcT = arcT;
        }
    }

    internal sealed class GlMeshBuffer
    {
        private const int GL_ELEMENT_ARRAY = 0x8893; // GL_ELEMENT_ARRAY_BUFFER
        private const int GL_UNSIGNED_INT  = 0x1405;
        private const int GL_TRIANGLES_VAL = 0x0004;
        private const int GL_LINES_VAL     = 0x0001;

        private int _vao;
        private int _vbo;
        private int _ibo;
        private int _indexCount;
        private int _vertexCount;
        private bool _hasIndices;
        private bool _isLineGeometry;
        private bool _isTextured;

        internal SolidVertex[]? CpuVertices { get; private set; }
        internal TexturedVertex[]? CpuTexturedVertices { get; private set; }
        internal uint[]? CpuIndices { get; private set; }

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
            ReadOnlySpan<uint> indices = default,
            bool keepCpuCopy = false)
        {
            Cleanup(gl); // release any previous resources

            _vertexCount = vertices.Length;
            _indexCount  = indices.Length;
            _hasIndices  = indices.Length > 0;

            if (keepCpuCopy)
            {
                CpuVertices = vertices.ToArray();
                CpuIndices = indices.Length > 0 ? indices.ToArray() : null;
            }

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

            int mode = _isLineGeometry ? GL_LINES_VAL : GL_TRIANGLES_VAL;
            if (_hasIndices)
                gl.DrawElements(mode, _indexCount,
                    GL_UNSIGNED_INT, IntPtr.Zero);
            else
                gl.DrawArrays(mode, 0, (IntPtr)_vertexCount);

            gl.BindVertexArray(0);
        }

        public bool IsLineGeometry => _isLineGeometry;
        public bool IsTextured => _isTextured;

        public unsafe void UploadTextured(
            GlInterface gl,
            ReadOnlySpan<TexturedVertex> vertices,
            ReadOnlySpan<uint> indices)
        {
            Cleanup(gl);
            _vertexCount = vertices.Length;
            _indexCount = indices.Length;
            _hasIndices = indices.Length > 0;
            _isTextured = true;
            CpuTexturedVertices = vertices.ToArray();

            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            _vbo = gl.GenBuffer();
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            fixed (TexturedVertex* ptr = vertices)
                gl.BufferData(GL_ARRAY_BUFFER,
                    (IntPtr)(vertices.Length * sizeof(TexturedVertex)),
                    (IntPtr)ptr, GL_STATIC_DRAW);

            const int stride = 8 * sizeof(float); // 32 bytes

            gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, stride, IntPtr.Zero);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 3, GL_FLOAT, 0, stride, (IntPtr)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 2, GL_FLOAT, 0, stride, (IntPtr)(6 * sizeof(float)));
            gl.EnableVertexAttribArray(2);

            if (_hasIndices)
            {
                _ibo = gl.GenBuffer();
                gl.BindBuffer(GL_ELEMENT_ARRAY, _ibo);
                fixed (uint* ptr = indices)
                    gl.BufferData(GL_ELEMENT_ARRAY,
                        (IntPtr)(indices.Length * sizeof(uint)),
                        (IntPtr)ptr, GL_STATIC_DRAW);
            }

            gl.BindVertexArray(0);
            gl.BindBuffer(GL_ARRAY_BUFFER, 0);
            if (_hasIndices) gl.BindBuffer(GL_ELEMENT_ARRAY, 0);
        }

        public unsafe void UploadLines(GlInterface gl, ReadOnlySpan<LineVertex> vertices)
        {
            Cleanup(gl);
            _vertexCount = vertices.Length;
            _isLineGeometry = true;

            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            _vbo = gl.GenBuffer();
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            fixed (LineVertex* ptr = vertices)
                gl.BufferData(GL_ARRAY_BUFFER,
                    (IntPtr)(vertices.Length * sizeof(LineVertex)),
                    (IntPtr)ptr, GL_STATIC_DRAW);

            const int stride = 8 * sizeof(float); // 32 bytes (pos3 + col4 + arcT1)
            gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, stride, IntPtr.Zero);
            gl.EnableVertexAttribArray(0);
            gl.VertexAttribPointer(1, 4, GL_FLOAT, 0, stride, (IntPtr)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);
            gl.VertexAttribPointer(2, 1, GL_FLOAT, 0, stride, (IntPtr)(7 * sizeof(float)));
            gl.EnableVertexAttribArray(2);

            gl.BindVertexArray(0);
            gl.BindBuffer(GL_ARRAY_BUFFER, 0);
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

        /// <summary>
        /// Generate a stylised flame mesh — a tapered, slightly wavy cone
        /// with a vertical colour gradient: deep red at the base, orange in
        /// the body, bright yellow / white near the tip. Each vertex carries
        /// the gradient colour so the existing solid lit shader paints the
        /// flame naturally; the fire point-light pass illuminates nearby
        /// decorations independently from this mesh.
        ///
        /// The mesh is intentionally simple (no animated displacement) so
        /// the GPU cost stays low — the flicker the user perceives comes
        /// from the fire-point-light shader pulse, not vertex morphing.
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateFlame(
            Vector3 baseCenter, float baseRadius, float height,
            int slices = 16, int stacks = 6)
        {
            if (slices < 4) slices = 4;
            if (stacks < 2) stacks = 2;

            // Palette: deep-red base → orange-yellow → near-white tip
            Vector4 colBase  = new(0.85f, 0.08f, 0.04f, 0.9f);
            Vector4 colMid   = new(1.00f, 0.55f, 0.12f, 0.85f);
            Vector4 colTip   = new(1.00f, 0.95f, 0.70f, 0.7f);

            var verts = new SolidVertex[(stacks + 1) * slices + 1];
            int vi = 0;

            // Rings from base (t=0) to just below tip (t=1).
            for (int s = 0; s <= stacks; s++)
            {
                float t = (float)s / stacks;
                // Radius profile: full at base, tapers to ~0 at tip.
                // sqrt for a fuller belly; pow 2.4 for a sharp top.
                float taper = MathF.Pow(1.0f - t, 0.55f);
                float r = baseRadius * (0.55f + 0.45f * MathF.Sqrt(taper)) * taper;
                if (s == stacks) r = baseRadius * 0.01f;
                float z = height * t;
                Vector4 col = t < 0.5f
                    ? Vector4.Lerp(colBase, colMid, t * 2f)
                    : Vector4.Lerp(colMid, colTip, (t - 0.5f) * 2f);

                for (int i = 0; i < slices; i++)
                {
                    float a = (float)(2.0 * Math.PI * i / slices);
                    // Small wobble so the silhouette isn't a perfect cone.
                    float wob = 1.0f + 0.06f * MathF.Sin(a * 3.0f + t * 4.0f);
                    var p = baseCenter + new Vector3(r * MathF.Cos(a) * wob,
                                                     r * MathF.Sin(a) * wob,
                                                     z);
                    // Outward-and-slightly-up normal, suitable for the
                    // shader's Lambert dot product against the sun / moon.
                    var n = Vector3.Normalize(new Vector3(MathF.Cos(a), MathF.Sin(a), 0.3f));
                    verts[vi++] = new SolidVertex(p, n, col);
                }
            }
            // Single apex vertex
            int apexIdx = vi;
            verts[vi++] = new SolidVertex(
                baseCenter + new Vector3(0, 0, height * 1.02f),
                new Vector3(0, 0, 1), colTip);

            // Triangulate sides + apex cap. Side quads are CCW seen from
            // outside; the top stack is wired into the apex as a fan.
            int triCount = stacks * slices * 2 + slices;
            var indices = new uint[triCount * 3];
            int ii = 0;
            for (int s = 0; s < stacks; s++)
            {
                int row0 = s * slices;
                int row1 = (s + 1) * slices;
                for (int i = 0; i < slices; i++)
                {
                    int i1 = (i + 1) % slices;
                    uint a = (uint)(row0 + i);
                    uint b = (uint)(row0 + i1);
                    uint c = (uint)(row1 + i);
                    uint d = (uint)(row1 + i1);
                    indices[ii++] = a; indices[ii++] = c; indices[ii++] = b;
                    indices[ii++] = b; indices[ii++] = c; indices[ii++] = d;
                }
            }
            // Apex fan from last ring
            int last = stacks * slices;
            for (int i = 0; i < slices; i++)
            {
                int i1 = (i + 1) % slices;
                indices[ii++] = (uint)(last + i);
                indices[ii++] = (uint)apexIdx;
                indices[ii++] = (uint)(last + i1);
            }
            return (verts, indices);
        }

        /// <summary>
        /// Generate a 3D arrow (cylinder shaft + cone head) oriented along
        /// the given direction vector. Used for release-direction indicators.
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateArrow(
            Vector3 origin, Vector3 direction, Vector4 color,
            float shaftRadius = 0.3f, float headRadius = 0.8f,
            float shaftLength = 4.5f, float headLength = 1.5f,
            int slices = 12)
        {
            // Build a local coordinate frame from the direction
            var forward = Vector3.Normalize(direction);
            // Pick a helper vector that isn't parallel to forward
            var helper = MathF.Abs(Vector3.Dot(forward, Vector3.UnitZ)) < 0.99f
                ? Vector3.UnitZ : Vector3.UnitX;
            var right = Vector3.Normalize(Vector3.Cross(forward, helper));
            var up    = Vector3.Cross(right, forward);

            var verts   = new System.Collections.Generic.List<SolidVertex>();
            var indices = new System.Collections.Generic.List<uint>();

            // ── Shaft (open cylinder from origin along direction) ────────
            for (int i = 0; i <= slices; i++)
            {
                float theta = 2f * MathF.PI * i / slices;
                float ct = MathF.Cos(theta), st = MathF.Sin(theta);
                var circleDir = right * ct + up * st;
                var normal = circleDir; // outward-facing

                var p0 = origin + circleDir * shaftRadius;
                var p1 = origin + forward * shaftLength + circleDir * shaftRadius;

                verts.Add(new SolidVertex(p0, normal, color));
                verts.Add(new SolidVertex(p1, normal, color));
            }
            for (int i = 0; i < slices; i++)
            {
                uint b = (uint)(i * 2);
                indices.Add(b);     indices.Add(b + 2); indices.Add(b + 1);
                indices.Add(b + 1); indices.Add(b + 2); indices.Add(b + 3);
            }

            // ── Cone head (from shaft end to tip) ───────────────────────
            var coneBase = origin + forward * shaftLength;
            var coneTip  = origin + forward * (shaftLength + headLength);

            // Cone slope normal: angled outward and forward
            float slopeAngle = MathF.Atan2(headRadius, headLength);
            float cosSlope = MathF.Cos(slopeAngle);
            float sinSlope = MathF.Sin(slopeAngle);

            uint tipBase = (uint)verts.Count;
            // Tip vertex (shared per-triangle for smooth cone top)
            for (int i = 0; i < slices; i++)
            {
                float theta0 = 2f * MathF.PI * i / slices;
                float theta1 = 2f * MathF.PI * (i + 1) / slices;
                float ct0 = MathF.Cos(theta0), st0 = MathF.Sin(theta0);
                float ct1 = MathF.Cos(theta1), st1 = MathF.Sin(theta1);

                var d0 = right * ct0 + up * st0;
                var d1 = right * ct1 + up * st1;

                var n0 = Vector3.Normalize(d0 * cosSlope + forward * sinSlope);
                var n1 = Vector3.Normalize(d1 * cosSlope + forward * sinSlope);
                var nMid = Vector3.Normalize((n0 + n1) * 0.5f);

                uint bi = (uint)verts.Count;
                verts.Add(new SolidVertex(coneBase + d0 * headRadius, n0, color));
                verts.Add(new SolidVertex(coneBase + d1 * headRadius, n1, color));
                verts.Add(new SolidVertex(coneTip, nMid, color));
                indices.Add(bi); indices.Add(bi + 1); indices.Add(bi + 2);
            }

            // Cone base cap (flat disc facing backward)
            uint capCenter = (uint)verts.Count;
            var backN = -forward;
            verts.Add(new SolidVertex(coneBase, backN, color));
            for (int i = 0; i <= slices; i++)
            {
                float theta = 2f * MathF.PI * i / slices;
                var d = right * MathF.Cos(theta) + up * MathF.Sin(theta);
                verts.Add(new SolidVertex(coneBase + d * headRadius, backN, color));
            }
            for (int i = 0; i < slices; i++)
            {
                indices.Add(capCenter);
                indices.Add(capCenter + (uint)i + 2);
                indices.Add(capCenter + (uint)i + 1);
            }

            return (verts.ToArray(), indices.ToArray());
        }

        /// <summary>
        /// Generate a sky-dome hemisphere mesh with per-vertex colour gradient
        /// from zenith (top) to horizon (bottom ring). Viewed from inside, so
        /// winding is reversed. Radius should be large enough to enclose the
        /// entire scene (500 m by default).
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateHemisphere(
            float radius, Vector4 zenithColor, Vector4 horizonColor,
            int stacks = 24, int slices = 32, bool fullSphere = false)
        {
            int vertCount = (stacks + 1) * (slices + 1);
            int idxCount  = stacks * slices * 6;
            var verts   = new SolidVertex[vertCount];
            var indices = new uint[idxCount];

            float maxPhi = fullSphere ? MathF.PI : MathF.PI * 0.5f;

            int vi = 0;
            for (int st = 0; st <= stacks; st++)
            {
                float phi = maxPhi * st / stacks;
                float sp  = MathF.Sin(phi);
                float cp  = MathF.Cos(phi);

                float t = (float)st / stacks;
                var color = Vector4.Lerp(zenithColor, horizonColor, MathF.Min(t * (fullSphere ? 2f : 1f), 1f));

                for (int sl = 0; sl <= slices; sl++)
                {
                    float theta = 2f * MathF.PI * sl / slices;
                    float ct    = MathF.Cos(theta);
                    float sth   = MathF.Sin(theta);

                    var pos = new Vector3(sp * ct * radius, sp * sth * radius, cp * radius);
                    // Normal points inward (we view from inside)
                    var n = -Vector3.Normalize(pos);
                    verts[vi++] = new SolidVertex(pos, n, color);
                }
            }

            // Indices — reversed winding for inside-out view
            int ii = 0;
            for (int st = 0; st < stacks; st++)
            {
                for (int sl = 0; sl < slices; sl++)
                {
                    uint a = (uint)(st * (slices + 1) + sl);
                    uint b = a + 1;
                    uint c = (uint)((st + 1) * (slices + 1) + sl);
                    uint d = c + 1;

                    // Reversed winding: a→b→c becomes a→c→b
                    indices[ii++] = a; indices[ii++] = b; indices[ii++] = c;
                    indices[ii++] = b; indices[ii++] = d; indices[ii++] = c;
                }
            }

            return (verts, indices);
        }

        /// <summary>
        /// Generate a flat circular ground disc at Z = <paramref name="elevation"/>
        /// with the specified radius and colour (128 segments).
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) GenerateGroundDisc(
            float radius, Vector4 color, float elevation = -0.02f, int segments = 128)
        {
            var n = Vector3.UnitZ;
            var verts = new SolidVertex[segments + 2];
            verts[0] = new SolidVertex(new Vector3(0, 0, elevation), n, color);

            for (int i = 0; i <= segments; i++)
            {
                float angle = 2f * MathF.PI * i / segments;
                float x = radius * MathF.Cos(angle);
                float y = radius * MathF.Sin(angle);
                verts[i + 1] = new SolidVertex(new Vector3(x, y, elevation), n, color);
            }

            var indices = new uint[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                indices[i * 3] = 0;
                indices[i * 3 + 1] = (uint)(i + 1);
                indices[i * 3 + 2] = (uint)(i + 2);
            }

            return (verts, indices);
        }

        // ── Result-driven generators (Views) ───────────────────────────

        /// <summary>
        /// Convert a <see cref="PortableMarchingCubes.IsosurfaceResult"/> to
        /// <see cref="SolidVertex"/> arrays suitable for GPU upload.
        /// </summary>
        public static (SolidVertex[] verts, uint[] indices) FromIsosurfaceResult(
            PortableMarchingCubes.IsosurfaceResult result, Vector4 color)
        {
            int vc = result.VertexCount;
            var verts = new SolidVertex[vc];
            for (int i = 0; i < vc; i++)
            {
                int b = i * 3;
                verts[i] = new SolidVertex(
                    new Vector3(result.Positions[b], result.Positions[b + 1], result.Positions[b + 2]),
                    new Vector3(result.Normals[b], result.Normals[b + 1], result.Normals[b + 2]),
                    color);
            }

            var indices = new uint[result.Indices.Length];
            for (int i = 0; i < result.Indices.Length; i++)
                indices[i] = (uint)result.Indices[i];

            return (verts, indices);
        }

        /// <summary>
        /// Generate a subdivided quad for contour-plane visualisation. Each
        /// vertex is coloured by sampling the concentration field at that
        /// position through the given <see cref="ColorMapName"/>.
        /// </summary>
        /// <param name="kind">XY, XZ, or YZ plane orientation.</param>
        /// <param name="planePos">Position along the plane's normal axis.</param>
        /// <param name="halfSize">Half-extent of the domain.</param>
        /// <param name="field">3D scalar field (already selected/merged time step).</param>
        /// <param name="gridRes">Grid cells per axis for the source field.</param>
        /// <param name="colorMap">Colour palette.</param>
        /// <param name="minVal">Lower bound of the colour scale.</param>
        /// <param name="maxVal">Upper bound of the colour scale.</param>
        /// <param name="opacity">Alpha channel (0..1).</param>
        /// <param name="resolution">Number of subdivisions per axis for the quad.</param>
        public static (SolidVertex[] verts, uint[] indices) GenerateContourPlane(
            ViewKind kind, double planePos, float halfSize,
            double[,,] field, int gridRes,
            ColorMapName colorMap, double minVal, double maxVal,
            float opacity, int resolution = 80)
        {
            int res = Math.Max(4, resolution);
            double range = Math.Max(maxVal - minVal, 1e-9);

            // Wrap field for trilinear interpolation
            var fld = new OpenFoamConcentrationField(field, halfSize, gridRes);

            int vertCount = (res + 1) * (res + 1);
            int idxCount = res * res * 6;
            var verts = new SolidVertex[vertCount];
            var indices = new uint[idxCount];

            // Determine plane normal
            Vector3 normal;
            switch (kind)
            {
                case ViewKind.ContourXZ: normal = Vector3.UnitY; break;
                case ViewKind.ContourYZ: normal = Vector3.UnitX; break;
                default: normal = Vector3.UnitZ; break; // ContourXY
            }

            // Generate vertex grid
            int vi = 0;
            for (int j = 0; j <= res; j++)
            {
                double v = (double)j / res; // 0..1
                for (int i = 0; i <= res; i++)
                {
                    double u = (double)i / res; // 0..1

                    // Map (u,v) to world position
                    double x, y, z;
                    switch (kind)
                    {
                        case ViewKind.ContourXY:
                            x = -halfSize + u * 2 * halfSize;
                            y = +halfSize - v * 2 * halfSize;
                            z = planePos;
                            break;
                        case ViewKind.ContourXZ:
                            x = -halfSize + u * 2 * halfSize;
                            y = planePos;
                            z = 2 * halfSize * (1 - v);
                            break;
                        case ViewKind.ContourYZ:
                            x = planePos;
                            y = -halfSize + u * 2 * halfSize;
                            z = 2 * halfSize * (1 - v);
                            break;
                        default:
                            x = y = z = 0;
                            break;
                    }

                    // Sample concentration and map to color
                    double val = fld.EvaluateConcentration(x, y, z);
                    double t = (val - minVal) / range;
                    t = Math.Max(0.0, Math.Min(1.0, t));

                    var c = ColorMapHelper.Sample(colorMap, t);
                    var color = new Vector4(c.ScR, c.ScG, c.ScB, opacity);

                    verts[vi++] = new SolidVertex(
                        new Vector3((float)x, (float)y, (float)z),
                        normal, color);
                }
            }

            // Generate triangle indices
            int ii = 0;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    uint a = (uint)(j * (res + 1) + i);
                    uint b = a + 1;
                    uint c = (uint)((j + 1) * (res + 1) + i);
                    uint d = c + 1;

                    indices[ii++] = a; indices[ii++] = b; indices[ii++] = c;
                    indices[ii++] = b; indices[ii++] = d; indices[ii++] = c;
                }
            }

            return (verts, indices);
        }

        /// <summary>
        /// Compute the min/max range of a 3D scalar field, used for
        /// auto-ranging contour planes.
        /// </summary>
        public static void ComputeFieldRange(double[,,] field, out double min, out double max)
        {
            min = double.MaxValue; max = double.MinValue;
            int nx = field.GetLength(0), ny = field.GetLength(1), nz = field.GetLength(2);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    for (int k = 0; k < nz; k++)
                    {
                        double v = field[i, j, k];
                        if (v < min) min = v;
                        if (v > max) max = v;
                    }
            if (min == max) max = min + 1e-9;
        }

        public static LineVertex[] GenerateStreamlines(
            WindField3D wf,
            double xMin, double xMax,
            double yMin, double yMax,
            double zMax,
            int seedsPerAxis = 12,
            int verticalLayers = 2,
            float opacity = 0.85f)
        {
            double dx = (xMax - xMin) / seedsPerAxis;
            double dy = (yMax - yMin) / seedsPerAxis;
            double step = 0.5 * Math.Min(dx, dy);
            int maxSteps = Math.Min(2000,
                (int)Math.Ceiling(1.5 * (xMax - xMin) / step));

            // Probe a coarse grid to find the speed range for colormap normalization
            float minSpeed = float.MaxValue, maxSpeed = 0.001f;
            for (int pi = 0; pi < 16; pi++)
            {
                double px = xMin + (pi + 0.5) * (xMax - xMin) / 16;
                for (int pj = 0; pj < 16; pj++)
                {
                    double py = yMin + (pj + 0.5) * (yMax - yMin) / 16;
                    double pz = Math.Min(5.0, zMax * 0.1);
                    var pv = wf.Interpolate(px, py, pz);
                    float ps = (float)Math.Sqrt(pv.X * pv.X + pv.Y * pv.Y + pv.Z * pv.Z);
                    if (ps > 0.05f)
                    {
                        if (ps < minSpeed) minSpeed = ps;
                        if (ps > maxSpeed) maxSpeed = ps;
                    }
                }
            }
            if (minSpeed >= maxSpeed) minSpeed = 0f;
            float speedRange = maxSpeed - minSpeed;
            if (speedRange < 1e-6f) speedRange = 1f;

            var lineVerts = new List<LineVertex>(seedsPerAxis * seedsPerAxis * verticalLayers * 200);

            for (int iz = 0; iz < verticalLayers; iz++)
            {
                double z0 = verticalLayers == 1
                    ? Math.Min(5.0, zMax * 0.1)
                    : zMax * 0.05 + iz * (zMax * 0.4) / Math.Max(1, verticalLayers - 1);

                for (int ix = 0; ix < seedsPerAxis; ix++)
                {
                    double sx = xMin + (ix + 0.5) * dx;
                    for (int iy = 0; iy < seedsPerAxis; iy++)
                    {
                        double sy = yMin + (iy + 0.5) * dy;
                        MarchStreamline(wf, sx, sy, z0,
                            step, maxSteps, +1,
                            xMin, xMax, yMin, yMax, zMax,
                            minSpeed, speedRange, opacity, lineVerts);
                        MarchStreamline(wf, sx, sy, z0,
                            step, maxSteps, -1,
                            xMin, xMax, yMin, yMax, zMax,
                            minSpeed, speedRange, opacity, lineVerts);
                    }
                }
            }

            return lineVerts.ToArray();
        }

        public static (SolidVertex[] verts, uint[] indices) GenerateGrassBlades(
            float halfSize, int bladeCount, uint seed = 12345,
            List<(float minX, float minY, float maxX, float maxY)>? exclusionZones = null)
        {
            var vertList = new List<SolidVertex>(bladeCount * 4);
            var idxList = new List<uint>(bladeCount * 6);

            uint rng = seed;
            uint NextRng() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return rng; }
            float Rand01() => (NextRng() & 0x7FFF) / 32767f;

            int placed = 0;
            int attempts = 0;
            int maxAttempts = bladeCount * 3;

            while (placed < bladeCount && attempts < maxAttempts)
            {
                attempts++;
                float bx = (Rand01() * 2f - 1f) * halfSize;
                float by = (Rand01() * 2f - 1f) * halfSize;

                if (exclusionZones != null)
                {
                    bool excluded = false;
                    for (int ez = 0; ez < exclusionZones.Count; ez++)
                    {
                        var z = exclusionZones[ez];
                        if (bx >= z.minX && bx <= z.maxX &&
                            by >= z.minY && by <= z.maxY)
                        { excluded = true; break; }
                    }
                    if (excluded) continue;
                }
                float height = 0.3f + Rand01() * 0.7f;
                float width = 0.06f + Rand01() * 0.06f;
                float angle = Rand01() * MathF.PI * 2f;
                float ca = MathF.Cos(angle), sa = MathF.Sin(angle);
                float dx = -sa * width * 0.5f, dy = ca * width * 0.5f;

                var darkGreen = new Vector4(0.18f, 0.32f, 0.10f, 1f);
                var lightGreen = new Vector4(0.35f, 0.55f, 0.20f, 0.9f);

                uint bi = (uint)vertList.Count;
                vertList.Add(new SolidVertex(
                    new Vector3(bx - dx, by - dy, 0f),
                    new Vector3(0, 0, 0f), darkGreen));
                vertList.Add(new SolidVertex(
                    new Vector3(bx + dx, by + dy, 0f),
                    new Vector3(0, 0, 0f), darkGreen));
                vertList.Add(new SolidVertex(
                    new Vector3(bx + dx * 0.3f, by + dy * 0.3f, height),
                    new Vector3(0, 0, 1f), lightGreen));
                vertList.Add(new SolidVertex(
                    new Vector3(bx - dx * 0.3f, by - dy * 0.3f, height),
                    new Vector3(0, 0, 1f), lightGreen));

                idxList.Add(bi); idxList.Add(bi + 1); idxList.Add(bi + 2);
                idxList.Add(bi); idxList.Add(bi + 2); idxList.Add(bi + 3);
                placed++;
            }

            return (vertList.ToArray(), idxList.ToArray());
        }

        private static void MarchStreamline(
            WindField3D wf,
            double x0, double y0, double z0,
            double step, int maxSteps, int direction,
            double xMin, double xMax, double yMin, double yMax, double zMax,
            float minSpeed, float speedRange, float opacity,
            List<LineVertex> verts)
        {
            double px = x0, py = y0, pz = z0;
            float cumLen = 0f;

            for (int i = 0; i < maxSteps; i++)
            {
                var v1 = wf.Interpolate(px, py, pz);
                double s1 = Math.Sqrt(v1.X * v1.X + v1.Y * v1.Y + v1.Z * v1.Z);
                if (s1 < 0.05) break;

                double d1x = v1.X / s1 * direction;
                double d1y = v1.Y / s1 * direction;
                double d1z = v1.Z / s1 * direction;

                double mx = px + d1x * step * 0.5;
                double my = py + d1y * step * 0.5;
                double mz = pz + d1z * step * 0.5;

                var v2 = wf.Interpolate(mx, my, mz);
                double s2 = Math.Sqrt(v2.X * v2.X + v2.Y * v2.Y + v2.Z * v2.Z);
                if (s2 < 0.05) break;

                double d2x = v2.X / s2 * direction;
                double d2y = v2.Y / s2 * direction;
                double d2z = v2.Z / s2 * direction;

                double nx = px + d2x * step;
                double ny = py + d2y * step;
                double nz = pz + d2z * step;

                if (nx < xMin || nx > xMax || ny < yMin || ny > yMax || nz < 0 || nz > zMax)
                    break;

                float segLen = (float)Math.Sqrt(
                    (nx - px) * (nx - px) + (ny - py) * (ny - py) + (nz - pz) * (nz - pz));
                float arcT0 = cumLen;
                cumLen += segLen;

                float t1 = Math.Clamp((float)((s1 - minSpeed) / speedRange), 0f, 1f);
                float t2 = Math.Clamp((float)((s2 - minSpeed) / speedRange), 0f, 1f);
                var c1 = ColorMapHelper.Sample(ColorMapName.Jet, t1);
                var c2 = ColorMapHelper.Sample(ColorMapName.Jet, t2);

                verts.Add(new LineVertex(
                    new Vector3((float)px, (float)py, (float)pz),
                    new Vector4(c1.ScR, c1.ScG, c1.ScB, opacity), arcT0));
                verts.Add(new LineVertex(
                    new Vector3((float)nx, (float)ny, (float)nz),
                    new Vector4(c2.ScR, c2.ScG, c2.ScB, opacity), cumLen));

                px = nx; py = ny; pz = nz;
            }
        }
    }
}
