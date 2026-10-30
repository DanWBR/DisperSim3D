#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DisperSim3D.Models;

namespace DisperSim3D.Extensibility
{
    /// <summary>
    /// A dispersion solver shipped in an extender assembly. It shows up next to the
    /// built-in solvers as <see cref="CfdSolverType.Extender"/> with its
    /// <see cref="Id"/> stored on the simulation, and runs through the same paths:
    /// the simulation managers of both desktop UIs, the CLI and the validation harness.
    /// </summary>
    public interface ISolverExtender
    {
        /// <summary>Stable identifier, saved in project files; never rename it.</summary>
        string Id { get; }

        /// <summary>Six upper-case characters shown as the solver tag, unique across all solvers.</summary>
        string Code { get; }

        /// <summary>Name shown in solver lists.</summary>
        string DisplayName { get; }

        /// <summary>Names accepted by the CLI <c>-s</c> option and by a benchmark's <c>solver</c> field (case-insensitive).</summary>
        IReadOnlyList<string> Aliases { get; }

        /// <summary>True when the result is one converged snapshot rather than a time series.</summary>
        bool IsSteady { get; }

        /// <summary>
        /// True when the solver reads the simulation's computed wind field, which then has to be
        /// Ready before a run; false when it takes only the wind field's meteorology.
        /// </summary>
        bool ReadsWindField => true;

        /// <summary>
        /// Runs the scenario to completion on the calling thread. Reports progress through
        /// the request and stops when its cancellation token is set.
        /// </summary>
        SolverExtenderResult Run(SolverExtenderRequest request);
    }

    /// <summary>
    /// Optional, on an <see cref="IExtenderCollection"/>: the solvers the assembly provides.
    /// A collection may have no menu commands and only solvers.
    /// </summary>
    public interface ISolverExtenderCollection
    {
        IReadOnlyList<ISolverExtender> Solvers { get; }
    }

    /// <summary>What a solver extender gets to run one scenario.</summary>
    public sealed class SolverExtenderRequest
    {
        public SolverExtenderRequest(Scene3D scene, DispersionScenario scenario, CfdConfiguration config,
            IReadOnlyList<BoundingBox> obstacles, Action<double, string>? progress, CancellationToken cancel)
        {
            Scene = scene;
            Scenario = scenario;
            Config = config;
            Obstacles = obstacles;
            Progress = progress;
            Cancel = cancel;
        }

        public Scene3D Scene { get; }

        /// <summary>The scenario to run: meteorology, sources, domain and grid size.</summary>
        public DispersionScenario Scenario { get; }

        public CfdConfiguration Config { get; }

        /// <summary>Obstacles as world-axis boxes, as the other grid solvers receive them.</summary>
        public IReadOnlyList<BoundingBox> Obstacles { get; }

        /// <summary>Progress (0 to 1, or negative for "no change") and a status line.</summary>
        public Action<double, string>? Progress { get; }

        public CancellationToken Cancel { get; }
    }

    /// <summary>The outcome of a solver extender run.</summary>
    public sealed class SolverExtenderResult
    {
        public bool Success { get; set; }

        public string? Error { get; set; }

        /// <summary>
        /// Concentration snapshots on the scenario's uniform grid — domain
        /// [−D, D]² × [0, height], kg/m³ — as the 3D view, monitors and detectors read them.
        /// </summary>
        public OpenFoamResult? Result { get; set; }

        /// <summary>Folder holding the run's files, if any.</summary>
        public string? CasePath { get; set; }

        /// <summary>
        /// Optional exact evaluation of the last snapshot at a point, in kg/m³. The
        /// validation harness samples sensors through it instead of the resampled grid.
        /// </summary>
        public Func<double, double, double, double>? Concentration { get; set; }

        /// <summary>Optional evaluation of the release mass fraction at a point.</summary>
        public Func<double, double, double, double>? MassFraction { get; set; }

        /// <summary>Optional velocity field of the run.</summary>
        public WindField3D? Wind { get; set; }

        public static SolverExtenderResult Fail(string error) => new SolverExtenderResult { Success = false, Error = error };
    }

    /// <summary>
    /// The solver extenders available in this process: those the extender folders
    /// provide (<see cref="ExtenderLoader.LoadAll"/>) plus any registered in code.
    /// </summary>
    public static class SolverExtenderRegistry
    {
        private static readonly object Gate = new object();
        private static List<ISolverExtender>? _solvers;

        public static IReadOnlyList<ISolverExtender> All
        {
            get
            {
                lock (Gate)
                {
                    if (_solvers == null)
                    {
                        _solvers = new List<ISolverExtender>();
                        try
                        {
                            foreach (var c in ExtenderLoader.LoadAll().Collections)
                                if (c is ISolverExtenderCollection sc && sc.Solvers != null)
                                    foreach (var s in sc.Solvers) AddUnique(s);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine("[SolverExtenders] " + ex);
                        }
                    }
                    return _solvers.ToList();
                }
            }
        }

        /// <summary>Adds a solver from code (tests, hosts that embed one); ignored when its ID is taken.</summary>
        public static void Register(ISolverExtender solver)
        {
            _ = All;
            lock (Gate) AddUnique(solver);
        }

        private static void AddUnique(ISolverExtender s)
        {
            if (s == null || string.IsNullOrWhiteSpace(s.Id)) return;
            if (_solvers!.Any(x => string.Equals(x.Id, s.Id, StringComparison.OrdinalIgnoreCase))) return;
            _solvers!.Add(s);
        }

        /// <summary>The solver with this ID, or null.</summary>
        public static ISolverExtender? Find(string? id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The solver whose ID, code or one of its aliases is <paramref name="name"/>, or null.</summary>
        public static ISolverExtender? FindByName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            return All.FirstOrDefault(s =>
                string.Equals(s.Id, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(s.Code, name, StringComparison.OrdinalIgnoreCase)
                || (s.Aliases != null && s.Aliases.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase))));
        }
    }
}
