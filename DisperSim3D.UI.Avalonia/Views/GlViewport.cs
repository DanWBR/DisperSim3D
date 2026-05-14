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

        // Shader state — sky dome (procedural clouds + sun + panorama texture)
        private int _skyProgram;
        private int _skyMvpUniform;
        private int _skySunDirUniform;
        private int _skyTimeUniform;
        private int _skyShowCloudsUniform;
        private int _skyCloudSpeedUniform;
        private int _skyUseSkyTextureUniform;
        private int _skySkyTextureUniform;
        private int _skyTextureId;

        // Shader state — grass blades (animated sway)
        private int _grassProgram;
        private int _grassMvpUniform;
        private int _grassTimeUniform;
        private int _grassWindDirUniform;
        private GlMeshBuffer? _grassMesh;

        // Shader state — ground plane (procedural textures)
        private int _groundProgram;
        private int _groundMvpUniform;
        private int _groundSunDirUniform;
        private int _groundSunColorUniform;
        private int _groundAmbientUniform;
        private int _groundMaterialUniform;
        private int _groundGridOverlayUniform;
        private int _groundUseTextureUniform;
        private int _groundTextureUniform;
        private int _groundTileSizeUniform;
        private int _groundGridMinorUniform;
        private int _groundGridMajorUniform;
        private int _groundGridHalfUniform;
        private int _groundTextureId;
        private int _groundTexWidth;
        private int _groundTexHeight;
        private int _groundMaterialIndex;
        private bool _groundShowGridOverlay;

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

        // Shader state — textured (lit) program for OBJ+MTL meshes
        private int _texProgram;
        private int _texMvpUniform;
        private int _texModelUniform;
        private int _texNormalMatUniform;
        private int _texSunDirUniform;
        private int _texSunColorUniform;
        private int _texAmbientUniform;
        private int _texTintUniform;
        private int _texAlphaUniform;
        private int _texTextureUniform;

        // Environment meshes — persist across scene rebuilds
        private GlMeshBuffer? _skyDomeMesh;
        private GlMeshBuffer? _groundPlaneMesh;

        // GL extension: functions not in Avalonia's GlInterface
        private delegate void D_glUniform3f(int loc, float v0, float v1, float v2);
        private delegate void D_glUniform2f(int loc, float v0, float v1);
        private delegate void D_glUniform1f(int loc, float v0);
        private delegate void D_glBlendFunc(int sfactor, int dfactor);
        private delegate void D_glCullFace(int mode);
        private unsafe delegate void D_glUniformMatrix3fv(
            int loc, int count, bool transpose, float* value);
        private delegate void D_glUniform1i(int loc, int v0);
        private D_glUniform3f? _glUniform3f;
        private D_glUniform2f? _glUniform2f;
        private D_glUniform1f? _glUniform1f;
        private D_glUniform1i? _glUniform1i;
        private D_glBlendFunc? _glBlendFunc;
        private D_glCullFace? _glCullFace;
        private D_glUniformMatrix3fv? _glUniformMatrix3fv;

        // GL texture operations
        private const int GL_TEXTURE_2D_VAL = 0x0DE1;
        private const int GL_TEXTURE0 = 0x84C0;
        private delegate void D_glBindTexture(int target, int texture);
        private delegate void D_glActiveTexture(int texture);
        private delegate void D_glUniform4f(int loc, float v0, float v1, float v2, float v3);
        private unsafe delegate void D_glDeleteTextures(int n, int* textures);
        private D_glBindTexture? _glBindTexture;
        private D_glActiveTexture? _glActiveTexture;
        private D_glUniform4f? _glUniform4f;
        private D_glDeleteTextures? _glDeleteTextures;

        // Texture cache (path → GL texture ID) + white 1×1 fallback
        private readonly Dictionary<string, int> _textureCache = new();
        private int _whiteTexture;

        private bool _initOk;

        // Scene population state — set via PopulateScene(), consumed in
        // OnOpenGlRender (GPU uploads require the GL context).
        private Scene3D? _pendingScene;
        private Scene3D? _loadedScene;
        private bool _sceneNeedsRebuild;

        // Cached environment settings from the last loaded scene
        private EnvironmentSettings? _loadedEnv;

        // Deferred dispersion frame update for playback
        private DispersionFrameRequest? _pendingDispersionFrame;

        // Wind animation state (kept for future use)
        private readonly System.Diagnostics.Stopwatch _animClock = System.Diagnostics.Stopwatch.StartNew();

        private int _frameCount;
        private double _fpsAccumulator;
        private double _lastFps;
        private double _lastFrameTime;

        public double CurrentFps => _lastFps;

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

        // ── Sky dome shader (procedural clouds + sun) ──────────────────

        private const string SkyVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec4 aCol;

uniform mat4 uMVP;

out vec3 vDir;
out vec4 vVertCol;

void main()
{
    vDir     = normalize(aPos);
    vVertCol = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string SkyFragBody = @"
in vec3 vDir;
in vec4 vVertCol;

uniform vec3  uSunDir;
uniform float uTime;
uniform float uShowClouds;
uniform float uCloudSpeed;
uniform float uUseSkyTexture;
uniform sampler2D uSkyTexture;

layout(location = 0) out vec4 fragColor;

const float PI = 3.14159265359;

float hash21(vec2 p)
{
    p = fract(p * vec2(234.34, 435.345));
    p += dot(p, p + 34.23);
    return fract(p.x * p.y);
}

float vnoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 5; i++) { v += a * vnoise(p); p *= 2.03; a *= 0.5; }
    return v;
}

void main()
{
    vec3 dir = normalize(vDir);

    // --- equirectangular panorama texture ---
    if (uUseSkyTexture > 0.5)
    {
        float u = atan(dir.y, dir.x) / (2.0 * PI) + 0.5;
        float v = asin(clamp(dir.z, -1.0, 1.0)) / PI + 0.5;
        fragColor = texture(uSkyTexture, vec2(u, v));
        return;
    }

    float el = max(dir.z, 0.0);

    // --- sky gradient (deep blue zenith -> light horizon) ---
    vec3 zenith  = vec3(0.22, 0.40, 0.82);
    vec3 horizon = vec3(0.68, 0.80, 0.94);
    vec3 sky = mix(horizon, zenith, pow(el, 0.55));

    // subtle warm band near horizon (atmospheric scatter)
    float hBand = exp(-el * 12.0);
    sky += vec3(0.12, 0.06, 0.0) * hBand;

    // --- sun disc + glow + halo ---
    vec3 sd = normalize(uSunDir);
    float cosA = dot(dir, sd);

    float disc = smoothstep(0.9996, 0.9999, cosA);
    float glow = pow(max(cosA, 0.0), 128.0) * 0.8;
    float halo = pow(max(cosA, 0.0), 12.0) * 0.25;

    vec3 sunCol  = vec3(1.0, 0.97, 0.88);
    vec3 haloCol = vec3(1.0, 0.85, 0.55);
    sky += sunCol * disc + sunCol * glow + haloCol * halo;

    // --- clouds (FBM noise on projected dome coords) ---
    vec2 cuv = dir.xy / (dir.z + 0.25) * 2.5;
    float cspd = uTime * uCloudSpeed;
    cuv += vec2(cspd * 0.008, cspd * 0.003);

    float c1 = fbm(cuv * 1.8);
    float c2 = fbm(cuv * 3.5 + 7.7);
    float cloud = smoothstep(0.42, 0.72, c1);
    cloud += smoothstep(0.50, 0.78, c2) * 0.4;
    cloud = clamp(cloud, 0.0, 1.0);

    // fade clouds near horizon to avoid hard edge
    cloud *= smoothstep(0.0, 0.18, el);

    // cloud lit side vs shadow
    float cLit = 0.6 + 0.4 * max(dot(vec3(0, 0, 1), sd), 0.0);
    vec3 cloudCol = vec3(cLit, cLit, cLit * 0.98);
    sky = mix(sky, cloudCol, cloud * 0.75 * uShowClouds);

    fragColor = vec4(sky, 1.0);
}
";

        // ── Grass blade shader (animated sway) ─────────────────────────

        private const string GrassVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec4 aCol;

uniform mat4  uMVP;
uniform float uTime;
uniform vec3  uWindDir;

out vec4 vCol;

void main()
{
    vec3 pos = aPos;
    float h = clamp(aNorm.z, 0.0, 1.0);

    float p1 = uTime * 1.8 + pos.x * 0.45 + pos.y * 0.32;
    float p2 = uTime * 1.1 + pos.x * 0.65 - pos.y * 0.48;
    float s1 = sin(p1) * h * h;
    float s2 = sin(p2) * h * h * 0.55;

    pos.x += uWindDir.x * s1 * 0.45 + (-uWindDir.y) * s2 * 0.22;
    pos.y += uWindDir.y * s1 * 0.45 +   uWindDir.x  * s2 * 0.22;
    pos.z += cos(p1) * h * 0.04;

    vCol = aCol;
    gl_Position = uMVP * vec4(pos, 1.0);
}
";

        private const string GrassFragBody = @"
in vec4 vCol;
layout(location = 0) out vec4 fragColor;
void main() { fragColor = vCol; }
";

        // ── Ground plane shader (procedural textures) ──────────────────

        private const string GroundVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec4 aCol;

uniform mat4 uMVP;

out vec3 vWorldPos;
out vec3 vNorm;
out vec4 vCol;

void main()
{
    vWorldPos = aPos;
    vNorm     = aNorm;
    vCol      = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string GroundFragBody = @"
in vec3 vWorldPos;
in vec3 vNorm;
in vec4 vCol;

uniform vec3  uSunDir;
uniform vec3  uSunColor;
uniform vec3  uAmbient;
uniform int   uMaterial;
uniform float uGridOverlay;
uniform float uUseGroundTexture;
uniform sampler2D uGroundTexture;
uniform vec2  uGroundTileSize;
uniform float uGridMinor;
uniform float uGridMajor;
uniform float uGridHalf;

layout(location = 0) out vec4 fragColor;

float hash21(vec2 p)
{
    p = fract(p * vec2(234.34, 435.345));
    p += dot(p, p + 34.23);
    return fract(p.x * p.y);
}

float vnoise(vec2 p)
{
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash21(i);
    float b = hash21(i + vec2(1.0, 0.0));
    float c = hash21(i + vec2(0.0, 1.0));
    float d = hash21(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p)
{
    float v = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++)
    {
        v += a * vnoise(p);
        p *= 2.01;
        a *= 0.5;
    }
    return v;
}

void main()
{
    vec2 uv = vWorldPos.xy;
    vec3 col;

    // --- user-supplied ground texture (tiled, aspect-correct) ---
    if (uUseGroundTexture > 0.5)
    {
        vec2 ts = max(uGroundTileSize, vec2(1.0));
        vec2 tuv = fract(uv / ts);
        col = texture(uGroundTexture, tuv).rgb;
    }
    else if (uMaterial == 1) // Grass
    {
        float n  = fbm(uv * 0.4);
        float d  = vnoise(uv * 3.0);
        col = mix(vec3(0.30, 0.44, 0.20), vec3(0.50, 0.62, 0.30), n);
        col += vec3(-0.02, 0.02, -0.01) * d;
    }
    else if (uMaterial == 2) // Concrete
    {
        float n = vnoise(uv * 2.0) * 0.04;
        col = vec3(0.74 + n, 0.74 + n, 0.72 + n);
        vec2 g = fract(uv / 5.0);
        float joint = 1.0 - smoothstep(0.0, 0.06, min(g.x, g.y));
        joint = max(joint, 1.0 - smoothstep(0.94, 1.0, max(g.x, g.y)));
        col = mix(col, vec3(0.52, 0.52, 0.50), joint * 0.55);
    }
    else if (uMaterial == 3) // Sand
    {
        float n = fbm(uv * 0.8);
        float grain = vnoise(uv * 8.0) * 0.06;
        col = vec3(0.84, 0.77, 0.58) + vec3(0.04, 0.02, -0.02) * n + grain;
    }
    else if (uMaterial == 4) // Asphalt
    {
        float n = vnoise(uv * 4.0) * 0.05;
        float stones = vnoise(uv * 12.0) * 0.03;
        col = vec3(0.24 + n, 0.24 + n, 0.26 + n) + stones;
    }
    else // Grid or fallback
    {
        col = vCol.rgb;
    }

    // Grid overlay — limited to the GridHalfSize area
    if (uGridOverlay > 0.5 && abs(uv.x) < uGridHalf && abs(uv.y) < uGridHalf)
    {
        vec2 g5  = abs(fract(uv / uGridMinor + 0.5) - 0.5);
        float l5 = 1.0 - smoothstep(0.015, 0.04, min(g5.x, g5.y));
        vec2 g25 = abs(fract(uv / uGridMajor + 0.5) - 0.5);
        float l25= 1.0 - smoothstep(0.008, 0.025, min(g25.x, g25.y));
        col = mix(col, vec3(0.25), l5  * 0.2);
        col = mix(col, vec3(0.15), l25 * 0.3);
    }

    // Sun + ambient lighting
    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    vec3 light = uAmbient + uSunColor * diff;

    fragColor = vec4(col * light, 1.0);
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

        // ── Textured (lit) shader for OBJ+MTL meshes ────────────────────

        private const string TexVertBody = @"
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNorm;
layout(location = 2) in vec2 aUV;

uniform mat4 uMVP;
uniform mat4 uModel;
uniform mat3 uNormalMat;

out vec3 vNorm;
out vec2 vUV;

void main()
{
    vNorm = uNormalMat * aNorm;
    vUV   = aUV;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string TexFragBody = @"
in vec3 vNorm;
in vec2 vUV;

uniform sampler2D uTexture;
uniform vec4  uTint;
uniform vec3  uSunDir;
uniform vec3  uSunColor;
uniform vec3  uAmbient;
uniform float uAlpha;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 texel = texture(uTexture, vUV);
    if (texel.a < 0.1) discard;

    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    vec3 light = uAmbient + uSunColor * diff;

    vec3 col = texel.rgb * uTint.rgb * light;
    fragColor = vec4(col, texel.a * uTint.a * uAlpha);
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
            if (_loadedScene != null)
            {
                var (bbMin, bbMax) = ComputeSceneBounds(_loadedScene);
                _camera.ZoomToFit(bbMin, bbMax);
            }
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
        /// Create a <see cref="CameraPreset"/> from the current view.
        /// </summary>
        public CameraPreset SaveCameraPreset(string name)
            => _camera.CreatePreset(name);

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
                var p1c = gl.GetProcAddress("glUniform2f");
                if (p1c != IntPtr.Zero)
                    _glUniform2f = Marshal.GetDelegateForFunctionPointer<D_glUniform2f>(p1c);
                var p2 = gl.GetProcAddress("glBlendFunc");
                if (p2 != IntPtr.Zero)
                    _glBlendFunc = Marshal.GetDelegateForFunctionPointer<D_glBlendFunc>(p2);
                var p3 = gl.GetProcAddress("glCullFace");
                if (p3 != IntPtr.Zero)
                    _glCullFace = Marshal.GetDelegateForFunctionPointer<D_glCullFace>(p3);
                var p4 = gl.GetProcAddress("glUniformMatrix3fv");
                if (p4 != IntPtr.Zero)
                    _glUniformMatrix3fv = Marshal.GetDelegateForFunctionPointer<D_glUniformMatrix3fv>(p4);
                var p5 = gl.GetProcAddress("glUniform1i");
                if (p5 != IntPtr.Zero)
                    _glUniform1i = Marshal.GetDelegateForFunctionPointer<D_glUniform1i>(p5);
                var pBT = gl.GetProcAddress("glBindTexture");
                if (pBT != IntPtr.Zero)
                    _glBindTexture = Marshal.GetDelegateForFunctionPointer<D_glBindTexture>(pBT);
                var pAT = gl.GetProcAddress("glActiveTexture");
                if (pAT != IntPtr.Zero)
                    _glActiveTexture = Marshal.GetDelegateForFunctionPointer<D_glActiveTexture>(pAT);
                var pU4f = gl.GetProcAddress("glUniform4f");
                if (pU4f != IntPtr.Zero)
                    _glUniform4f = Marshal.GetDelegateForFunctionPointer<D_glUniform4f>(pU4f);
                var pDT = gl.GetProcAddress("glDeleteTextures");
                if (pDT != IntPtr.Zero)
                    _glDeleteTextures = Marshal.GetDelegateForFunctionPointer<D_glDeleteTextures>(pDT);

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

                // ── Compile sky shader ──────────────────────────────────
                _skyProgram          = CompileProgram(gl, SkyVertBody, SkyFragBody);
                _skyMvpUniform       = GetUniformLoc(gl, _skyProgram, "uMVP");
                _skySunDirUniform    = GetUniformLoc(gl, _skyProgram, "uSunDir");
                _skyTimeUniform      = GetUniformLoc(gl, _skyProgram, "uTime");
                _skyShowCloudsUniform = GetUniformLoc(gl, _skyProgram, "uShowClouds");
                _skyCloudSpeedUniform = GetUniformLoc(gl, _skyProgram, "uCloudSpeed");
                _skyUseSkyTextureUniform = GetUniformLoc(gl, _skyProgram, "uUseSkyTexture");
                _skySkyTextureUniform = GetUniformLoc(gl, _skyProgram, "uSkyTexture");

                // ── Compile grass shader ─────────────────────────────────
                _grassProgram       = CompileProgram(gl, GrassVertBody, GrassFragBody);
                _grassMvpUniform    = GetUniformLoc(gl, _grassProgram, "uMVP");
                _grassTimeUniform   = GetUniformLoc(gl, _grassProgram, "uTime");
                _grassWindDirUniform = GetUniformLoc(gl, _grassProgram, "uWindDir");

                // ── Compile ground shader ──────────────────────────────
                _groundProgram           = CompileProgram(gl, GroundVertBody, GroundFragBody);
                _groundMvpUniform        = GetUniformLoc(gl, _groundProgram, "uMVP");
                _groundSunDirUniform     = GetUniformLoc(gl, _groundProgram, "uSunDir");
                _groundSunColorUniform   = GetUniformLoc(gl, _groundProgram, "uSunColor");
                _groundAmbientUniform    = GetUniformLoc(gl, _groundProgram, "uAmbient");
                _groundMaterialUniform   = GetUniformLoc(gl, _groundProgram, "uMaterial");
                _groundGridOverlayUniform = GetUniformLoc(gl, _groundProgram, "uGridOverlay");
                _groundUseTextureUniform = GetUniformLoc(gl, _groundProgram, "uUseGroundTexture");
                _groundTextureUniform    = GetUniformLoc(gl, _groundProgram, "uGroundTexture");
                _groundTileSizeUniform   = GetUniformLoc(gl, _groundProgram, "uGroundTileSize");
                _groundGridMinorUniform  = GetUniformLoc(gl, _groundProgram, "uGridMinor");
                _groundGridMajorUniform  = GetUniformLoc(gl, _groundProgram, "uGridMajor");
                _groundGridHalfUniform   = GetUniformLoc(gl, _groundProgram, "uGridHalf");

                // ── Compile textured shader ────────────────────────────
                _texProgram          = CompileProgram(gl, TexVertBody, TexFragBody);
                _texMvpUniform       = GetUniformLoc(gl, _texProgram, "uMVP");
                _texModelUniform     = GetUniformLoc(gl, _texProgram, "uModel");
                _texNormalMatUniform = GetUniformLoc(gl, _texProgram, "uNormalMat");
                _texSunDirUniform    = GetUniformLoc(gl, _texProgram, "uSunDir");
                _texSunColorUniform  = GetUniformLoc(gl, _texProgram, "uSunColor");
                _texAmbientUniform   = GetUniformLoc(gl, _texProgram, "uAmbient");
                _texTintUniform      = GetUniformLoc(gl, _texProgram, "uTint");
                _texAlphaUniform     = GetUniformLoc(gl, _texProgram, "uAlpha");
                _texTextureUniform   = GetUniformLoc(gl, _texProgram, "uTexture");

                // ── Create white 1×1 fallback texture ───────────────────
                _whiteTexture = GlTextureLoader.CreateWhite1x1(gl);

                // ── Build grid geometry ─────────────────────────────────
                var initEnv = _pendingScene?.Environment ?? new EnvironmentSettings();
                _grid.HalfSize = (float)initEnv.GridHalfSize;
                _grid.MinorStep = (float)initEnv.GridMinorSpacing;
                _grid.MajorStep = (float)initEnv.GridMajorSpacing;
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
            double sunAz, sunEl;
            if (env.UseSolarClock)
                (sunAz, sunEl) = env.ComputeSolarPosition();
            else
                (sunAz, sunEl) = (env.SunAzimuthDeg, env.SunElevationDeg);

            float azRad  = (float)(sunAz * Math.PI / 180.0);
            float elRad  = (float)(sunEl * Math.PI / 180.0);
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

            // ── 1. Sky dome (procedural clouds + sun, no depth write) ───
            if (env.SkydomeEnabled && _skyDomeMesh != null && _skyProgram != 0)
            {
                gl.Disable(GL_DEPTH_TEST);
                gl.DepthMask(0);

                gl.UseProgram(_skyProgram);

                float timeSky = (float)_animClock.Elapsed.TotalSeconds;
                _glUniform3f?.Invoke(_skySunDirUniform, sunX, sunY, sunZ);
                _glUniform1f?.Invoke(_skyTimeUniform, timeSky);
                _glUniform1f?.Invoke(_skyShowCloudsUniform, env.ShowClouds ? 1f : 0f);
                _glUniform1f?.Invoke(_skyCloudSpeedUniform, (float)env.CloudSpeed);

                bool useSkyTex = _skyTextureId != 0;
                _glUniform1f?.Invoke(_skyUseSkyTextureUniform, useSkyTex ? 1f : 0f);
                if (useSkyTex)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _skyTextureId);
                    _glUniform1i?.Invoke(_skySkyTextureUniform, 0);
                }

                var camPos = _camera.Eye;
                var skyModel = Matrix4x4.CreateTranslation(camPos);
                var skyMvp = skyModel * view * proj;
                SetMatrixUniform(gl, _skyMvpUniform, skyMvp);

                gl.Disable(GL_CULL_FACE);
                _skyDomeMesh.Draw(gl);

                if (useSkyTex)
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);

                gl.UseProgram(0);
            }

            // Re-enable depth for all subsequent geometry
            gl.Enable(GL_DEPTH_TEST);
            gl.DepthFunc(GL_LEQUAL);
            gl.DepthMask(1);
            gl.Clear(GL_DEPTH_BUFFER_BIT); // reset depth after sky

            // ── 2. Ground plane (procedural textures + lighting) ──────────
            if (_groundPlaneMesh != null && _groundProgram != 0)
            {
                gl.UseProgram(_groundProgram);

                _glUniform1i?.Invoke(_groundMaterialUniform, _groundMaterialIndex);
                _glUniform1f?.Invoke(_groundGridOverlayUniform,
                    _groundShowGridOverlay ? 1f : 0f);
                _glUniform1f?.Invoke(_groundGridMinorUniform, (float)env.GridMinorSpacing);
                _glUniform1f?.Invoke(_groundGridMajorUniform, (float)env.GridMajorSpacing);
                _glUniform1f?.Invoke(_groundGridHalfUniform, (float)env.GridHalfSize);
                _glUniform3f?.Invoke(_groundSunDirUniform, sunX, sunY, sunZ);
                _glUniform3f?.Invoke(_groundSunColorUniform,
                    warmR * sunI, warmG * sunI, warmB * sunI);
                _glUniform3f?.Invoke(_groundAmbientUniform,
                    0.431f * ambI, 0.490f * ambI, 0.588f * ambI);

                bool useGndTex = _groundTextureId != 0;
                _glUniform1f?.Invoke(_groundUseTextureUniform, useGndTex ? 1f : 0f);
                if (useGndTex)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _groundTextureId);
                    _glUniform1i?.Invoke(_groundTextureUniform, 0);

                    int tw = _groundTexWidth > 0 ? _groundTexWidth : 1;
                    int th = _groundTexHeight > 0 ? _groundTexHeight : 1;
                    float pixelsPerMeter = 100f;
                    float tileSizeX = tw / pixelsPerMeter;
                    float tileSizeY = th / pixelsPerMeter;
                    _glUniform2f?.Invoke(_groundTileSizeUniform, tileSizeX, tileSizeY);
                }

                SetMatrixUniform(gl, _groundMvpUniform, mvp);

                gl.Enable(GL_CULL_FACE);
                _glCullFace?.Invoke(GL_BACK);
                _groundPlaneMesh.Draw(gl);

                if (useGndTex)
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);

                gl.UseProgram(0);
            }

            // ── 3. Grid overlay ─────────────────────────────────────────
            gl.UseProgram(_lineProgram);
            _glUniform1f?.Invoke(_lineTimeUniform, 0f);
            _glUniform1f?.Invoke(_lineAnimScaleUniform, 0f);
            SetMatrixUniform(gl, _mvpUniform, mvp);
            _grid.Render(gl);

            // ── 3b. Animated grass blades ───────────────────────────────
            if (_grassMesh != null && _grassProgram != 0)
            {
                gl.UseProgram(_grassProgram);

                float grassTime = (float)_animClock.Elapsed.TotalSeconds;
                _glUniform1f?.Invoke(_grassTimeUniform, grassTime);
                _glUniform3f?.Invoke(_grassWindDirUniform, 0.7f, 0.7f, 0f);
                SetMatrixUniform(gl, _grassMvpUniform, mvp);

                gl.Disable(GL_CULL_FACE);
                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                _grassMesh.Draw(gl);
                gl.Disable(GL_BLEND);

                gl.UseProgram(0);
            }

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
                    if (!obj.Visible || obj.Mesh.IsLineGeometry || obj.Mesh.IsTextured) continue;

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

            // ── 4b. Textured scene objects (OBJ+MTL decorations) ────────
            if (_texProgram != 0)
            {
                bool hasTextured = false;
                foreach (var obj in _sceneObjects)
                {
                    if (obj.Visible && obj.Mesh.IsTextured && !obj.Mesh.IsLineGeometry)
                    { hasTextured = true; break; }
                }

                if (hasTextured)
                {
                    gl.UseProgram(_texProgram);

                    _glUniform3f?.Invoke(_texSunDirUniform, sunX, sunY, sunZ);
                    _glUniform3f?.Invoke(_texSunColorUniform,
                        warmR * sunI, warmG * sunI, warmB * sunI);
                    _glUniform3f?.Invoke(_texAmbientUniform,
                        0.431f * ambI, 0.490f * ambI, 0.588f * ambI);
                    _glUniform1i?.Invoke(_texTextureUniform, 0);

                    gl.Disable(GL_CULL_FACE);
                    gl.Enable(GL_BLEND);
                    _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

                    foreach (var obj in _sceneObjects)
                    {
                        if (!obj.Visible || !obj.Mesh.IsTextured || obj.Mesh.IsLineGeometry)
                            continue;

                        var model = obj.ModelMatrix;
                        var objMvp = model * view * proj;
                        SetMatrixUniform(gl, _texMvpUniform, objMvp);
                        SetMatrixUniform(gl, _texModelUniform, model);

                        if (Matrix4x4.Invert(model, out var inv))
                        {
                            var normalMat = Matrix4x4.Transpose(inv);
                            SetMatrix3Uniform(gl, _texNormalMatUniform, normalMat);
                        }

                        _glActiveTexture?.Invoke(GL_TEXTURE0);
                        _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, obj.TextureId);

                        var tint = obj.Tint;
                        _glUniform4f?.Invoke(_texTintUniform,
                            tint.X, tint.Y, tint.Z, tint.W);
                        _glUniform1f?.Invoke(_texAlphaUniform, 1.0f);

                        obj.Mesh.Draw(gl);
                    }

                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                    gl.Disable(GL_BLEND);
                    gl.UseProgram(0);
                }
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

            _frameCount++;
            _fpsAccumulator += _animClock.Elapsed.TotalSeconds - _lastFrameTime;
            if (_fpsAccumulator >= 0.5)
            {
                _lastFps = _frameCount / _fpsAccumulator;
                _frameCount = 0;
                _fpsAccumulator = 0;
            }
            _lastFrameTime = _animClock.Elapsed.TotalSeconds;

            if (_hasWindLines || _grassMesh != null || _skyDomeMesh != null)
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
            if (_skyProgram != 0)
            {
                gl.DeleteProgram(_skyProgram);
                _skyProgram = 0;
            }
            if (_grassProgram != 0)
            {
                gl.DeleteProgram(_grassProgram);
                _grassProgram = 0;
            }
            if (_groundProgram != 0)
            {
                gl.DeleteProgram(_groundProgram);
                _groundProgram = 0;
            }
            _grassMesh?.Cleanup(gl);
            _grassMesh = null;
            if (_texProgram != 0)
            {
                gl.DeleteProgram(_texProgram);
                _texProgram = 0;
            }
            DeleteGlTexture(_skyTextureId);
            _skyTextureId = 0;
            DeleteGlTexture(_groundTextureId);
            _groundTextureId = 0;
            CleanupTextureCache();
            _textureCache.Clear();
            DeleteGlTexture(_whiteTexture);
            _whiteTexture = 0;
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
            _loadedScene = scene;

            // Release existing GPU resources
            foreach (var obj in _sceneObjects)
                obj.Mesh.Cleanup(gl);
            _sceneObjects.Clear();

            // Release cached textures from previous scene
            CleanupTextureCache();
            _textureCache.Clear();

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

            // Sky panorama texture
            DeleteGlTexture(_skyTextureId);
            _skyTextureId = 0;
            {
                var skyPath = BuiltinAssetResolver.Resolve(env.SkyTexturePath);
                if (!string.IsNullOrEmpty(skyPath) && System.IO.File.Exists(skyPath))
                    _skyTextureId = GlTextureLoader.LoadFromFile(gl, skyPath);
            }

            // Ground texture
            DeleteGlTexture(_groundTextureId);
            _groundTextureId = 0;
            _groundTexWidth = 0; _groundTexHeight = 0;
            {
                var gndPath = BuiltinAssetResolver.Resolve(env.GroundTexturePath);
                if (!string.IsNullOrEmpty(gndPath) && System.IO.File.Exists(gndPath))
                    _groundTextureId = GlTextureLoader.LoadFromFile(gl, gndPath,
                        out _groundTexWidth, out _groundTexHeight);
            }

            // Ground plane — procedural shader handles material appearance
            _groundPlaneMesh?.Cleanup(gl);
            _groundPlaneMesh = null;
            _groundMaterialIndex = (int)env.Ground;
            _groundShowGridOverlay = env.ShowGridOverlay;
            {
                var fallback = new Vector4(0.48f, 0.58f, 0.35f, 1f);
                var (gndV, gndI) = GlMeshBuffer.GenerateGroundQuad(
                    500f, fallback);
                _groundPlaneMesh = new GlMeshBuffer();
                _groundPlaneMesh.Upload(gl, gndV, gndI, keepCpuCopy: true);
            }

            // Grass blades — independent toggle; exclusion zones come from decorations
            _grassMesh?.Cleanup(gl);
            _grassMesh = null;
            if (env.ShowGrassBlades && scene != null)
            {
                var exclusions = BuildDecorationFootprints(scene);
                int bladeCount = Math.Clamp(env.GrassBladeCount, 500, 100000);
                var (gV, gI) = GlMeshBuffer.GenerateGrassBlades(80f, bladeCount,
                    exclusionZones: exclusions);
                _grassMesh = new GlMeshBuffer();
                _grassMesh.Upload(gl, gV, gI);
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

                    // Try textured loading first (OBJ with MTL/textures)
                    var texturedSubs = MeshFileLoader.LoadTextured(deco.FilePath);
                    if (texturedSubs != null)
                    {
                        // Standalone TexturePath overrides all MTL textures
                        string? overrideTex = !string.IsNullOrEmpty(deco.TexturePath)
                            && File.Exists(deco.TexturePath) ? deco.TexturePath : null;

                        for (int s = 0; s < texturedSubs.Count; s++)
                        {
                            var sub = texturedSubs[s];
                            var mesh = new GlMeshBuffer();
                            mesh.UploadTextured(gl, sub.Vertices, sub.Indices);

                            string? texPath = overrideTex ?? sub.TexturePath;
                            int texId = 0;
                            if (texPath != null)
                            {
                                if (!_textureCache.TryGetValue(texPath, out texId))
                                {
                                    texId = GlTextureLoader.LoadFromFile(gl, texPath);
                                    if (texId > 0)
                                        _textureCache[texPath] = texId;
                                }
                            }
                            if (texId == 0)
                                texId = _whiteTexture;

                            var obj = new SceneObject(mesh, model, $"deco:{i}:{s}")
                            {
                                TextureId = texId,
                                Tint = overrideTex != null ? Vector4.One : sub.DiffuseColor
                            };
                            _sceneObjects.Add(obj);
                        }
                        continue;
                    }

                    // Fall back to solid (untextured) loading
                    var mc = deco.MaterialColor;
                    var decoColor = new Vector4(
                        mc.ScR, mc.ScG, mc.ScB,
                        (float)deco.Opacity);

                    var loaded = MeshFileLoader.Load(deco.FilePath, decoColor);
                    if (loaded == null) continue;

                    var solidMesh = new GlMeshBuffer();
                    solidMesh.Upload(gl, loaded.Value.verts, loaded.Value.indices,
                        keepCpuCopy: true);

                    _sceneObjects.Add(new SceneObject(
                        solidMesh, model, "deco:" + i));
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

            // ── Camera: apply preset, or zoom to fit scene content ───
            if (scene.CameraPresets != null && scene.CameraPresets.Count > 0)
            {
                _camera.ApplyPreset(scene.CameraPresets[0]);
            }
            else
            {
                var (bbMin, bbMax) = ComputeSceneBounds(scene);
                _camera.Reset();
                _camera.ZoomToFit(bbMin, bbMax);
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

        private unsafe void DeleteGlTexture(int texId)
        {
            if (_glDeleteTextures == null || texId <= 0) return;
            int t = texId;
            _glDeleteTextures(1, &t);
        }

        private void CleanupTextureCache()
        {
            foreach (var texId in _textureCache.Values)
                DeleteGlTexture(texId);
        }

        private static List<(float minX, float minY, float maxX, float maxY)>
            BuildDecorationFootprints(Scene3D scene)
        {
            var result = new List<(float, float, float, float)>();
            if (scene.Decorations == null) return result;

            foreach (var deco in scene.Decorations)
            {
                var bb = deco.BoundingBox;
                if (bb != null)
                {
                    float s = (float)deco.Scale;
                    float px = (float)deco.Position.X;
                    float py = (float)deco.Position.Y;
                    float x0 = (float)bb.Min.X * s + px;
                    float y0 = (float)bb.Min.Y * s + py;
                    float x1 = (float)bb.Max.X * s + px;
                    float y1 = (float)bb.Max.Y * s + py;
                    if (x0 > x1) (x0, x1) = (x1, x0);
                    if (y0 > y1) (y0, y1) = (y1, y0);
                    result.Add((x0, y0, x1, y1));
                    continue;
                }

                if (!string.IsNullOrEmpty(deco.FilePath))
                {
                    float s = (float)deco.Scale;
                    float px = (float)deco.Position.X;
                    float py = (float)deco.Position.Y;
                    result.Add((px - 5f * s, py - 5f * s,
                                px + 5f * s, py + 5f * s));
                }
            }

            return result;
        }

        private static (Vector3 min, Vector3 max) ComputeSceneBounds(Scene3D scene)
        {
            float xMin = -50, yMin = -50, zMin = 0;
            float xMax = 50, yMax = 50, zMax = 10;

            void Expand(float x, float y, float z, float r = 0)
            {
                xMin = MathF.Min(xMin, x - r);
                yMin = MathF.Min(yMin, y - r);
                zMin = MathF.Min(zMin, z);
                xMax = MathF.Max(xMax, x + r);
                yMax = MathF.Max(yMax, y + r);
                zMax = MathF.Max(zMax, z + r);
            }

            if (scene.TopLevelSources != null)
                foreach (var src in scene.TopLevelSources)
                {
                    var p = src.EffectivePosition;
                    Expand((float)p.X, (float)p.Y, (float)p.Z, 5f);
                }

            if (scene.Decorations != null)
                foreach (var d in scene.Decorations)
                {
                    float s = (float)d.Scale;
                    Expand((float)d.Position.X, (float)d.Position.Y,
                           (float)d.Position.Z, 10f * s);
                }

            if (scene.MonitorPoints != null)
                foreach (var m in scene.MonitorPoints)
                    Expand((float)m.Position.X, (float)m.Position.Y,
                           (float)m.Position.Z, 2f);

            if (scene.GasDetectors != null)
                foreach (var g in scene.GasDetectors)
                    Expand((float)g.Position.X, (float)g.Position.Y,
                           (float)g.Position.Z, 2f);

            return (new Vector3(xMin, yMin, zMin), new Vector3(xMax, yMax, zMax));
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

        /// <summary>GL texture ID for textured meshes (0 = no texture).</summary>
        public int TextureId { get; set; }

        /// <summary>Material tint multiplied with texture colour.</summary>
        public Vector4 Tint { get; set; } = Vector4.One;

    }
}

