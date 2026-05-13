#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Rendering;
using DisperSim3D.Core;
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
        private int _lineTimeUniform;
        private int _lineAnimScaleUniform;
        private bool _hasWindLines;

        // Shader state — solid (lit) program with sun + rim light
        private int _solidProgram;
        private int _solidMvpUniform;
        private int _solidModelUniform;
        private int _solidNormalMatUniform;
        private int _solidSunDirUniform;
        private int _solidSunColorUniform;
        private int _solidAmbientUniform;
        private int _solidRimDirUniform;
        private int _solidRimColorUniform;
        private int _solidAlphaUniform;

        // Environment meshes — persist across scene rebuilds
        private GlMeshBuffer? _skyDomeMesh;
        private GlMeshBuffer? _groundPlaneMesh;

        // GL extension: functions not in Avalonia's GlInterface
        private delegate void D_glUniform3f(int loc, float v0, float v1, float v2);
        private delegate void D_glUniform1f(int loc, float v0);
        private delegate void D_glBlendFunc(int sfactor, int dfactor);
        private delegate void D_glCullFace(int mode);
        private unsafe delegate void D_glUniformMatrix3fv(
            int loc, int count, bool transpose, float* value);
        private D_glUniform3f? _glUniform3f;
        private D_glUniform1f? _glUniform1f;
        private D_glBlendFunc? _glBlendFunc;
        private D_glCullFace? _glCullFace;
        private D_glUniformMatrix3fv? _glUniformMatrix3fv;

        private bool _initOk;

        // Scene population state — set via PopulateScene(), consumed in
        // OnOpenGlRender (GPU uploads require the GL context).
        private Scene3D? _pendingScene;
        private bool _sceneNeedsRebuild;

        // Cached environment settings from the last loaded scene
        private EnvironmentSettings? _loadedEnv;

        // Deferred dispersion frame update for playback
        private DispersionFrameRequest? _pendingDispersionFrame;

        // Wind animation state (kept for future use)
        private readonly System.Diagnostics.Stopwatch _animClock = System.Diagnostics.Stopwatch.StartNew();

        // Mouse interaction state
        private Point _lastMouse;
        private bool _orbiting;
        private bool _panning;

        // Pick mode state
        private bool _pickModeActive;
        private SceneObject? _ghostSphere;
        private SceneObject? _ghostArrow;
        private bool _ghostVisible;
        private readonly HashSet<string> _ghostTags = new() { "ghost:sphere", "ghost:arrow" };

        /// <summary>Fired when the user clicks a surface in pick mode.
        /// Parameters: world position, outward surface normal.</summary>
        internal event Action<Vector3, Vector3>? PickCompleted;

        // ── GLSL shader bodies ──────────────────────────────────────────
        // The #version prefix is prepended at compile time depending on
        // whether the context is desktop GL or GL ES.

        private const string LineVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec4 aCol;
layout(location = 2) in float aArcT;

uniform mat4 uMVP;
uniform float uTime;
uniform float uAnimScale;

out vec4 vCol;
out float vArcPhase;

void main()
{
    vCol = aCol;
    vArcPhase = fract(aArcT * uAnimScale - uTime);
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string LineFragBody = @"
in vec4 vCol;
in float vArcPhase;

layout(location = 0) out vec4 fragColor;

vec3 hsv2rgb(float h, float s, float v)
{
    float c = v * s;
    float x = c * (1.0 - abs(mod(h * 6.0, 2.0) - 1.0));
    float m = v - c;
    vec3 rgb;
    if      (h < 1.0/6.0) rgb = vec3(c, x, 0.0);
    else if (h < 2.0/6.0) rgb = vec3(x, c, 0.0);
    else if (h < 3.0/6.0) rgb = vec3(0.0, c, x);
    else if (h < 4.0/6.0) rgb = vec3(0.0, x, c);
    else if (h < 5.0/6.0) rgb = vec3(x, 0.0, c);
    else                   rgb = vec3(c, 0.0, x);
    return rgb + m;
}

void main()
{
    vec3 rainbow = hsv2rgb(vArcPhase, 1.0, 1.0);
    fragColor = vec4(rainbow, vCol.a);
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
uniform vec3 uRimDir;
uniform vec3 uRimColor;
uniform float uAlpha;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    float rim  = max(dot(N, normalize(uRimDir)), 0.0);
    vec3 light = uAmbient + uSunColor * diff + uRimColor * rim;
    fragColor  = vec4(vCol.rgb * light, vCol.a * uAlpha);
}
";

        // ── Construction ────────────────────────────────────────────────

        public GlViewport()
        {
            // Do NOT set Focusable = true — it steals keyboard focus
            // from menus and the tree view when the viewport is clicked.
        }

        // OpenGlControlBase renders via GL, not the Avalonia visual tree,
        // so the default hit-test would miss the control (no visual content).
        // ICustomHitTest makes the control area interactive — but ONLY
        // within its layout bounds.  Returning true unconditionally would
        // intercept clicks across the entire window because Avalonia does
        // NOT clip to bounds before calling this method.
        bool ICustomHitTest.HitTest(Point point) =>
            point.X >= 0 && point.Y >= 0 &&
            point.X < Bounds.Width && point.Y < Bounds.Height;

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
        /// Apply a saved camera preset and redraw.
        /// </summary>
        public void ApplyCameraPreset(CameraPreset preset)
        {
            _camera.ApplyPreset(preset);
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
                var p1b = gl.GetProcAddress("glUniform1f");
                if (p1b != IntPtr.Zero)
                    _glUniform1f = Marshal.GetDelegateForFunctionPointer<D_glUniform1f>(p1b);
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
                _lineTimeUniform = GetUniformLoc(gl, _lineProgram, "uTime");
                _lineAnimScaleUniform = GetUniformLoc(gl, _lineProgram, "uAnimScale");

                // ── Compile solid (lit) shader ──────────────────────────
                _solidProgram        = CompileProgram(gl, SolidVertBody, SolidFragBody);
                _solidMvpUniform     = GetUniformLoc(gl, _solidProgram, "uMVP");
                _solidModelUniform   = GetUniformLoc(gl, _solidProgram, "uModel");
                _solidNormalMatUniform = GetUniformLoc(gl, _solidProgram, "uNormalMat");
                _solidSunDirUniform  = GetUniformLoc(gl, _solidProgram, "uSunDir");
                _solidSunColorUniform = GetUniformLoc(gl, _solidProgram, "uSunColor");
                _solidAmbientUniform = GetUniformLoc(gl, _solidProgram, "uAmbient");
                _solidRimDirUniform  = GetUniformLoc(gl, _solidProgram, "uRimDir");
                _solidRimColorUniform = GetUniformLoc(gl, _solidProgram, "uRimColor");
                _solidAlphaUniform   = GetUniformLoc(gl, _solidProgram, "uAlpha");

                // ── Build grid geometry ─────────────────────────────────
                _grid.Init(gl);

                // ── Build default environment (sky dome + ground) ───────
                // These get rebuilt when PopulateScene() is called with a
                // project, picking up the project's EnvironmentSettings.
                RebuildSceneObjects(gl, null);

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

            // ── Deferred dispersion frame update (playback) ────────────
            if (_pendingDispersionFrame != null)
            {
                var req = _pendingDispersionFrame;
                _pendingDispersionFrame = null;
                ApplyDispersionFrame(gl, req);
            }

            gl.BindFramebuffer(GL_FRAMEBUFFER, fb);

            // Pixel-accurate viewport (accounts for HiDPI scaling)
            double scaling = VisualRoot?.RenderScaling ?? 1.0;
            int w = Math.Max(1, (int)(Bounds.Width  * scaling));
            int h = Math.Max(1, (int)(Bounds.Height * scaling));
            gl.Viewport(0, 0, w, h);

            // Background — horizon colour for a seamless sky-dome blend
            gl.ClearColor(0.863f, 0.882f, 0.902f, 1.0f); // RGB(220,225,230)/255
            gl.Clear(GL_COLOR_BUFFER_BIT | GL_DEPTH_BUFFER_BIT);

            // ── Compute view-projection matrix ──────────────────────────
            float aspect = w / (float)h;
            var view = _camera.ViewMatrix;
            var proj = _camera.ProjectionMatrix(aspect);
            var mvp  = view * proj;

            // ── Read environment settings from the loaded scene ─────────
            var env = _pendingScene?.Environment ?? _loadedEnv ?? new EnvironmentSettings();

            // Compute sun direction from azimuth + elevation
            float azRad  = (float)(env.SunAzimuthDeg * Math.PI / 180.0);
            float elRad  = (float)(env.SunElevationDeg * Math.PI / 180.0);
            float cosEl  = MathF.Cos(elRad);
            float sunX   = cosEl * MathF.Sin(azRad);
            float sunY   = cosEl * MathF.Cos(azRad);
            float sunZ   = MathF.Sin(elRad);
            float sunI   = (float)env.SunIntensity;
            float ambI   = (float)env.AmbientIntensity;

            // Warm sun tint (more orange at low elevation)
            float warmR = 1.0f;
            float warmG = 0.85f + 0.15f * MathF.Min(1f, elRad / 1.0f);
            float warmB = 0.70f + 0.30f * MathF.Min(1f, elRad / 1.0f);

            // ── 1. Sky dome (drawn first, no depth write) ───────────────
            if (env.SkydomeEnabled && _skyDomeMesh != null && _solidProgram != 0)
            {
                gl.Disable(GL_DEPTH_TEST);
                gl.DepthMask(0); // don't write depth

                gl.UseProgram(_solidProgram);

                // Emissive: ambient = (1,1,1), sun = (0,0,0), rim = (0,0,0)
                _glUniform1f?.Invoke(_solidAlphaUniform, 1.0f);
                _glUniform3f?.Invoke(_solidAmbientUniform, 1f, 1f, 1f);
                _glUniform3f?.Invoke(_solidSunColorUniform, 0f, 0f, 0f);
                _glUniform3f?.Invoke(_solidSunDirUniform, 0f, 0f, 1f);
                _glUniform3f?.Invoke(_solidRimColorUniform, 0f, 0f, 0f);
                _glUniform3f?.Invoke(_solidRimDirUniform, 0f, 0f, -1f);

                // Sky dome follows camera position (always around the viewer)
                var camPos = _camera.Eye;
                var skyModel = Matrix4x4.CreateTranslation(camPos);
                var skyMvp = skyModel * view * proj;
                SetMatrixUniform(gl, _solidMvpUniform, skyMvp);
                SetMatrixUniform(gl, _solidModelUniform, skyModel);

                float* identMat3 = stackalloc float[9]
                    { 1,0,0, 0,1,0, 0,0,1 };
                _glUniformMatrix3fv?.Invoke(_solidNormalMatUniform, 1, false, identMat3);

                // Disable culling — hemisphere is viewed from inside
                gl.Disable(GL_CULL_FACE);
                _skyDomeMesh.Draw(gl);

                gl.UseProgram(0);
            }

            // Re-enable depth for all subsequent geometry
            gl.Enable(GL_DEPTH_TEST);
            gl.DepthFunc(GL_LEQUAL);
            gl.DepthMask(1);
            gl.Clear(GL_DEPTH_BUFFER_BIT); // reset depth after sky

            // ── 2. Ground plane (lit, responds to sun) ──────────────────
            if (_groundPlaneMesh != null && _solidProgram != 0)
            {
                gl.UseProgram(_solidProgram);

                _glUniform1f?.Invoke(_solidAlphaUniform, 1.0f);
                // Set full scene lighting for ground
                _glUniform3f?.Invoke(_solidSunDirUniform, sunX, sunY, sunZ);
                _glUniform3f?.Invoke(_solidSunColorUniform,
                    warmR * sunI, warmG * sunI, warmB * sunI);
                _glUniform3f?.Invoke(_solidAmbientUniform,
                    0.431f * ambI, 0.490f * ambI, 0.588f * ambI);
                // Rim light: opposite sun, cool blue, 25% intensity
                _glUniform3f?.Invoke(_solidRimDirUniform, -sunX, -sunY, sunZ * 0.5f);
                _glUniform3f?.Invoke(_solidRimColorUniform,
                    0.549f * 0.25f, 0.627f * 0.25f, 0.784f * 0.25f);

                SetMatrixUniform(gl, _solidMvpUniform, mvp);
                SetMatrixUniform(gl, _solidModelUniform, Matrix4x4.Identity);

                float* identMat3g = stackalloc float[9]
                    { 1,0,0, 0,1,0, 0,0,1 };
                _glUniformMatrix3fv?.Invoke(_solidNormalMatUniform, 1, false, identMat3g);

                gl.Enable(GL_CULL_FACE);
                _glCullFace?.Invoke(GL_BACK);
                _groundPlaneMesh.Draw(gl);

                gl.UseProgram(0);
            }

            // ── 3. Grid overlay ─────────────────────────────────────────
            gl.UseProgram(_lineProgram);
            _glUniform1f?.Invoke(_lineTimeUniform, 0f);
            _glUniform1f?.Invoke(_lineAnimScaleUniform, 0f);
            SetMatrixUniform(gl, _mvpUniform, mvp);
            _grid.Render(gl);

            // ── 4. Solid scene objects ───────────────────────────────────
            if (_sceneObjects.Count > 0 && _solidProgram != 0)
            {
                gl.UseProgram(_solidProgram);

                // Scene lighting
                _glUniform3f?.Invoke(_solidSunDirUniform, sunX, sunY, sunZ);
                _glUniform3f?.Invoke(_solidSunColorUniform,
                    warmR * sunI, warmG * sunI, warmB * sunI);
                _glUniform3f?.Invoke(_solidAmbientUniform,
                    0.431f * ambI, 0.490f * ambI, 0.588f * ambI);
                _glUniform3f?.Invoke(_solidRimDirUniform, -sunX, -sunY, sunZ * 0.5f);
                _glUniform3f?.Invoke(_solidRimColorUniform,
                    0.549f * 0.25f, 0.627f * 0.25f, 0.784f * 0.25f);

                gl.Enable(GL_CULL_FACE);
                _glCullFace?.Invoke(GL_BACK);

                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible || obj.Mesh.IsLineGeometry) continue;

                    var model = obj.ModelMatrix;

                    bool isViewObj = obj.Tag != null &&
                        (obj.Tag.StartsWith("view:") || obj.Tag.StartsWith("dispersion:"));
                    if (isViewObj)
                        gl.Disable(GL_CULL_FACE);
                    else
                    {
                        gl.Enable(GL_CULL_FACE);
                        _glCullFace?.Invoke(GL_BACK);
                    }

                    _glUniform1f?.Invoke(_solidAlphaUniform, 1.0f);

                    var objMvp = model * view * proj;
                    SetMatrixUniform(gl, _solidMvpUniform, objMvp);
                    SetMatrixUniform(gl, _solidModelUniform, model);

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

            // ── 5. Line scene objects (wind streamlines, animated rainbow) ─
            bool hasLineObjects = false;
            foreach (var obj in _sceneObjects)
            {
                if (obj.Visible && obj.Mesh.IsLineGeometry)
                { hasLineObjects = true; break; }
            }
            _hasWindLines = hasLineObjects;
            if (hasLineObjects && _lineProgram != 0)
            {
                gl.UseProgram(_lineProgram);
                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

                float timeSec = (float)_animClock.Elapsed.TotalSeconds;
                _glUniform1f?.Invoke(_lineTimeUniform, timeSec * 0.4f);
                _glUniform1f?.Invoke(_lineAnimScaleUniform, 0.04f);

                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible || !obj.Mesh.IsLineGeometry) continue;
                    var objMvp = obj.ModelMatrix * view * proj;
                    SetMatrixUniform(gl, _mvpUniform, objMvp);
                    obj.Mesh.Draw(gl);
                }

                gl.Disable(GL_BLEND);
            }

            // ── Cleanup state ───────────────────────────────────────────
            gl.UseProgram(0);

            if (_hasWindLines)
                RequestNextFrameRendering();
        }

        protected override void OnOpenGlDeinit(GlInterface gl)
        {
            _grid.Cleanup(gl);
            _skyDomeMesh?.Cleanup(gl);
            _skyDomeMesh = null;
            _groundPlaneMesh?.Cleanup(gl);
            _groundPlaneMesh = null;
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

        // Object-type colors matching the WPF EnvironmentRenderer palette
        private static readonly Vector4 SourceColor   = new(1.00f, 0.31f, 0.00f, 1.0f); // orange (255,80,0)
        private static readonly Vector4 ArrowColor    = new(1.00f, 0.12f, 0.12f, 1.0f); // red    (255,30,30)
        private static readonly Vector4 FireColor     = new(0.95f, 0.30f, 0.15f, 0.9f); // red-orange
        private static readonly Vector4 MonitorColor  = new(0.25f, 0.55f, 0.95f, 0.9f); // blue
        private static readonly Vector4 DetectorColor = new(0.20f, 0.80f, 0.35f, 0.9f); // green

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

            // ── Environment: sky dome + ground plane ──────────────────
            var env = scene?.Environment ?? new EnvironmentSettings();
            _loadedEnv = env;

            // Sky dome — rebuild each time (zenith/horizon colours may change)
            _skyDomeMesh?.Cleanup(gl);
            _skyDomeMesh = null;
            if (env.SkydomeEnabled)
            {
                var zenith = new Vector4(
                    env.SkyZenithColor.ScR, env.SkyZenithColor.ScG,
                    env.SkyZenithColor.ScB, 1f);
                var horizon = new Vector4(
                    env.SkyHorizonColor.ScR, env.SkyHorizonColor.ScG,
                    env.SkyHorizonColor.ScB, 1f);
                var (skyV, skyI) = GlMeshBuffer.GenerateHemisphere(
                    500f, zenith, horizon);
                _skyDomeMesh = new GlMeshBuffer();
                _skyDomeMesh.Upload(gl, skyV, skyI);
            }

            // Ground plane — colour depends on material type
            _groundPlaneMesh?.Cleanup(gl);
            _groundPlaneMesh = null;
            // Ground colours — brighter than raw material tones because
            // directional lighting will darken shadowed areas. These values
            // are tuned so that under the default sun (azimuth 135°,
            // elevation 55°) the visible result approximates the WPF
            // EnvironmentRenderer's procedural textures.
            Vector4 groundColor = env.Ground switch
            {
                GroundMaterial.Grass    => new Vector4(0.48f, 0.58f, 0.35f, 1f),
                GroundMaterial.Concrete => new Vector4(0.78f, 0.78f, 0.76f, 1f),
                GroundMaterial.Sand     => new Vector4(0.87f, 0.80f, 0.63f, 1f),
                GroundMaterial.Asphalt  => new Vector4(0.28f, 0.28f, 0.30f, 1f),
                _                       => new Vector4(0.48f, 0.58f, 0.35f, 1f)
            };
            {
                var (gndV, gndI) = GlMeshBuffer.GenerateGroundQuad(
                    200f, groundColor);
                _groundPlaneMesh = new GlMeshBuffer();
                _groundPlaneMesh.Upload(gl, gndV, gndI, keepCpuCopy: true);
            }

            if (scene is null) return;

            // ── Release sources → orange sphere + red direction arrow ───
            if (scene.TopLevelSources != null)
            {
                for (int i = 0; i < scene.TopLevelSources.Count; i++)
                {
                    var src = scene.TopLevelSources[i];
                    var epos = src.EffectivePosition;
                    float x = (float)epos.X, y = (float)epos.Y, z = (float)epos.Z;
                    var center = new Vector3(x, y, z);

                    // Sphere marker (radius 1.5 m)
                    var (sv, si) = GlMeshBuffer.GenerateSphere(
                        center, 1.5f, SourceColor);
                    var sphereMesh = new GlMeshBuffer();
                    sphereMesh.Upload(gl, sv, si);
                    _sceneObjects.Add(new SceneObject(
                        sphereMesh, Matrix4x4.Identity, "source:" + i));

                    // Direction arrow (red, along ReleaseDirection)
                    var dir = src.ReleaseDirection;
                    var dirVec = new Vector3((float)dir.X, (float)dir.Y, (float)dir.Z);
                    if (dirVec.LengthSquared() > 0.001f)
                    {
                        var (av, ai) = GlMeshBuffer.GenerateArrow(
                            center, dirVec, ArrowColor,
                            shaftRadius: 0.3f, headRadius: 0.8f,
                            shaftLength: 4.5f, headLength: 1.5f);
                        var arrowMesh = new GlMeshBuffer();
                        arrowMesh.Upload(gl, av, ai);
                        _sceneObjects.Add(new SceneObject(
                            arrowMesh, Matrix4x4.Identity, "sourcearrow:" + i));
                    }
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

            // ── Decorations → loaded from STL/OBJ files ──────────────
            if (scene.Decorations != null)
            {
                for (int i = 0; i < scene.Decorations.Count; i++)
                {
                    var deco = scene.Decorations[i];
                    if (string.IsNullOrEmpty(deco.FilePath)) continue;

                    // Material color from decoration, with opacity
                    var mc = deco.MaterialColor;
                    var decoColor = new Vector4(
                        mc.ScR, mc.ScG, mc.ScB,
                        (float)deco.Opacity);

                    var loaded = MeshFileLoader.Load(deco.FilePath, decoColor);
                    if (loaded == null) continue;

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, loaded.Value.verts, loaded.Value.indices,
                        keepCpuCopy: true);

                    // Build model matrix: Scale → RotateZ → RotateY → RotateX → Translate
                    float scale = (float)deco.Scale;
                    float rx = (float)(deco.Rotation.X * Math.PI / 180.0);
                    float ry = (float)(deco.Rotation.Y * Math.PI / 180.0);
                    float rz = (float)(deco.Rotation.Z * Math.PI / 180.0);
                    var model =
                        Matrix4x4.CreateScale(scale) *
                        Matrix4x4.CreateRotationZ(rz) *
                        Matrix4x4.CreateRotationY(ry) *
                        Matrix4x4.CreateRotationX(rx) *
                        Matrix4x4.CreateTranslation(
                            (float)deco.Position.X,
                            (float)deco.Position.Y,
                            (float)deco.Position.Z);

                    _sceneObjects.Add(new SceneObject(
                        mesh, model, "deco:" + i));
                }
            }

            // ── Wind field streamlines ────────────────────────────────
            if (scene.WindFieldScenarios != null)
            {
                foreach (var wfs in scene.WindFieldScenarios)
                {
                    if (wfs.WindField == null && !string.IsNullOrEmpty(wfs.CasePath))
                    {
                        wfs.WindField = FluidX3DWindFieldRunner.LoadFromCase(wfs)
                            ?? WindFieldRunner.LoadFromCase(wfs);
                    }

                    var wf = wfs.WindField;
                    if (wf == null) continue;

                    double half = wfs.DomainSizeM;
                    double height = wfs.DomainHeightM;
                    double displayExtent = wfs.DisplayExtentM > 0
                        ? wfs.DisplayExtentM : half;

                    int seedsPerAxis = Math.Max(6, wfs.ArrowsPerAxis);
                    int vertLayers = Math.Max(1, wfs.ArrowVerticalLayers);

                    var lineVerts = GlMeshBuffer.GenerateStreamlines(
                        wf,
                        -displayExtent, displayExtent,
                        -displayExtent, displayExtent,
                        height,
                        seedsPerAxis, vertLayers,
                        (float)wfs.ArrowOpacity);

                    if (lineVerts.Length >= 2)
                    {
                        var mesh = new GlMeshBuffer();
                        mesh.UploadLines(gl, lineVerts);
                        _sceneObjects.Add(new SceneObject(
                            mesh, Matrix4x4.Identity, "wind"));
                    }
                }
            }

            // ── Views (isosurfaces + contour planes) ─────────────────
            if (scene.Views != null && scene.Simulations != null)
            {
                foreach (var view in scene.Views)
                {
                    if (!view.IsVisible) continue;
                    var sim = scene.Simulations.FirstOrDefault(
                        s => s.Id == view.SimulationId);
                    if (sim == null || sim.Status != SimulationStatus.Completed)
                        continue;

                    try
                    {
                        var field = LoadViewField(view, sim, scene);
                        if (field == null) continue;

                        if (view.Kind == ViewKind.Isosurface)
                        {
                            BuildIsosurfaceView(gl, view, sim, field);
                        }
                        else if (view.IsContourPlane)
                        {
                            BuildContourPlaneView(gl, view, sim, field);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[GlViewport] View '{view.Name}' failed: {ex.Message}");
                    }
                }
            }

            // ── Apply first camera preset if available ────────────────
            if (scene.CameraPresets != null && scene.CameraPresets.Count > 0)
            {
                _camera.ApplyPreset(scene.CameraPresets[0]);
            }

            // ── Ghost preview for pick mode ──────────────────────────────
            _ghostSphere?.Mesh.Cleanup(gl);
            _ghostArrow?.Mesh.Cleanup(gl);

            var ghostColor = new Vector4(0f, 0.8f, 0.9f, 0.35f);
            var (gsV, gsI) = GlMeshBuffer.GenerateSphere(Vector3.Zero, 1f, ghostColor);
            var gsMesh = new GlMeshBuffer();
            gsMesh.Upload(gl, gsV, gsI);
            _ghostSphere = new SceneObject(gsMesh, Matrix4x4.Identity, "ghost:sphere")
                { Visible = false };
            _sceneObjects.Add(_ghostSphere);

            var arrowColor = new Vector4(0.9f, 0.15f, 0.1f, 0.7f);
            var (gaV, gaI) = GlMeshBuffer.GenerateArrow(
                Vector3.Zero, Vector3.UnitZ, arrowColor);
            var gaMesh = new GlMeshBuffer();
            gaMesh.Upload(gl, gaV, gaI);
            _ghostArrow = new SceneObject(gaMesh, Matrix4x4.Identity, "ghost:arrow")
                { Visible = false };
            _sceneObjects.Add(_ghostArrow);

            System.Diagnostics.Debug.WriteLine(
                $"[GlViewport] Scene populated: {_sceneObjects.Count} objects");
        }

        // ── View rendering helpers ──────────────────────────────────────

        /// <summary>
        /// Load the scalar field for a view from the simulation's result data.
        /// Mirrors <c>ViewRenderer.BuildVisual</c> field-loading logic.
        /// </summary>
        private static double[,,]? LoadViewField(View view, Simulation sim, Scene3D scene)
        {
            if (string.IsNullOrEmpty(sim.CasePath))
                return null;

            int nx = sim.SnapshotGridResolution > 0 ? sim.SnapshotGridResolution : 60;
            int ny = nx;
            int nz = Math.Max(1, nx / 2);
            double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

            // Try analytic field first (thermal radiation)
            if (FieldTransform.IsAnalytic(view.FieldProperty))
                return FieldTransform.BuildRadiationField(scene, nx, ny, nz, half);

            // Resolve OpenFOAM field name from the view property
            string? fieldName = ResolveFieldName(view.FieldProperty, sim);

            OpenFoamResult? result = null;

            // Check if simulation has a cached result
            if (sim.ResultTag is OpenFoamResult cachedResult)
            {
                result = cachedResult;
            }
            else if (Directory.Exists(sim.CasePath))
            {
                // Try OpenFOAM-style read
                result = OpenFoamResultReader.ReadResults(
                    sim.CasePath, nx, ny, nz, half,
                    scalarFieldName: fieldName);

                // Fall back to flat-bin (FluidX3D) layout
                if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                {
                    bool wantTemp = view.FieldProperty == ViewFieldProperty.Temperature;
                    result = TryLoadFlatBinCase(
                        sim.CasePath, ref nx, ref ny, ref nz, half, wantTemp);
                }

                // Cache for next time
                if (result != null && result.IsLoaded)
                    sim.ResultTag = result;
            }

            if (result == null || !result.IsLoaded || result.TimeSteps.Count == 0)
                return null;

            // Select time step
            var field = SelectField(result, view.TimeMode, view.SpecificTimeS);
            if (field == null) return null;

            // Apply unit transform if needed (mass fraction → ppm, %LFL, etc.)
            if (FieldTransform.NeedsSpeciesField(view.FieldProperty))
            {
                var gas = ResolveGasForSimulation(sim, scene);
                field = FieldTransform.FromMassFraction(field, view.FieldProperty, gas);
            }

            return field;
        }

        /// <summary>Build an isosurface mesh for the given View and upload it.</summary>
        private void BuildIsosurfaceView(
            GlInterface gl, View view, Simulation sim, double[,,] field)
        {
            int fnx = field.GetLength(0);
            double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;
            double cell = (2.0 * half) / fnx;

            var isoResult = PortableMarchingCubes.GenerateIsosurface(
                field, view.IsoValue,
                -half, -half, 0,
                cell, cell, cell);

            if (isoResult == null) return;

            float alpha = (float)Math.Max(0, Math.Min(1, view.Opacity));
            var isoColor = new Vector4(
                view.IsoColor.ScR, view.IsoColor.ScG, view.IsoColor.ScB, alpha);

            var (verts, idx) = GlMeshBuffer.FromIsosurfaceResult(isoResult.Value, isoColor);
            var mesh = new GlMeshBuffer();
            mesh.Upload(gl, verts, idx);
            _sceneObjects.Add(new SceneObject(
                mesh, Matrix4x4.Identity, $"view:iso:{view.Id}"));

            System.Diagnostics.Debug.WriteLine(
                $"[GlViewport] Isosurface '{view.Name}': {isoResult.Value.TriangleCount} tris");
        }

        /// <summary>Build a contour-plane mesh for the given View and upload it.</summary>
        private void BuildContourPlaneView(
            GlInterface gl, View view, Simulation sim, double[,,] field)
        {
            int fnx = field.GetLength(0);
            int gridRes = fnx;
            double half = sim.SnapshotDomainSizeM > 0 ? sim.SnapshotDomainSizeM : 200;

            // Determine colour scale range
            double minV, maxV;
            if (view.MinValue == 0 && view.MaxValue == 0)
                GlMeshBuffer.ComputeFieldRange(field, out minV, out maxV);
            else
            { minV = view.MinValue; maxV = view.MaxValue; }

            int res = view.SampleResolution > 0 ? view.SampleResolution : 80;
            float alpha = (float)Math.Max(0, Math.Min(1, view.Opacity));

            var (verts, idx) = GlMeshBuffer.GenerateContourPlane(
                view.Kind, view.PlanePosition, (float)half,
                field, gridRes, view.ColorMap,
                minV, maxV, alpha, res);

            var mesh = new GlMeshBuffer();
            mesh.Upload(gl, verts, idx);
            _sceneObjects.Add(new SceneObject(
                mesh, Matrix4x4.Identity, $"view:contour:{view.Id}"));

            System.Diagnostics.Debug.WriteLine(
                $"[GlViewport] Contour '{view.Name}': {res}×{res} plane at {view.PlanePosition}");
        }

        // ── Field resolution helpers (mirrors ViewRenderer logic) ───

        private static string? ResolveFieldName(ViewFieldProperty prop, Simulation sim)
        {
            if (FieldTransform.NeedsSpeciesField(prop))
                return OpenFoamCaseGenerator.ResolveOpenFoamSpecies(sim.SnapshotSource);
            return prop switch
            {
                ViewFieldProperty.Temperature       => "T",
                ViewFieldProperty.WindSpeed          => "magU",
                ViewFieldProperty.Pressure           => "p_rgh",
                ViewFieldProperty.TurbulentK         => "k",
                ViewFieldProperty.TurbulentEpsilon   => "epsilon",
                ViewFieldProperty.TurbulentViscosity => "nut",
                _                                    => null
            };
        }

        private static GasProperties? ResolveGasForSimulation(Simulation sim, Scene3D scene)
        {
            if (sim.SnapshotSource == null) return null;
            var src = sim.SnapshotSource;
            if (!string.IsNullOrEmpty(src.GasRefId) && scene.GasLibrary != null)
            {
                var lib = scene.GasLibrary.Find(g => g.Id == src.GasRefId);
                if (lib != null) return lib.AsGasProperties();
            }
            return src.Gas;
        }

        /// <summary>Select the correct time-step field from a result.</summary>
        private static double[,,]? SelectField(
            OpenFoamResult result, ViewTimeMode mode, double specT)
        {
            if (mode == ViewTimeMode.FinalSnapshot || result.TimeSteps.Count == 1)
            {
                double last = result.TimeSteps[result.TimeSteps.Count - 1];
                return result.GetField(last);
            }
            if (mode == ViewTimeMode.SpecificTime)
            {
                double bestT = result.TimeSteps[0];
                double bestDelta = Math.Abs(specT - bestT);
                foreach (var t in result.TimeSteps)
                {
                    double d = Math.Abs(specT - t);
                    if (d < bestDelta) { bestDelta = d; bestT = t; }
                }
                return result.GetField(bestT);
            }
            // PeakOverTime: per-cell maximum across every loaded timestep
            double[,,]? acc = null;
            foreach (var t in result.TimeSteps)
            {
                var f = result.GetField(t);
                if (f == null) continue;
                if (acc == null)
                {
                    int ax = f.GetLength(0), ay = f.GetLength(1), az = f.GetLength(2);
                    acc = new double[ax, ay, az];
                }
                int fnx = acc.GetLength(0), fny = acc.GetLength(1), fnz = acc.GetLength(2);
                for (int i = 0; i < fnx; i++)
                    for (int j = 0; j < fny; j++)
                        for (int k = 0; k < fnz; k++)
                            if (f[i, j, k] > acc[i, j, k]) acc[i, j, k] = f[i, j, k];
            }
            return acc;
        }

        /// <summary>
        /// Load a flat-bin case directory (FluidX3D dispersion / fire layout).
        /// Mirrors <c>ViewRenderer.TryLoadFlatBinCase</c>.
        /// </summary>
        internal static OpenFoamResult? TryLoadFlatBinCasePublic(
            string caseDir, ref int nx, ref int ny, ref int nz,
            double half, bool temperatureChannel = false)
            => TryLoadFlatBinCase(caseDir, ref nx, ref ny, ref nz, half, temperatureChannel);

        private static OpenFoamResult? TryLoadFlatBinCase(
            string caseDir, ref int nx, ref int ny, ref int nz,
            double half, bool temperatureChannel = false)
        {
            if (string.IsNullOrEmpty(caseDir) || !Directory.Exists(caseDir)) return null;
            // Skip if this looks like an OpenFOAM case (has system/controlDict)
            string controlDict = Path.Combine(caseDir, "system", "controlDict");
            if (File.Exists(controlDict)) return null;

            var allBin = Directory.GetFiles(caseDir, "*.bin", SearchOption.TopDirectoryOnly);
            if (allBin.Length == 0) return null;

            var binFiles = allBin.Where(p =>
            {
                bool isT = Path.GetFileNameWithoutExtension(p)
                    .EndsWith("_T", StringComparison.OrdinalIgnoreCase);
                return temperatureChannel ? isT : !isT;
            }).ToArray();
            if (binFiles.Length == 0) return null;

            // Infer grid resolution from first file size
            try
            {
                long bytes = new FileInfo(binFiles[0]).Length;
                long doubles = bytes / sizeof(double);
                for (int c = 8; c <= 1024; c++)
                {
                    int cnz = Math.Max(8, c / 2);
                    if ((long)c * c * cnz == doubles)
                    { nx = c; ny = c; nz = cnz; break; }
                }
            }
            catch { /* fall back to caller's nx/ny/nz */ }

            var result = new OpenFoamResult
            {
                GridNx = nx, GridNy = ny, GridNz = nz,
                DomainSizeM = half,
                DomainXMin = -half, DomainXMax = half,
                DomainYMin = -half, DomainYMax = half,
                DomainZMax = half,
                CaseDir = caseDir
            };

            foreach (var f in binFiles)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (temperatureChannel && name.EndsWith("_T", StringComparison.OrdinalIgnoreCase))
                    name = name.Substring(0, name.Length - 2);
                if (double.TryParse(name,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double t))
                {
                    result.TimeSteps.Add(t);
                    result.TimeStepPaths[t] = f;
                }
            }
            result.TimeSteps.Sort();
            result.IsLoaded = result.TimeSteps.Count > 0;
            return result;
        }

        // ── Dispersion playback ─────────────────────────────────────────

        internal sealed class DispersionFrameRequest
        {
            public double[,,] Field { get; }
            public List<DispersionThreshold> Thresholds { get; }
            public double DomainHalf { get; }

            public DispersionFrameRequest(
                double[,,] field, List<DispersionThreshold> thresholds, double domainHalf)
            {
                Field = field;
                Thresholds = thresholds;
                DomainHalf = domainHalf;
            }
        }

        public void UpdateDispersionFrame(
            double[,,] field, List<DispersionThreshold> thresholds, double domainHalf)
        {
            _pendingDispersionFrame = new DispersionFrameRequest(field, thresholds, domainHalf);
            RequestNextFrameRendering();
        }

        public void ClearDispersionFrame()
        {
            _pendingDispersionFrame = null;
            for (int i = _sceneObjects.Count - 1; i >= 0; i--)
            {
                if (_sceneObjects[i].Tag != null && _sceneObjects[i].Tag!.StartsWith("dispersion:"))
                    _sceneObjects.RemoveAt(i);
            }
            RequestNextFrameRendering();
        }

        private void ApplyDispersionFrame(GlInterface gl, DispersionFrameRequest req)
        {
            for (int i = _sceneObjects.Count - 1; i >= 0; i--)
            {
                if (_sceneObjects[i].Tag != null && _sceneObjects[i].Tag!.StartsWith("dispersion:"))
                {
                    _sceneObjects[i].Mesh.Cleanup(gl);
                    _sceneObjects.RemoveAt(i);
                }
            }

            int fnx = req.Field.GetLength(0);
            double cell = (2.0 * req.DomainHalf) / fnx;

            // Render thresholds in descending order (inner shells first)
            foreach (var th in req.Thresholds)
            {
                if (!th.Visible || th.ConcentrationValue <= 0) continue;

                var isoResult = PortableMarchingCubes.GenerateIsosurface(
                    req.Field, th.ConcentrationValue,
                    -req.DomainHalf, -req.DomainHalf, 0,
                    cell, cell, cell);

                if (isoResult == null) continue;

                float alpha = (float)Math.Max(0.1, Math.Min(1, th.Opacity));
                var color = new Vector4(th.Color.ScR, th.Color.ScG, th.Color.ScB, alpha);

                var (verts, idx) = GlMeshBuffer.FromIsosurfaceResult(isoResult.Value, color);
                var mesh = new GlMeshBuffer();
                mesh.Upload(gl, verts, idx);
                _sceneObjects.Add(new SceneObject(
                    mesh, Matrix4x4.Identity, $"dispersion:{th.Name}"));
            }
        }

        // ── Mouse interaction ───────────────────────────────────────────
        //
        // No pointer capture — Avalonia's Pointer.Capture redirects ALL
        // pointer events window-wide to the capturing control, which
        // blocks menus, tree-view clicks, splitters, and the inspector.
        // Instead we simply track button state; orbit/pan stops if the
        // cursor leaves the viewport, which is perfectly acceptable UX.

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var pt = e.GetCurrentPoint(this);
            _lastMouse = pt.Position;

            if (_pickModeActive && pt.Properties.IsLeftButtonPressed)
            {
                var hit = DoRaycast(pt.Position.X, pt.Position.Y);
                if (hit != null)
                {
                    PickCompleted?.Invoke(hit.Value.Position, hit.Value.Normal);
                    ExitPickMode();
                }
                e.Handled = true;
                return;
            }

            if (_pickModeActive && pt.Properties.IsRightButtonPressed)
            {
                ExitPickMode();
                e.Handled = true;
                return;
            }

            if (pt.Properties.IsLeftButtonPressed)
            {
                _panning = true;
            }
            else if (pt.Properties.IsRightButtonPressed)
            {
                _orbiting = true;
            }
            else if (pt.Properties.IsMiddleButtonPressed)
            {
                _panning = true;
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);

            if (_pickModeActive)
            {
                var pos = e.GetPosition(this);
                var hit = DoRaycast(pos.X, pos.Y);
                if (hit != null)
                    UpdateGhostPosition(hit.Value.Position, hit.Value.Normal);
                else if (_ghostSphere != null)
                { _ghostSphere.Visible = false; _ghostArrow!.Visible = false; _ghostVisible = false; RequestNextFrameRendering(); }
                return;
            }

            if (!_orbiting && !_panning) return;

            var mpos = e.GetPosition(this);
            float dx = (float)(mpos.X - _lastMouse.X);
            float dy = (float)(mpos.Y - _lastMouse.Y);
            _lastMouse = mpos;

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
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            // Stop any active drag when the cursor leaves the viewport.
            _orbiting = false;
            _panning  = false;
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            _camera.Zoom((float)e.Delta.Y);
            RequestNextFrameRendering();
            e.Handled = true; // prevent parent scroll
        }

        // ── GL helper methods ───────────────────────────────────────────

        // ── Scene object management ──────────────────────────────────────

        /// <summary>Read-only access to the scene object list.</summary>
        internal IReadOnlyList<SceneObject> SceneObjects => _sceneObjects;

        internal bool IsPickModeActive => _pickModeActive;

        internal void EnterPickMode()
        {
            _pickModeActive = true;
            _ghostVisible = false;
            Cursor = new global::Avalonia.Input.Cursor(
                global::Avalonia.Input.StandardCursorType.Cross);
            RequestNextFrameRendering();
        }

        internal void ExitPickMode()
        {
            _pickModeActive = false;
            _ghostVisible = false;
            if (_ghostSphere != null) { _ghostSphere.Visible = false; }
            if (_ghostArrow != null) { _ghostArrow.Visible = false; }
            Cursor = global::Avalonia.Input.Cursor.Default;
            RequestNextFrameRendering();
        }

        private RayHit? DoRaycast(double mouseX, double mouseY)
        {
            float w = (float)Bounds.Width;
            float h = (float)Bounds.Height;
            if (w < 1 || h < 1) return null;

            var view = _camera.ViewMatrix;
            var proj = _camera.ProjectionMatrix(w / h);
            var (origin, dir) = RayCaster.ScreenToRay(mouseX, mouseY, w, h, view, proj);

            var hit = RayCaster.RaycastScene(origin, dir, _sceneObjects, _ghostTags);
            hit ??= RayCaster.RaycastGroundPlane(origin, dir);
            return hit;
        }

        private void UpdateGhostPosition(Vector3 position, Vector3 normal)
        {
            _ghostVisible = true;
            if (_ghostSphere != null)
            {
                _ghostSphere.ModelMatrix = Matrix4x4.CreateScale(1.5f) *
                    Matrix4x4.CreateTranslation(position);
                _ghostSphere.Visible = true;
            }
            if (_ghostArrow != null)
            {
                var rot = AlignToNormal(normal);
                _ghostArrow.ModelMatrix = rot *
                    Matrix4x4.CreateTranslation(position + normal * 1.5f);
                _ghostArrow.Visible = true;
            }
            RequestNextFrameRendering();
        }

        private static Matrix4x4 AlignToNormal(Vector3 normal)
        {
            var up = Vector3.UnitZ;
            if (MathF.Abs(Vector3.Dot(normal, up)) > 0.99f)
                up = Vector3.UnitY;
            var right = Vector3.Normalize(Vector3.Cross(up, normal));
            var camUp = Vector3.Cross(normal, right);
            return new Matrix4x4(
                right.X, right.Y, right.Z, 0,
                camUp.X, camUp.Y, camUp.Z, 0,
                normal.X, normal.Y, normal.Z, 0,
                0, 0, 0, 1);
        }

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

