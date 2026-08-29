#nullable enable
using System;
using System.Globalization;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Core;

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
        /// sensible starting unit without pulling in HelixToolkit.</summary>
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

        /// <summary>The unit the user settled on, for the status line.</summary>
        public ModelUnit SelectedUnit => (ModelUnit)Math.Max(0, CboUnit.SelectedIndex);

        // Guard against the NUD ↔ slider ↔ combo feedback loop. Without this,
        // setting NudScale.Value from ScaleSlider_ValueChanged would re-fire the
        // slider's own ValueChanged via the value change, and vice versa.
        private bool _updatingScale;

        private ModelInfo _info;

        public ImportModelDialog() : this("(no file)", default, 200.0) { }

        public ImportModelDialog(string fileName, ModelInfo info, double groundSize = 200.0)
        {
            InitializeComponent();

            Title = "Import 3D Model - " + Path.GetFileName(fileName ?? "");
            _info = info;

            // STL and OBJ carry no unit, and the scene works in metres. Rather
            // than normalise the model to some fraction of the grid — which hides
            // an author's unit mistake instead of surfacing it — the dialog picks
            // the likeliest unit and asks the user to confirm. Same rule as the
            // WinForms importer, so projects round-trip identically.
            double maxExt = Math.Max(info.SizeX, Math.Max(info.SizeY, info.SizeZ));
            var guess = ModelUnits.Guess(maxExt);
            double defaultScale = ModelUnits.FactorFor(guess);

            foreach (var label in ModelUnits.Labels)
                CboUnit.Items.Add(label);

            _updatingScale = true;
            CboUnit.SelectedIndex = (int)guess;
            NudScale.Value = (decimal)defaultScale;
            ScaleSlider.Value = ScaleToTrack(defaultScale);
            _updatingScale = false;

            LblInfo.Text = BuildInfoText(defaultScale);

            NudScale.ValueChanged += (_, _) =>
            {
                double scale = (double)(NudScale.Value ?? 1m);
                LblInfo.Text = BuildInfoText(scale);
                if (_updatingScale) return;
                _updatingScale = true;
                ScaleSlider.Value = ScaleToTrack(scale);
                // Dialling the value away from a named unit means the user is no
                // longer asserting that unit, so the combo drops to Custom rather
                // than continuing to claim one.
                CboUnit.SelectedIndex = (int)ModelUnits.Match(scale);
                _updatingScale = false;
            };
        }

        /// <summary>
        /// Triangle count, the raw numbers in the file, and the size the model will
        /// actually have in the scene. The last line is the one that catches a wrong
        /// unit: a valve that reads 300 m tall is not a valve.
        /// </summary>
        private string BuildInfoText(double scale)
        {
            var inv = CultureInfo.InvariantCulture;
            return string.Format(inv,
                "Triangles: {0}\nFile size: {1:F1} × {2:F1} × {3:F1} units\nIn the scene: {4:F1} × {5:F1} × {6:F1} m",
                _info.TriangleCount, _info.SizeX, _info.SizeY, _info.SizeZ,
                _info.SizeX * scale, _info.SizeY * scale, _info.SizeZ * scale);
        }

        // Log-mapped scale slider: [1..200] → log10 scale in [-4 .. 4] →
        // [1e-4 .. 1e4]. Wide enough to reach kilometres at one end and to
        // rescue a model authored in thousandths at the other. Same math as
        // the WinForms mapping.
        private static double ScaleToTrack(double scale)
        {
            double log = Math.Log10(Math.Max(scale, 1e-9));
            double val = (log + 4) * 25;
            return Math.Max(1, Math.Min(200, val));
        }

        private static double TrackToScale(double track)
        {
            double log = track / 25.0 - 4.0;
            return Math.Pow(10, log);
        }

        private void ScaleSlider_ValueChanged(object? sender,
            global::Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (_updatingScale) return;
            _updatingScale = true;
            double sv = TrackToScale(e.NewValue);
            sv = Math.Max(0.000001, Math.Min(100000.0, sv));
            NudScale.Value = (decimal)sv;
            LblInfo.Text = BuildInfoText(sv);
            CboUnit.SelectedIndex = (int)ModelUnits.Match(sv);
            _updatingScale = false;
        }

        private void CboUnit_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            if (_updatingScale) return;
            var picked = (ModelUnit)Math.Max(0, CboUnit.SelectedIndex);
            if (picked == ModelUnit.Custom) return;  // the user drives the value directly

            _updatingScale = true;
            double factor = ModelUnits.FactorFor(picked);
            NudScale.Value = (decimal)factor;
            ScaleSlider.Value = ScaleToTrack(factor);
            LblInfo.Text = BuildInfoText(factor);
            _updatingScale = false;
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e) => Close(true);
    }
}
