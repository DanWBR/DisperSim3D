using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Reflection-based thin wrapper around <c>DWSIM.Automation.FluentAPI</c>.
    ///
    /// We don't take a compile-time reference on DWSIM because the installer ships
    /// 200+ DLLs (CoolProp, GTK#, SkiaSharp, MathNet, …) that would bloat the
    /// DisperSim3D output. Instead, the user points <see cref="AppSettings.DwsimInstallPath"/>
    /// at their DWSIM install dir; we install an <c>AssemblyResolve</c> handler that
    /// probes that directory and invoke FluentAPI types via reflection.
    ///
    /// All public methods return primitive types (double / string / List&lt;…&gt;) so
    /// callers don't need DWSIM types either.
    /// </summary>
    public static class DwsimThermo
    {
        private static bool _resolverInstalled;
        private static bool _initialised;
        private static Assembly _fluentApi;
        private static Type _flowsheetType;
        private static Type _propertyPackagesType;
        private static string _installPath = "";
        private static List<string> _availableCompounds;

        /// <summary>True when the DWSIM install dir is configured and FluentAPI loaded.</summary>
        public static bool IsAvailable => _initialised && _fluentApi != null;

        /// <summary>Last error message from a failed Initialize/load attempt.</summary>
        public static string LastError { get; private set; } = "";

        /// <summary>Initialises the resolver and loads <c>DWSIM.Automation.FluentAPI.dll</c>.
        /// Returns true on success; sets <see cref="LastError"/> on failure.</summary>
        public static bool Initialize(string dwsimInstallPath)
        {
            if (_initialised && _installPath == dwsimInstallPath && _fluentApi != null) return true;
            LastError = "";
            try
            {
                if (string.IsNullOrEmpty(dwsimInstallPath))
                { LastError = "DWSIM install path is not set."; return false; }
                if (!Directory.Exists(dwsimInstallPath))
                { LastError = "DWSIM install path does not exist: " + dwsimInstallPath; return false; }

                string fluentDll = Path.Combine(dwsimInstallPath, "DWSIM.Automation.FluentAPI.dll");
                if (!File.Exists(fluentDll))
                { LastError = "DWSIM.Automation.FluentAPI.dll not found at " + dwsimInstallPath; return false; }

                // Path changed — any prior cached Flowsheet was built against the
                // OLD assemblies and is stale; drop it.
                if (_installPath != dwsimInstallPath) ResetFlowsheetCache();
                _installPath = dwsimInstallPath;
                InstallResolver(dwsimInstallPath);

                // DWSIM's database loaders and some property packages probe the process
                // CWD for configuration / data files. Point it at the install dir so
                // ChemSep, CoolProp, ChEDL etc. all find their resources. Most embedded
                // databases don't need this, but compiled extensions and addon DBs do.
                try { Environment.CurrentDirectory = dwsimInstallPath; } catch { }

                _fluentApi = Assembly.LoadFrom(fluentDll);

                // Pre-load the critical thermodynamics DLLs eagerly. Lazy loading via the
                // AssemblyResolve handler works for most cases, but the Automation3
                // constructor reflects over property packages and database providers in
                // tight loops — pre-binding avoids dozens of resolver dispatches and
                // surfaces FileNotFoundException for any missing dependency right here.
                string[] eagerDlls =
                {
                    "DWSIM.Interfaces.dll",
                    "DWSIM.GlobalSettings.dll",
                    "DWSIM.SharedClasses.dll",
                    "DWSIM.MathOps.dll",
                    "DWSIM.FlowsheetBase.dll",
                    "DWSIM.FlowsheetSolver.dll",
                    "DWSIM.Thermodynamics.dll",
                    "DWSIM.UnitOperations.dll",
                    "DWSIM.Automation.dll"
                };
                foreach (var dll in eagerDlls)
                {
                    string p = Path.Combine(dwsimInstallPath, dll);
                    if (File.Exists(p))
                    {
                        try { Assembly.LoadFrom(p); } catch { /* let resolver retry lazily */ }
                    }
                }

                _flowsheetType = _fluentApi.GetType("DWSIM.Automation.FluentAPI.Flowsheet", throwOnError: false);
                _propertyPackagesType = _fluentApi.GetType("DWSIM.Automation.FluentAPI.PropertyPackages", throwOnError: false);
                if (_flowsheetType == null)
                { LastError = "Couldn't resolve Flowsheet type from FluentAPI."; return false; }
                _initialised = true;
                return true;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                _initialised = false;
                return false;
            }
        }

        private static void InstallResolver(string baseDir)
        {
            if (_resolverInstalled) return;
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                try
                {
                    string name = new AssemblyName(e.Name).Name + ".dll";
                    string[] candidateDirs =
                    {
                        baseDir,
                        Path.Combine(baseDir, "extenders"),
                        Path.Combine(baseDir, "extenders2"),
                        Path.Combine(baseDir, "unitops"),
                        Path.Combine(baseDir, "unitops2"),
                        Path.Combine(baseDir, "ppacks"),
                        Path.Combine(baseDir, "ppacks2")
                    };
                    foreach (var dir in candidateDirs)
                    {
                        if (!Directory.Exists(dir)) continue;
                        string p = Path.Combine(dir, name);
                        if (File.Exists(p)) return Assembly.LoadFrom(p);
                    }
                }
                catch { }
                return null;
            };
            _resolverInstalled = true;
        }

        /// <summary>Returns the canonical names of every DWSIM property package
        /// exposed by the FluentAPI <c>PropertyPackages</c> constants class.
        /// Reflection-pulled so we stay version-tolerant (new packages added by
        /// later DWSIM releases surface automatically). Empty when DWSIM isn't
        /// initialised.</summary>
        public static IReadOnlyList<string> AvailablePropertyPackages()
        {
            if (!IsAvailable || _propertyPackagesType == null) return Array.Empty<string>();
            try
            {
                var consts = _propertyPackagesType.GetFields(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));
                var list = consts.Select(f => f.GetRawConstantValue() as string)
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return list;
            }
            catch (Exception ex)
            {
                LastError = "AvailablePropertyPackages: " + ex.Message;
                return Array.Empty<string>();
            }
        }

        /// <summary>Returns every compound name in DWSIM's process-wide catalog. The first
        /// call triggers a one-time database load (~1–2 s); subsequent calls are cached.
        /// When the catalog ends up empty, sets <see cref="LastError"/> with the deepest
        /// inner exception so the user can see WHY the database failed to load.</summary>
        public static IReadOnlyList<string> AvailableCompounds()
        {
            if (!IsAvailable) return Array.Empty<string>();
            if (_availableCompounds != null && _availableCompounds.Count > 0) return _availableCompounds;
            try
            {
                // Need to create an Automation3 instance once so the compound catalog
                // populates. Easiest path: call Flowsheet.Create which triggers Bootstrap.
                var create = _flowsheetType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                object fs;
                try { fs = create.Invoke(null, new object[] { (string)"probe" }); }
                catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }

                // Try the Bootstrap.Automation static path first.
                var bootstrapType = _fluentApi.GetType("DWSIM.Automation.FluentAPI.Bootstrap", throwOnError: false);
                var autoProp = bootstrapType?.GetProperty("Automation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var auto = autoProp?.GetValue(null);
                System.Collections.IDictionary catalog =
                    auto?.GetType().GetProperty("AvailableCompounds")?.GetValue(auto)
                    as System.Collections.IDictionary;

                // Fallback: read from the flowsheet's own catalog (FlowsheetBase mirrors
                // Automation3.AvailableCompounds onto every newly-created flowsheet).
                if (catalog == null || catalog.Count == 0)
                {
                    var innerProp = _flowsheetType.GetProperty("Inner",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var inner = innerProp?.GetValue(fs);
                    catalog = inner?.GetType().GetProperty("AvailableCompounds")?.GetValue(inner)
                              as System.Collections.IDictionary;
                }

                var list = new List<string>();
                if (catalog != null)
                    foreach (var k in catalog.Keys) list.Add(k.ToString());
                list.Sort(StringComparer.OrdinalIgnoreCase);
                _availableCompounds = list;
                if (list.Count == 0)
                {
                    LastError = "DWSIM compound database is empty. Verify DWSIM.Thermodynamics.dll " +
                                "and its embedded ChemSep/CoolProp/Biodiesel databases loaded — " +
                                "missing dependency or wrong install path.";
                }
                return list;
            }
            catch (Exception ex)
            {
                // Drill to the deepest cause so the user sees the actual missing-DLL /
                // FileNotFoundException name instead of a generic "object reference".
                var root = ex;
                while (root.InnerException != null) root = root.InnerException;
                LastError = "AvailableCompounds: " + root.GetType().Name + " — " + root.Message;
                return Array.Empty<string>();
            }
        }

        /// <summary>Constant per-compound properties pulled from DWSIM's database.
        /// All units are SI: M [kg/mol], Tc [K], Pc [Pa], Tb [K]. ω is dimensionless.</summary>
        public sealed class CompoundInfo
        {
            public string Name;
            public double MolarMassKgMol;
            public double CriticalTemperatureK;
            public double CriticalPressurePa;
            public double AcentricFactor;
            public double NormalBoilingPointK;
            public double CriticalVolumeM3Mol;
        }

        private static readonly Dictionary<string, CompoundInfo> _compoundCache =
            new Dictionary<string, CompoundInfo>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Looks up the constant properties of a single compound in DWSIM's
        /// catalog and returns them in SI. Result is cached. Returns null when the
        /// compound is unknown or DWSIM isn't initialised.</summary>
        public static CompoundInfo GetCompoundInfo(string compoundName)
        {
            if (string.IsNullOrEmpty(compoundName) || !IsAvailable) return null;
            if (_compoundCache.TryGetValue(compoundName, out var cached)) return cached;
            try
            {
                // Ensure the catalog is loaded; AvailableCompounds primes Bootstrap.Automation.
                if (_availableCompounds == null) AvailableCompounds();
                var bootstrapType = _fluentApi.GetType("DWSIM.Automation.FluentAPI.Bootstrap", false);
                var autoProp = bootstrapType?.GetProperty("Automation",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                var auto = autoProp?.GetValue(null);
                var availableProp = auto?.GetType().GetProperty("AvailableCompounds");
                var catalog = availableProp?.GetValue(auto) as System.Collections.IDictionary;
                if (catalog == null || !catalog.Contains(compoundName)) return null;
                object cp = catalog[compoundName];
                var ct = cp.GetType();
                var info = new CompoundInfo
                {
                    Name = compoundName,
                    // DWSIM stores Molar_Weight in g/mol; convert to kg/mol.
                    MolarMassKgMol      = GetDouble(ct, cp, "Molar_Weight") / 1000.0,
                    CriticalTemperatureK = GetDouble(ct, cp, "Critical_Temperature"),
                    CriticalPressurePa   = GetDouble(ct, cp, "Critical_Pressure"),
                    AcentricFactor       = GetDouble(ct, cp, "Acentric_Factor"),
                    NormalBoilingPointK  = GetDouble(ct, cp, "Normal_Boiling_Point"),
                    CriticalVolumeM3Mol  = GetDouble(ct, cp, "Critical_Volume") / 1000.0 // L/mol → m³/mol (DWSIM stores L/mol)
                };
                _compoundCache[compoundName] = info;
                return info;
            }
            catch (Exception ex)
            {
                LastError = "GetCompoundInfo: " + ex.Message;
                return null;
            }
        }

        private static double GetDouble(Type t, object obj, string propName)
        {
            try
            {
                var p = t.GetProperty(propName);
                if (p == null) return 0;
                var v = p.GetValue(obj);
                if (v == null) return 0;
                if (v is double d) return d;
                return Convert.ToDouble(v);
            }
            catch { return 0; }
        }

        /// <summary>Container returned by <see cref="ComputeMixtureProperties"/>.</summary>
        public sealed class MixtureProperties
        {
            public double MolarMassKgMol;     // mixture molar mass
            public double DensityKgM3;        // mass density at T, P
            public double ViscosityPaS;       // dynamic viscosity
            public double CpJPerKgK;          // specific heat capacity
            public double VaporFraction;      // single-phase = 1 (gas)
            public double GammaCpCv;          // ratio Cp/Cv
            public string Error;
        }

        // Cached flowsheet — keyed by the sorted compound-set signature so the same
        // mixture (independent of mole-fraction values) reuses the same DWSIM
        // Flowsheet, MaterialStream, and Peng-Robinson 1978 property package across
        // every flash. Recreating these is the slow part: a fresh flowsheet runs
        // Bootstrap, instantiates the PP, registers compounds, builds the stream
        // (~150–400 ms cold) — versus ~5–30 ms for a re-solve of the cached one.
        private static string _cachedKey;
        private static object _cachedFlowsheet;
        private static object _cachedStreamObj;
        private static Type _cachedStreamObjType;
        private static List<string> _cachedSelectedOrder;

        private static string BuildKey(IEnumerable<string> compoundNames)
        {
            var sorted = compoundNames
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
            return string.Join("|", sorted);
        }

        /// <summary>Drops the cached flowsheet so the next call rebuilds it. Call
        /// this if the user changed the DWSIM install path or the FluentAPI is
        /// in a known-bad state.</summary>
        public static void ResetFlowsheetCache()
        {
            _cachedKey = null;
            _cachedFlowsheet = null;
            _cachedStreamObj = null;
            _cachedStreamObjType = null;
            _cachedSelectedOrder = null;
        }

        /// <summary>Runs a Peng-Robinson 1978 flash on the supplied mole-fraction mixture
        /// and returns aggregate mixture properties (M, ρ, μ, Cp, γ). Composition is
        /// normalised before sending to DWSIM. The Flowsheet and its MaterialStream
        /// are cached by compound-set: subsequent calls with the same compounds reuse
        /// them (~10× faster — see <see cref="ResetFlowsheetCache"/>).</summary>
        public static MixtureProperties ComputeMixtureProperties(IDictionary<string, double> moleFractions,
            double temperatureK, double pressurePa)
        {
            var result = new MixtureProperties();
            if (!IsAvailable) { result.Error = "DWSIM not initialised."; return result; }
            if (moleFractions == null || moleFractions.Count == 0)
            { result.Error = "Empty mixture composition."; return result; }
            try
            {
                // Normalise composition.
                double sum = 0; foreach (var v in moleFractions.Values) sum += v;
                if (sum <= 0) { result.Error = "All mole fractions are zero."; return result; }

                string key = BuildKey(moleFractions.Keys);
                object fs;
                object streamObj;
                Type streamObjType;
                List<string> order;

                if (key != _cachedKey || _cachedFlowsheet == null || _cachedStreamObj == null)
                {
                    // 1. Create a fresh headless flowsheet.
                    var create = _flowsheetType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
                    fs = create.Invoke(null, new object[] { (string)"DispersionThermo" });

                    // 2. Add compounds (chained WithCompound calls).
                    var withCompound = _flowsheetType.GetMethod("WithCompound", new[] { typeof(string) });
                    foreach (var c in moleFractions.Keys)
                        withCompound.Invoke(fs, new object[] { c });

                    // 3. Pick property package — user-configurable via AppSettings.
                    //    Defaults to Peng-Robinson 1978 when empty / unset.
                    string ppName = AppSettings.Instance.DwsimPropertyPackage;
                    if (string.IsNullOrWhiteSpace(ppName))
                        ppName = _propertyPackagesType?.GetField("PengRobinson1978",
                            BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
                            ?? "Peng-Robinson 1978 (PR78)";
                    var withPp = _flowsheetType.GetMethod("WithPropertyPackage", new[] { typeof(string) });
                    withPp.Invoke(fs, new object[] { ppName });

                    // 4. Add the material stream once.
                    var addStream = _flowsheetType.GetMethod("AddMaterialStream", new[] { typeof(string) });
                    var streamBuilder = addStream.Invoke(fs, new object[] { "S1" });
                    var streamBuilderType = streamBuilder.GetType();
                    var streamProp = streamBuilderType.GetProperty("Object");
                    streamObj = streamProp.GetValue(streamBuilder);
                    streamObjType = streamObj.GetType();
                    streamObjType.GetMethod("SetMassFlow", new[] { typeof(double) })
                        .Invoke(streamObj, new object[] { 1.0 }); // 1 kg/s basis (constant)

                    // Selected-compound order is fixed once the PP is attached.
                    var innerProp = _flowsheetType.GetProperty("Inner",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var inner = innerProp.GetValue(fs);
                    var selectedProp = inner.GetType().GetProperty("SelectedCompounds");
                    var selected = selectedProp.GetValue(inner) as System.Collections.IDictionary;
                    order = new List<string>();
                    foreach (var k in selected.Keys) order.Add(k.ToString());

                    _cachedKey = key;
                    _cachedFlowsheet = fs;
                    _cachedStreamObj = streamObj;
                    _cachedStreamObjType = streamObjType;
                    _cachedSelectedOrder = order;
                }
                else
                {
                    // Reuse the cached flowsheet/stream verbatim — only T, P, and
                    // composition change between flashes.
                    fs = _cachedFlowsheet;
                    streamObj = _cachedStreamObj;
                    streamObjType = _cachedStreamObjType;
                    order = _cachedSelectedOrder;
                }

                // Update T, P, composition for THIS flash.
                streamObjType.GetMethod("SetTemperature", new[] { typeof(double) })
                    .Invoke(streamObj, new object[] { temperatureK });
                streamObjType.GetMethod("SetPressure", new[] { typeof(double) })
                    .Invoke(streamObj, new object[] { pressurePa });

                var fracArray = new double[order.Count];
                for (int i = 0; i < order.Count; i++)
                {
                    moleFractions.TryGetValue(order[i], out double f);
                    fracArray[i] = f / sum;
                }
                streamObjType.GetMethod("SetOverallComposition", new[] { typeof(double[]) })
                    .Invoke(streamObj, new object[] { fracArray });

                // 5. Solve.
                _flowsheetType.GetMethod("Solve", new Type[0]).Invoke(fs, null);

                // 6. Read aggregate properties off the overall phase (phase index 0).
                // GetProp(propname, phase) returns object[] of doubles. The names DWSIM uses
                // are stable across versions: "molecularWeight", "density", "viscosity",
                // "heatCapacity", "fraction".
                var phasesProp = streamObjType.GetProperty("Phases");
                var phases = phasesProp.GetValue(streamObj) as System.Collections.IDictionary;
                // Phase 0 = overall mixture (covers single-phase gas).
                object overallPhase = phases[0];
                var phaseProps = overallPhase.GetType().GetProperty("Properties").GetValue(overallPhase);
                var phasePropsType = phaseProps.GetType();
                result.MolarMassKgMol = GetD(phasePropsType, phaseProps, "molecularWeight") / 1000.0; // g/mol → kg/mol
                result.DensityKgM3 = GetD(phasePropsType, phaseProps, "density");
                result.ViscosityPaS = GetD(phasePropsType, phaseProps, "viscosity");
                result.CpJPerKgK = GetD(phasePropsType, phaseProps, "heatCapacityCp") * 1000.0; // kJ/kg/K → J/kg/K
                double cv = GetD(phasePropsType, phaseProps, "heatCapacityCv") * 1000.0;
                result.GammaCpCv = cv > 0 ? result.CpJPerKgK / cv : 1.3;

                // Vapor fraction sometimes hides on a separate phase index — defer.
                result.VaporFraction = 1.0;
                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        private static double GetD(Type t, object obj, string propName) => GetDouble(t, obj, propName);
    }
}
