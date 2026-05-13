#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>DispersionStudyDialog</c>.
    /// Creates or edits a <see cref="DispersionStudy"/>: name + description,
    /// detection criterion (quantity + threshold), and which simulations
    /// contribute their final snapshot to the detection target. Replaces the
    /// WinForms ListView with a DataGrid whose first column is a checkbox.
    /// </summary>
    public partial class DispersionStudyDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly DispersionStudy? _editing;
        private readonly ObservableCollection<SimRow> _simRows = new();

        public DispersionStudy? Result { get; private set; }

        public DispersionStudyDialog() : this(new Scene3D()) { }

        public DispersionStudyDialog(Scene3D scene, DispersionStudy? editing = null)
        {
            _scene = scene;
            _editing = editing;
            InitializeComponent();

            Title = editing == null ? "New Dispersion Study" : "Edit Dispersion Study";

            // Populate quantity combo from the enum so future ViewFieldProperty
            // additions appear automatically.
            foreach (ViewFieldProperty vfp in Enum.GetValues(typeof(ViewFieldProperty)))
                CmbQuantity.Items.Add(new ComboBoxItem { Content = vfp.ToString(), Tag = vfp });
            SelectQuantity(ViewFieldProperty.PercentLFL);

            // Build the sim-chooser DataGrid: checkbox + 5 informational columns.
            GridSims.ItemsSource = _simRows;
            GridSims.Columns.Add(new DataGridCheckBoxColumn
            {
                Header = "✓",
                Width = new DataGridLength(40),
                Binding = new Binding(nameof(SimRow.IsChecked)) { Mode = BindingMode.TwoWay }
            });
            GridSims.Columns.Add(new DataGridTextColumn
            {
                Header = "Simulation",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                IsReadOnly = true,
                Binding = new Binding(nameof(SimRow.Name))
            });
            GridSims.Columns.Add(new DataGridTextColumn
            {
                Header = "Solver",
                Width = new DataGridLength(80),
                IsReadOnly = true,
                Binding = new Binding(nameof(SimRow.Solver))
            });
            GridSims.Columns.Add(new DataGridTextColumn
            {
                Header = "Source",
                Width = new DataGridLength(140),
                IsReadOnly = true,
                Binding = new Binding(nameof(SimRow.Source))
            });
            GridSims.Columns.Add(new DataGridTextColumn
            {
                Header = "Wind Field",
                Width = new DataGridLength(140),
                IsReadOnly = true,
                Binding = new Binding(nameof(SimRow.WindField))
            });
            GridSims.Columns.Add(new DataGridTextColumn
            {
                Header = "Status",
                Width = new DataGridLength(90),
                IsReadOnly = true,
                Binding = new Binding(nameof(SimRow.Status))
            });

            foreach (var s in _scene.Simulations.OrderBy(s => s.Name ?? ""))
            {
                string srcName = _scene.TopLevelSources.FirstOrDefault(x => x.Id == s.SourceId)?.Name
                    ?? s.SnapshotSource?.Name ?? "(?)";
                string wfName = _scene.WindFieldScenarios.FirstOrDefault(w => w.Id == s.WindFieldId)?.Name
                    ?? "(?)";
                _simRows.Add(new SimRow
                {
                    SimulationId = s.Id ?? "",
                    Name         = s.Name ?? "(unnamed)",
                    Solver       = SolverCode.Of(s.SolverType),
                    Source       = srcName,
                    WindField    = wfName,
                    Status       = s.Status.ToString(),
                    IsChecked    = false
                });
            }

            // Default name for fresh studies.
            TxtName.Text = "Study " + (_scene.DispersionStudies.Count + 1);

            PopulateFromEditing();
            UpdateUnitLabel();
        }

        private void PopulateFromEditing()
        {
            if (_editing is null) return;
            TxtName.Text        = _editing.Name ?? "";
            TxtDescription.Text = _editing.Description ?? "";
            SelectQuantity(_editing.DetectionQuantity);
            NudThreshold.Value  = (decimal)Math.Max(0,
                Math.Min(1_000_000_000.0, _editing.DetectionThreshold));

            var included = new HashSet<string>(_editing.SimulationIds ?? new List<string>(),
                StringComparer.Ordinal);
            foreach (var row in _simRows)
                row.IsChecked = included.Contains(row.SimulationId);
        }

        private void SelectQuantity(ViewFieldProperty q)
        {
            for (int i = 0; i < CmbQuantity.Items.Count; i++)
                if (CmbQuantity.Items[i] is ComboBoxItem cbi
                    && cbi.Tag is ViewFieldProperty p && p == q)
                {
                    CmbQuantity.SelectedIndex = i;
                    return;
                }
            if (CmbQuantity.Items.Count > 0)
                CmbQuantity.SelectedIndex = 0;
        }

        private void CmbQuantity_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => UpdateUnitLabel();

        private void UpdateUnitLabel()
        {
            var q = (CmbQuantity.SelectedItem as ComboBoxItem)?.Tag is ViewFieldProperty p
                ? p
                : ViewFieldProperty.PercentLFL;
            LblUnit.Text = FieldTransform.UnitFor(q);
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            var target = _editing ?? new DispersionStudy();
            target.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Study" : TxtName.Text.Trim();
            target.Description = TxtDescription.Text ?? "";
            target.DetectionQuantity = (CmbQuantity.SelectedItem as ComboBoxItem)?.Tag is ViewFieldProperty p
                ? p
                : ViewFieldProperty.PercentLFL;
            target.DetectionThreshold = (double)(NudThreshold.Value ?? 50m);
            target.SimulationIds.Clear();
            foreach (var row in _simRows)
                if (row.IsChecked)
                    target.SimulationIds.Add(row.SimulationId);
            Result = target;
            Close(true);
        }

        /// <summary>Row backing for the simulation chooser. Carries the
        /// simulation Id so we can rebuild <see cref="DispersionStudy.SimulationIds"/>
        /// without re-looking-up by name.</summary>
        public sealed class SimRow
        {
            public string SimulationId { get; set; } = "";
            public string Name { get; set; } = "";
            public string Solver { get; set; } = "";
            public string Source { get; set; } = "";
            public string WindField { get; set; } = "";
            public string Status { get; set; } = "";
            public bool IsChecked { get; set; }
        }
    }
}
