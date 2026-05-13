#nullable enable
using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>ImportModelDialog</c>.
    /// Edits position, rotation and scale for an imported 3D model. The
    /// WPF version embedded a HelixToolkit preview viewport; that depends on
    /// WPF + Media3D and isn't available cross-platform. This port keeps
    /// the parameter UI 1:1 and surfaces the same result properties so a
    /// future Avalonia 3D importer (OpenTK / Silk.NET) can reuse it without
    /// any changes. The preview block is replaced with a hint banner.
    ///
    /// Use <see cref="ModelInfo"/> to pass lightweight model metadata (size,
    /// origin, triangle count) — no WPF types are required.
    /// </summary>
    public partial class ImportModelDialog : Window
    {
        /// <summary>Lightweight stand-in for the WPF <c>Model3DGroup.Bounds</c>
        /// + triangle count. Lets the dialog show its info card and pick a
        /// sensible auto-scale without pulling in HelixToolkit.</summary>
        public readonly record struct ModelInfo(
            double OffsetX, double OffsetY, double OffsetZ,
            double SizeX, double SizeY, double SizeZ,
            int TriangleCount);

        public double PosX => (double)(NudPosX.Value ?? 0m);
        public double PosY => (double)(NudPosY.Value ?? 0m);
        public double PosZ => (double)(NudPosZ.Value ?? 0m);
        public double RotX => (double)(NudRotX.Value ?? 0m);
        public double RotY => (double)(NudRotY.Value ?? 0m);
        public double RotZ => (double)(NudRotZ.Value ?? 0m);
        public double ModelScale => (double)(NudScale.Value ?? 1m);

        // Guard against the NUD ↔ slider feedback loop. Without this, setting
        // NudScale.Value from ScaleSlider_ValueChanged would re-fire the
        // slider's own ValueChanged via the value change, and vice versa.
        private bool _updatingScale;

        public ImportModelDialog() : this("(no file)", default, 200.0) { }

        public ImportModelDialog(string fileName, ModelInfo info, double groundSize = 200.0)
        {
            InitializeComponent();

            Title = "Import 3D Model - " + Path.GetFileName(fileName ?? "");

            // Auto-scale heuristic: target ~40% of the ground grid. Same
            // rule the WinForms importer uses so projects round-trip with
            // identical default placement.
            double gs = groundSize > 0 ? groundSize : 200.0;
            double maxExt = Math.Max(info.SizeX, Math.Max(info.SizeY, info.SizeZ));
            double defaultScale = maxExt > 0.001 ? gs * 0.4 / maxExt : 1.0;

            var inv = CultureInfo.InvariantCulture;
            LblInfo.Text = string.Format(inv,
                "Triangles: {0}\nSize: {1:F1} × {2:F1} × {3:F1} m\nAuto scale: {4:F4}",
                info.TriangleCount, info.SizeX, info.SizeY, info.SizeZ, defaultScale);

            NudScale.Value = (decimal)Math.Max(0.001, Math.Min(100.0, defaultScale));
            ScaleSlider.Value = ScaleToTrack(defaultScale);

            NudScale.ValueChanged += (_, _) =>
            {
                if (_updatingScale) return;
                _updatingScale = true;
                ScaleSlider.Value = ScaleToTrack((double)(NudScale.Value ?? 1m));
                _updatingScale = false;
            };
        }

        // Log-mapped scale slider: [1..200] → log10 scale in [-3 .. 2] →
        // [0.001 .. 100]. Same math as the WinForms TrackBar mapping.
        private static int ScaleToTrack(double scale)
        {
            double log = Math.Log10(Math.Max(scale, 1e-9));
            int val = (int)((log + 3) * 40);
            return Math.Max(1, Math.Min(200, val));
        }

        private static double TrackToScale(double track)
        {
            double log = track / 40.0 - 3.0;
            return Math.Pow(10, log);
        }

        private void ScaleSlider_ValueChanged(object? sender,
            global::Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_updatingScale) return;
            _updatingScale = true;
            double sv = TrackToScale(e.NewValue);
            NudScale.Value = (decimal)Math.Max(0.001, Math.Min(100.0, sv));
            _updatingScale = false;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e) => Close(true);
    }
}
