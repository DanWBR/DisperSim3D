#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Resolves <c>Decoration3D.FilePath</c> entries of the form
    /// <c>primitive:&lt;kind&gt;[?k=v&amp;…]</c> into a solid mesh. Lets the
    /// scene reference parametric primitives without needing files on disk —
    /// a "primitive:cube" decoration round-trips through the project XML and
    /// reconstructs the mesh on load.
    ///
    /// Supported kinds:
    /// <list type="bullet">
    ///   <item><c>cube</c> / <c>box</c>     — <c>?w=1&amp;h=1&amp;d=1</c></item>
    ///   <item><c>sphere</c>                — <c>?r=1&amp;slices=24&amp;stacks=16</c></item>
    ///   <item><c>cylinder</c>              — <c>?r=1&amp;h=2&amp;slices=24</c></item>
    ///   <item><c>cone</c>                  — <c>?r=1&amp;h=2&amp;slices=24</c></item>
    ///   <item><c>pyramid</c>               — <c>?s=1&amp;h=1</c> (square base)</item>
    /// </list>
    ///
    /// All dimensions default to 1 m. The decoration's existing
    /// <c>Scale</c> field multiplies on top, so a "primitive:cube" at
    /// Scale=5 becomes a 5 m cube without editing the path.
    /// </summary>
    internal static class PrimitiveBuilder
    {
        public static (SolidVertex[] verts, uint[] indices)? Build(string spec, Vector4 color)
        {
            // Strip "primitive:" prefix
            string body = spec.Substring("primitive:".Length);

            string kind;
            Dictionary<string, double> p = new(StringComparer.OrdinalIgnoreCase);
            int q = body.IndexOf('?');
            if (q < 0)
            {
                kind = body.Trim().ToLowerInvariant();
            }
            else
            {
                kind = body.Substring(0, q).Trim().ToLowerInvariant();
                foreach (var pair in body.Substring(q + 1).Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = pair.Substring(0, eq);
                    string val = pair.Substring(eq + 1);
                    if (double.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                        p[key] = d;
                }
            }

            double Get(string key, double fallback) =>
                p.TryGetValue(key, out double v) ? v : fallback;

            switch (kind)
            {
                case "cube":
                case "box":
                {
                    float w = (float)Get("w", 1.0);
                    float h = (float)Get("h", 1.0);
                    float d = (float)Get("d", 1.0);
                    // Box centred on origin in XY, sitting on the ground in Z
                    // (Z range 0..h) so dropping one at Position (0,0,0) lands
                    // it on the ground rather than half-buried.
                    return GlMeshBuffer.GenerateBox(
                        new Vector3(0, 0, h * 0.5f), w, d, h, color);
                }
                case "sphere":
                {
                    float r = (float)Get("r", 1.0);
                    int slices = (int)Get("slices", 24);
                    int stacks = (int)Get("stacks", 16);
                    // Sit the sphere on the ground (centre at Z = r)
                    return GlMeshBuffer.GenerateSphere(
                        new Vector3(0, 0, r), r, color, slices, stacks);
                }
                case "cylinder":
                {
                    float r = (float)Get("r", 1.0);
                    float h = (float)Get("h", 2.0);
                    int slices = (int)Get("slices", 24);
                    return GlMeshBuffer.GenerateCylinder(
                        Vector3.Zero, r, h, color, slices);
                }
                case "cone":
                {
                    float r = (float)Get("r", 1.0);
                    float h = (float)Get("h", 2.0);
                    int slices = (int)Get("slices", 24);
                    return GlMeshBuffer.GenerateCone(
                        Vector3.Zero, r, h, color, slices);
                }
                case "pyramid":
                {
                    float s = (float)Get("s", 1.0);
                    float h = (float)Get("h", 1.0);
                    return GlMeshBuffer.GeneratePyramid(
                        Vector3.Zero, s, h, color);
                }
                default:
                    System.Diagnostics.Debug.WriteLine(
                        $"[PrimitiveBuilder] Unknown primitive '{kind}' in '{spec}'");
                    return null;
            }
        }
    }
}
