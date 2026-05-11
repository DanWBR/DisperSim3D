using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Resolves the <see cref="WindField3D"/> associated with a <see cref="DispersionScenario"/>
    /// via its <see cref="DispersionScenario.WindFieldScenarioId"/>, loading the field from disk
    /// if it has not yet been cached.
    /// </summary>
    public static class WindFieldResolver
    {
        /// <summary>
        /// Looks up the <see cref="WindFieldScenario"/> referenced by the dispersion scenario.
        /// </summary>
        public static WindFieldScenario FindWindFieldScenario(Scene3D scene, DispersionScenario scenario)
        {
            if (scene == null || scenario == null || string.IsNullOrEmpty(scenario.WindFieldScenarioId))
                return null;
            return scene.WindFieldScenarios?.FirstOrDefault(w => w.Id == scenario.WindFieldScenarioId);
        }

        /// <summary>
        /// Resolves the WindField3D for the scenario, loading it from disk if necessary.
        /// Returns null if not set, not ready, or load failed.
        /// </summary>
        public static WindField3D ResolveWindField(Scene3D scene, DispersionScenario scenario)
        {
            var wf = FindWindFieldScenario(scene, scenario);
            if (wf == null) return null;
            if (wf.WindField != null) return wf.WindField;
            if (wf.Status != WindFieldStatus.Ready) return null;
            // FluidX3D-generated wind fields save windfield.bin to their CasePath. Try
            // that loader unconditionally — TryLoad just checks for windfield.bin presence,
            // so it's a cheap no-op when the case is actually OpenFOAM. This also rescues
            // projects saved before UseFluidX3D was being serialised.
            var fx = FluidX3DWindFieldRunner.LoadFromCase(wf);
            if (fx != null) return fx;
            return WindFieldRunner.LoadFromCase(wf);
        }

        /// <summary>
        /// Validation result message; empty string means OK.
        /// </summary>
        public static string ValidateForDispersion(Scene3D scene, DispersionScenario scenario)
        {
            if (scene == null || scenario == null) return "No scenario selected.";
            if (string.IsNullOrEmpty(scenario.WindFieldScenarioId))
                return "No wind field is associated with this dispersion scenario. Open Dispersion → Manage Wind Fields... to create and run one, then select it in the Scenario Manager.";
            var wf = FindWindFieldScenario(scene, scenario);
            if (wf == null)
                return "The associated wind field scenario was not found in this scene.";
            if (wf.Status != WindFieldStatus.Ready)
                return "The associated wind field '" + wf.Name + "' has not been computed yet (status: " + wf.Status + "). Run it from Dispersion → Manage Wind Fields...";
            return string.Empty;
        }
    }
}
