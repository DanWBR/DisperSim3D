using System;
using System.ComponentModel;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;

namespace DisperSim3D.PropertyAdapters
{
    public class ReleaseSourcePropertyAdapter
    {
        private readonly ReleaseSource3D _src;
        private readonly Action _onChanged;

        public ReleaseSourcePropertyAdapter(ReleaseSource3D source, Action onChanged)
        {
            _src = source;
            _onChanged = onChanged;
        }

        [Category("General")]
        [DisplayName("Name")]
        public string Name
        {
            get => _src.Name;
            set { _src.Name = value; _onChanged?.Invoke(); }
        }

        [Category("General")]
        [DisplayName("ID")]
        [ReadOnly(true)]
        public string Id => _src.Id;

        [Category("Position")]
        [DisplayName("X (m)")]
        public double PosX
        {
            get => _src.Position.X;
            set { _src.Position = new Point3D(value, _src.Position.Y, _src.Position.Z); _onChanged?.Invoke(); }
        }

        [Category("Position")]
        [DisplayName("Y (m)")]
        public double PosY
        {
            get => _src.Position.Y;
            set { _src.Position = new Point3D(_src.Position.X, value, _src.Position.Z); _onChanged?.Invoke(); }
        }

        [Category("Position")]
        [DisplayName("Z (m)")]
        public double PosZ
        {
            get => _src.Position.Z;
            set { _src.Position = new Point3D(_src.Position.X, _src.Position.Y, value); _onChanged?.Invoke(); }
        }

        [Category("Position")]
        [DisplayName("Height Offset (m)")]
        [Description("Vertical offset from the base position")]
        public double HeightOffset
        {
            get => _src.ReleaseHeightOffset;
            set { _src.ReleaseHeightOffset = Math.Max(0, value); _onChanged?.Invoke(); }
        }

        [Category("Release Direction")]
        [DisplayName("Azimuth (°)")]
        [Description("0=North(+Y), 90=East(+X), 180=South, 270=West")]
        public double Azimuth
        {
            get => _src.ReleaseAzimuthDeg;
            set { _src.ReleaseAzimuthDeg = ((value % 360) + 360) % 360; _onChanged?.Invoke(); }
        }

        [Category("Release Direction")]
        [DisplayName("Elevation (°)")]
        [Description("-90=down, 0=horizontal, +90=up")]
        public double Elevation
        {
            get => _src.ReleaseElevationDeg;
            set { _src.ReleaseElevationDeg = Math.Max(-90, Math.Min(90, value)); _onChanged?.Invoke(); }
        }

        [Category("Release")]
        [DisplayName("Release Rate (kg/s)")]
        public double ReleaseRate
        {
            get => _src.ReleaseRateKgPerS;
            set { _src.ReleaseRateKgPerS = Math.Max(0.0001, value); _onChanged?.Invoke(); }
        }

        [Category("Release")]
        [DisplayName("Duration (s)")]
        public double Duration
        {
            get => _src.ReleaseDurationS;
            set { _src.ReleaseDurationS = Math.Max(1, value); _onChanged?.Invoke(); }
        }

        [Category("Release")]
        [DisplayName("Puff Interval (s)")]
        public double PuffInterval
        {
            get => _src.PuffIntervalS;
            set { _src.PuffIntervalS = Math.Max(0.1, value); _onChanged?.Invoke(); }
        }

        [Category("Gas")]
        [DisplayName("Gas Name")]
        [ReadOnly(true)]
        public string GasName => _src.Gas != null ? _src.Gas.Name : "";

        [Category("Gas")]
        [DisplayName("Molar Mass (kg/mol)")]
        public double MolarMass
        {
            get => _src.Gas != null ? _src.Gas.MolarMass : 0;
            set { if (_src.Gas != null) { _src.Gas.MolarMass = value; _onChanged?.Invoke(); } }
        }

        [Category("Gas")]
        [DisplayName("LFL (kg/m³)")]
        public double LFL
        {
            get => _src.Gas != null ? _src.Gas.LFL : 0;
            set { if (_src.Gas != null) { _src.Gas.LFL = value; _onChanged?.Invoke(); } }
        }

        [Category("Gas")]
        [DisplayName("IDLH (kg/m³)")]
        public double IDLH
        {
            get => _src.Gas != null ? _src.Gas.IDLH : 0;
            set { if (_src.Gas != null) { _src.Gas.IDLH = value; _onChanged?.Invoke(); } }
        }

        [Category("Jet / Orifice")]
        [DisplayName("Orifice Diameter (mm)")]
        [Description("Leak orifice or stack diameter. Set > 0 to enable jet calculation")]
        public double StackDiameter
        {
            get => _src.StackDiameterM * 1000.0;
            set { _src.StackDiameterM = Math.Max(0, value) / 1000.0; _onChanged?.Invoke(); }
        }

        [Category("Jet / Orifice")]
        [DisplayName("Exit Velocity (m/s)")]
        [Description("Override: set > 0 to use directly instead of calculating from rate + diameter")]
        public double ExitVelocity
        {
            get => _src.ExitVelocityMPerS;
            set { _src.ExitVelocityMPerS = Math.Max(0, value); _onChanged?.Invoke(); }
        }

        [Category("Jet / Orifice")]
        [DisplayName("Exit Temperature (°C)")]
        public double ExitTemperature
        {
            get => _src.ExitTemperatureK - 273.15;
            set { _src.ExitTemperatureK = Math.Max(-173, value) + 273.15; _onChanged?.Invoke(); }
        }

        [Category("Jet / Orifice")]
        [DisplayName("Computed Velocity (m/s)")]
        [Description("Calculated from Release Rate / (density * orifice area)")]
        [ReadOnly(true)]
        public double ComputedVelocity => Math.Round(_src.ComputedExitVelocity, 2);

        [Category("High Pressure Leak")]
        [DisplayName("Enable HP Leak")]
        [Description("Calculate rate from vessel pressure, temperature and orifice")]
        public bool HPLeakEnabled
        {
            get => _src.HighPressureLeak != null;
            set
            {
                if (value && _src.HighPressureLeak == null)
                {
                    _src.HighPressureLeak = new Core.HighPressureLeakParams
                    {
                        GasMolarMassKgMol = _src.Gas != null ? _src.Gas.MolarMass : 0.016,
                        OrificeDiameterM = _src.StackDiameterM > 0 ? _src.StackDiameterM : 0.01
                    };
                }
                else if (!value)
                {
                    _src.HighPressureLeak = null;
                }
                _onChanged?.Invoke();
            }
        }

        [Category("High Pressure Leak")]
        [DisplayName("Vessel Pressure (bar)")]
        public double HPVesselPressure
        {
            get => _src.HighPressureLeak != null ? _src.HighPressureLeak.VesselPressurePa / 1e5 : 0;
            set { if (_src.HighPressureLeak != null) { _src.HighPressureLeak.VesselPressurePa = Math.Max(1.01325, value) * 1e5; _onChanged?.Invoke(); } }
        }

        [Category("High Pressure Leak")]
        [DisplayName("Vessel Temperature (°C)")]
        public double HPVesselTemperature
        {
            get => _src.HighPressureLeak != null ? _src.HighPressureLeak.VesselTemperatureK - 273.15 : 0;
            set { if (_src.HighPressureLeak != null) { _src.HighPressureLeak.VesselTemperatureK = Math.Max(-173, value) + 273.15; _onChanged?.Invoke(); } }
        }

        [Category("High Pressure Leak")]
        [DisplayName("Vessel Volume (m³)")]
        public double HPVesselVolume
        {
            get => _src.HighPressureLeak != null ? _src.HighPressureLeak.VesselVolumeM3 : 0;
            set { if (_src.HighPressureLeak != null) { _src.HighPressureLeak.VesselVolumeM3 = Math.Max(0.001, value); _onChanged?.Invoke(); } }
        }

        [Category("High Pressure Leak")]
        [DisplayName("Gas Gamma (Cp/Cv)")]
        public double HPGasGamma
        {
            get => _src.HighPressureLeak != null ? _src.HighPressureLeak.GasGamma : 0;
            set { if (_src.HighPressureLeak != null) { _src.HighPressureLeak.GasGamma = Math.Max(1.01, Math.Min(2.0, value)); _onChanged?.Invoke(); } }
        }

        [Category("High Pressure Leak")]
        [DisplayName("Computed Rate (kg/s)")]
        [Description("Mass flow rate from HP leak model (choked or unchoked)")]
        [ReadOnly(true)]
        public double HPComputedRate
        {
            get
            {
                if (_src.HighPressureLeak == null) return 0;
                return Math.Round(Core.HighPressureLeakModel.MassFlowRate(_src.HighPressureLeak), 4);
            }
        }

        [Category("High Pressure Leak")]
        [DisplayName("Flow Regime")]
        [ReadOnly(true)]
        public string HPFlowRegime
        {
            get
            {
                if (_src.HighPressureLeak == null) return "";
                return Core.HighPressureLeakModel.IsChoked(_src.HighPressureLeak) ? "Choked (sonic)" : "Unchoked (subsonic)";
            }
        }
    }
}
