using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// One field a solver writes beside its species snapshots, in the flat-bin layout
    /// (docs/file-format.md): <c>{t:F3}_{Suffix}.bin</c>, little-endian doubles on the
    /// simulation's grid, i outermost and k innermost.
    /// </summary>
    public sealed class ResultChannel
    {
        public ResultChannel(string suffix, string name, string unit, bool lowIsHazard = false)
        {
            Suffix = suffix ?? "";
            Name = name;
            Unit = unit;
            LowIsHazard = lowIsHazard;
        }

        /// <summary>File-name suffix after the time; empty for the species.</summary>
        public string Suffix { get; }

        /// <summary>Name shown in lists and reports.</summary>
        public string Name { get; }

        public string Unit { get; }

        /// <summary>
        /// True when the hazardous extreme is the minimum (oxygen, visibility), so a
        /// "peak over time" takes the lowest value each cell reached.
        /// </summary>
        public bool LowIsHazard { get; }

        /// <summary>The file name of this channel at time <paramref name="timeS"/>.</summary>
        public string FileName(double timeS) =>
            timeS.ToString("F3", CultureInfo.InvariantCulture) + (Suffix.Length > 0 ? "_" + Suffix : "") + ".bin";

        public override string ToString() => Name + (Unit.Length > 0 ? " (" + Unit + ")" : "");
    }

    /// <summary>
    /// The result channels DisperSim reads from a simulation's folder. A solver writes the
    /// ones it computes; a missing channel falls back to what DisperSim derives itself
    /// (the analytic fire models for radiation, nothing for the rest).
    /// </summary>
    public static class ResultChannels
    {
        /// <summary><c>{t:F3}.bin</c>: the released species — kg/m³ for the extender contract,
        /// the analytic engines and imported results; the mass fraction for the OpenFOAM and
        /// FluidX3D solvers (see <see cref="FieldTransform.StoresSpeciesAsKgPerM3"/>).</summary>
        public static readonly ResultChannel Species = new ResultChannel("", "Species", "");

        /// <summary><c>{t:F3}_T.bin</c>: temperature, K.</summary>
        public static readonly ResultChannel Temperature = new ResultChannel("T", "Temperature", "K");

        /// <summary><c>{t:F3}_Q.bin</c>: incident thermal radiation, kW/m², replacing the analytic fire models.</summary>
        public static readonly ResultChannel Radiation = new ResultChannel("Q", "Thermal radiation", "kW/m²");

        /// <summary><c>{t:F3}_Soot.bin</c>: soot mass concentration, kg/m³.</summary>
        public static readonly ResultChannel Soot = new ResultChannel("Soot", "Soot", "kg/m³");

        /// <summary><c>{t:F3}_CO.bin</c>: carbon monoxide, volume fraction.</summary>
        public static readonly ResultChannel CarbonMonoxide = new ResultChannel("CO", "CO", "mol/mol");

        /// <summary><c>{t:F3}_CO2.bin</c>: carbon dioxide, volume fraction.</summary>
        public static readonly ResultChannel CarbonDioxide = new ResultChannel("CO2", "CO2", "mol/mol");

        /// <summary><c>{t:F3}_O2.bin</c>: oxygen, volume fraction (0.2095 in clean air).</summary>
        public static readonly ResultChannel Oxygen = new ResultChannel("O2", "O2", "mol/mol", lowIsHazard: true);

        /// <summary><c>{t:F3}_Vis.bin</c>: visibility through smoke, m — the solver's cap in clean air.</summary>
        public static readonly ResultChannel Visibility = new ResultChannel("Vis", "Visibility", "m", lowIsHazard: true);

        /// <summary>Suffix of the per-triangle heat load, <c>{t:F3}_surface.bin</c>; see <see cref="SurfaceHeatLoad"/>.</summary>
        public const string SurfaceSuffix = "surface";

        private static readonly object Gate = new object();
        private static readonly List<ResultChannel> Registered = new List<ResultChannel>
        {
            Species, Temperature, Radiation, Soot, CarbonMonoxide, CarbonDioxide, Oxygen, Visibility
        };

        /// <summary>Every known channel, the built-in ones first.</summary>
        public static IReadOnlyList<ResultChannel> All
        {
            get { lock (Gate) return Registered.ToList(); }
        }

        /// <summary>
        /// Adds a channel a solver writes beyond the built-in ones, so it is listed and
        /// bundled with its simulation; ignored when the suffix is taken.
        /// </summary>
        public static void Register(ResultChannel channel)
        {
            if (channel == null || channel.Suffix.Length == 0
                || string.Equals(channel.Suffix, SurfaceSuffix, StringComparison.OrdinalIgnoreCase)) return;
            lock (Gate)
                if (!Registered.Any(c => string.Equals(c.Suffix, channel.Suffix, StringComparison.OrdinalIgnoreCase)))
                    Registered.Add(channel);
        }

        /// <summary>The channel with this suffix (case-insensitive), or null.</summary>
        public static ResultChannel Find(string suffix)
        {
            suffix ??= "";
            lock (Gate)
                return Registered.FirstOrDefault(c => string.Equals(c.Suffix, suffix, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The stored channel a view or sensor property reads directly, or null when it reads none.</summary>
        public static ResultChannel For(ViewFieldProperty property)
        {
            switch (property)
            {
                case ViewFieldProperty.Temperature: return Temperature;
                case ViewFieldProperty.ThermalRadiationKwM2: return Radiation;
                case ViewFieldProperty.SootDensityKgM3: return Soot;
                case ViewFieldProperty.COVolumeFraction: return CarbonMonoxide;
                case ViewFieldProperty.CO2VolumeFraction: return CarbonDioxide;
                case ViewFieldProperty.O2VolumeFraction: return Oxygen;
                case ViewFieldProperty.VisibilityM: return Visibility;
                default: return null;
            }
        }
    }
}
