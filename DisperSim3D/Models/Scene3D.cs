using System.Collections.Generic;
using System.Linq;

namespace DisperSim3D.Models
{
    /// <summary>
    /// Main scene model containing all 3D elements for dispersion study
    /// </summary>
    public class Scene3D
    {
        /// <summary>
        /// Gets or sets the name of the scene.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets a textual description of the scene.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the list of working planes (elevation levels) in the scene.
        /// </summary>
        public List<WorkPlane> WorkPlanes { get; set; }

        /// <summary>
        /// Gets or sets the currently active working plane.
        /// </summary>
        public WorkPlane CurrentWorkPlane { get; set; }

        /// <summary>
        /// Gets or sets the list of imported 3D decorations such as obstacles and buildings.
        /// </summary>
        public List<Decoration3D> Decorations { get; set; }

        /// <summary>
        /// Gets or sets the list of gas dispersion scenarios defined in the scene.
        /// </summary>
        public List<DispersionScenario> DispersionScenarios { get; set; }

        /// <summary>
        /// Gets or sets the zero-based index of the active dispersion scenario.
        /// </summary>
        public int ActiveScenarioIndex { get; set; }

        /// <summary>
        /// Gets or sets the currently active <see cref="Models.DispersionScenario"/>.
        /// Returns <c>null</c> if no scenarios exist or the index is out of range.
        /// Setting this property replaces the scenario at <see cref="ActiveScenarioIndex"/>,
        /// or adds a new one if the list is empty.
        /// </summary>
        public DispersionScenario DispersionScenario
        {
            get => DispersionScenarios.Count > 0 && ActiveScenarioIndex < DispersionScenarios.Count
                ? DispersionScenarios[ActiveScenarioIndex] : null;
            set
            {
                if (value == null) return;
                if (DispersionScenarios.Count == 0)
                    DispersionScenarios.Add(value);
                else if (ActiveScenarioIndex < DispersionScenarios.Count)
                    DispersionScenarios[ActiveScenarioIndex] = value;
            }
        }

        /// <summary>
        /// Gets or sets the list of monitor points used for concentration probing.
        /// </summary>
        public List<MonitorPoint3D> MonitorPoints { get; set; }

        /// <summary>
        /// Gets or sets the wind rose data associated with the scene.
        /// </summary>
        public WindRoseData WindRose { get; set; }

        /// <summary>
        /// Gets or sets the list of saved camera presets.
        /// </summary>
        public List<CameraPreset> CameraPresets { get; set; }

        /// <summary>
        /// Gets or sets the fire scenario configuration for the scene.
        /// </summary>
        public FireScenario FireScenario { get; set; }

        /// <summary>
        /// Gets or sets the list of gas detectors placed in the scene.
        /// </summary>
        public List<GasDetector3D> GasDetectors { get; set; }

        /// <summary>
        /// Gets or sets the list of completed CFD simulation entries.
        /// </summary>
        public List<CfdSimulationEntry> CfdSimulations { get; set; }

        /// <summary>
        /// Gets or sets the list of pre-computed wind field scenarios available to dispersion runs.
        /// </summary>
        public List<WindFieldScenario> WindFieldScenarios { get; set; }

        /// <summary>
        /// Project-wide settings (defaults for new sources, simulations, etc.).
        /// </summary>
        public ProjectSettings GeneralSettings { get; set; }

        /// <summary>
        /// Project gas library — pure substances and mixtures, referenced by sources via <see cref="ReleaseSource3D.GasRefId"/>.
        /// </summary>
        public List<GasLibraryItem> GasLibrary { get; set; }

        /// <summary>
        /// Top-level sources, decoupled from dispersion scenarios. Used by Simulations.
        /// </summary>
        public List<ReleaseSource3D> TopLevelSources { get; set; }

        /// <summary>
        /// Project-level simulations (snapshot pairings of Source × WindField).
        /// </summary>
        public List<Simulation> Simulations { get; set; }

        /// <summary>
        /// Gets or sets the grid spacing in meters.
        /// </summary>
        public double GridSpacing { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether objects snap to the grid.
        /// </summary>
        public bool SnapToGrid { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="Scene3D"/> class with default values.
        /// </summary>
        public Scene3D()
        {
            Name = "New Project";
            Description = string.Empty;
            WorkPlanes = new List<WorkPlane> { WorkPlane.CreateGroundLevel() };
            CurrentWorkPlane = WorkPlanes[0];
            Decorations = new List<Decoration3D>();
            DispersionScenarios = new List<DispersionScenario>();
            MonitorPoints = new List<MonitorPoint3D>();
            CameraPresets = new List<CameraPreset>();
            FireScenario = new FireScenario();
            GasDetectors = new List<GasDetector3D>();
            CfdSimulations = new List<CfdSimulationEntry>();
            WindFieldScenarios = new List<WindFieldScenario>();
            GeneralSettings = new ProjectSettings();
            GasLibrary = new List<GasLibraryItem>();
            TopLevelSources = new List<ReleaseSource3D>();
            Simulations = new List<Simulation>();
            GridSpacing = 5.0;
            SnapToGrid = true;
        }

        /// <summary>
        /// Finds a decoration by its unique identifier.
        /// </summary>
        /// <param name="id">The unique identifier of the decoration to find.</param>
        /// <returns>The matching <see cref="Decoration3D"/>, or <c>null</c> if not found.</returns>
        public Decoration3D FindDecoration(string id)
        {
            return Decorations.FirstOrDefault(d => d.Id == id);
        }

        /// <summary>
        /// Clears all decorations, scenarios, monitor points, gas detectors, and resets the fire scenario.
        /// </summary>
        public void Clear()
        {
            Decorations.Clear();
            DispersionScenarios.Clear();
            ActiveScenarioIndex = 0;
            MonitorPoints.Clear();
            FireScenario = new FireScenario();
            GasDetectors.Clear();
        }
    }
}
