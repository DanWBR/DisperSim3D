using System.Text.Json.Nodes;
using DisperSim3D.Core;
using DisperSim3D.Geometry;
using DisperSim3D.Models;

namespace DisperSim3D.Automation
{
    /// <summary>Release sources: adding one, with or without a high-pressure leak, and changing
    /// how a choked leak enters the CFD solvers.</summary>
    internal sealed partial class AutomationServer
    {
        private async Task<Response> AddReleaseSourceAsync(Request r)
        {
            JsonObject body = ReadJson(r);
            double[] p = RequiredPoint(body, "position");
            object? created = null;
            string? error = null;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; return Task.CompletedTask; }
                var src = new ReleaseSource3D
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = OptionalString(body, "name") ?? "Release " + ((scene.TopLevelSources?.Count ?? 0) + 1),
                    Position = new Point3D(p[0], p[1], p[2]),
                    ReleaseRateKgPerS = OptionalDouble(body, "releaseRateKgS") ?? 1.0,
                    ReleaseAzimuthDeg = OptionalDouble(body, "azimuthDeg") ?? 0,
                    ReleaseElevationDeg = OptionalDouble(body, "elevationDeg") ?? 0,
                    ReleaseHeightOffset = 0,
                    ExitTemperatureK = OptionalDouble(body, "exitTemperatureK") ?? 293.15
                };
                string? gasName = OptionalString(body, "gas");
                if (gasName != null)
                {
                    var item = scene.GasLibrary?.FirstOrDefault(g => string.Equals(g.Name, gasName, StringComparison.OrdinalIgnoreCase) || g.Id == gasName);
                    if (item == null) { error = "No gas " + gasName + " in the project's gas library."; return Task.CompletedTask; }
                    src.GasRefId = item.Id;
                    src.Gas = item.AsGasProperties();
                }
                if (body["highPressureLeak"] is JsonObject leak)
                {
                    double? mdot = OptionalDouble(leak, "massFlowKgS");
                    src.HighPressureLeak = new HighPressureLeakParams
                    {
                        VesselPressurePa = OptionalDouble(leak, "vesselPressurePa") ?? 1e6,
                        VesselTemperatureK = OptionalDouble(leak, "vesselTemperatureK") ?? 293.15,
                        OrificeDiameterM = OptionalDouble(leak, "orificeDiameterM") ?? 0.025,
                        GasGamma = OptionalDouble(leak, "gamma") ?? (src.Gas?.EffectiveHeatCapacityRatio > 1 ? src.Gas.EffectiveHeatCapacityRatio : 1.31),
                        GasMolarMassKgMol = OptionalDouble(leak, "molarMassKgMol") ?? (src.Gas?.MolarMass > 0 ? src.Gas.MolarMass : 0.016),
                        DischargeCoefficient = OptionalDouble(leak, "dischargeCoefficient") ?? 0.65,
                        AmbientPressurePa = OptionalDouble(leak, "ambientPressurePa") ?? 101325.0,
                        SpecifyMassFlow = mdot != null,
                        SpecifiedMassFlowKgPerS = mdot ?? 1.0
                    };
                }
                error = ApplyJetSource(src, body);
                if (error != null) return Task.CompletedTask;
                scene.TopLevelSources ??= new List<ReleaseSource3D>();
                scene.TopLevelSources.Add(src);
                Host3.RefreshProject();
                created = DescribeSource(src);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(error.StartsWith("No project") ? 409 : 400, error);
            _log.Add("added source " + OptionalString(body, "name"));
            return JsonResponse(200, new { ok = true, source = created });
        }

        private async Task<Response> UpdateReleaseSourceAsync(Request r)
        {
            JsonObject body = ReadJson(r);
            string key = RequiredString(body, "source");
            object? updated = null;
            string? error = null;
            int status = 400;
            await _host.RunOnUiThreadAsync(() =>
            {
                Scene3D? scene = _host.CurrentScene;
                if (scene == null) { error = "No project is open."; status = 409; return Task.CompletedTask; }
                var src = scene.TopLevelSources?.FirstOrDefault(s => s.Id == key)
                          ?? scene.TopLevelSources?.FirstOrDefault(s => string.Equals(s.Name, key, StringComparison.OrdinalIgnoreCase));
                if (src == null) { error = "No source " + key + " in the project."; status = 404; return Task.CompletedTask; }
                error = ApplyJetSource(src, body);
                if (error != null) return Task.CompletedTask;
                Host3.RefreshProject();
                updated = DescribeSource(src);
                return Task.CompletedTask;
            });
            if (error != null) throw new HttpError(status, error);
            _log.Add("updated source " + key);
            return JsonResponse(200, new { ok = true, source = updated });
        }

        /// <summary>Sets <see cref="ReleaseSource3D.JetSource"/> and
        /// <see cref="ReleaseSource3D.PseudoSourceVelocityMS"/> from the body when it gives them.</summary>
        private static string? ApplyJetSource(ReleaseSource3D src, JsonObject body)
        {
            string? kind = OptionalString(body, "jetSource");
            if (kind != null)
            {
                if (!Enum.TryParse(kind, true, out JetSourceModel model))
                    return "jetSource must be " + string.Join(" or ", Enum.GetNames<JetSourceModel>()) + ".";
                src.JetSource = model;
            }
            double? v = OptionalDouble(body, "pseudoSourceVelocityMS");
            if (v != null)
            {
                if (v <= 0) return "pseudoSourceVelocityMS must be positive.";
                src.PseudoSourceVelocityMS = v.Value;
            }
            return null;
        }

        private static object DescribeSource(ReleaseSource3D x) => new
        {
            x.Id,
            x.Name,
            position = new[] { x.Position.X, x.Position.Y, x.Position.Z },
            releaseRateKgS = x.EffectiveReleaseRateKgPerS,
            highPressureLeak = x.HighPressureLeak != null,
            choked = x.HighPressureLeak != null && HighPressureLeakModel.IsChoked(x.HighPressureLeak),
            jetSource = x.JetSource.ToString(),
            pseudoSourceVelocityMS = x.PseudoSourceVelocityMS,
            cfdDiameterM = x.ExpandedDiameterForCfdM,
            cfdVelocityMS = x.ExpandedVelocityForCfdMS
        };
    }
}
