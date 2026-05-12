using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Editor for a release source's equipment inventory (the list of equipment
    /// items that contribute to its IOGP 434-01-derived leak frequency).
    ///
    /// The dialog displays:
    /// <list type="bullet">
    ///   <item>The hole-size band selector (Tiny / Small / Medium / Large / Rupture).</item>
    ///   <item>A grid of inventory rows — type, nominal diameter (mm), count (or
    ///   length-in-metres for pipes), and an optional note.</item>
    ///   <item>An auto-compute toggle plus a manual leak-frequency override field.</item>
    ///   <item>A live "Computed leak frequency" label that re-evaluates against
    ///   <see cref="IogpFrequencyTable"/> as the user edits.</item>
    /// </list>
    ///
    /// The source's <see cref="ReleaseSource3D.EquipmentInventory"/>,
    /// <see cref="ReleaseSource3D.HoleSizeBand"/>,
    /// <see cref="ReleaseSource3D.AutoComputeLeakFrequency"/> and
    /// <see cref="ReleaseSource3D.LeakFrequencyPerYear"/> are mutated in-place on OK.
    /// On Cancel the original values are restored from the snapshot taken on entry.
    /// </summary>
    public class EquipmentInventoryDialog : Form
    {
        private readonly ReleaseSource3D _source;
        private readonly List<EquipmentInventoryItem> _originalSnapshot;
        private readonly IogpHoleSizeBand _originalBand;
        private readonly bool _originalAuto;
        private readonly double _originalLeakFreq;

        private ComboBox _cmbBand;
        private DataGridView _grid;
        private CheckBox _chkAuto;
        private NumericUpDown _nudManualFreq;
        private Label _lblComputedFreq;

        public EquipmentInventoryDialog(ReleaseSource3D source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            if (_source.EquipmentInventory == null)
                _source.EquipmentInventory = new List<EquipmentInventoryItem>();

            // Snapshot for Cancel rollback.
            _originalSnapshot = new List<EquipmentInventoryItem>(_source.EquipmentInventory.Count);
            foreach (var it in _source.EquipmentInventory)
            {
                _originalSnapshot.Add(new EquipmentInventoryItem
                {
                    Type = it.Type,
                    NominalDiameterMm = it.NominalDiameterMm,
                    Count = it.Count,
                    Note = it.Note
                });
            }
            _originalBand = _source.HoleSizeBand;
            _originalAuto = _source.AutoComputeLeakFrequency;
            _originalLeakFreq = _source.LeakFrequencyPerYear;

            BuildUI();
            PopulateGrid();
            RecomputeFrequency();
        }

        private void BuildUI()
        {
            Text = "Equipment Inventory — " + (_source.Name ?? "Source");
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            var dpi = DeviceDpi / 96f;
            MinimumSize = new System.Drawing.Size((int)(640 * dpi), (int)(440 * dpi));
            Size = new System.Drawing.Size((int)(820 * dpi), (int)(540 * dpi));
            Padding = new Padding((int)(10 * dpi));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // band row
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // grid
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // toolbar
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // freq row
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons

            // ── Hole-size band row ───────────────────────────────────────────
            var bandPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2
            };
            bandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bandPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bandPanel.Controls.Add(new Label
            {
                Text = "Hole-size band:",
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            }, 0, 0);
            _cmbBand = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = (int)(280 * dpi)
            };
            foreach (IogpHoleSizeBand b in Enum.GetValues(typeof(IogpHoleSizeBand)))
                _cmbBand.Items.Add(IogpFrequencyTable.DescribeBand(b));
            _cmbBand.SelectedIndex = (int)_source.HoleSizeBand;
            _cmbBand.SelectedIndexChanged += (s, e) => RecomputeFrequency();
            bandPanel.Controls.Add(_cmbBand, 1, 0);
            root.Controls.Add(bandPanel, 0, 0);

            // ── Inventory grid ───────────────────────────────────────────────
            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                RowHeadersVisible = false,
                BackgroundColor = System.Drawing.SystemColors.Window,
                BorderStyle = BorderStyle.FixedSingle
            };
            var colType = new DataGridViewComboBoxColumn
            {
                Name = "Type",
                HeaderText = "Equipment type (IOGP 434-01)",
                FlatStyle = FlatStyle.Flat,
                FillWeight = 35
            };
            foreach (IogpEquipmentType t in Enum.GetValues(typeof(IogpEquipmentType)))
                colType.Items.Add(DescribeType(t));
            _grid.Columns.Add(colType);
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Diameter",
                HeaderText = "Diameter (mm)",
                FillWeight = 15
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Count",
                HeaderText = "Count / Length (m)",
                FillWeight = 18
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Note",
                HeaderText = "Note",
                FillWeight = 32
            });
            _grid.CellValueChanged += (s, e) =>
            {
                CommitGridToInventory();
                RecomputeFrequency();
            };
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            root.Controls.Add(_grid, 0, 1);

            // ── Toolbar (Add / Remove) ───────────────────────────────────────
            var toolbar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 4, 0, 0)
            };
            var btnAdd = new Button
            {
                Text = "Add item",
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnAdd.Click += (s, e) =>
            {
                _source.EquipmentInventory.Add(new EquipmentInventoryItem());
                PopulateGrid();
                RecomputeFrequency();
            };
            var btnRemove = new Button
            {
                Text = "Remove selected",
                AutoSize = true,
                Margin = new Padding(0, 0, 6, 0)
            };
            btnRemove.Click += (s, e) =>
            {
                if (_grid.CurrentRow == null) return;
                int idx = _grid.CurrentRow.Index;
                if (idx < 0 || idx >= _source.EquipmentInventory.Count) return;
                _source.EquipmentInventory.RemoveAt(idx);
                PopulateGrid();
                RecomputeFrequency();
            };
            toolbar.Controls.Add(btnAdd);
            toolbar.Controls.Add(btnRemove);
            toolbar.Controls.Add(new Label
            {
                Text = "(Length in metres for pipe types; count for everything else.)",
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Margin = new Padding(8, 6, 0, 0)
            });
            root.Controls.Add(toolbar, 0, 2);

            // ── Frequency display + override ─────────────────────────────────
            var freqPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                Margin = new Padding(0, 8, 0, 0)
            };
            freqPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            freqPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            freqPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            freqPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _chkAuto = new CheckBox
            {
                Text = "Auto-compute from inventory",
                Checked = _source.AutoComputeLeakFrequency,
                AutoSize = true,
                Margin = new Padding(0, 6, 8, 0)
            };
            _chkAuto.CheckedChanged += (s, e) =>
            {
                _nudManualFreq.Enabled = !_chkAuto.Checked;
                RecomputeFrequency();
            };
            freqPanel.Controls.Add(_chkAuto, 0, 0);

            freqPanel.Controls.Add(new Label
            {
                Text = "Manual override (events/year):",
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
                AutoSize = true,
                Margin = new Padding(0, 6, 6, 0)
            }, 1, 0);
            _nudManualFreq = new NumericUpDown
            {
                Minimum = 0m,
                Maximum = 1m,
                DecimalPlaces = 8,
                Increment = 0.0001m,
                Value = ClampDecimal(_source.LeakFrequencyPerYear, 0m, 1m),
                Enabled = !_source.AutoComputeLeakFrequency,
                Width = (int)(130 * dpi)
            };
            _nudManualFreq.ValueChanged += (s, e) => RecomputeFrequency();
            freqPanel.Controls.Add(_nudManualFreq, 2, 0);

            _lblComputedFreq = new Label
            {
                Text = "Effective: —",
                AutoSize = true,
                Font = new System.Drawing.Font(Font, System.Drawing.FontStyle.Bold),
                Margin = new Padding(16, 6, 0, 0)
            };
            freqPanel.Controls.Add(_lblComputedFreq, 3, 0);

            root.Controls.Add(freqPanel, 0, 3);

            // ── Button row ───────────────────────────────────────────────────
            var btnRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Padding(0, 8, 0, 0)
            };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOk.Click += (s, e) => CommitAll();
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            btnCancel.Click += (s, e) => RollbackAll();
            // Per project memory: Cancel left, OK right.
            btnRow.Controls.Add(btnOk);
            btnRow.Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
            root.Controls.Add(btnRow, 0, 4);

            Controls.Add(root);
        }

        private void PopulateGrid()
        {
            _grid.Rows.Clear();
            foreach (var it in _source.EquipmentInventory)
            {
                var rowIdx = _grid.Rows.Add();
                var row = _grid.Rows[rowIdx];
                row.Cells["Type"].Value = DescribeType(it.Type);
                row.Cells["Diameter"].Value = it.NominalDiameterMm.ToString("0.##", CultureInfo.InvariantCulture);
                row.Cells["Count"].Value = it.Count.ToString("0.###", CultureInfo.InvariantCulture);
                row.Cells["Note"].Value = it.Note ?? "";
            }
        }

        private void CommitGridToInventory()
        {
            for (int i = 0; i < _grid.Rows.Count && i < _source.EquipmentInventory.Count; i++)
            {
                var item = _source.EquipmentInventory[i];
                var row = _grid.Rows[i];
                if (row.Cells["Type"].Value is string typeStr)
                    item.Type = ParseType(typeStr);
                item.NominalDiameterMm = ParseDouble(row.Cells["Diameter"].Value, item.NominalDiameterMm);
                item.Count = ParseDouble(row.Cells["Count"].Value, item.Count);
                item.Note = row.Cells["Note"].Value as string ?? "";
            }
        }

        private void RecomputeFrequency()
        {
            var band = (IogpHoleSizeBand)_cmbBand.SelectedIndex;
            _source.HoleSizeBand = band;
            _source.AutoComputeLeakFrequency = _chkAuto.Checked;
            _source.LeakFrequencyPerYear = (double)_nudManualFreq.Value;

            double effective = _source.EffectiveLeakFrequencyPerYear;
            _lblComputedFreq.Text = "Effective: " + effective.ToString("E3", CultureInfo.InvariantCulture)
                + " events/yr";
        }

        private void CommitAll()
        {
            CommitGridToInventory();
            RecomputeFrequency();   // also writes HoleSizeBand / auto / manual freq
            // OK: changes already on the source.
        }

        private void RollbackAll()
        {
            _source.EquipmentInventory.Clear();
            foreach (var it in _originalSnapshot) _source.EquipmentInventory.Add(it);
            _source.HoleSizeBand = _originalBand;
            _source.AutoComputeLeakFrequency = _originalAuto;
            _source.LeakFrequencyPerYear = _originalLeakFreq;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Friendly display label for the equipment type. Stored verbatim
        /// in the DataGridView combo column and round-tripped via
        /// <see cref="ParseType"/>.</summary>
        private static string DescribeType(IogpEquipmentType t)
        {
            int n = (int)t;
            switch (t)
            {
                case IogpEquipmentType.SteelProcessPipe:        return n + ". Steel process pipe (per metre)";
                case IogpEquipmentType.FlangedJoint:            return n + ". Flanged joint";
                case IogpEquipmentType.ManualValve:             return n + ". Manual valve";
                case IogpEquipmentType.ActuatedValve:           return n + ". Actuated valve";
                case IogpEquipmentType.InstrumentConnection:    return n + ". Instrument connection";
                case IogpEquipmentType.PressureVessel:          return n + ". Pressure vessel";
                case IogpEquipmentType.PumpCentrifugal:         return n + ". Pump, centrifugal";
                case IogpEquipmentType.PumpReciprocating:       return n + ". Pump, reciprocating";
                case IogpEquipmentType.CompressorCentrifugal:   return n + ". Compressor, centrifugal";
                case IogpEquipmentType.CompressorReciprocating: return n + ". Compressor, reciprocating";
                case IogpEquipmentType.HxShellTubeShellSide:    return n + ". HX shell+tube, HC shell side";
                case IogpEquipmentType.HxShellTubeTubeSide:     return n + ". HX shell+tube, HC tube side";
                case IogpEquipmentType.HxPlate:                 return n + ". HX plate";
                case IogpEquipmentType.HxAirCooled:             return n + ". HX air-cooled";
                case IogpEquipmentType.Filter:                  return n + ". Filter";
                case IogpEquipmentType.PigTrap:                 return n + ". Pig trap";
                case IogpEquipmentType.FlexiblePipe:            return n + ". Flexible pipe (per metre)";
                case IogpEquipmentType.PressureVesselOther:     return n + ". Pressure vessel (Other)";
                case IogpEquipmentType.Degasser:                return n + ". Degasser";
                case IogpEquipmentType.Expander:                return n + ". Expander";
                case IogpEquipmentType.XmasTree:                return n + ". Xmas tree";
                case IogpEquipmentType.Turbine:                 return n + ". Turbine";
                case IogpEquipmentType.PipelineEsdv:            return n + ". Pipeline ESDV";
                case IogpEquipmentType.SsivAssembly:            return n + ". SSIV assembly";
                default:                                        return n + ". " + t;
            }
        }

        private static IogpEquipmentType ParseType(string label)
        {
            if (string.IsNullOrEmpty(label)) return IogpEquipmentType.SteelProcessPipe;
            int dot = label.IndexOf('.');
            if (dot <= 0) return IogpEquipmentType.SteelProcessPipe;
            if (int.TryParse(label.Substring(0, dot), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int n) && n >= 1 && n <= 24)
                return (IogpEquipmentType)n;
            return IogpEquipmentType.SteelProcessPipe;
        }

        private static double ParseDouble(object value, double fallback)
        {
            if (value == null) return fallback;
            string s = value.ToString();
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return v;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
                return v;
            return fallback;
        }

        private static decimal ClampDecimal(double v, decimal min, decimal max)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return min;
            decimal d;
            try { d = (decimal)v; }
            catch { return min; }
            if (d < min) return min;
            if (d > max) return max;
            return d;
        }
    }
}
