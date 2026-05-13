#nullable enable
using System;
using System.Collections.Generic;
using Avalonia.OpenGL;
using static Avalonia.OpenGL.GlConsts;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Renders a ground grid with major/minor lines and colored axis indicators.
    /// Uses GL_LINES with per-vertex color.
    /// Vertex format: (X, Y, Z, R, G, B, A) = 7 floats = 28 bytes per vertex.
    /// </summary>
    internal sealed class GlGridRenderer
    {
        // GL constants not in Avalonia's GlConsts
        private const int GL_LINES = 0x0001;

        private int _vao;
        private int _vbo;
        private int _vertexCount;

        /// <summary>Half-extent of the grid in meters (grid spans -Half to +Half).</summary>
        public float HalfSize { get; set; } = 100f;

        /// <summary>
        /// Build grid geometry and upload to GPU.  Call once from OnOpenGlInit.
        /// </summary>
        public unsafe void Init(GlInterface gl)
        {
            float half = HalfSize;
            const float minorStep = 2f;
            const float majorStep = 10f;

            // Pre-allocate: ~100 lines per direction * 2 verts * 7 floats
            var verts = new List<float>(8000);

            // ── Grid lines at Z = 0 ─────────────────────────────────────
            for (float v = -half; v <= half + 0.01f; v += minorStep)
            {
                bool isOrigin = MathF.Abs(v) < 0.01f;
                if (isOrigin) continue; // axes drawn separately

                bool isMajor = MathF.Abs(v % majorStep) < 0.01f;
                // Line brightness tuned for light sky + ground plane
                float c = isMajor ? 0.42f : 0.52f;

                // Line parallel to X axis (at y = v)
                AddLine(verts, -half, v, 0f, half, v, 0f, c, c, c, 1f);
                // Line parallel to Y axis (at x = v)
                AddLine(verts, v, -half, 0f, v, half, 0f, c, c, c, 1f);
            }

            // ── Axis lines (drawn last so they render on top) ───────────
            AddLine(verts, -half, 0, 0, half, 0, 0,
                    0.75f, 0.22f, 0.22f, 1f); // X axis -> red
            AddLine(verts, 0, -half, 0, 0, half, 0,
                    0.22f, 0.75f, 0.22f, 1f); // Y axis -> green
            AddLine(verts, 0, 0, 0, 0, 0, 15f,
                    0.30f, 0.30f, 0.90f, 1f); // Z axis -> blue

            // ── Axis tip arrows (small perpendicular lines at the ends) ─
            float tip = 1.5f;
            // X-axis tip
            AddLine(verts, half, -tip, 0, half, tip, 0, 0.75f, 0.22f, 0.22f, 1f);
            // Y-axis tip
            AddLine(verts, -tip, half, 0, tip, half, 0, 0.22f, 0.75f, 0.22f, 1f);
            // Z-axis tip
            AddLine(verts, -tip, 0, 15f, tip, 0, 15f, 0.30f, 0.30f, 0.90f, 1f);

            // ── Cardinal labels (N/S/E/W) as small cross marks ──────────
            float labelDist = half + 5f;
            float labelSize = 2f;
            // North (+Y)
            AddLine(verts, -labelSize, labelDist, 0, labelSize, labelDist, 0,
                    0.5f, 0.5f, 0.5f, 0.8f);
            AddLine(verts, 0, labelDist - labelSize, 0, 0, labelDist + labelSize, 0,
                    0.5f, 0.5f, 0.5f, 0.8f);

            float[] data = verts.ToArray();
            _vertexCount = data.Length / 7;

            // ── Create VAO + VBO ─────────────────────────────────────────
            _vao = gl.GenVertexArray();
            gl.BindVertexArray(_vao);

            _vbo = gl.GenBuffer();
            gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
            fixed (float* ptr = data)
                gl.BufferData(GL_ARRAY_BUFFER,
                    (IntPtr)(data.Length * sizeof(float)),
                    (IntPtr)ptr,
                    GL_STATIC_DRAW);

            const int stride = 7 * sizeof(float);

            // Attribute 0: position (vec3)
            gl.VertexAttribPointer(0, 3, GL_FLOAT, 0, stride, IntPtr.Zero);
            gl.EnableVertexAttribArray(0);

            // Attribute 1: color (vec4)
            gl.VertexAttribPointer(1, 4, GL_FLOAT, 0, stride,
                (IntPtr)(3 * sizeof(float)));
            gl.EnableVertexAttribArray(1);

            gl.BindVertexArray(0);
            gl.BindBuffer(GL_ARRAY_BUFFER, 0);
        }

        /// <summary>
        /// Draw the grid.  The caller must bind the line shader program and
        /// set the MVP uniform before calling this.
        /// </summary>
        public void Render(GlInterface gl)
        {
            if (_vao == 0) return;
            gl.BindVertexArray(_vao);
            gl.DrawArrays(GL_LINES, 0, (IntPtr)_vertexCount);
            gl.BindVertexArray(0);
        }

        /// <summary>
        /// Release GPU resources.  Call from OnOpenGlDeinit.
        /// </summary>
        public void Cleanup(GlInterface gl)
        {
            if (_vao != 0) { gl.DeleteVertexArray(_vao); _vao = 0; }
            if (_vbo != 0) { gl.DeleteBuffer(_vbo); _vbo = 0; }
        }

        // ── Helpers ─────────────────────────────────────────────────────

        private static void AddLine(List<float> v,
            float x1, float y1, float z1,
            float x2, float y2, float z2,
            float r, float g, float b, float a)
        {
            v.Add(x1); v.Add(y1); v.Add(z1);
            v.Add(r);  v.Add(g);  v.Add(b); v.Add(a);
            v.Add(x2); v.Add(y2); v.Add(z2);
            v.Add(r);  v.Add(g);  v.Add(b); v.Add(a);
        }
    }
}
