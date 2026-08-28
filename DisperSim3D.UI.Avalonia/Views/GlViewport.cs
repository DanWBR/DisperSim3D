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
        private int _skyBrightnessUniform;
        private int _skyVOffsetUniform;
        private int _skyNightModeUniform;
        private int _skyShowStarsUniform;
        private int _skyMoonDirUniform;
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
        private int _solidFireCountUniform;
        private int _solidFirePosUniform;
        private int _solidFireColorUniform;
        private int _solidFireRadiusUniform;
        private int _solidFireTimeUniform;
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
        private delegate void D_glPolygonOffset(float factor, float units);
        private unsafe delegate void D_glUniformMatrix3fv(
            int loc, int count, bool transpose, float* value);
        private delegate void D_glUniform1i(int loc, int v0);
        private unsafe delegate void D_glUniform3fv(int loc, int count, float* value);
        private unsafe delegate void D_glUniform1fv(int loc, int count, float* value);
        private D_glUniform3f? _glUniform3f;
        private D_glUniform2f? _glUniform2f;
        private D_glUniform1f? _glUniform1f;
        private D_glUniform1i? _glUniform1i;
        private D_glUniform3fv? _glUniform3fv;
        private D_glUniform1fv? _glUniform1fv;
        private D_glBlendFunc? _glBlendFunc;
        private D_glCullFace? _glCullFace;
        private D_glPolygonOffset? _glPolygonOffset;
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

        // Shadow mapping — GL extensions
        private const int GL_DEPTH_COMPONENT_VAL = 0x1902;
        private const int GL_DEPTH_COMPONENT24_VAL = 0x81A6;
        private const int GL_DEPTH_ATTACHMENT_VAL = 0x8D00;
        private const int GL_NONE_VAL = 0;
        private const int GL_FLOAT_VAL = 0x1406;
        private const int GL_UNSIGNED_INT_VAL = 0x1405;
        private const int GL_TEXTURE1 = 0x84C1;
        private const int GL_CLAMP_TO_BORDER = 0x812D;
        private const int GL_TEXTURE_BORDER_COLOR = 0x1004;
        private const int GL_TEXTURE_MIN_FILTER_VAL = 0x2801;
        private const int GL_TEXTURE_MAG_FILTER_VAL = 0x2800;
        private const int GL_TEXTURE_WRAP_S_VAL = 0x2802;
        private const int GL_TEXTURE_WRAP_T_VAL = 0x2803;
        private const int GL_NEAREST_VAL = 0x2600;
        private const int GL_LINEAR_VAL = 0x2601;
        private const int SHADOW_MAP_SIZE = 4096;

        private unsafe delegate void D_glGenFramebuffers(int n, int* ids);
        private unsafe delegate void D_glDeleteFramebuffers(int n, int* ids);
        private delegate void D_glFramebufferTexture2D(int target, int attachment,
            int textarget, int texture, int level);
        private delegate void D_glDrawBuffer(int mode);
        private delegate int D_glCheckFramebufferStatus(int target);
        private unsafe delegate void D_glTexImage2D_Shadow(int target, int level,
            int internalformat, int width, int height, int border,
            int format, int type, IntPtr data);
        private unsafe delegate void D_glTexParameteri(int target, int pname, int param);
        private unsafe delegate void D_glTexParameterfv(int target, int pname, float* pparams);

        private D_glGenFramebuffers? _glGenFramebuffers;
        private D_glDeleteFramebuffers? _glDeleteFramebuffers;
        private D_glFramebufferTexture2D? _glFramebufferTexture2D;
        private D_glDrawBuffer? _glDrawBuffer;
        private D_glCheckFramebufferStatus? _glCheckFramebufferStatus;
        private D_glTexImage2D_Shadow? _glTexImage2D;
        private D_glTexParameteri? _glTexParameteri;
        private D_glTexParameterfv? _glTexParameterfv;

        // Shadow mapping — state
        private int _shadowFbo;
        private int _shadowDepthTex;
        private int _shadowProgram;
        private int _shadowLightVPUniform;
        private int _shadowModelUniform;
        private bool _shadowReady;

        // Shadow catcher — transparent ground overlay that shows only shadows
        private int _shadowCatcherProgram;
        private int _scMvpUniform, _scShadowMapUniform, _scLightSpaceUniform, _scStrengthUniform;
        private GlMeshBuffer? _shadowCatcherMesh;

        // Shadow uniforms in lit shaders
        private int _solidShadowMapUniform, _solidLightSpaceUniform, _solidShadowEnabledUniform;
        private int _groundShadowMapUniform, _groundLightSpaceUniform, _groundShadowEnabledUniform;
        private int _texShadowMapUniform, _texLightSpaceUniform, _texShadowEnabledUniform;

        // Fog uniforms (per-program)
        private int _groundFogColorUniform, _groundFogDensityUniform, _groundCameraPosUniform;
        private int _solidFogColorUniform, _solidFogDensityUniform, _solidCameraPosUniform;
        private int _texFogColorUniform, _texFogDensityUniform, _texCameraPosUniform;
        private int _lineFogColorUniform, _lineFogDensityUniform, _lineCameraPosUniform;
        private int _grassFogColorUniform, _grassFogDensityUniform, _grassCameraPosUniform;
        private int _scCameraPosUniform, _scFogDensityUniform;
        private int _skyFogColorUniform, _skyFogDensityUniform;

        // Cloud (gas leak) shader
        private int _cloudProgram;
        private int _cloudMvpUniform, _cloudModelUniform, _cloudNormalMatUniform;
        private int _cloudSunDirUniform, _cloudSunColorUniform, _cloudAmbientUniform;
        private int _cloudAlphaUniform, _cloudTimeUniform;
        private int _cloudFogColorUniform, _cloudFogDensityUniform, _cloudCameraPosUniform;

        // Texture cache (path → GL texture ID) + white 1×1 fallback
        private readonly Dictionary<string, int> _textureCache = new();
        private int _whiteTexture;

        private bool _initOk;

        // Scene population state — set via PopulateScene(), consumed in
        // OnOpenGlRender (GPU uploads require the GL context).
        private Scene3D? _pendingScene;
        private Scene3D? _loadedScene;
        private bool _sceneNeedsRebuild;
        private bool _envNeedsRebuild;

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

        // ── Primitive draw mode state ───────────────────────────────────
        // Drag-on-the-ground placement for cube/sphere/cylinder/cone/pyramid.
        // Click sets the base point on Z=0; drag extends a preview mesh;
        // release fires DrawPrimitiveCompleted with the kind + the final
        // bounding values so MainWindow can spawn the actual Decoration3D.
        public enum PrimitiveKind { Cube, Sphere, Cylinder, Cone, Pyramid }
        private bool _drawActive;
        private PrimitiveKind _drawKind;
        private bool _drawDragging;
        private Vector3 _drawAnchor;
        private Vector3 _drawCurrent;
        private SceneObject? _drawPreview;
        private const string DrawPreviewTag = "draw:preview";

        /// <summary>
        /// Fired when the user finishes a click-drag in primitive draw mode.
        /// Args: kind, base world-space centre (XY on ground, Z=0), planar
        /// half-extent in metres (max(|dx|,|dy|) from the anchor), height in
        /// metres. The MainWindow turns this into a Decoration3D.
        /// </summary>
        internal event Action<PrimitiveKind, Vector3, float, float>? DrawPrimitiveCompleted;

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
out vec3 vWorldPos;

void main()
{
    vCol = aCol;
    vArcPhase = fract(aArcT * uAnimScale - uTime);
    vWorldPos = aPos;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string LineFragBody = @"
in vec4 vCol;
in float vArcPhase;
in vec3 vWorldPos;

uniform vec3  uFogColor;
uniform float uFogDensity;
uniform vec3  uCameraPos;

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

    // Atmospheric fog (exponential squared)
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    fragColor.rgb = mix(uFogColor, fragColor.rgb, clamp(exp(-fd * fd), 0.0, 1.0));
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
uniform float uSkyBrightness;
uniform float uSkyVOffset;
uniform vec3  uFogColor;
uniform float uFogDensity;
uniform float uNightMode;   // 0 = day, 1 = night
uniform float uShowStars;   // 0/1 — only honoured in night mode
uniform vec3  uMoonDir;     // used as the moon disc location in night mode

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
        float rawV = 0.5 - asin(clamp(dir.z, -1.0, 1.0)) / PI;

        // Compress V near horizon — texture features there appear smaller/farther.
        // Upper hemi: rawV 0..0.5;  lower hemi: rawV 0.5..1.
        float upper = clamp(rawV * 2.0, 0.0, 1.0);
        upper = pow(upper, 1.4);
        float lower = clamp((rawV - 0.5) * 2.0, 0.0, 1.0);
        lower = pow(lower, 0.7);
        float v = rawV < 0.5 ? upper * 0.5 : 0.5 + lower * 0.5;

        v = clamp(v + uSkyVOffset, 0.0, 1.0);
        vec4 texCol = texture(uSkyTexture, vec2(u, v));
        float gamma = 1.0 / max(0.01, uSkyBrightness);
        vec3 texRgb = pow(texCol.rgb, vec3(gamma));

        // Atmospheric perspective — always-on haze near horizon
        float texEl = max(dir.z, 0.0);
        float atmosFade = 1.0 - smoothstep(0.0, 0.18, texEl);
        vec3 hazeCol = uFogDensity > 0.0001 ? uFogColor : vec3(0.72, 0.78, 0.88);
        texRgb = mix(texRgb, hazeCol, atmosFade * 0.45);

        // Horizon fog band — additional fade when fog is enabled
        float texFogBand = 1.0 - smoothstep(0.0, 0.05 + uFogDensity * 50.0, texEl);
        texRgb = mix(texRgb, uFogColor, texFogBand * clamp(uFogDensity * 250.0, 0.0, 0.85));
        fragColor = vec4(texRgb, texCol.a);
        return;
    }

    float el = max(dir.z, 0.0);

    // --- sky gradient (zenith → horizon) — day vs night palette ---
    vec3 zenithDay  = vec3(0.22, 0.40, 0.82);
    vec3 horizonDay = vec3(0.68, 0.80, 0.94);
    vec3 zenithNight  = vec3(0.01, 0.02, 0.06);     // near-black with hint of blue
    vec3 horizonNight = vec3(0.04, 0.06, 0.13);     // deep navy near horizon
    vec3 zenith  = mix(zenithDay,  zenithNight,  uNightMode);
    vec3 horizon = mix(horizonDay, horizonNight, uNightMode);
    vec3 sky = mix(horizon, zenith, pow(el, 0.55));

    // subtle warm band near horizon (atmospheric scatter) — daytime only
    float hBand = exp(-el * 12.0);
    sky += vec3(0.12, 0.06, 0.0) * hBand * (1.0 - uNightMode);

    if (uNightMode > 0.5)
    {
        // --- stars ---
        // Spherical-coord hash grid. For each cell whose hash exceeds the
        // density threshold we render a single point-like star: random
        // position inside the cell + radial Gaussian falloff (core + halo)
        // so it looks like a tiny glowing point instead of a flat cell-sized
        // square. Twinkle phase and colour temperature vary per-star.
        if (uShowStars > 0.5)
        {
            vec2 starUV = vec2(atan(dir.y, dir.x), asin(clamp(dir.z, -1.0, 1.0))) * 80.0;
            vec2 cell  = floor(starUV);
            vec2 frac  = fract(starUV) - 0.5;
            float starHash = hash21(cell);
            if (starHash > 0.965)
            {
                // Randomised position within the cell (kept slightly inset so
                // the star never lands exactly on a cell boundary).
                float hx = hash21(cell + vec2(13.7,  5.1));
                float hy = hash21(cell + vec2(91.3, 47.9));
                vec2  starCenter = (vec2(hx, hy) - 0.5) * 0.7;
                float d = length(frac - starCenter);

                // 0..1 intensity scale across the eligible hash band.
                float starIntensity = (starHash - 0.965) / 0.035;

                // Tight bright core + wider soft glow.
                float coreR = 0.020 + 0.020 * starIntensity;
                float haloR = 0.060 + 0.060 * starIntensity;
                float core  = exp(-(d * d) / (coreR * coreR));
                float halo  = exp(-(d * d) / (haloR * haloR)) * 0.45;

                // Per-star twinkle: phase + frequency vary so stars pulse
                // asynchronously (some fast, some slow).
                float twHash = hash21(cell + vec2(7.3, 2.1));
                float twFreq = 1.2 + 3.5 * twHash;
                float twinkle = 0.45 + 0.55 * sin(uTime * twFreq + starHash * 31.4);

                // Subtle colour temperature variation (warm gold ↔ cool blue).
                vec3 starCol = mix(vec3(1.00, 0.94, 0.82),
                                   vec3(0.82, 0.90, 1.00),
                                   hash21(cell + vec2(3.1, 9.9)));

                float horizonFade = smoothstep(0.0, 0.15, el);
                sky += starCol * (core + halo) * starIntensity * twinkle * horizonFade;
            }
        }

        // --- moon disc + soft halo ---
        vec3 md = normalize(uMoonDir);
        float cosM = dot(dir, md);
        float mDisc = smoothstep(0.9985, 0.9995, cosM);
        float mHalo = pow(max(cosM, 0.0), 96.0) * 0.5
                    + pow(max(cosM, 0.0), 12.0) * 0.10;
        vec3 moonCol = vec3(0.92, 0.94, 1.0);
        sky += moonCol * mDisc;
        sky += moonCol * mHalo;
    }
    else
    {
        // --- sun disc + glow + halo ---
        vec3 sd = normalize(uSunDir);
        float cosA = dot(dir, sd);
        float disc = smoothstep(0.9996, 0.9999, cosA);
        float glow = pow(max(cosA, 0.0), 128.0) * 0.8;
        float halo = pow(max(cosA, 0.0), 12.0) * 0.25;
        vec3 sunCol  = vec3(1.0, 0.97, 0.88);
        vec3 haloCol = vec3(1.0, 0.85, 0.55);
        sky += sunCol * disc + sunCol * glow + haloCol * halo;
    }

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

    // cloud lit side vs shadow. The sd reference above (sun in day) only
    // existed inside the daytime else-branch, so for the cloud-lighting
    // computation we re-derive the active primary light direction here.
    vec3 cloudLightDir = mix(normalize(uSunDir), normalize(uMoonDir), uNightMode);
    float cLit = 0.6 + 0.4 * max(dot(vec3(0, 0, 1), cloudLightDir), 0.0);
    vec3 cloudColDay   = vec3(cLit, cLit, cLit * 0.98);
    vec3 cloudColNight = vec3(0.08, 0.10, 0.16);    // dark greyblue at night
    vec3 cloudCol = mix(cloudColDay, cloudColNight, uNightMode);
    sky = mix(sky, cloudCol, cloud * 0.75 * uShowClouds);

    // Horizon fog band — matches the scene's distance-based fog
    float fogBand = 1.0 - smoothstep(0.0, 0.05 + uFogDensity * 50.0, el);
    sky = mix(sky, uFogColor, fogBand * clamp(uFogDensity * 250.0, 0.0, 0.85));

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
out vec3 vWorldPos;

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
    vWorldPos = pos;
    gl_Position = uMVP * vec4(pos, 1.0);
}
";

        private const string GrassFragBody = @"
in vec4 vCol;
in vec3 vWorldPos;

uniform vec3  uFogColor;
uniform float uFogDensity;
uniform vec3  uCameraPos;

layout(location = 0) out vec4 fragColor;

void main()
{
    fragColor = vCol;

    // Atmospheric fog (exponential squared)
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    fragColor.rgb = mix(uFogColor, fragColor.rgb, clamp(exp(-fd * fd), 0.0, 1.0));
}
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
uniform sampler2D uShadowMap;
uniform mat4 uLightSpaceMat;
uniform float uShadowEnabled;
uniform vec3  uFogColor;
uniform float uFogDensity;
uniform vec3  uCameraPos;

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

    // Shadow
    float shadow = 0.0;
    if (uShadowEnabled > 0.5)
    {
        vec4 lsPos = uLightSpaceMat * vec4(vWorldPos, 1.0);
        vec3 pc = lsPos.xyz / lsPos.w * 0.5 + 0.5;
        if (pc.z <= 1.0 && pc.x >= 0.0 && pc.x <= 1.0 && pc.y >= 0.0 && pc.y <= 1.0)
        {
            float bias = 0.003;
            vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
            for (int x = -1; x <= 1; ++x)
                for (int y = -1; y <= 1; ++y)
                {
                    float d = texture(uShadowMap, pc.xy + vec2(x, y) * texel).r;
                    shadow += pc.z - bias > d ? 1.0 : 0.0;
                }
            shadow /= 9.0;
        }
    }

    // Sun + ambient lighting
    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    vec3 light = uAmbient + uSunColor * diff * (1.0 - shadow * 0.7);

    fragColor = vec4(col * light, 1.0);

    // Atmospheric fog (exponential squared)
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    fragColor.rgb = mix(uFogColor, fragColor.rgb, clamp(exp(-fd * fd), 0.0, 1.0));

    // Soft horizon — over the outer band of the disc (in world XY, measured
    // from origin) blend the ground colour fully into the sky/fog colour so
    // there is no hard rim where the disc geometry ends. Disc radius is
    // 2000 m (see GenerateGroundDisc call); fade band 1300–1980 m gives the
    // user ~1.3 km of fully-opaque ground around the origin.
    float rXY = length(vWorldPos.xy);
    float horizonBand = smoothstep(1300.0, 1980.0, rXY);
    fragColor.rgb = mix(fragColor.rgb, uFogColor, horizonBand);
}
";

        // ── Shadow catcher shader — transparent layer that only shows shadows ──

        private const string ShadowCatcherVertBody = @"
layout(location = 0) in vec3 aPos;

uniform mat4 uMVP;

out vec3 vWorldPos;

void main()
{
    vWorldPos   = aPos;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string ShadowCatcherFragBody = @"
in vec3 vWorldPos;

uniform sampler2D uShadowMap;
uniform mat4  uLightSpaceMat;
uniform float uShadowStrength;
uniform vec3  uCameraPos;
uniform float uFogDensity;

layout(location = 0) out vec4 fragColor;

void main()
{
    vec4 lsPos = uLightSpaceMat * vec4(vWorldPos, 1.0);
    vec3 pc    = lsPos.xyz / lsPos.w * 0.5 + 0.5;

    if (pc.z > 1.0 || pc.x < 0.0 || pc.x > 1.0 || pc.y < 0.0 || pc.y > 1.0)
        discard;

    float bias  = 0.003;
    vec2  texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    float shadow = 0.0;
    for (int x = -1; x <= 1; ++x)
        for (int y = -1; y <= 1; ++y)
        {
            float d = texture(uShadowMap, pc.xy + vec2(x, y) * texel).r;
            shadow += pc.z - bias > d ? 1.0 : 0.0;
        }
    shadow /= 9.0;

    if (shadow < 0.01) discard;

    // Fade shadows with atmospheric fog
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    float fogFactor = clamp(exp(-fd * fd), 0.0, 1.0);

    fragColor = vec4(0.0, 0.0, 0.0, shadow * uShadowStrength * fogFactor);
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
out vec3 vWorldPos;

void main()
{
    vWorldPos = (uModel * vec4(aPos, 1.0)).xyz;
    vNorm = uNormalMat * aNorm;
    vCol  = aCol;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string SolidFragBody = @"
in vec3 vNorm;
in vec4 vCol;
in vec3 vWorldPos;

uniform vec3 uSunDir;
uniform vec3 uSunColor;
uniform vec3 uAmbient;
uniform vec3 uRimDir;
uniform vec3 uRimColor;
uniform float uAlpha;
uniform sampler2D uShadowMap;
uniform mat4 uLightSpaceMat;
uniform float uShadowEnabled;
uniform vec3  uFogColor;
uniform float uFogDensity;
uniform vec3  uCameraPos;

// Up to 8 fire point lights. uFireColor carries the tint pre-multiplied
// by intensity (the C# side does that scaling), and uFireRadius is the
// physical flame radius — beyond ~5R the light is effectively zero.
#define MAX_FIRES 8
uniform int   uFireCount;
uniform vec3  uFirePos[MAX_FIRES];
uniform vec3  uFireColor[MAX_FIRES];
uniform float uFireRadius[MAX_FIRES];
uniform float uFireTime;

layout(location = 0) out vec4 fragColor;

float calcShadow()
{
    if (uShadowEnabled < 0.5) return 0.0;
    vec4 lsPos = uLightSpaceMat * vec4(vWorldPos, 1.0);
    vec3 pc = lsPos.xyz / lsPos.w * 0.5 + 0.5;
    if (pc.z > 1.0 || pc.x < 0.0 || pc.x > 1.0 || pc.y < 0.0 || pc.y > 1.0)
        return 0.0;
    vec3 N = normalize(vNorm);
    float bias = max(0.005 * (1.0 - dot(N, normalize(uSunDir))), 0.001);
    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    for (int x = -1; x <= 1; ++x)
        for (int y = -1; y <= 1; ++y)
        {
            float d = texture(uShadowMap, pc.xy + vec2(x, y) * texel).r;
            shadow += pc.z - bias > d ? 1.0 : 0.0;
        }
    return shadow / 9.0;
}

void main()
{
    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    float rim  = max(dot(N, normalize(uRimDir)), 0.0);
    float shadow = calcShadow();
    vec3 light = uAmbient + (uSunColor * diff + uRimColor * rim) * (1.0 - shadow * 0.7);

    // Fire point lights — Lambert diffuse with 1/(1 + a·d + b·d²) attenuation,
    // a smoothstep falloff that cuts the contribution to zero at ~5R, and a
    // shader-side flicker so each flame pulses asynchronously.
    for (int i = 0; i < uFireCount && i < MAX_FIRES; i++)
    {
        vec3  toFire = uFirePos[i] - vWorldPos;
        float d = length(toFire);
        if (d < 1e-3 || d > uFireRadius[i] * 8.0) continue;
        vec3  L = toFire / d;
        float att = 1.0 / (1.0 + 0.15 * d + 0.04 * d * d);
        att *= smoothstep(uFireRadius[i] * 8.0, uFireRadius[i] * 0.5, d);
        float flick = 0.78 + 0.22 * sin(uFireTime * 28.0 + float(i) * 4.3)
                            * cos(uFireTime * 17.0 + float(i) * 9.1);
        float fdiff = max(dot(N, L), 0.0);
        light += uFireColor[i] * fdiff * att * flick;
    }

    fragColor  = vec4(vCol.rgb * light, vCol.a * uAlpha);

    // Atmospheric fog (exponential squared)
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    fragColor.rgb = mix(uFogColor, fragColor.rgb, clamp(exp(-fd * fd), 0.0, 1.0));
}
";

        // ── Cloud (gas leak) shader ────────────────────────────────────
        // Same vertex layout as the solid shader; the fragment shader uses
        // 3D noise, Fresnel edge softening and subsurface-scatter
        // approximation to make the isosurface look like a real gas cloud.

        private const string CloudFragBody = @"
in vec3 vNorm;
in vec4 vCol;
in vec3 vWorldPos;

uniform vec3  uSunDir;
uniform vec3  uSunColor;
uniform vec3  uAmbient;
uniform float uAlpha;
uniform float uTime;
uniform vec3  uCameraPos;
uniform vec3  uFogColor;
uniform float uFogDensity;

layout(location = 0) out vec4 fragColor;

// ── 3D value noise (Inigo Quilez) ──────────────────────────────
float hash(vec3 p)
{
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float noise3(vec3 x)
{
    vec3 i = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(mix(hash(i),               hash(i+vec3(1,0,0)), f.x),
                   mix(hash(i+vec3(0,1,0)),   hash(i+vec3(1,1,0)), f.x), f.y),
               mix(mix(hash(i+vec3(0,0,1)),   hash(i+vec3(1,0,1)), f.x),
                   mix(hash(i+vec3(0,1,1)),   hash(i+vec3(1,1,1)), f.x), f.y), f.z);
}

float fbm(vec3 p)
{
    float f  = 0.5000 * noise3(p); p *= 2.01;
          f += 0.2500 * noise3(p); p *= 2.02;
          f += 0.1250 * noise3(p); p *= 2.03;
          f += 0.0625 * noise3(p);
    return f / 0.9375;
}

void main()
{
    vec3 N = normalize(vNorm);
    vec3 V = normalize(uCameraPos - vWorldPos);

    // ── Fresnel edge softening — edges fade to transparent ─────
    float NdotV = abs(dot(N, V));
    float fresnel = 1.0 - NdotV;
    float edgeFade = smoothstep(0.0, 0.45, NdotV);

    // ── 3D turbulent noise — slow drift with time ─────────────
    vec3 np = vWorldPos * 0.35 + vec3(uTime * 0.12, uTime * 0.05, uTime * 0.08);
    float n = fbm(np);

    // Higher-frequency detail layer
    float detail = noise3(vWorldPos * 1.2 + vec3(uTime * 0.2, 0.0, uTime * 0.15));

    // Edge-aware noise: noise cuts harder near silhouette edges
    float edgeNoise = mix(n * 0.6, n, edgeFade);

    // ── Cloud alpha: dense center, wispy dissolving edges ─────
    float baseAlpha = vCol.a * uAlpha;
    float cloudAlpha = baseAlpha
        * edgeFade                    // smooth silhouette fade
        * (0.3 + edgeNoise * 0.7)    // turbulence (stronger at edges)
        * (0.7 + detail * 0.3);      // fine detail
    cloudAlpha = clamp(cloudAlpha, 0.0, 1.0);

    // ── Soft lighting with subsurface scatter approximation ───
    float diff = max(dot(N, normalize(uSunDir)), 0.0) * 0.35;
    // Forward scatter: light passing through the cloud
    float scatter = pow(max(dot(-V, normalize(uSunDir)), 0.0), 3.0) * 0.25;
    // Wrap lighting for softer shadows
    float wrap = (dot(N, normalize(uSunDir)) + 1.0) * 0.25;

    vec3 light = uAmbient * 1.3 + uSunColor * (diff + scatter + wrap);
    vec3 col = vCol.rgb * light;

    fragColor = vec4(col, cloudAlpha);

    // Atmospheric fog
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    fragColor.rgb = mix(uFogColor, fragColor.rgb, clamp(exp(-fd * fd), 0.0, 1.0));
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
out vec3 vWorldPos;

void main()
{
    vWorldPos = (uModel * vec4(aPos, 1.0)).xyz;
    vNorm = uNormalMat * aNorm;
    vUV   = aUV;
    gl_Position = uMVP * vec4(aPos, 1.0);
}
";

        private const string TexFragBody = @"
in vec3 vNorm;
in vec2 vUV;
in vec3 vWorldPos;

uniform sampler2D uTexture;
uniform vec4  uTint;
uniform vec3  uSunDir;
uniform vec3  uSunColor;
uniform vec3  uAmbient;
uniform float uAlpha;
uniform sampler2D uShadowMap;
uniform mat4 uLightSpaceMat;
uniform float uShadowEnabled;
uniform vec3  uFogColor;
uniform float uFogDensity;
uniform vec3  uCameraPos;

layout(location = 0) out vec4 fragColor;

float calcShadow()
{
    if (uShadowEnabled < 0.5) return 0.0;
    vec4 lsPos = uLightSpaceMat * vec4(vWorldPos, 1.0);
    vec3 pc = lsPos.xyz / lsPos.w * 0.5 + 0.5;
    if (pc.z > 1.0 || pc.x < 0.0 || pc.x > 1.0 || pc.y < 0.0 || pc.y > 1.0)
        return 0.0;
    vec3 N = normalize(vNorm);
    float bias = max(0.005 * (1.0 - dot(N, normalize(uSunDir))), 0.001);
    float shadow = 0.0;
    vec2 texel = 1.0 / vec2(textureSize(uShadowMap, 0));
    for (int x = -1; x <= 1; ++x)
        for (int y = -1; y <= 1; ++y)
        {
            float d = texture(uShadowMap, pc.xy + vec2(x, y) * texel).r;
            shadow += pc.z - bias > d ? 1.0 : 0.0;
        }
    return shadow / 9.0;
}

void main()
{
    vec4 texel = texture(uTexture, vUV);
    if (texel.a < 0.1) discard;

    vec3 N    = normalize(vNorm);
    float diff = max(dot(N, normalize(uSunDir)), 0.0);
    float shadow = calcShadow();
    vec3 light = uAmbient + uSunColor * diff * (1.0 - shadow * 0.7);

    vec3 col = texel.rgb * uTint.rgb * light;
    fragColor = vec4(col, texel.a * uTint.a * uAlpha);

    // Atmospheric fog (exponential squared)
    float fogDist = length(vWorldPos - uCameraPos);
    float fd = uFogDensity * fogDist;
    fragColor.rgb = mix(uFogColor, fragColor.rgb, clamp(exp(-fd * fd), 0.0, 1.0));
}
";

        // ── Shadow depth shader ─────────────────────────────────────────

        private const string ShadowVertBody = @"
layout(location = 0) in vec3 aPos;
uniform mat4 uLightVP;
uniform mat4 uModel;
void main() { gl_Position = uLightVP * uModel * vec4(aPos, 1.0); }
";

        private const string ShadowFragBody = @"
void main() { }
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

        /// <summary>
        /// Rebuild only environment geometry (sky dome, ground, grass) without
        /// touching scene objects. Use when EnvironmentSettings change — avoids
        /// destroying views/simulations/dispersion playback.
        /// </summary>
        public void RefreshEnvironment()
        {
            _envNeedsRebuild = true;
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
                var p1d = gl.GetProcAddress("glUniform3fv");
                if (p1d != IntPtr.Zero)
                    _glUniform3fv = Marshal.GetDelegateForFunctionPointer<D_glUniform3fv>(p1d);
                var p1e = gl.GetProcAddress("glUniform1fv");
                if (p1e != IntPtr.Zero)
                    _glUniform1fv = Marshal.GetDelegateForFunctionPointer<D_glUniform1fv>(p1e);
                var p2 = gl.GetProcAddress("glBlendFunc");
                if (p2 != IntPtr.Zero)
                    _glBlendFunc = Marshal.GetDelegateForFunctionPointer<D_glBlendFunc>(p2);
                var p3 = gl.GetProcAddress("glCullFace");
                if (p3 != IntPtr.Zero)
                    _glCullFace = Marshal.GetDelegateForFunctionPointer<D_glCullFace>(p3);
                var pPO = gl.GetProcAddress("glPolygonOffset");
                if (pPO != IntPtr.Zero)
                    _glPolygonOffset = Marshal.GetDelegateForFunctionPointer<D_glPolygonOffset>(pPO);
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

                // ── Load shadow mapping GL extensions ──────────────────
                LoadProc(gl, "glGenFramebuffers", out _glGenFramebuffers);
                LoadProc(gl, "glDeleteFramebuffers", out _glDeleteFramebuffers);
                LoadProc(gl, "glFramebufferTexture2D", out _glFramebufferTexture2D);
                LoadProc(gl, "glDrawBuffer", out _glDrawBuffer);
                LoadProc(gl, "glCheckFramebufferStatus", out _glCheckFramebufferStatus);
                LoadProc(gl, "glTexImage2D", out _glTexImage2D);
                LoadProc(gl, "glTexParameteri", out _glTexParameteri);
                LoadProc(gl, "glTexParameterfv", out _glTexParameterfv);

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
                _solidFireCountUniform  = GetUniformLoc(gl, _solidProgram, "uFireCount");
                _solidFirePosUniform    = GetUniformLoc(gl, _solidProgram, "uFirePos");
                _solidFireColorUniform  = GetUniformLoc(gl, _solidProgram, "uFireColor");
                _solidFireRadiusUniform = GetUniformLoc(gl, _solidProgram, "uFireRadius");
                _solidFireTimeUniform   = GetUniformLoc(gl, _solidProgram, "uFireTime");

                // ── Compile cloud (gas leak) shader ─────────────────────
                _cloudProgram          = CompileProgram(gl, SolidVertBody, CloudFragBody);
                _cloudMvpUniform       = GetUniformLoc(gl, _cloudProgram, "uMVP");
                _cloudModelUniform     = GetUniformLoc(gl, _cloudProgram, "uModel");
                _cloudNormalMatUniform = GetUniformLoc(gl, _cloudProgram, "uNormalMat");
                _cloudSunDirUniform    = GetUniformLoc(gl, _cloudProgram, "uSunDir");
                _cloudSunColorUniform  = GetUniformLoc(gl, _cloudProgram, "uSunColor");
                _cloudAmbientUniform   = GetUniformLoc(gl, _cloudProgram, "uAmbient");
                _cloudAlphaUniform     = GetUniformLoc(gl, _cloudProgram, "uAlpha");
                _cloudTimeUniform      = GetUniformLoc(gl, _cloudProgram, "uTime");
                _cloudFogColorUniform  = GetUniformLoc(gl, _cloudProgram, "uFogColor");
                _cloudFogDensityUniform = GetUniformLoc(gl, _cloudProgram, "uFogDensity");
                _cloudCameraPosUniform = GetUniformLoc(gl, _cloudProgram, "uCameraPos");

                // ── Compile sky shader ──────────────────────────────────
                _skyProgram          = CompileProgram(gl, SkyVertBody, SkyFragBody);
                _skyMvpUniform       = GetUniformLoc(gl, _skyProgram, "uMVP");
                _skySunDirUniform    = GetUniformLoc(gl, _skyProgram, "uSunDir");
                _skyTimeUniform      = GetUniformLoc(gl, _skyProgram, "uTime");
                _skyShowCloudsUniform = GetUniformLoc(gl, _skyProgram, "uShowClouds");
                _skyCloudSpeedUniform = GetUniformLoc(gl, _skyProgram, "uCloudSpeed");
                _skyUseSkyTextureUniform = GetUniformLoc(gl, _skyProgram, "uUseSkyTexture");
                _skySkyTextureUniform = GetUniformLoc(gl, _skyProgram, "uSkyTexture");
                _skyBrightnessUniform = GetUniformLoc(gl, _skyProgram, "uSkyBrightness");
                _skyVOffsetUniform = GetUniformLoc(gl, _skyProgram, "uSkyVOffset");

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

                // ── Shadow uniforms in lit shaders ─────────────────────
                _solidShadowMapUniform     = GetUniformLoc(gl, _solidProgram, "uShadowMap");
                _solidLightSpaceUniform    = GetUniformLoc(gl, _solidProgram, "uLightSpaceMat");
                _solidShadowEnabledUniform = GetUniformLoc(gl, _solidProgram, "uShadowEnabled");

                _groundShadowMapUniform     = GetUniformLoc(gl, _groundProgram, "uShadowMap");
                _groundLightSpaceUniform    = GetUniformLoc(gl, _groundProgram, "uLightSpaceMat");
                _groundShadowEnabledUniform = GetUniformLoc(gl, _groundProgram, "uShadowEnabled");

                _texShadowMapUniform     = GetUniformLoc(gl, _texProgram, "uShadowMap");
                _texLightSpaceUniform    = GetUniformLoc(gl, _texProgram, "uLightSpaceMat");
                _texShadowEnabledUniform = GetUniformLoc(gl, _texProgram, "uShadowEnabled");

                // ── Fog uniforms ───────────────────────────────────────
                _groundFogColorUniform   = GetUniformLoc(gl, _groundProgram, "uFogColor");
                _groundFogDensityUniform = GetUniformLoc(gl, _groundProgram, "uFogDensity");
                _groundCameraPosUniform  = GetUniformLoc(gl, _groundProgram, "uCameraPos");

                _solidFogColorUniform   = GetUniformLoc(gl, _solidProgram, "uFogColor");
                _solidFogDensityUniform = GetUniformLoc(gl, _solidProgram, "uFogDensity");
                _solidCameraPosUniform  = GetUniformLoc(gl, _solidProgram, "uCameraPos");

                _texFogColorUniform   = GetUniformLoc(gl, _texProgram, "uFogColor");
                _texFogDensityUniform = GetUniformLoc(gl, _texProgram, "uFogDensity");
                _texCameraPosUniform  = GetUniformLoc(gl, _texProgram, "uCameraPos");

                _lineFogColorUniform   = GetUniformLoc(gl, _lineProgram, "uFogColor");
                _lineFogDensityUniform = GetUniformLoc(gl, _lineProgram, "uFogDensity");
                _lineCameraPosUniform  = GetUniformLoc(gl, _lineProgram, "uCameraPos");

                _grassFogColorUniform   = GetUniformLoc(gl, _grassProgram, "uFogColor");
                _grassFogDensityUniform = GetUniformLoc(gl, _grassProgram, "uFogDensity");
                _grassCameraPosUniform  = GetUniformLoc(gl, _grassProgram, "uCameraPos");

                _skyFogColorUniform   = GetUniformLoc(gl, _skyProgram, "uFogColor");
                _skyFogDensityUniform = GetUniformLoc(gl, _skyProgram, "uFogDensity");
                _skyNightModeUniform  = GetUniformLoc(gl, _skyProgram, "uNightMode");
                _skyShowStarsUniform  = GetUniformLoc(gl, _skyProgram, "uShowStars");
                _skyMoonDirUniform    = GetUniformLoc(gl, _skyProgram, "uMoonDir");

                // ── Compile shadow depth shader + create FBO ────────────
                _shadowProgram = CompileProgram(gl, ShadowVertBody, ShadowFragBody);
                _shadowLightVPUniform = GetUniformLoc(gl, _shadowProgram, "uLightVP");
                _shadowModelUniform   = GetUniformLoc(gl, _shadowProgram, "uModel");
                CreateShadowFbo(gl);

                // ── Compile shadow catcher shader ─────────────────────
                _shadowCatcherProgram = CompileProgram(gl, ShadowCatcherVertBody, ShadowCatcherFragBody);
                _scMvpUniform         = GetUniformLoc(gl, _shadowCatcherProgram, "uMVP");
                _scShadowMapUniform   = GetUniformLoc(gl, _shadowCatcherProgram, "uShadowMap");
                _scLightSpaceUniform  = GetUniformLoc(gl, _shadowCatcherProgram, "uLightSpaceMat");
                _scStrengthUniform    = GetUniformLoc(gl, _shadowCatcherProgram, "uShadowStrength");
                _scCameraPosUniform   = GetUniformLoc(gl, _shadowCatcherProgram, "uCameraPos");
                _scFogDensityUniform  = GetUniformLoc(gl, _shadowCatcherProgram, "uFogDensity");

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
                _envNeedsRebuild = false;
                RebuildSceneObjects(gl, _pendingScene);
                _pendingScene = null;
            }
            else if (_envNeedsRebuild)
            {
                _envNeedsRebuild = false;
                RebuildEnvironmentGeometry(gl);
            }

            // ── Deferred dispersion frame update (playback) ────────────
            if (_pendingDispersionFrame != null)
            {
                var req = _pendingDispersionFrame;
                _pendingDispersionFrame = null;
                ApplyDispersionFrame(gl, req);
            }

            // ── Deferred draw-mode preview upload ───────────────────────
            if (_pendingDrawPreview != null)
                ApplyPendingDrawPreview(gl);

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
            // True sun direction (always — used for shader sky disc + cloud
            // lighting fallback even at night, since `sun` is what we hand
            // to the sky shader as the daytime disc position).
            float sunTrueX = cosEl * MathF.Sin(azRad);
            float sunTrueY = cosEl * MathF.Cos(azRad);
            float sunTrueZ = MathF.Sin(elRad);

            // Moon direction
            float moonAzRad = (float)(env.MoonAzimuthDeg * Math.PI / 180.0);
            float moonElRad = (float)(env.MoonElevationDeg * Math.PI / 180.0);
            float moonCosEl = MathF.Cos(moonElRad);
            float moonX = moonCosEl * MathF.Sin(moonAzRad);
            float moonY = moonCosEl * MathF.Cos(moonAzRad);
            float moonZ = MathF.Sin(moonElRad);

            // Decide night vs day. Manual NightMode flag wins; otherwise
            // auto-night when the solar clock is on and the sun is below the
            // horizon by more than 2° (gives a brief twilight band rather
            // than snapping at exactly the horizon).
            bool autoNight = env.UseSolarClock && sunEl < -2.0;
            bool isNight = env.NightMode || autoNight;

            // Primary light direction passed to all lit shaders. In night
            // mode the moon acts as the directional light.
            float sunX = isNight ? moonX : sunTrueX;
            float sunY = isNight ? moonY : sunTrueY;
            float sunZ = isNight ? moonZ : sunTrueZ;
            float primElRad = isNight ? moonElRad : elRad;

            float sunI   = (float)(isNight ? env.MoonIntensity : env.SunIntensity);
            float ambI   = (float)env.AmbientIntensity * (isNight ? 0.3f : 1.0f);

            // UseSunLighting only gates the daytime Sun. At night the Moon is
            // the primary directional light and is governed by NightMode /
            // MoonIntensity, independent of UseSunLighting — otherwise turning
            // off Sun would silently kill the Moon too, which is surprising
            // (Sun and Moon are mutually-exclusive light sources, not coupled).
            if (!env.UseSunLighting && !isNight)
            {
                sunI = 0f;
                ambI = 1f;
            }

            // Light tint: warm for sun (more orange at low elevation), cool
            // blue-white for moon.
            float warmR, warmG, warmB;
            if (isNight)
            {
                warmR = 0.70f;
                warmG = 0.78f;
                warmB = 1.00f;
            }
            else
            {
                warmR = 1.0f;
                warmG = 0.85f + 0.15f * MathF.Min(1f, primElRad / 1.0f);
                warmB = 0.70f + 0.30f * MathF.Min(1f, primElRad / 1.0f);
            }

            // Fog parameters — derive colour from sky horizon for a seamless
            // blend at the horizon line. At night the sky shader hard-codes a
            // deep-navy horizon (vec3(0.04, 0.06, 0.13)); using the day-time
            // SkyHorizonColor in that case left a bright fog band against the
            // dark sky, visible as a seam. Match the shader's night palette
            // when isNight so ground/fog/sky line up.
            float fogDensity = env.FogEnabled ? (float)env.FogDensity : 0f;
            float fogR, fogG, fogB;
            if (isNight)
            {
                fogR = 0.04f;
                fogG = 0.06f;
                fogB = 0.13f;
            }
            else
            {
                var hc = env.SkyHorizonColor;
                fogR = hc.R / 255f;
                fogG = hc.G / 255f;
                fogB = hc.B / 255f;
            }
            var camPos = _camera.Eye;

            // ── 0. Shadow depth pass ────────────────────────────────────
            bool doShadows = env.ShadowsEnabled && env.UseSunLighting &&
                             _shadowReady && _shadowProgram != 0 && sunZ > 0.01f;
            Matrix4x4 lightSpaceMat = Matrix4x4.Identity;
            if (doShadows)
            {
                var lightDir = new Vector3(sunX, sunY, sunZ);

                // Compute world-space AABB of shadow-casting objects
                var bbMin = new Vector3(float.MaxValue);
                var bbMax = new Vector3(float.MinValue);
                bool hasCasters = false;
                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible || obj.Mesh.IsLineGeometry) continue;
                    var t = obj.ModelMatrix.Translation;
                    float radius = 5f;
                    if (obj.Mesh.CpuVertices != null && obj.Mesh.CpuVertices.Length > 0)
                    {
                        var localMin = new Vector3(float.MaxValue);
                        var localMax = new Vector3(float.MinValue);
                        foreach (var v in obj.Mesh.CpuVertices)
                        {
                            localMin = Vector3.Min(localMin, v.Position);
                            localMax = Vector3.Max(localMax, v.Position);
                        }
                        var worldMin = Vector3.Transform(localMin, obj.ModelMatrix);
                        var worldMax = Vector3.Transform(localMax, obj.ModelMatrix);
                        bbMin = Vector3.Min(bbMin, Vector3.Min(worldMin, worldMax));
                        bbMax = Vector3.Max(bbMax, Vector3.Max(worldMin, worldMax));
                    }
                    else if (obj.Mesh.CpuTexturedVertices != null && obj.Mesh.CpuTexturedVertices.Length > 0)
                    {
                        var localMin = new Vector3(float.MaxValue);
                        var localMax = new Vector3(float.MinValue);
                        foreach (var v in obj.Mesh.CpuTexturedVertices)
                        {
                            localMin = Vector3.Min(localMin, v.Position);
                            localMax = Vector3.Max(localMax, v.Position);
                        }
                        var worldMin = Vector3.Transform(localMin, obj.ModelMatrix);
                        var worldMax = Vector3.Transform(localMax, obj.ModelMatrix);
                        bbMin = Vector3.Min(bbMin, Vector3.Min(worldMin, worldMax));
                        bbMax = Vector3.Max(bbMax, Vector3.Max(worldMin, worldMax));
                    }
                    else
                    {
                        bbMin = Vector3.Min(bbMin, t - new Vector3(radius));
                        bbMax = Vector3.Max(bbMax, t + new Vector3(radius));
                    }
                    hasCasters = true;
                }

                if (!hasCasters)
                {
                    bbMin = new Vector3(-50f);
                    bbMax = new Vector3(50f);
                }

                // Include ground plane (Z ≈ −0.02) so the shadow frustum
                // covers the surface that receives cast shadows.
                bbMin = new Vector3(bbMin.X, bbMin.Y, MathF.Min(bbMin.Z, -0.1f));

                // Project the 4 top AABB corners along the light direction
                // onto the ground (Z = 0) to include the shadow footprint.
                // Without this, low-sun shadows fall far from the objects
                // and outside the shadow frustum.
                if (lightDir.Z > 0.01f)
                {
                    float topZ = MathF.Max(bbMax.Z, 0.1f);
                    float maxT = MathF.Min(topZ / lightDir.Z, 400f);
                    for (int cx = 0; cx < 2; cx++)
                        for (int cy = 0; cy < 2; cy++)
                        {
                            float px = cx == 0 ? bbMin.X : bbMax.X;
                            float py = cy == 0 ? bbMin.Y : bbMax.Y;
                            float sx = px - lightDir.X * maxT;
                            float sy = py - lightDir.Y * maxT;
                            bbMin = Vector3.Min(bbMin, new Vector3(sx, sy, bbMin.Z));
                            bbMax = Vector3.Max(bbMax, new Vector3(sx, sy, bbMax.Z));
                        }
                }

                var sceneCenter = (bbMin + bbMax) * 0.5f;
                var sceneExtent = bbMax - bbMin;
                float sceneRadius = sceneExtent.Length() * 0.5f;

                // Place light on the sun's side of the scene so depth
                // ordering is correct: objects near the sun get small depth
                // values and the ground behind them gets large values.
                var up = MathF.Abs(sunZ) > 0.99f ? Vector3.UnitY : Vector3.UnitZ;
                var lightView = Matrix4x4.CreateLookAt(
                    sceneCenter + lightDir * (sceneRadius + 10f), sceneCenter, up);

                // Transform all 8 AABB corners into light-view space to get a
                // tight frustum that covers the actual scene content.
                var lvMin = new Vector3(float.MaxValue);
                var lvMax = new Vector3(float.MinValue);
                for (int c = 0; c < 8; c++)
                {
                    var corner = new Vector3(
                        (c & 1) == 0 ? bbMin.X : bbMax.X,
                        (c & 2) == 0 ? bbMin.Y : bbMax.Y,
                        (c & 4) == 0 ? bbMin.Z : bbMax.Z);
                    var lv = Vector3.Transform(corner, lightView);
                    lvMin = Vector3.Min(lvMin, lv);
                    lvMax = Vector3.Max(lvMax, lv);
                }

                float margin = 10f;
                float lLeft   = lvMin.X - margin;
                float lRight  = lvMax.X + margin;
                float lBottom = lvMin.Y - margin;
                float lTop    = lvMax.Y + margin;
                float lNear   = -lvMax.Z - margin;
                float lFar    = -lvMin.Z + margin;
                lNear = MathF.Max(lNear, 0.1f);
                if (lFar <= lNear) lFar = lNear + 100f;

                // OpenGL-convention ortho: maps Z to [-1, 1] so the full depth
                // buffer range is used and the shader's *0.5+0.5 works directly.
                float w2 = lRight - lLeft;
                float h2 = lTop - lBottom;
                float d2 = lFar - lNear;
                var lightProj = new Matrix4x4(
                    2f / w2, 0, 0, 0,
                    0, 2f / h2, 0, 0,
                    0, 0, -2f / d2, 0,
                    -(lRight + lLeft) / w2, -(lTop + lBottom) / h2, -(lFar + lNear) / d2, 1f);
                lightSpaceMat = lightView * lightProj;

                gl.BindFramebuffer(GL_FRAMEBUFFER, _shadowFbo);
                gl.Viewport(0, 0, SHADOW_MAP_SIZE, SHADOW_MAP_SIZE);
                gl.Clear(GL_DEPTH_BUFFER_BIT);
                gl.Enable(GL_DEPTH_TEST);
                gl.DepthFunc(GL_LEQUAL);
                gl.DepthMask(1);
                gl.Disable(GL_CULL_FACE);
                gl.Enable(0x8037); // GL_POLYGON_OFFSET_FILL
                _glPolygonOffset?.Invoke(2.0f, 4.0f);

                gl.UseProgram(_shadowProgram);
                SetMatrixUniform(gl, _shadowLightVPUniform, lightSpaceMat);

                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible || obj.Mesh.IsLineGeometry) continue;
                    SetMatrixUniform(gl, _shadowModelUniform, obj.ModelMatrix);
                    obj.Mesh.Draw(gl);
                }

                gl.UseProgram(0);
                gl.Disable(0x8037); // GL_POLYGON_OFFSET_FILL

                // Restore Avalonia's framebuffer and viewport
                gl.BindFramebuffer(GL_FRAMEBUFFER, fb);
                gl.Viewport(0, 0, w, h);
            }

            // ── 1. Sky dome (procedural clouds + sun, no depth write) ───
            if (env.SkydomeEnabled && _skyDomeMesh != null && _skyProgram != 0)
            {
                gl.Disable(GL_DEPTH_TEST);
                gl.DepthMask(0);

                gl.UseProgram(_skyProgram);

                float timeSky = (float)_animClock.Elapsed.TotalSeconds;
                // Sky shader gets the TRUE sun direction so the sun disc /
                // halo render at the real position even at night (when the
                // sun is below the horizon you simply won't see them);
                // moon direction goes in a separate uniform.
                _glUniform3f?.Invoke(_skySunDirUniform, sunTrueX, sunTrueY, sunTrueZ);
                _glUniform3f?.Invoke(_skyMoonDirUniform, moonX, moonY, moonZ);
                _glUniform1f?.Invoke(_skyNightModeUniform, isNight ? 1f : 0f);
                _glUniform1f?.Invoke(_skyShowStarsUniform, env.ShowStars ? 1f : 0f);
                _glUniform1f?.Invoke(_skyTimeUniform, timeSky);
                _glUniform1f?.Invoke(_skyShowCloudsUniform, env.ShowClouds ? 1f : 0f);
                _glUniform1f?.Invoke(_skyCloudSpeedUniform, (float)env.CloudSpeed);
                _glUniform3f?.Invoke(_skyFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_skyFogDensityUniform, fogDensity);

                bool useSkyTex = _skyTextureId != 0;
                _glUniform1f?.Invoke(_skyUseSkyTextureUniform, useSkyTex ? 1f : 0f);
                _glUniform1f?.Invoke(_skyBrightnessUniform, (float)env.SkyTextureBrightness);
                _glUniform1f?.Invoke(_skyVOffsetUniform, (float)env.SkyTextureVOffset);
                if (useSkyTex)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _skyTextureId);
                    _glUniform1i?.Invoke(_skySkyTextureUniform, 0);
                }

                var skyModel = Matrix4x4.CreateTranslation(camPos.X, camPos.Y, 0f);
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

                // Fog
                _glUniform3f?.Invoke(_groundFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_groundFogDensityUniform, fogDensity);
                _glUniform3f?.Invoke(_groundCameraPosUniform, camPos.X, camPos.Y, camPos.Z);

                // Shadow map
                _glUniform1f?.Invoke(_groundShadowEnabledUniform, doShadows ? 1f : 0f);
                if (doShadows)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE1);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _shadowDepthTex);
                    _glUniform1i?.Invoke(_groundShadowMapUniform, 1);
                    SetMatrixUniform(gl, _groundLightSpaceUniform, lightSpaceMat);
                }

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
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                }
                if (doShadows)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE1);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                }

                gl.UseProgram(0);
            }

            // ── 2b. Shadow catcher — transparent overlay that projects shadows ─
            if (doShadows && _shadowCatcherMesh != null && _shadowCatcherProgram != 0)
            {
                gl.UseProgram(_shadowCatcherProgram);
                SetMatrixUniform(gl, _scMvpUniform, mvp);
                SetMatrixUniform(gl, _scLightSpaceUniform, lightSpaceMat);
                _glUniform1f?.Invoke(_scStrengthUniform, 0.55f);
                _glUniform3f?.Invoke(_scCameraPosUniform, camPos.X, camPos.Y, camPos.Z);
                _glUniform1f?.Invoke(_scFogDensityUniform, fogDensity);

                _glActiveTexture?.Invoke(GL_TEXTURE0);
                _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _shadowDepthTex);
                _glUniform1i?.Invoke(_scShadowMapUniform, 0);

                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                gl.DepthMask(0);
                gl.Disable(GL_CULL_FACE);

                _shadowCatcherMesh.Draw(gl);

                gl.DepthMask(1);
                gl.Disable(GL_BLEND);
                _glActiveTexture?.Invoke(GL_TEXTURE0);
                _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                gl.UseProgram(0);
            }

            // ── 3. Grid overlay ─────────────────────────────────────────
            if (_groundShowGridOverlay)
            {
                gl.UseProgram(_lineProgram);
                _glUniform1f?.Invoke(_lineTimeUniform, 0f);
                _glUniform1f?.Invoke(_lineAnimScaleUniform, 0f);
                _glUniform3f?.Invoke(_lineFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_lineFogDensityUniform, fogDensity);
                _glUniform3f?.Invoke(_lineCameraPosUniform, camPos.X, camPos.Y, camPos.Z);
                SetMatrixUniform(gl, _mvpUniform, mvp);
                _grid.Render(gl);
            }

            // ── 3b. Animated grass blades ───────────────────────────────
            if (_grassMesh != null && _grassProgram != 0)
            {
                gl.UseProgram(_grassProgram);

                float grassTime = (float)_animClock.Elapsed.TotalSeconds;
                _glUniform1f?.Invoke(_grassTimeUniform, grassTime);
                _glUniform3f?.Invoke(_grassWindDirUniform, 0.7f, 0.7f, 0f);
                _glUniform3f?.Invoke(_grassFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_grassFogDensityUniform, fogDensity);
                _glUniform3f?.Invoke(_grassCameraPosUniform, camPos.X, camPos.Y, camPos.Z);
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

                // Fog
                _glUniform3f?.Invoke(_solidFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_solidFogDensityUniform, fogDensity);
                _glUniform3f?.Invoke(_solidCameraPosUniform, camPos.X, camPos.Y, camPos.Z);

                // Shadow map
                _glUniform1f?.Invoke(_solidShadowEnabledUniform, doShadows ? 1f : 0f);
                if (doShadows)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE1);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _shadowDepthTex);
                    _glUniform1i?.Invoke(_solidShadowMapUniform, 1);
                    SetMatrixUniform(gl, _solidLightSpaceUniform, lightSpaceMat);
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                }

                // Fire point lights — gather up to 8 from the active scene's
                // FireScenario and feed them as packed float arrays. The
                // shader does the per-fragment falloff + flicker.
                BindFireLights(gl, _solidFireCountUniform, _solidFirePosUniform,
                    _solidFireColorUniform, _solidFireRadiusUniform, _solidFireTimeUniform);

                gl.Enable(GL_CULL_FACE);
                _glCullFace?.Invoke(GL_BACK);

                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);

                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible || obj.Mesh.IsLineGeometry || obj.Mesh.IsTextured) continue;
                    if (obj.UseCloudShader) continue; // rendered in the cloud pass below

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

                    // Disable fog for scientific visualizations (isosurfaces, contour planes)
                    _glUniform1f?.Invoke(_solidFogDensityUniform,
                        isViewObj ? 0f : fogDensity);

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

                if (doShadows)
                {
                    _glActiveTexture?.Invoke(GL_TEXTURE1);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                    _glActiveTexture?.Invoke(GL_TEXTURE0);
                }

                gl.Disable(GL_CULL_FACE);
                gl.Disable(GL_BLEND);
            }

            // ── 4a. Cloud (gas leak) objects ─────────────────────────────
            bool hasCloudObjects = false;
            foreach (var obj in _sceneObjects)
            {
                if (obj.Visible && obj.UseCloudShader && !obj.Mesh.IsLineGeometry && !obj.Mesh.IsTextured)
                { hasCloudObjects = true; break; }
            }
            if (hasCloudObjects && _cloudProgram != 0)
            {
                gl.UseProgram(_cloudProgram);

                float cloudTime = (float)_animClock.Elapsed.TotalSeconds;
                _glUniform3f?.Invoke(_cloudSunDirUniform, sunX, sunY, sunZ);
                _glUniform3f?.Invoke(_cloudSunColorUniform,
                    warmR * sunI, warmG * sunI, warmB * sunI);
                _glUniform3f?.Invoke(_cloudAmbientUniform,
                    0.431f * ambI, 0.490f * ambI, 0.588f * ambI);
                _glUniform1f?.Invoke(_cloudTimeUniform, cloudTime);
                _glUniform3f?.Invoke(_cloudCameraPosUniform, camPos.X, camPos.Y, camPos.Z);
                _glUniform3f?.Invoke(_cloudFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_cloudFogDensityUniform, 0f);

                // Transparent-isosurface render state. We deliberately *keep*
                // depth TESTING on so opaque geometry in front still occludes
                // cloud fragments, but turn depth WRITES off so successive
                // cloud layers blend together instead of the first one
                // killing all subsequent ones via the z-buffer. Two-sided
                // (no cull) so the back face of each shell is visible from
                // inside. The clouds are then drawn back-to-front by their
                // bbox centroid distance from the camera — this is the same
                // ordering HelixToolkit applies automatically in the WPF
                // path, and is what makes nested 100 % / 60 % / 20 % LFL
                // surfaces composite correctly.
                gl.Disable(GL_CULL_FACE);
                gl.Enable(GL_BLEND);
                _glBlendFunc?.Invoke(GL_SRC_ALPHA, GL_ONE_MINUS_SRC_ALPHA);
                gl.DepthMask(0);

                // Gather visible cloud objects, compute world-space centroid
                // for each (cached on the SceneObject), sort by descending
                // camera distance so the FAR layers paint first and the
                // NEAR layers blend over them.
                var cloudObjs = new System.Collections.Generic.List<SceneObject>();
                foreach (var obj in _sceneObjects)
                {
                    if (!obj.Visible || !obj.UseCloudShader || obj.Mesh.IsLineGeometry || obj.Mesh.IsTextured)
                        continue;
                    cloudObjs.Add(obj);
                }
                cloudObjs.Sort((a, b) =>
                {
                    var ca = ComputeWorldCentroid(a);
                    var cb = ComputeWorldCentroid(b);
                    float da = Vector3.DistanceSquared(ca, camPos);
                    float db = Vector3.DistanceSquared(cb, camPos);
                    return db.CompareTo(da); // descending: far first
                });

                foreach (var obj in cloudObjs)
                {
                    var model = obj.ModelMatrix;
                    _glUniform1f?.Invoke(_cloudAlphaUniform, 1.0f);

                    var objMvp = model * view * proj;
                    SetMatrixUniform(gl, _cloudMvpUniform, objMvp);
                    SetMatrixUniform(gl, _cloudModelUniform, model);

                    if (Matrix4x4.Invert(model, out var inv))
                    {
                        var normalMat = Matrix4x4.Transpose(inv);
                        SetMatrix3Uniform(gl, _cloudNormalMatUniform, normalMat);
                    }

                    obj.Mesh.Draw(gl);
                }

                gl.DepthMask(1);
                gl.Disable(GL_BLEND);
                gl.UseProgram(0);
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

                    // Fog
                    _glUniform3f?.Invoke(_texFogColorUniform, fogR, fogG, fogB);
                    _glUniform1f?.Invoke(_texFogDensityUniform, fogDensity);
                    _glUniform3f?.Invoke(_texCameraPosUniform, camPos.X, camPos.Y, camPos.Z);

                    // Shadow map
                    _glUniform1f?.Invoke(_texShadowEnabledUniform, doShadows ? 1f : 0f);
                    if (doShadows)
                    {
                        _glActiveTexture?.Invoke(GL_TEXTURE1);
                        _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _shadowDepthTex);
                        _glUniform1i?.Invoke(_texShadowMapUniform, 1);
                        SetMatrixUniform(gl, _texLightSpaceUniform, lightSpaceMat);
                        _glActiveTexture?.Invoke(GL_TEXTURE0);
                    }

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
                    if (doShadows)
                    {
                        _glActiveTexture?.Invoke(GL_TEXTURE1);
                        _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                        _glActiveTexture?.Invoke(GL_TEXTURE0);
                    }
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
                _glUniform3f?.Invoke(_lineFogColorUniform, fogR, fogG, fogB);
                _glUniform1f?.Invoke(_lineFogDensityUniform, fogDensity);
                _glUniform3f?.Invoke(_lineCameraPosUniform, camPos.X, camPos.Y, camPos.Z);

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

            if (_hasWindLines || _grassMesh != null || _skyDomeMesh != null || hasCloudObjects)
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
            if (_cloudProgram != 0)
            {
                gl.DeleteProgram(_cloudProgram);
                _cloudProgram = 0;
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
            _shadowCatcherMesh?.Cleanup(gl);
            _shadowCatcherMesh = null;
            if (_shadowCatcherProgram != 0)
            {
                gl.DeleteProgram(_shadowCatcherProgram);
                _shadowCatcherProgram = 0;
            }
            CleanupShadowFbo(gl);
            if (_shadowProgram != 0)
            {
                gl.DeleteProgram(_shadowProgram);
                _shadowProgram = 0;
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
        private static readonly Vector4 IgnitionColor = new(1.00f, 0.84f, 0.32f, 0.95f); // amber

        /// <summary>
        /// Rebuild only environment geometry (sky dome, textures, ground, grass)
        /// without touching <see cref="_sceneObjects"/>. Called when
        /// EnvironmentSettings change so views/simulations stay intact.
        /// </summary>
        private void RebuildEnvironmentGeometry(GlInterface gl)
        {
            var env = _loadedScene?.Environment ?? _loadedEnv ?? new EnvironmentSettings();
            _loadedEnv = env;

            DeleteGlTexture(_skyTextureId);
            _skyTextureId = 0;
            {
                var skyPath = BuiltinAssetResolver.Resolve(env.SkyTexturePath);
                if (!string.IsNullOrEmpty(skyPath) && System.IO.File.Exists(skyPath))
                    _skyTextureId = GlTextureLoader.LoadFromFile(gl, skyPath);
            }

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
                    5000f, zenith, horizon,
                    stacks: 48, slices: 64, fullSphere: true);
                _skyDomeMesh = new GlMeshBuffer();
                _skyDomeMesh.Upload(gl, skyV, skyI);
            }

            DeleteGlTexture(_groundTextureId);
            _groundTextureId = 0;
            _groundTexWidth = 0; _groundTexHeight = 0;
            {
                var gndPath = BuiltinAssetResolver.Resolve(env.GroundTexturePath);
                if (!string.IsNullOrEmpty(gndPath) && System.IO.File.Exists(gndPath))
                    _groundTextureId = GlTextureLoader.LoadFromFile(gl, gndPath,
                        out _groundTexWidth, out _groundTexHeight);
            }

            _groundPlaneMesh?.Cleanup(gl);
            _groundPlaneMesh = null;
            _groundMaterialIndex = (int)env.Ground;
            _groundShowGridOverlay = env.ShowGridOverlay;
            {
                var fallback = new Vector4(0.48f, 0.58f, 0.35f, 1f);
                // Disc radius pushed out to 2000 m so the horizon fade band
                // (1300–1980 m, see ground frag shader) has room and the user
                // can't easily walk to the disc edge in typical refinery
                // viewpoints. Old radius was 500 m which exposed a sharp rim.
                var (gndV, gndI) = GlMeshBuffer.GenerateGroundDisc(
                    2000f, fallback);
                _groundPlaneMesh = new GlMeshBuffer();
                _groundPlaneMesh.Upload(gl, gndV, gndI, keepCpuCopy: true);
            }

            _shadowCatcherMesh?.Cleanup(gl);
            _shadowCatcherMesh = null;
            {
                float sc = 2000f;
                float z  = 0.01f;
                var scV = new SolidVertex[]
                {
                    new(new Vector3(-sc, -sc, z), Vector3.UnitZ, Vector4.Zero),
                    new(new Vector3( sc, -sc, z), Vector3.UnitZ, Vector4.Zero),
                    new(new Vector3( sc,  sc, z), Vector3.UnitZ, Vector4.Zero),
                    new(new Vector3(-sc,  sc, z), Vector3.UnitZ, Vector4.Zero),
                };
                var scI = new uint[] { 0, 1, 2, 0, 2, 3 };
                _shadowCatcherMesh = new GlMeshBuffer();
                _shadowCatcherMesh.Upload(gl, scV, scI);
            }

            _grassMesh?.Cleanup(gl);
            _grassMesh = null;
            if (env.ShowGrassBlades && _loadedScene != null)
            {
                var exclusions = BuildDecorationFootprints(_loadedScene);
                int bladeCount = Math.Clamp(env.GrassBladeCount, 500, 100000);
                var (gV, gI) = GlMeshBuffer.GenerateGrassBlades(80f, bladeCount,
                    exclusionZones: exclusions);
                _grassMesh = new GlMeshBuffer();
                _grassMesh.Upload(gl, gV, gI);
            }

            _grid.HalfSize = (float)env.GridHalfSize;
            _grid.MinorStep = (float)env.GridMinorSpacing;
            _grid.MajorStep = (float)env.GridMajorSpacing;
            _grid.Init(gl);
        }

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

            // Sky panorama texture (load before dome so we know if full sphere is needed)
            DeleteGlTexture(_skyTextureId);
            _skyTextureId = 0;
            {
                var skyPath = BuiltinAssetResolver.Resolve(env.SkyTexturePath);
                if (!string.IsNullOrEmpty(skyPath) && System.IO.File.Exists(skyPath))
                    _skyTextureId = GlTextureLoader.LoadFromFile(gl, skyPath);
            }

            // Sky dome — full sphere when textured (matches WPF panorama wrapping)
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
                    5000f, zenith, horizon,
                    stacks: 48, slices: 64, fullSphere: true);
                _skyDomeMesh = new GlMeshBuffer();
                _skyDomeMesh.Upload(gl, skyV, skyI);
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
                // Matches the other rebuild path above — 2000 m disc with
                // horizon fade in the outer band so the rim isn't visible.
                var (gndV, gndI) = GlMeshBuffer.GenerateGroundDisc(
                    2000f, fallback);
                _groundPlaneMesh = new GlMeshBuffer();
                _groundPlaneMesh.Upload(gl, gndV, gndI, keepCpuCopy: true);
            }

            // Shadow catcher — transparent quad just above ground for shadow projection
            _shadowCatcherMesh?.Cleanup(gl);
            _shadowCatcherMesh = null;
            {
                float sc = 2000f;
                float z  = 0.01f;
                var scV = new SolidVertex[]
                {
                    new(new Vector3(-sc, -sc, z), Vector3.UnitZ, Vector4.Zero),
                    new(new Vector3( sc, -sc, z), Vector3.UnitZ, Vector4.Zero),
                    new(new Vector3( sc,  sc, z), Vector3.UnitZ, Vector4.Zero),
                    new(new Vector3(-sc,  sc, z), Vector3.UnitZ, Vector4.Zero),
                };
                var scI = new uint[] { 0, 1, 2, 0, 2, 3 };
                _shadowCatcherMesh = new GlMeshBuffer();
                _shadowCatcherMesh.Upload(gl, scV, scI);
            }

            // Grass is deferred until after decorations are loaded so
            // bounding boxes can be computed from the mesh vertices.
            _grassMesh?.Cleanup(gl);
            _grassMesh = null;

            if (scene is null) return;

            // ── Release sources → orange sphere + red direction arrow ───
            if (scene.TopLevelSources != null)
            {
                for (int i = 0; i < scene.TopLevelSources.Count; i++)
                {
                    var src = scene.TopLevelSources[i];
                    if (!src.IsVisible) continue;
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

            // ── Fire sources → stylised flame mesh ──────────────────────
            // Replaces the legacy red-orange diamond marker. Pool fires use
            // a wide base proportional to the pool diameter; jet fires use a
            // narrower base scaled from the orifice (so a 25 mm orifice still
            // shows a visible flame). The flame mesh is purely visual — the
            // physical illumination of nearby decorations is handled by the
            // fire point-light uniforms in the solid lit shader.
            if (scene.FireScenario?.Sources != null)
            {
                for (int i = 0; i < scene.FireScenario.Sources.Count; i++)
                {
                    var fire = scene.FireScenario.Sources[i];
                    if (!fire.IsVisible) continue;
                    var pos = fire.Position;
                    float x = (float)pos.X, y = (float)pos.Y, z = (float)pos.Z;

                    float baseR, flameH;
                    if (fire.IsPoolFire)
                    {
                        baseR  = (float)Math.Max(0.4, fire.PoolDiameterM * 0.5);
                        flameH = baseR * 4.0f;
                    }
                    else
                    {
                        baseR  = (float)Math.Max(0.2, fire.OrificeDiameterM * 4.0);
                        flameH = baseR * 7.0f;
                    }

                    var (verts, idx) = GlMeshBuffer.GenerateFlame(
                        new Vector3(x, y, z), baseR, flameH);

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, verts, idx);
                    _sceneObjects.Add(new SceneObject(
                        mesh, Matrix4x4.Identity, "fire:" + i));
                }
            }

            // ── Ignitions → amber sparks ───────────────────────────────
            // A diamond, not a flame: an ignition is an event on a dispersion
            // result, and it has to read differently from a FireSource at a glance.
            if (scene.Ignitions != null)
            {
                for (int i = 0; i < scene.Ignitions.Count; i++)
                {
                    var ignition = scene.Ignitions[i];
                    if (!ignition.IsVisible) continue;
                    var pos = ignition.Position;

                    var (verts, idx) = GlMeshBuffer.GenerateDiamond(
                        new Vector3((float)pos.X, (float)pos.Y, (float)pos.Z),
                        1.0f, 3.0f, IgnitionColor);

                    var mesh = new GlMeshBuffer();
                    mesh.Upload(gl, verts, idx);
                    _sceneObjects.Add(new SceneObject(
                        mesh, Matrix4x4.Identity, "ignition:" + i));
                }
            }

            // ── Monitor points → blue spheres ──────────────────────────
            if (scene.MonitorPoints != null)
            {
                for (int i = 0; i < scene.MonitorPoints.Count; i++)
                {
                    var mon = scene.MonitorPoints[i];
                    if (!mon.Visible) continue;
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
                    if (!det.Visible) continue;
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
                    if (!deco.IsVisible) continue;
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
                        // Compute AABB from all submesh vertices for grass exclusion
                        if (deco.BoundingBox == null)
                            deco.BoundingBox = ComputeBoundingBoxFromTextured(texturedSubs);

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

                    if (deco.BoundingBox == null)
                        deco.BoundingBox = ComputeBoundingBoxFromSolid(loaded.Value.verts);

                    var solidMesh = new GlMeshBuffer();
                    solidMesh.Upload(gl, loaded.Value.verts, loaded.Value.indices,
                        keepCpuCopy: true);

                    _sceneObjects.Add(new SceneObject(
                        solidMesh, model, "deco:" + i));
                }
            }

            // Grass blades — generated AFTER decorations so bounding boxes are available
            if (env.ShowGrassBlades)
            {
                var exclusions = BuildDecorationFootprints(scene);
                int bladeCount = Math.Clamp(env.GrassBladeCount, 500, 100000);
                var (gV, gI) = GlMeshBuffer.GenerateGrassBlades(80f, bladeCount,
                    exclusionZones: exclusions);
                _grassMesh = new GlMeshBuffer();
                _grassMesh.Upload(gl, gV, gI);
            }

            // ── Wind field streamlines ────────────────────────────────
            if (scene.WindFieldScenarios != null)
            {
                foreach (var wfs in scene.WindFieldScenarios)
                {
                    if (!wfs.IsVisible) continue;
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

            // Try analytic fields first (thermal radiation, dose, fatality)
            if (FieldTransform.IsAnalytic(view.FieldProperty))
                return FieldTransform.BuildAnalyticField(
                    scene, view.FieldProperty, nx, ny, nz, half);

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

            // Select time step. A flash fire burns the cloud as it stood when it
            // was lit, so the ignition instant overrides the view's time mode.
            var ignition = FieldTransform.IsIgnitionDerived(view.FieldProperty)
                ? FlashFireEngine.FindIgnitionFor(scene, view.SimulationId)
                : null;
            var field = ignition != null
                ? SelectField(result, ViewTimeMode.SpecificTime, ignition.TimeS)
                : SelectField(result, view.TimeMode, view.SpecificTimeS);
            if (field == null) return null;

            // Apply unit transform if needed (mass fraction → ppm, %LFL, etc.)
            if (FieldTransform.NeedsSpeciesField(view.FieldProperty))
            {
                var gas = ResolveGasForSimulation(sim, scene);
                field = FieldTransform.FromMassFraction(field, view.FieldProperty, gas);

                // Flash fire: the concentration snapshot is the input, not the
                // output — burn it and render the envelope or the arrival time.
                if (FieldTransform.IsIgnitionDerived(view.FieldProperty))
                    field = FlashFireEngine.BuildViewField(scene, view, field, gas, half);
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
            var c = view.UseCloudAppearance ? view.CloudColor : view.IsoColor;
            var isoColor = new Vector4(c.ScR, c.ScG, c.ScB, alpha);

            var (verts, idx) = GlMeshBuffer.FromIsosurfaceResult(isoResult.Value, isoColor);
            // Pre-compute the local-space centroid for the cloud back-to-front
            // sort. We don't keep the CPU vertex copy alive (~MBs per surface)
            // because the value never changes for an immutable iso mesh.
            var centroid = ComputeCentroidFromSolidVerts(verts);
            var mesh = new GlMeshBuffer();
            mesh.Upload(gl, verts, idx);
            _sceneObjects.Add(new SceneObject(
                mesh, Matrix4x4.Identity, $"view:iso:{view.Id}")
            {
                UseCloudShader = view.UseCloudAppearance,
                LocalCentroid = centroid,
            });

            System.Diagnostics.Debug.WriteLine(
                $"[GlViewport] Isosurface '{view.Name}': {isoResult.Value.TriangleCount} tris" +
                (view.UseCloudAppearance ? " [cloud]" : ""));
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

                float alpha = (float)Math.Max(0, Math.Min(1, th.Opacity));
                var c = th.UseCloudAppearance ? th.CloudColor : th.Color;
                var color = new Vector4(c.ScR, c.ScG, c.ScB, alpha);

                var (verts, idx) = GlMeshBuffer.FromIsosurfaceResult(isoResult.Value, color);
                var centroid = ComputeCentroidFromSolidVerts(verts);
                var mesh = new GlMeshBuffer();
                mesh.Upload(gl, verts, idx);
                _sceneObjects.Add(new SceneObject(
                    mesh, Matrix4x4.Identity, $"dispersion:{th.Name}")
                {
                    UseCloudShader = th.UseCloudAppearance,
                    LocalCentroid = centroid,
                });
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

            // ── Primitive draw mode ─────────────────────────────────────
            if (_drawActive && pt.Properties.IsLeftButtonPressed)
            {
                var ground = RaycastGroundPoint(pt.Position.X, pt.Position.Y);
                if (ground != null)
                {
                    _drawAnchor = ground.Value;
                    _drawCurrent = ground.Value;
                    _drawDragging = true;
                    BuildOrUpdateDrawPreview();
                }
                e.Handled = true;
                return;
            }
            if (_drawActive && pt.Properties.IsRightButtonPressed)
            {
                CancelDrawPrimitive();
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

            // ── Primitive draw mode — update preview while dragging ────
            if (_drawActive && _drawDragging)
            {
                var pos = e.GetPosition(this);
                var ground = RaycastGroundPoint(pos.X, pos.Y);
                if (ground != null)
                {
                    _drawCurrent = ground.Value;
                    BuildOrUpdateDrawPreview();
                }
                e.Handled = true;
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

            // Commit primitive draw on left-button release.
            if (_drawActive && _drawDragging)
            {
                _drawDragging = false;
                var (centre, halfXY, height) = ComputeDrawDims();
                ClearDrawPreview();
                _drawActive = false;
                Cursor = global::Avalonia.Input.Cursor.Default;
                DrawPrimitiveCompleted?.Invoke(_drawKind, centre, halfXY, height);
                RequestNextFrameRendering();
            }

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

        /// <summary>Begin click-drag placement of a primitive on the ground.</summary>
        internal void BeginDrawPrimitive(PrimitiveKind kind)
        {
            _drawActive = true;
            _drawKind = kind;
            _drawDragging = false;
            Cursor = new global::Avalonia.Input.Cursor(
                global::Avalonia.Input.StandardCursorType.Cross);
            RequestNextFrameRendering();
        }

        /// <summary>Cancel draw mode without emitting a primitive (Esc / RMB).</summary>
        internal void CancelDrawPrimitive()
        {
            _drawActive = false;
            _drawDragging = false;
            ClearDrawPreview();
            Cursor = global::Avalonia.Input.Cursor.Default;
            RequestNextFrameRendering();
        }

        internal bool IsDrawPrimitiveActive => _drawActive;

        private void ClearDrawPreview()
        {
            if (_drawPreview == null) return;
            // Remove from scene list. The mesh is freed lazily on next
            // rebuild — keeping the GL cleanup off the input thread.
            for (int i = _sceneObjects.Count - 1; i >= 0; i--)
                if (ReferenceEquals(_sceneObjects[i], _drawPreview))
                    _sceneObjects.RemoveAt(i);
            _drawPreview = null;
        }

        /// <summary>
        /// Project a screen position straight onto the Z=0 ground plane.
        /// Returns null when the camera is parallel to the ground or looking
        /// up at the sky (no intersection in front of the camera).
        /// </summary>
        private Vector3? RaycastGroundPoint(double mouseX, double mouseY)
        {
            float w = (float)Bounds.Width;
            float h = (float)Bounds.Height;
            if (w < 1 || h < 1) return null;
            var view = _camera.ViewMatrix;
            var proj = _camera.ProjectionMatrix(w / h);
            var (origin, dir) = RayCaster.ScreenToRay(mouseX, mouseY, w, h, view, proj);
            var hit = RayCaster.RaycastGroundPlane(origin, dir);
            return hit?.Position;
        }

        /// <summary>
        /// Convert (anchor, current) drag points into the parameters the
        /// primitive builders want: a centre point on the ground, a planar
        /// half-extent (sphere/cone/cylinder radius, or half-side for box /
        /// pyramid), and a height. Height is derived so the primitive looks
        /// proportional — clamp tiny drags to a 0.5 m floor so a click
        /// without drag still produces a visible object.
        /// </summary>
        private (Vector3 centre, float halfXY, float height) ComputeDrawDims()
        {
            var d = _drawCurrent - _drawAnchor;
            float halfXY;
            Vector3 centre;
            switch (_drawKind)
            {
                case PrimitiveKind.Cube:
                    // Anchor is one base corner; current is opposite corner.
                    // The box's centre is the midpoint of the diagonal.
                    halfXY = MathF.Max(MathF.Abs(d.X), MathF.Abs(d.Y)) * 0.5f;
                    if (halfXY < 0.25f) halfXY = 0.5f;
                    centre = new Vector3(
                        (_drawAnchor.X + _drawCurrent.X) * 0.5f,
                        (_drawAnchor.Y + _drawCurrent.Y) * 0.5f, 0);
                    break;
                default:
                    // Anchor is centre; current sets the radius.
                    halfXY = new Vector2(d.X, d.Y).Length();
                    if (halfXY < 0.25f) halfXY = 0.5f;
                    centre = _drawAnchor;
                    break;
            }
            // Height heuristic: 2× the planar half-extent so primitives look
            // proportional (a 1 m-radius cylinder is 2 m tall, etc.). Box /
            // pyramid follow the same rule.
            float height = halfXY * 2f;
            return (centre, halfXY, height);
        }

        /// <summary>
        /// Build (or rebuild) the wireframe-like preview mesh shown while
        /// the user drags. Same primitive type and parameters as what would
        /// be committed on release — the user gets WYSIWYG feedback.
        /// </summary>
        private void BuildOrUpdateDrawPreview()
        {
            var (centre, halfXY, height) = ComputeDrawDims();

            // Semi-transparent cyan so the preview reads as a placement
            // affordance and not a real object.
            var color = new Vector4(0.20f, 0.85f, 1.0f, 0.45f);
            (SolidVertex[] verts, uint[] idx) mesh;
            switch (_drawKind)
            {
                case PrimitiveKind.Cube:
                    mesh = GlMeshBuffer.GenerateBox(
                        new Vector3(centre.X, centre.Y, height * 0.5f),
                        halfXY * 2f, halfXY * 2f, height, color);
                    break;
                case PrimitiveKind.Sphere:
                    mesh = GlMeshBuffer.GenerateSphere(
                        new Vector3(centre.X, centre.Y, halfXY),
                        halfXY, color, 24, 16);
                    break;
                case PrimitiveKind.Cylinder:
                    mesh = GlMeshBuffer.GenerateCylinder(
                        centre, halfXY, height, color, 24);
                    break;
                case PrimitiveKind.Cone:
                    mesh = GlMeshBuffer.GenerateCone(
                        centre, halfXY, height, color, 24);
                    break;
                case PrimitiveKind.Pyramid:
                    mesh = GlMeshBuffer.GeneratePyramid(
                        centre, halfXY * 2f, height, color);
                    break;
                default:
                    return;
            }

            // The mesh upload requires an active GL context. Schedule the
            // rebuild on the next render frame instead of doing it here on
            // the input thread.
            _pendingDrawPreview = mesh;
            RequestNextFrameRendering();
        }

        private (SolidVertex[] verts, uint[] idx)? _pendingDrawPreview;

        /// <summary>
        /// Called from OnOpenGlRender when there's a queued preview mesh.
        /// Replaces the previous preview SceneObject (if any) with a fresh
        /// upload. Keeping it as a SceneObject means it goes through the
        /// regular lit-solid pipeline → user sees a properly-shaded blue
        /// ghost of what they're about to drop.
        /// </summary>
        private void ApplyPendingDrawPreview(GlInterface gl)
        {
            if (_pendingDrawPreview == null) return;
            var (v, i) = _pendingDrawPreview.Value;
            _pendingDrawPreview = null;

            // Free the previous preview's GPU buffer before replacing it.
            if (_drawPreview != null)
            {
                _drawPreview.Mesh.Cleanup(gl);
                for (int k = _sceneObjects.Count - 1; k >= 0; k--)
                    if (ReferenceEquals(_sceneObjects[k], _drawPreview))
                        _sceneObjects.RemoveAt(k);
                _drawPreview = null;
            }

            var mesh = new GlMeshBuffer();
            mesh.Upload(gl, v, i);
            _drawPreview = new SceneObject(mesh, Matrix4x4.Identity, DrawPreviewTag);
            _sceneObjects.Add(_drawPreview);
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

        private static void LoadProc<T>(GlInterface gl, string name, out T? del)
            where T : Delegate
        {
            var p = gl.GetProcAddress(name);
            del = p != IntPtr.Zero
                ? Marshal.GetDelegateForFunctionPointer<T>(p) : null;
        }

        private unsafe void CreateShadowFbo(GlInterface gl)
        {
            _shadowReady = false;
            if (_glGenFramebuffers == null || _glFramebufferTexture2D == null ||
                _glTexImage2D == null || _glTexParameteri == null) return;

            int fbo = 0;
            _glGenFramebuffers(1, &fbo);
            if (fbo == 0) return;
            _shadowFbo = fbo;

            _shadowDepthTex = gl.GenTexture();
            _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _shadowDepthTex);
            _glTexImage2D(GL_TEXTURE_2D_VAL, 0, GL_DEPTH_COMPONENT24_VAL,
                SHADOW_MAP_SIZE, SHADOW_MAP_SIZE, 0,
                GL_DEPTH_COMPONENT_VAL, GL_UNSIGNED_INT_VAL, IntPtr.Zero);
            _glTexParameteri(GL_TEXTURE_2D_VAL, GL_TEXTURE_MIN_FILTER_VAL, GL_NEAREST_VAL);
            _glTexParameteri(GL_TEXTURE_2D_VAL, GL_TEXTURE_MAG_FILTER_VAL, GL_NEAREST_VAL);
            _glTexParameteri(GL_TEXTURE_2D_VAL, GL_TEXTURE_WRAP_S_VAL, GL_CLAMP_TO_BORDER);
            _glTexParameteri(GL_TEXTURE_2D_VAL, GL_TEXTURE_WRAP_T_VAL, GL_CLAMP_TO_BORDER);
            if (_glTexParameterfv != null)
            {
                float* border = stackalloc float[4] { 1f, 1f, 1f, 1f };
                _glTexParameterfv(GL_TEXTURE_2D_VAL, GL_TEXTURE_BORDER_COLOR, border);
            }
            _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);

            gl.BindFramebuffer(GL_FRAMEBUFFER, _shadowFbo);
            _glFramebufferTexture2D(GL_FRAMEBUFFER, GL_DEPTH_ATTACHMENT_VAL,
                GL_TEXTURE_2D_VAL, _shadowDepthTex, 0);
            _glDrawBuffer?.Invoke(GL_NONE_VAL);

            if (_glCheckFramebufferStatus != null)
            {
                int status = _glCheckFramebufferStatus(GL_FRAMEBUFFER);
                if (status != 0x8CD5)
                {
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, _shadowDepthTex);
                    _glTexImage2D(GL_TEXTURE_2D_VAL, 0, GL_DEPTH_COMPONENT_VAL,
                        SHADOW_MAP_SIZE, SHADOW_MAP_SIZE, 0,
                        GL_DEPTH_COMPONENT_VAL, GL_FLOAT_VAL, IntPtr.Zero);
                    _glBindTexture?.Invoke(GL_TEXTURE_2D_VAL, 0);
                    status = _glCheckFramebufferStatus(GL_FRAMEBUFFER);
                }
                _shadowReady = status == 0x8CD5;
            }
            else
                _shadowReady = true;

            gl.BindFramebuffer(GL_FRAMEBUFFER, 0);
        }

        private unsafe void CleanupShadowFbo(GlInterface gl)
        {
            if (_shadowDepthTex != 0)
            {
                gl.DeleteTexture(_shadowDepthTex);
                _shadowDepthTex = 0;
            }
            if (_shadowFbo != 0 && _glDeleteFramebuffers != null)
            {
                int fbo = _shadowFbo;
                _glDeleteFramebuffers(1, &fbo);
                _shadowFbo = 0;
            }
            _shadowReady = false;
        }

        /// <summary>
        /// Mean of vertex positions — small helper used at upload time so
        /// the SceneObject's LocalCentroid is populated up front instead of
        /// requiring the CPU vertex copy to be kept alive for the
        /// back-to-front depth sort.
        /// </summary>
        private static Vector3 ComputeCentroidFromSolidVerts(SolidVertex[] verts)
        {
            if (verts == null || verts.Length == 0) return Vector3.Zero;
            var sum = Vector3.Zero;
            for (int i = 0; i < verts.Length; i++) sum += verts[i].Position;
            return sum / verts.Length;
        }

        /// <summary>
        /// World-space centroid of a SceneObject, used as the depth-sort key
        /// for the back-to-front cloud render pass. Lazily computes the
        /// local-space centroid from the mesh's host-side vertex copy
        /// (caches on the SceneObject so subsequent frames are O(1)) then
        /// transforms it through the current ModelMatrix.
        /// </summary>
        private static Vector3 ComputeWorldCentroid(SceneObject obj)
        {
            if (obj.LocalCentroid == null)
            {
                var sum = Vector3.Zero;
                int n = 0;
                var sv = obj.Mesh.CpuVertices;
                if (sv != null && sv.Length > 0)
                {
                    foreach (var v in sv) { sum += v.Position; n++; }
                }
                else
                {
                    var tv = obj.Mesh.CpuTexturedVertices;
                    if (tv != null && tv.Length > 0)
                        foreach (var v in tv) { sum += v.Position; n++; }
                }
                obj.LocalCentroid = n > 0 ? sum / n : Vector3.Zero;
            }
            return Vector3.Transform(obj.LocalCentroid.Value, obj.ModelMatrix);
        }

        /// <summary>
        /// Build the per-fire light arrays from the currently loaded scene's
        /// FireScenario and push them as uniform arrays to whichever lit
        /// shader is currently bound. Up to 8 fires are honoured; extras
        /// are dropped. Pass −1 for any uniform location that the active
        /// shader doesn't expose.
        /// </summary>
        private unsafe void BindFireLights(GlInterface gl,
            int locCount, int locPos, int locColor, int locRadius, int locTime)
        {
            if (locCount < 0) return;
            var scene = _pendingScene ?? _loadedScene;
            int count = 0;
            const int MAX = 8;
            var posBuf = stackalloc float[MAX * 3];
            var colBuf = stackalloc float[MAX * 3];
            var radBuf = stackalloc float[MAX];
            if (scene?.FireScenario?.Sources != null)
            {
                foreach (var fire in scene.FireScenario.Sources)
                {
                    if (count >= MAX) break;
                    if (!fire.IsVisible) continue;
                    // Sit the light source slightly above the base so the
                    // illumination glances down onto nearby decorations.
                    posBuf[count * 3 + 0] = (float)fire.Position.X;
                    posBuf[count * 3 + 1] = (float)fire.Position.Y;
                    posBuf[count * 3 + 2] = (float)(fire.Position.Z + 1.5);
                    // Warm fire tint, intensity baked in. Pool fires
                    // (broader base) feel a bit more orange; jet fires
                    // tend brighter / whiter at the tip but for the
                    // ambient-illumination pass we use the same colour.
                    const float intensity = 3.0f;
                    colBuf[count * 3 + 0] = 1.00f * intensity;
                    colBuf[count * 3 + 1] = 0.55f * intensity;
                    colBuf[count * 3 + 2] = 0.18f * intensity;
                    // Radius drives the falloff envelope. For a jet use the
                    // orifice, for a pool use the diameter.
                    double r = fire.IsPoolFire
                        ? Math.Max(0.5, fire.PoolDiameterM * 0.5)
                        : Math.Max(0.25, fire.OrificeDiameterM * 6.0);
                    radBuf[count] = (float)r;
                    count++;
                }
            }
            _glUniform1i?.Invoke(locCount, count);
            if (count > 0)
            {
                if (locPos    >= 0) _glUniform3fv?.Invoke(locPos,    count, posBuf);
                if (locColor  >= 0) _glUniform3fv?.Invoke(locColor,  count, colBuf);
                if (locRadius >= 0) _glUniform1fv?.Invoke(locRadius, count, radBuf);
            }
            if (locTime >= 0)
                _glUniform1f?.Invoke(locTime, (float)_animClock.Elapsed.TotalSeconds);
        }

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

        private static BoundingBox ComputeBoundingBoxFromTextured(
            List<TexturedSubmesh> subs)
        {
            float xMin = float.MaxValue, yMin = float.MaxValue, zMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue, zMax = float.MinValue;
            foreach (var sub in subs)
                foreach (var v in sub.Vertices)
                {
                    if (v.Position.X < xMin) xMin = v.Position.X;
                    if (v.Position.Y < yMin) yMin = v.Position.Y;
                    if (v.Position.Z < zMin) zMin = v.Position.Z;
                    if (v.Position.X > xMax) xMax = v.Position.X;
                    if (v.Position.Y > yMax) yMax = v.Position.Y;
                    if (v.Position.Z > zMax) zMax = v.Position.Z;
                }
            return new BoundingBox(
                new DisperSim3D.Geometry.Point3D(xMin, yMin, zMin),
                new DisperSim3D.Geometry.Point3D(xMax, yMax, zMax));
        }

        private static BoundingBox ComputeBoundingBoxFromSolid(
            SolidVertex[] verts)
        {
            float xMin = float.MaxValue, yMin = float.MaxValue, zMin = float.MaxValue;
            float xMax = float.MinValue, yMax = float.MinValue, zMax = float.MinValue;
            foreach (var v in verts)
            {
                if (v.Position.X < xMin) xMin = v.Position.X;
                if (v.Position.Y < yMin) yMin = v.Position.Y;
                if (v.Position.Z < zMin) zMin = v.Position.Z;
                if (v.Position.X > xMax) xMax = v.Position.X;
                if (v.Position.Y > yMax) yMax = v.Position.Y;
                if (v.Position.Z > zMax) zMax = v.Position.Z;
            }
            return new BoundingBox(
                new DisperSim3D.Geometry.Point3D(xMin, yMin, zMin),
                new DisperSim3D.Geometry.Point3D(xMax, yMax, zMax));
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

        /// <summary>Render with the cloud (gas leak) shader instead of the solid shader.</summary>
        public bool UseCloudShader { get; set; }

        /// <summary>
        /// Cached local-space centroid of the mesh (mean of CpuVertices /
        /// CpuTexturedVertices). Computed lazily on first read; set to
        /// <c>null</c> if the mesh has no host-side vertex data, in which
        /// case callers fall back to ModelMatrix.Translation.
        /// </summary>
        public Vector3? LocalCentroid { get; set; }
    }
}

