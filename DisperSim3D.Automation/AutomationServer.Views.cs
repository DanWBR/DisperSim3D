using System.Text.Json.Nodes;
using DisperSim3D.Core;
using DisperSim3D.Extensibility;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Automation
{
    /// <summary>The project's views and decorations, and the 3D view's camera.</summary>
    internal sealed partial class AutomationServer
    {
        // ── Views ─────────────────────────────────────────────────────────

        /// <summary>
        /// The fields a request sets on a view, read and checked before the UI thread
        /// is touched; null leaves the view's own value.
        /// </summary>
        private sealed class ViewPatch
        {
            public string? Name, Simulation;
            public ViewKind? Kind;
            public ViewFieldProperty? Field;
            public ViewTimeMode? TimeMode;
            public ColorMapName? ColorMap;
            public Color? IsoColor;
            public double? SpecificTimeS, Opacity, IsoValue, PlanePosition, MinValue, MaxValue;
            public int? SampleResolution;
            public bool? Visible, CloudAppearance, ClipToRange;

            public static ViewPatch Read(JsonObject body) => new()
            {
                Name = OptionalString(body, "name"),
                Simulation = OptionalString(body, "simulation"),
                Kind = OptionalEnum<ViewKind>(body, "kind"),
                Field = OptionalEnum<ViewFieldProperty>(body, "field"),
                TimeMode = OptionalEnum<ViewTimeMode>(body, "timeMode"),
                ColorMap = OptionalEnum<ColorMapName>(body, "colorMap"),
                IsoColor = OptionalColor(body, "isoColor"),
                SpecificTimeS = OptionalDouble(body, "specificTimeS"),
                Opacity = OptionalDouble(body, "opacity"),
                IsoValue = OptionalDouble(body, "isoValue"),
                PlanePosition = OptionalDouble(body, "planePosition"),
                MinValue = OptionalDouble(body, "minValue"),
                MaxValue = OptionalDouble(body, "maxValue"),
                SampleResolution = OptionalDouble(body, "sampleResolution") is { } n ? (int)n : null,
                Visible = OptionalBool(body, "visible"),
                CloudAppearance = OptionalBool(body, "cloudAppearance"),
                ClipToRange = OptionalBool(body, "clipToRange")
            };

            /// <summary>Sets the fields on <paramref name="v"/>; null when done, otherwise why not.</summary>
            public string? ApplyTo(View v, Scene3D scene)
            {
                if (Simulation != null)
                {
                    var sim = scene.Simulations.FirstOrDefault(s => s.Id == Simulation)
                              ?? scene.Simulations.FirstOrDefault(s => string.Equals(s.Name, Simulation, StringComparison.OrdinalIgnoreCase));
                    if (sim == null) return "No simulation " + Simulation + " in the project.";
                    v.SimulationId = sim.Id;
                }
                if (Name != null) v.Name = Name;
                if (Kind is { } k) v.Kind = k;
                if (Field is { } f) v.FieldProperty = f;
                if (TimeMode is { } tm) v.TimeMode = tm;
                if (ColorMap is { } cm) v.ColorMap = cm;
                if (IsoColor is { } c) v.IsoColor = c;
                if (SpecificTimeS is { } st) v.SpecificTimeS = st;
                if (Opacity is { } o) v.Opacity = Math.Clamp(o, 0, 1);
                if (IsoValue is { } iso) v.IsoValue = iso;
                if (PlanePosition is { } pp) v.PlanePosition = pp;
                if (MinValue is { } min) v.MinValue = min;
                if (MaxValue is { } max) v.MaxValue = max;
                if (SampleResolution is { } sr) v.SampleResolution = Math.Max(4, sr);
                if (Visible is { } vis) v.IsVisible = vis;
                if (CloudAppearance is { } cloud) v.UseCloudAppearance = cloud;
                if (ClipToRange is { } clip) v.ClipToRange = clip;
                return null;
            }
        }

        private async Task<List<object>> ViewsAsync()
        {
            var list = new List<object>();
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene != null)
                    foreach (View v in scene.Views)
                        list.Add(DescribeView(scene, v));
                return Task.CompletedTask;
            });
            return list;
        }

        private static object DescribeView(Scene3D scene, View v) => new
        {
            v.Id,
            v.Name,
            kind = v.Kind.ToString(),
            simulation = scene.Simulations.FirstOrDefault(s => s.Id == v.SimulationId)?.Name,
            simulationId = v.SimulationId,
            field = v.FieldProperty.ToString(),
            timeMode = v.TimeMode.ToString(),
            specificTimeS = v.SpecificTimeS,
            visible = v.IsVisible,
            v.Opacity,
            v.IsoValue,
            isoColor = v.IsoColor.ToString(),
            cloudAppearance = v.UseCloudAppearance,
            v.PlanePosition,
            colorMap = v.ColorMap.ToString(),
            v.MinValue,
            v.MaxValue,
            v.ClipToRange,
            v.SampleResolution
        };

        private async Task<Response> AddViewAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            RequiredString(body, "simulation");
            ViewPatch patch = ViewPatch.Read(body);
            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                var view = new View { Name = "View " + (scene.Views.Count + 1) };
                error = patch.ApplyTo(view, scene);
                if (error != null) return Task.CompletedTask;
                scene.Views.Add(view);
                host.RefreshProject();
                created = DescribeView(scene, view);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : 404, error);
            _log.Add("added view " + patch.Kind + " of " + patch.Field + " on " + patch.Simulation);
            return JsonResponse(200, new { ok = true, view = created });
        }

        private async Task<Response> UpdateViewAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            string id = RequiredString(body, "id");
            ViewPatch patch = ViewPatch.Read(body);
            object? updated = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                View? view = scene?.Views.FirstOrDefault(v => v.Id == id);
                if (scene == null || view == null) { error = "No view " + id + " in the project."; return Task.CompletedTask; }
                error = patch.ApplyTo(view, scene);
                if (error != null) return Task.CompletedTask;
                host.RefreshProject();
                updated = DescribeView(scene, view);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(404, error);
            _log.Add("updated view " + id);
            return JsonResponse(200, new { ok = true, view = updated });
        }

        private async Task<Response> DeleteViewAsync(Request r)
        {
            var host = Host3;
            string id = RequiredString(ReadJson(r), "id");
            bool removed = false;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                View? view = scene?.Views.FirstOrDefault(v => v.Id == id);
                if (scene != null && view != null)
                {
                    removed = scene.Views.Remove(view);
                    host.RefreshProject();
                }
                return Task.CompletedTask;
            });
            if (!removed) throw new HttpError(404, "No view " + id + " in the project.");
            _log.Add("deleted view " + id);
            return JsonResponse(200, new { ok = true, id });
        }

        // ── Decorations ───────────────────────────────────────────────────

        private async Task<List<object>> DecorationsAsync()
        {
            var list = new List<object>();
            await _host.RunOnUiThreadAsync(() =>
            {
                foreach (Decoration3D d in _host.CurrentScene?.Decorations ?? new List<Decoration3D>())
                    list.Add(DescribeDecoration(d));
                return Task.CompletedTask;
            });
            return list;
        }

        private static object DescribeDecoration(Decoration3D d) => new
        {
            d.Id,
            d.Name,
            visible = d.IsVisible,
            d.Opacity,
            d.ClipEnabled,
            clipAxis = d.ClipAxis.ToString(),
            d.ClipValue,
            d.ClipAbove
        };

        private async Task<Response> UpdateDecorationAsync(Request r)
        {
            var host = Host3;
            JsonObject body = ReadJson(r);
            string key = OptionalString(body, "id") ?? OptionalString(body, "name")
                         ?? throw new HttpError(400, "Give the decoration as \"id\" or \"name\".");
            bool? visible = OptionalBool(body, "visible"), clipEnabled = OptionalBool(body, "clipEnabled"), clipAbove = OptionalBool(body, "clipAbove");
            double? opacity = OptionalDouble(body, "opacity"), clipValue = OptionalDouble(body, "clipValue");
            ClipAxis? clipAxis = OptionalEnum<ClipAxis>(body, "clipAxis");
            object? updated = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                var all = _host.CurrentScene?.Decorations ?? new List<Decoration3D>();
                Decoration3D? d = all.FirstOrDefault(x => x.Id == key)
                                  ?? all.FirstOrDefault(x => string.Equals(x.Name, key, StringComparison.OrdinalIgnoreCase));
                if (d == null) return Task.CompletedTask;
                if (visible is { } v) d.IsVisible = v;
                if (opacity is { } o) d.Opacity = Math.Clamp(o, 0, 1);
                if (clipEnabled is { } ce) d.ClipEnabled = ce;
                if (clipAxis is { } ca) d.ClipAxis = ca;
                if (clipValue is { } cv) d.ClipValue = cv;
                if (clipAbove is { } cab) d.ClipAbove = cab;
                host.RefreshProject();
                updated = DescribeDecoration(d);
                return Task.CompletedTask;
            });
            if (updated == null) throw new HttpError(404, "No decoration " + key + " in the project.");
            _log.Add("updated decoration " + key);
            return JsonResponse(200, new { ok = true, decoration = updated });
        }

        // ── Camera ────────────────────────────────────────────────────────

        private IExtenderHost4 Host4 => _host as IExtenderHost4
            ?? throw new HttpError(501, "This DisperSim 3D cannot move its camera for extenders.");

        private Response CameraResponse() => JsonResponse(200, DescribeCamera(Host4.GetCamera()));

        private static object DescribeCamera(CameraPreset c) => new
        {
            position = new[] { c.Position.X, c.Position.Y, c.Position.Z },
            target = new[] { c.Position.X + c.LookDirection.X, c.Position.Y + c.LookDirection.Y, c.Position.Z + c.LookDirection.Z },
            lookDirection = new[] { c.LookDirection.X, c.LookDirection.Y, c.LookDirection.Z }
        };

        private Task<Response> SetCameraAsync(Request r)
        {
            var host = Host4;
            JsonObject body = ReadJson(r);
            double[] position = RequiredPoint(body, "position"), target = RequiredPoint(body, "target");
            var look = new Vector3D(target[0] - position[0], target[1] - position[1], target[2] - position[2]);
            if (look.X * look.X + look.Y * look.Y + look.Z * look.Z < 1e-12)
                throw new HttpError(400, "\"position\" and \"target\" must differ.");
            host.SetCamera(new CameraPreset
            {
                Name = "Camera",
                Position = new Point3D(position[0], position[1], position[2]),
                LookDirection = look
            });
            _log.Add("camera at (" + string.Join(", ", position) + ") looking at (" + string.Join(", ", target) + ")");
            return Task.FromResult(JsonResponse(200, new { ok = true, camera = DescribeCamera(host.GetCamera()) }));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static double[] RequiredPoint(JsonObject body, string name)
        {
            if (body[name] is JsonArray a && a.Count == 3 && a.All(n => n is JsonValue v && v.TryGetValue(out double _)))
                return a.Select(n => n!.GetValue<double>()).ToArray();
            throw new HttpError(400, "\"" + name + "\" must be [x, y, z] in metres.");
        }

        private static bool? OptionalBool(JsonObject body, string name)
        {
            if (body[name] is not JsonValue v) return null;
            if (v.TryGetValue(out bool b)) return b;
            throw new HttpError(400, "\"" + name + "\" must be true or false.");
        }

        private static T? OptionalEnum<T>(JsonObject body, string name) where T : struct, Enum
        {
            string? text = OptionalString(body, name);
            if (text == null) return null;
            if (Enum.TryParse(text, ignoreCase: true, out T value) && Enum.IsDefined(value)) return value;
            throw new HttpError(400, "\"" + name + "\" is one of " + string.Join(", ", Enum.GetNames<T>()) + ".");
        }

        private static Color? OptionalColor(JsonObject body, string name)
        {
            string? text = OptionalString(body, name);
            if (text == null) return null;
            try { return Color.Parse(text); }
            catch (Exception) { throw new HttpError(400, "\"" + name + "\" must be a colour such as #FF8800."); }
        }
    }
}
