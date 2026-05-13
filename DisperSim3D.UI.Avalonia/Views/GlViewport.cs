#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using DisperSim3D.Models;
using static Avalonia.OpenGL.GlConsts;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Cross-platform 3D viewport built on Avalonia's <see cref="OpenGlControlBase"/>.
    /// Renders a ground grid with turntable camera orbit, pan, and zoom.
    /// </summary>
    /// <remarks>
    /// Replaces the HelixToolkit.Wpf viewport with a portable OpenGL 3.3
    /// (or OpenGL ES 3.0 via ANGLE) implementation that runs on Windows,
    /// Linux, and macOS.
    /// </remarks>
    public class GlViewport : OpenGlControlBase, ICustomHitTest
    {
        // GL constants not in Avalonia's GlConsts
        private const int GL_LEQUAL = 0x0203;
        private const int GL_BLEND  = 0x0BE2;
        private const int GL_SRC_ALPHA = 0x0302;
        private const int GL_ONE_MINUS_SRC_ALPHA = 0x0303;
        private const int GL_BACK   = 0x0405;

        private readonly GlCamera _camera = new();
        private readonly GlGridRenderer _grid = new();

        // Scene objects — each is a (mesh, model-matrix) pair
        private readonly List<SceneObject> _sceneObjects = new();

        // Shader state — line program
        private int _lineProgram;
        private int _mvpUniform;

        // Shader state — solid (lit) program
        private int _solidProgram;
        private int _solidMvpUniform;
        private int _solidModelUniform;
        private int _solidNormalMatUniform;
        private int _solidSunDirUniform;
        private int _solidSunColorUniform;
        private int _solidAmbientUniform;

        // GL extension: functions not in Avalonia's GlInterface
        private delegate void D_glUniform3f(int loc, float v0, float v1, float v2);
        private delegate void D_glBlendFunc(int sfactor, int dfactor);
        private delegate void D_glCullFace(int mode);
        private unsafe delegate void D_glUniformMatrix3fv(
            int loc, int count, bool transpose, float* value);
        private D_glUniform3f? _glUniform3f;
        private D_glBlendFunc? _glBlendFunc;
        private D_glCullFace? _glCullFace;
        private D_glUniformMatrix3fv? _glUniformMatrix3fv;

        private bool _initOk;

        // Scene population state — set via PopulateScene(), consumed in
        // OnOpenGlRender (GPU uploads require the GL context).
        private Scene3D? _pendingScene;
        private bool _sceneNeedsRebuild;

        // Mouse interaction state
        private Point _lastMouse;
        private bool _orbiting;
        private bool _panning;

        // ── GLSL shader bodies ──────────────────────────────────────────
        // The #version prefix is prepended at compile time depending on
        // whether the context is desktop GL or GL ES.

        private const string LineVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aCol;

uniform mat4 uMVP;

out vec4 vCol;

void main()
{
    vCol = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string LineFragBody = @"
in vec4 vCol;

layout(location = 0) out vec4 fragColor;

void main()
{
    fragColor = vCol;
}
";

        // ── Solid (lit) shader for 3D meshes ────────────────────────────

        private const string SolidVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec4 aCol;

uniform mat4 uMVP;
uniform mat4 uModel;
uniform mat3 uNormalMat;

out vec3 vNorm;
out vec4 vCol;

void main()
{
    vNorm = uNormalMat * aNorm;
    vCol  = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string SolidFragBody = @"
in vec3 vNorm;
in vec4 vCol;

uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform vec3 uAmbient;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    vec3 light = uAmbient + uSunColor * diff;
    fragColor  = vec4(vCol.rgb * light, vCol.a);
}
";

        // ── Construction ────────────────────────────────────────────────

        public GlViewport()
        {
            Focusable = true;
        }

        // OpenGlControlBase renders via GL, not the Avalonia visual tree,
        // so the default hit-test would miss the control (no visual content).
        // ICustomHitTest makes the entire control area interactive.
        bool ICustomHitTest.HitTest(Point point) => true;

        // ── Public API ──────────────────────────────────────────────────

        /// <summary>The viewport camera (turntable orbit).</summary>
        internal GlCamera Camera => _camera;

        /// <summary>Reset camera to default view and redraw.</summary>
        public void ResetView()
        {
            _camera.Reset();
            RequestNextFrameRendering();
        }

        /// <summary>
        /// Populate the viewport with 3D markers for every object in the
        /// given scene (sources, monitors, detectors, fire sources).
        /// The actual GPU upload is deferred to the next render frame
        /// because mesh creation requires the GL context.
        /// Pass <c>null</c> to clear all scene objects.
        /// </summary>
        public void PopulateScene(Scene3D? scene)
        {
            _pendingScene = scene;
            _sceneNeedsRebuild = true;
            RequestNextFrameRendering();
        }

        // ── OpenGL lifecycle ────────────────────────────────────────────

        protected override void OnOpenGlInit(GlInterface gl)
        {
            try
            {
                // ── Load GL extension functions not in GlInterface ───────
                var p1 = gl.GetProcAddress("glUniform3f");
                if (p1 != IntPtr.Zero)
                    _glUniform3f = Marshal.GetDelegateForFunctionPointer<D_glUniform3f>(p1);
                var p2 = gl.GetProcAddress("glBlendFunc");
                if (p2 != IntPtr.Zero)
                    _glBlendFunc = Marshal.GetDelegateForFunctionPointer<D_glBlendFunc>(p2);
                var p3 = gl.GetProcAddress("glCullFace");
                if (p3 != IntPtr.Zero)
                    _glCullFace = Marshal.GetDelegateForFunctionPointer<D_glCullFace>(p3);
                var p4 = gl.GetProcAddress("glUniformMatrix3fv");
                if (p4 != IntPtr.Zero)
                    _glUniformMatrix3fv = Marshal.GetDelegateForFunctionPointer<D_glUniformMatrix3fv>(p4);

                // ── Compile line shader ─────────────────────────────────
                _lineProgram = CompileProgram(gl, LineVertBody, LineFragBody);
                _mvpUniform  = GetUniformLoc(gl, _lineProgram, "uMVP");

                // ── Compile solid (lit) shader ──────────────────────────
                _solidProgram        = CompileProgram(gl, SolidVertBody, SolidFragBody);
                _solidMvpUniform     = GetUniformLoc(gl, _solidProgram, "uMVP");
                _solidModelUniform   = GetUniformLoc(gl, _solidProgram, "uModel");
                _solidNormalMatUniform = GetUniformLoc(gl, _solidProgram, "uNormalMat");
                _solidSunDirUniform  = GetUniformLoc(gl, _solidProgram, "uSunDir");
                _solidSunColorUniform = GetUniformLoc(gl, _solidProgram, "uSunColor");
                _solidAmbientUniform = GetUniformLoc(gl, _solidProgram, "uAmbient");

                // ── Build grid geometry ─────────────────────────────────
                _grid.Init(gl);

                _initOk = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[GlViewport] OpenGL init failed: " + ex.Message);
                _initOk = false;
            }
        }

        protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
        {
            if (!_initOk) return;
            if (Bounds.Width < 1 || Bounds.Height < 1) return;

            // ── Deferred scene rebuild (GPU uploads need GL context) ────
            if (_sceneNeedsRebuild)
            {
                _sceneNeedsRebuild = false;
                RebuildSceneObjects(gl, _pendingScene);
                _pendingScene = null;
            }

            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);

            // Pixel-accurate viewport (accounts for HiDPI scaling)
            double scaling = VisualRoot?.RenderScaling ?? 1.0;
            int w = Math.Max(1, (int)(Bounds.Width  * scaling));
            int h = Math.Max(1, (int)(Bounds.Height * scaling));
            gl.Viewport(0, 0, w, h);

            // Dark background matching #1A1A1A
            gl.ClearColor(0.102f, 0.102f, 0.102f, 1.0f);
            gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

            // Depth testing
            gl.Enable(GL_DEPTH_TEST);
            gl.DepthFunc(GL_LEQUAL);
            gl.DepthMask(1); // write to depth buffer

            // ── Compute view-projection matrix ──────────────────────────
            // System.Numerics stores row-major; OpenGL reads column-major.
            // With transpose=false the two conventions cancel out, giving
            // the correct result when the GLSL shader does uMVP * vec4(pos,1).
            float aspect = w / (float)h;
            var view = _camera.ViewMatrix;
            var proj = _camera.ProjectionMatrix(aspect);
            var mvp  = view * proj;

            // ── Draw grid ───────────────────────────────────────────────
            gl.UseProgram(_lineProgram);
            SetMatrixUniform(gl, _mvpUniform, mvp);
            _grid.Render(gl);

            // ── Draw solid scene objects ─────────────────────────────────
            if (_sceneObjects.Count > 0 && _solidProgram != 0)
            {
                gl.UseProgram(_solidProgram);

                // Lighting: sun from upper-right-front
                _glUniform3f?.Invoke(_solidSunDirUniform,
                    0.5f, 0.3f, 0.8f);  // direction toward the light
                _glUniform3f?.Invoke(_solidSunColorUniform,
                    0.85f, 0.82f, 0.78f); // warm white
                _glUniform3f?.Invoke(_solidAmbientUniform,
                    0.20f, 0.22f, 0.28f); // cool sky ambient

                // Enable back-face culling for solid meshes
                gl.Enable(GL_CULL_FACE);
                _glCullFace?.Invoke(GL_BACK);

                // Enable blending for semi-transparent objects
                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible) continue;

                    var model = obj.ModelMatrix;
                    var objMvp = model * view * proj;
                    SetMatrixUniform(gl, _solidMvpUniform, objMvp);
                    SetMatrixUniform(gl, _solidModelUniform, model);

                    // Normal matrix = transpose(inverse(model))
                    // For uniform scaling, this simplifies to the upper-3x3.
                    if (Matrix4x4.Invert(model, out var inv))
                    {
                        var normalMat = Matrix4x4.Transpose(inv);
                        SetMatrix3Uniform(gl, _solidNormalMatUniform, normalMat);
                    }

                    obj.Mesh.Draw(gl);
                }

                gl.Disable(GL_CULL_FACE);
                gl.Disable(GL_BLEND);
            }

            // ── Cleanup state ───────────────────────────────────────────
            gl.UseProgram(0);
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            _grid.Cleanup(gl);
            foreach (var obj in _sceneObjects)
                obj.Mesh.Cleanup(gl);
            _sceneObjects.Clear();
            if (_lineProgram != 0)
            {
                gl.DeleteProgram(_lineProgram);
                _lineProgram = 0;
            }
            if (_solidProgram != 0)
            {
                gl.DeleteProgram(_solidProgram);
                _solidProgram = 0;
            }
            _initOk = false;
        }

        // ── Scene population ────────────────────────────────────────────

        // Object-type colors (RGBA, semi-transparent for blending)
        private static readonly Vector4 SourceColor   = new(0.95f, 0.75f, 0.20f, 0.90f); // amber
        private static readonly Vector4 FireColor     = new(0.95f, 0.30f, 0.15f, 0.90f); // red-orange
        private static readonly Vector4 MonitorColor  = new(0.25f, 0.55f, 0.95f, 0.90f); // blue
        private static readonly Vector4 DetectorColor = new(0.20f, 0.80f, 0.35f, 0.90f); // green

        /// <summary>
        /// Rebuild all scene objects from the given <see cref="Scene3D"/>.
        /// Called inside <see cref="OnOpenGlRender"/> where the GL context
        /// is active, so <see cref="GlMeshBuffer.Upload"/> is safe.
        /// </summary>
        private void RebuildSceneObjects(GlInterface gl, Scene3D? scene)
        {
            // Release existing GPU resources
            foreach (var obj in _sceneObjects)
                obj.Mesh.Cleanup(gl);
            _sceneObjects.Clear();

            if (scene is null) return;

            // ── Release sources → amber cylinders ──────────────────────
            if (scene.TopLevelSources != null)
            {
                for (int i = 0; i < scene.TopLevelSources.Count; i++)
                {
                    var src = scene.TopLevelSources[i];
                    var pos = src.Position;
                    float x = (float)pos.X, y = (float)pos.Y, z = (float)pos.Z;

                    // Cylinder: radius 1.5m, height 4m, base at source position
                    var (verts, idx) = GlMeshBuffer.GenerateCylinder(
                        new Vector3(x, y, z), 1.5f, 4f, SourceColor);

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, verts, idx);
                    _sceneObjects.Add(new SceneObject(
                        mesh, Matrix4x4.Identity, "source:" + i));
                }
            }

            // ── Fire sources → red-orange diamonds ─────────────────────
            if (scene.FireScenario?.Sources != null)
            {
                for (int i = 0; i < scene.FireScenario.Sources.Count; i++)
                {
                    var fire = scene.FireScenario.Sources[i];
                    var pos = fire.Position;
                    float x = (float)pos.X, y = (float)pos.Y, z = (float)pos.Z;

                    var (verts, idx) = GlMeshBuffer.GenerateDiamond(
                        new Vector3(x, y, z), 2f, 5f, FireColor);

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, verts, idx);
                    _sceneObjects.Add(new SceneObject(
                        mesh, Matrix4x4.Identity, "fire:" + i));
                }
            }

            // ── Monitor points → blue spheres ──────────────────────────
            if (scene.MonitorPoints != null)
            {
                for (int i = 0; i < scene.MonitorPoints.Count; i++)
                {
                    var mon = scene.MonitorPoints[i];
                    var pos = mon.Position;
                    float x = (float)pos.X, y = (float)pos.Y, z = (float)pos.Z;

                    var (verts, idx) = GlMeshBuffer.GenerateSphere(
                        new Vector3(x, y, z), 1.2f, MonitorColor);

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, verts, idx);
                    _sceneObjects.Add(new SceneObject(
                        mesh, Matrix4x4.Identity, "monitor:" + i));
                }
            }

            // ── Gas detectors → green boxes ────────────────────────────
            if (scene.GasDetectors != null)
            {
                for (int i = 0; i < scene.GasDetectors.Count; i++)
                {
                    var det = scene.GasDetectors[i];
                    var pos = det.Position;
                    float x = (float)pos.X, y = (float)pos.Y, z = (float)pos.Z;

                    var (verts, idx) = GlMeshBuffer.GenerateBox(
                        new Vector3(x, y, z + 1f), // offset up so base sits on Z
                        new Vector3(0.8f, 0.8f, 1f), DetectorColor);

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, verts, idx);
                    _sceneObjects.Add(new SceneObject(
                        mesh, Matrix4x4.Identity, "detector:" + i));
                }
            }

            System.Diagnostics.Debug.WriteLine(
                $"[GlViewport] Scene populated: {_sceneObjects.Count} objects");
        }

        // ── Mouse interaction ───────────────────────────────────────────

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            _lastMouse = pt.Position;

            if (pt.Properties.IsLeftButtonPressed)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    _panning = true;
                else
                    _orbiting = true;

                e.Pointer.Capture(this);
                e.Handled = true;
            }
            else if (pt.Properties.IsMiddleButtonPressed)
            {
                _panning = true;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
            else if (pt.Properties.IsRightButtonPressed)
            {
                _panning = true;
                e.Pointer.Capture(this);
                e.Handled = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            if (!_orbiting && !_panning) return;

            var pos = e.GetPosition(this);
            float dx = (float)(pos.X - _lastMouse.X);
            float dy = (float)(pos.Y - _lastMouse.Y);
            _lastMouse = pos;

            if (_orbiting)
                _camera.Orbit(dx * 0.005f, dy * 0.005f);
            else if (_panning)
                _camera.Pan(dx, dy);

            RequestNextFrameRendering();
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            _orbiting = false;
            _panning  = false;
            e.Pointer.Capture(null);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _camera.Zoom((float)e.Delta.Y);
            RequestNextFrameRendering();
            e.Handled = true;
        }

        // ── GL helper methods ───────────────────────────────────────────

        // ── Scene object management ──────────────────────────────────────

        /// <summary>Read-only access to the scene object list.</summary>
        internal IReadOnlyList<SceneObject> SceneObjects => _sceneObjects;

        /// <summary>
        /// Add a mesh to the scene.  The mesh must already be uploaded to GPU
        /// (call <see cref="GlMeshBuffer.Upload"/> before adding).
        /// </summary>
        internal SceneObject AddSceneObject(
            GlMeshBuffer mesh,
            Matrix4x4 modelMatrix,
            string? tag = null)
        {
            var obj = new SceneObject(mesh, modelMatrix, tag);
            _sceneObjects.Add(obj);
            RequestNextFrameRendering();
            return obj;
        }

        /// <summary>Remove a scene object and request a redraw.</summary>
        internal void RemoveSceneObject(SceneObject obj)
        {
            _sceneObjects.Remove(obj);
            RequestNextFrameRendering();
        }

        /// <summary>Remove all scene objects (does NOT release GPU resources).</summary>
        internal void ClearSceneObjects()
        {
            _sceneObjects.Clear();
            RequestNextFrameRendering();
        }

        // ── GL helper methods ───────────────────────────────────────────

        /// <summary>Upload a System.Numerics Matrix4x4 to a mat4 uniform.</summary>
        private static unsafe void SetMatrixUniform(
            GlInterface gl, int location, Matrix4x4 mat)
        {
            // System.Numerics stores M11..M44 in row-major order as 16
            // contiguous floats.  With transpose=false, OpenGL reads them
            // as column-major, which is the transpose of the row-major
            // layout — and that's exactly what we want because
            // System.Numerics uses v*M (row-vector) convention while
            // GLSL uses M*v (column-vector) convention.
            gl.UniformMatrix4fv(location, 1, false, &mat.M11);
        }

        /// <summary>
        /// Marshal a C# string to a null-terminated UTF-8 byte array and
        /// call glGetUniformLocation.
        /// </summary>
        private static unsafe int GetUniformLoc(
            GlInterface gl, int program, string name)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(name + '\0');
            fixed (byte* ptr = utf8)
                return gl.GetUniformLocation(program, (IntPtr)ptr);
        }

        /// <summary>
        /// Compile a vertex + fragment shader pair and link them into a
        /// program.  Throws <see cref="InvalidOperationException"/> on
        /// compile / link errors.
        /// </summary>
        private int CompileProgram(
            GlInterface gl, string vertBody, string fragBody)
        {
            // GLSL version prefix depends on the GL context type
            bool isEs = GlVersion.Type == GlProfileType.OpenGLES;
            string vp = isEs
                ? "#version 300 es\nprecision highp float;\n"
                : "#version 330 core\n";
            string fp = isEs
                ? "#version 300 es\nprecision mediump float;\n"
                : "#version 330 core\n";

            int vs = gl.CreateShader(GL_VERTEX_SHADER);
            string? vsErr = gl.CompileShaderAndGetError(vs, vp + vertBody);
            if (!string.IsNullOrEmpty(vsErr))
            {
                gl.DeleteShader(vs);
                throw new InvalidOperationException(
                    "Vertex shader compile error: " + vsErr);
            }

            int fs = gl.CreateShader(GL_FRAGMENT_SHADER);
            string? fsErr = gl.CompileShaderAndGetError(fs, fp + fragBody);
            if (!string.IsNullOrEmpty(fsErr))
            {
                gl.DeleteShader(vs);
                gl.DeleteShader(fs);
                throw new InvalidOperationException(
                    "Fragment shader compile error: " + fsErr);
            }

            int prog = gl.CreateProgram();
            gl.AttachShader(prog, vs);
            gl.AttachShader(prog, fs);
            string? linkErr = gl.LinkProgramAndGetError(prog);
            if (!string.IsNullOrEmpty(linkErr))
            {
                gl.DeleteShader(vs);
                gl.DeleteShader(fs);
                gl.DeleteProgram(prog);
                throw new InvalidOperationException(
                    "Program link error: " + linkErr);
            }

            // Shaders can be deleted after linking; the program keeps
            // its own copy of the compiled binary.
            gl.DeleteShader(vs);
            gl.DeleteShader(fs);

            return prog;
        }

        /// <summary>
        /// Upload the upper-3×3 of a Matrix4x4 to a mat3 uniform.
        /// Used for the normal matrix in the solid shader.
        /// </summary>
        private unsafe void SetMatrix3Uniform(
            GlInterface gl, int location, Matrix4x4 m)
        {
            if (_glUniformMatrix3fv == null) return;

            // Extract 3×3 from the 4×4 matrix (row-major → 9 floats)
            // Same transpose trick as for mat4.
            float* buf = stackalloc float[9];
            buf[0] = m.M11; buf[1] = m.M12; buf[2] = m.M13;
            buf[3] = m.M21; buf[4] = m.M22; buf[5] = m.M23;
            buf[6] = m.M31; buf[7] = m.M32; buf[8] = m.M33;

            _glUniformMatrix3fv(location, 1, false, buf);
        }
    }

    // ── Scene object ────────────────────────────────────────────────────

    /// <summary>
    /// A renderable object in the 3D viewport scene graph.
    /// Holds a reference to a GPU mesh buffer and a model transform.
    /// </summary>
    internal sealed class SceneObject
    {
        public SceneObject(GlMeshBuffer mesh, Matrix4x4 modelMatrix, string? tag = null)
        {
            Mesh = mesh;
            ModelMatrix = modelMatrix;
            Tag = tag;
        }

        /// <summary>GPU mesh data (vertices + indices).</summary>
        public GlMeshBuffer Mesh { get; }

        /// <summary>World-space transform (translation, rotation, scale).</summary>
        public Matrix4x4 ModelMatrix { get; set; }

        /// <summary>Whether this object is drawn.</summary>
        public bool Visible { get; set; } = true;

        /// <summary>
        /// Application-level identifier for correlating scene objects with
        /// engine data (e.g. "source:0", "monitor:3", "detector:7").
        /// </summary>
        public string? Tag { get; set; }
    }
}

