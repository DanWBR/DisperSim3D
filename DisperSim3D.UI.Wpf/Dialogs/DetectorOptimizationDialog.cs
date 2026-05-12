using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Configuration + result dialog for the gas-detector placement optimisation
    /// (Vianna 2019 Set Covering Problem). The user picks which Completed simulations
    /// to consider, defines the protected region, and the dialog returns a list of
    /// optimal detector positions to add to the project.
    /// </summary>
    public class DetectorOptimizationDialog : Form
    {
        private readonly Scene3D _scene;
        private CheckedListBox _lstSimulations;
        private NumericUpDown _nudXMin, _nudYMin, _nudZMin, _nudXMax, _nudYMax, _nudZMax;
        private NumericUpDown _nudThreshold;
        private NumericUpDown _nudMeshOverride;
        private NumericUpDown _nudRadius;
        private ComboBox _cmbNeighborhood;
        private CheckBox _chkExactSolver;
        private Label _lblStatus;
        private DataGridView _grid;

        public List<DisperSim3D.Geometry.Point3D> ResultDetectorPositions { get; private set; }

        public DetectorOptimizationDialog(Scene3D scene)
        {
            _scene = scene;
            ResultDetectorPositions = new List<DisperSim3D.Geometry.Point3D>();
            BuildUI();
            PopulateSimulations();
            PrepopulateRegionFromScene();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "Gas Detector Placement Optimization";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimumSize = new System.Drawing.Size((int)(700 * dpi), (int)(560 * dpi));
            this.Size = new System.Drawing.Size((int)(820 * dpi), (int)(640 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ── Top: simulation list ──
            var simBox = new GroupBox { Text = "Simulations to consider (Completed only)", Dock = DockStyle.Fill };
            _lstSimulations = new CheckedListBox
            {
                Dock = DockStyle.Fill, CheckOnClick = true
            };
            simBox.Controls.Add(_lstSimulations);
            outer.Controls.Add(simBox, 0, 0);

            // ── Middle: parameters ──
            var paramsBox = new GroupBox { Text = "Parameters", Dock = DockStyle.Fill, AutoSize = true };
            var paramsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Padding = new Padding(4)
            };
            paramsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            paramsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            paramsTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            paramsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            paramsTable.Controls.Add(new Label { Text = "X min (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
            _nudXMin = MakeNud(-100000m, 100000m, -50m, 1);
            paramsTable.Controls.Add(_nudXMin);
            paramsTable.Controls.Add(new Label { Text = "X max (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 0) });
            _nudXMax = MakeNud(-100000m, 100000m, 50m, 1);
            paramsTable.Controls.Add(_nudXMax);

            paramsTable.Controls.Add(new Label { Text = "Y min (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
            _nudYMin = MakeNud(-100000m, 100000m, -50m, 1);
            paramsTable.Controls.Add(_nudYMin);
            paramsTable.Controls.Add(new Label { Text = "Y max (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 0) });
            _nudYMax = MakeNud(-100000m, 100000m, 50m, 1);
            paramsTable.Controls.Add(_nudYMax);

            paramsTable.Controls.Add(new Label { Text = "Z min (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
            _nudZMin = MakeNud(0m, 100000m, 0m, 1);
            paramsTable.Controls.Add(_nudZMin);
            paramsTable.Controls.Add(new Label { Text = "Z max (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 0) });
            _nudZMax = MakeNud(0m, 100000m, 10m, 1);
            paramsTable.Controls.Add(_nudZMax);

            paramsTable.Controls.Add(new Label { Text = "Threshold (kg/m³):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
            _nudThreshold = MakeNud(0m, 10m, 0m, 4);
            paramsTable.Controls.Add(_nudThreshold);
            paramsTable.Controls.Add(new Label { Text = "Mesh L override (m):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 0) });
            _nudMeshOverride = MakeNud(0m, 100m, 0m, 2);
            paramsTable.Controls.Add(_nudMeshOverride);

            paramsTable.Controls.Add(new Label { Text = "Dominance radius (cells):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
            _nudRadius = MakeNud(1m, 10m, 1m, 0);
            paramsTable.Controls.Add(_nudRadius);
            paramsTable.Controls.Add(new Label { Text = "Neighbourhood:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 0) });
            _cmbNeighborhood = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbNeighborhood.Items.Add("Cardinal (6 face-adjacent)");
            _cmbNeighborhood.Items.Add("Moore (26 surrounding)");
            _cmbNeighborhood.SelectedIndex = 0;
            paramsTable.Controls.Add(_cmbNeighborhood);

            paramsTable.Controls.Add(new Label { Text = "Solver:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 6, 0) });
            _chkExactSolver = new CheckBox
            {
                Text = "Exact (Balas branch-and-bound, slower)",
                AutoSize = true, Anchor = AnchorStyles.Left, Checked = true
            };
            paramsTable.Controls.Add(_chkExactSolver);
            paramsTable.Controls.Add(new Label
            {
                Text = "Threshold = 0 → use gas LFL; Mesh = 0 → auto from min cloud volume.",
                AutoSize = true, ForeColor = System.Drawing.SystemColors.GrayText,
                Anchor = AnchorStyles.Left, Margin = new Padding(8, 6, 6, 0)
            });
            paramsTable.Controls.Add(new Label());

            paramsBox.Controls.Add(paramsTable);
            outer.Controls.Add(paramsBox, 0, 1);

            // ── Bottom: results grid + status ──
            var resultsBox = new GroupBox { Text = "Optimal detectors", Dock = DockStyle.Fill };
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = System.Drawing.SystemColors.Window
            };
            _grid.Columns.Add("Idx", "#");
            _grid.Columns.Add("X", "X (m)");
            _grid.Columns.Add("Y", "Y (m)");
            _grid.Columns.Add("Z", "Z (m)");
            resultsBox.Controls.Add(_grid);
            outer.Controls.Add(resultsBox, 0, 2);

            // ── Bottom buttons + status ──
            var buttonsRow = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 4, RowCount = 1, Padding = new Padding(4)
            };
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttonsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            _lblStatus = new Label
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                AutoSize = false,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                Text = "Ready."
            };
            var btnOptimize = new Button { Text = "Optimize", AutoSize = true };
            btnOptimize.Click += (s, e) => DoOptimize();
            var btnAdd = new Button { Text = "Add as Detectors", AutoSize = true, DialogResult = DialogResult.OK };
            var btnCancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

            buttonsRow.Controls.Add(_lblStatus, 0, 0);
            buttonsRow.Controls.Add(btnOptimize, 1, 0);
            buttonsRow.Controls.Add(btnAdd, 2, 0);
            buttonsRow.Controls.Add(btnCancel, 3, 0);
            outer.Controls.Add(buttonsRow, 0, 3);

            this.AcceptButton = btnAdd;
            this.CancelButton = btnCancel;
            this.Controls.Add(outer);
            this.ApplyDpiScaling();
        }

        private void PopulateSimulations()
        {
            _lstSimulations.Items.Clear();
            if (_scene.Simulations == null) return;
            foreach (var sim in _scene.Simulations)
            {
                if (sim.Status != SimulationStatus.Completed) continue;
                _lstSimulations.Items.Add(sim, true);
            }
        }

        private void PrepopulateRegionFromScene()
        {
            // Default the region to the largest decoration bounding box, if any
            if (_scene.Decorations == null || _scene.Decorations.Count == 0) return;
            BoundingBox biggest = null;
            double biggestVol = 0;
            foreach (var d in _scene.Decorations)
            {
                if (d.BoundingBox == null) continue;
                double v = (d.BoundingBox.Max.X - d.BoundingBox.Min.X)
                         * (d.BoundingBox.Max.Y - d.BoundingBox.Min.Y)
                         * (d.BoundingBox.Max.Z - d.BoundingBox.Min.Z);
                if (v > biggestVol) { biggestVol = v; biggest = d.BoundingBox; }
            }
            if (biggest == null) return;
            _nudXMin.Value = (decimal)biggest.Min.X;
            _nudYMin.Value = (decimal)biggest.Min.Y;
            _nudZMin.Value = (decimal)Math.Max(0, biggest.Min.Z);
            _nudXMax.Value = (decimal)biggest.Max.X;
            _nudYMax.Value = (decimal)biggest.Max.Y;
            _nudZMax.Value = (decimal)biggest.Max.Z;
        }

        private void DoOptimize()
        {
            var sims = new List<Simulation>();
            for (int i = 0; i < _lstSimulations.Items.Count; i++)
                if (_lstSimulations.GetItemChecked(i))
                    sims.Add((Simulation)_lstSimulations.Items[i]);
            if (sims.Count == 0)
            {
                _lblStatus.Text = "Select at least one Completed simulation.";
                return;
            }

            var input = new DetectorOptimizer.Input
            {
                Simulations = sims,
                Scene = _scene,
                ProtectedRegion = new BoundingBox(
                    new System.Windows.Media.Media3D.Point3D(
                        (double)_nudXMin.Value, (double)_nudYMin.Value, (double)_nudZMin.Value),
                    new System.Windows.Media.Media3D.Point3D(
                        (double)_nudXMax.Value, (double)_nudYMax.Value, (double)_nudZMax.Value)),
                ConcentrationThresholdKgM3 = (double)_nudThreshold.Value,
                MeshSizeMOverride = (double)_nudMeshOverride.Value,
                DominanceRadiusCells = (int)_nudRadius.Value,
                Neighborhood = _cmbNeighborhood.SelectedIndex == 1
                    ? DetectorOptimizer.NeighborhoodKind.Moore
                    : DetectorOptimizer.NeighborhoodKind.Cardinal,
                UseExactSolver = _chkExactSolver.Checked
            };

            _lblStatus.Text = "Running optimisation...";
            this.Refresh();

            try
            {
                var r = DetectorOptimizer.Run(input, msg => { _lblStatus.Text = msg; this.Refresh(); });
                ResultDetectorPositions = r.DetectorPositions;
                FillGrid(r);
                _lblStatus.Text = string.Format(
                    "{0} detectors • L = {1:F2} m • {2}/{3} cells covered{4}",
                    r.DetectorPositions.Count, r.MeshSizeM, r.RequiredCoverageCells, r.TotalCells,
                    string.IsNullOrEmpty(r.Notes) ? "" : " — " + r.Notes);
            }
            catch (Exception ex)
            {
                _lblStatus.Text = "Error: " + ex.Message;
            }
        }

        private void FillGrid(DetectorOptimizer.OptimizationResult r)
        {
            _grid.Rows.Clear();
            for (int i = 0; i < r.DetectorPositions.Count; i++)
            {
                var p = r.DetectorPositions[i];
                _grid.Rows.Add(i + 1,
                    p.X.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    p.Y.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                    p.Z.ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        private static NumericUpDown MakeNud(decimal min, decimal max, decimal value, int decimals)
        {
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals,
                Dock = DockStyle.Fill
            };
            nud.Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1;
            return nud;
        }
    }
}
