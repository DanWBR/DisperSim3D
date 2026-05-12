using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Create / edit a <see cref="DispersionStudy"/> — name, detection criterion
    /// (quantity + threshold), and which Simulations contribute their final-snapshot
    /// cloud to the detection target.
    /// Layout: TableLayoutPanel only. Cancel-left / OK-right (project convention).
    /// </summary>
    public class DispersionStudyDialog : Form
    {
        private readonly Scene3D _scene;
        private readonly DispersionStudy _editing;
        private TextBox _txtName, _txtDescription;
        private ComboBox _cmbQuantity;
        private NumericUpDown _nudThreshold;
        private Label _lblUnit;
        private ListView _lvSims;

        public DispersionStudy Result { get; private set; }

        public DispersionStudyDialog(Scene3D scene, DispersionStudy editing = null)
        {
            _scene = scene;
            _editing = editing;
            Text = editing == null ? "New Dispersion Study" : "Edit Dispersion Study";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = new Size(720, 520);
            Size = new Size(820, 600);
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
            PopulateFromEditing();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding((int)(10 * dpi)),
                ColumnCount = 1,
                RowCount = 4
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // identity
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // criterion
            outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // sim list
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons

            // Identity
            var idBox = new GroupBox { Text = "Identity", Dock = DockStyle.Fill,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi)) };
            var ig = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2 };
            ig.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ig.Controls.Add(MakeLabel("Name:", dpi), 0, 0);
            _txtName = new TextBox { Dock = DockStyle.Fill, Text = "Study " + (_scene.DispersionStudies.Count + 1) };
            ig.Controls.Add(_txtName, 1, 0);
            ig.Controls.Add(MakeLabel("Description:", dpi), 0, 1);
            _txtDescription = new TextBox { Dock = DockStyle.Fill, Text = "" };
            ig.Controls.Add(_txtDescription, 1, 1);
            idBox.Controls.Add(ig);

            // Detection criterion
            //
            // Single-row TableLayoutPanel: Quantity | combo | gap | Threshold ≥ | NUD | unit | stretch
            // Labels carry a small top-margin so their text baselines align with the
            // ComboBox/NumericUpDown text rather than sitting flush at the cell top.
            var crBox = new GroupBox { Text = "Detection criterion (applied per simulation, with each sim's gas)",
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi)) };
            var cg = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 7, RowCount = 1 };
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 0 Quantity:
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 1 combo
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(24 * dpi))); // 2 gap
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 3 Threshold ≥
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 4 NUD
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 5 unit label
            cg.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // 6 stretch
            cg.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // ── Quantity ─────────────────────────────────────────────────────
            cg.Controls.Add(InlineLabel("Quantity:", dpi), 0, 0);
            _cmbQuantity = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = (int)(240 * dpi),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, (int)(2 * dpi), 0, (int)(2 * dpi))
            };
            foreach (ViewFieldProperty vfp in Enum.GetValues(typeof(ViewFieldProperty)))
                _cmbQuantity.Items.Add(vfp);
            _cmbQuantity.SelectedItem = ViewFieldProperty.PercentLFL;
            _cmbQuantity.SelectedIndexChanged += (s, e) => UpdateUnitLabel();
            cg.Controls.Add(_cmbQuantity, 1, 0);

            // ── Threshold ────────────────────────────────────────────────────
            cg.Controls.Add(InlineLabel("Threshold ≥", dpi), 3, 0);
            _nudThreshold = new NumericUpDown
            {
                Minimum = 0m,
                Maximum = 1_000_000_000m,
                Value = 50m,
                DecimalPlaces = 4,
                Increment = 1m,
                Width = (int)(110 * dpi),
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, (int)(2 * dpi), 0, (int)(2 * dpi))
            };
            cg.Controls.Add(_nudThreshold, 4, 0);
            _lblUnit = new Label
            {
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding((int)(4 * dpi), (int)(6 * dpi), 0, 0)
            };
            cg.Controls.Add(_lblUnit, 5, 0);
            crBox.Controls.Add(cg);

            // Simulation chooser
            var simBox = new GroupBox { Text = "Member simulations (check to include — only Completed runs)",
                Dock = DockStyle.Fill, Padding = new Padding((int)(8 * dpi)) };
            _lvSims = new ListView
            {
                Dock = DockStyle.Fill,
                View = System.Windows.Forms.View.Details,
                FullRowSelect = true,
                CheckBoxes = true,
                GridLines = true
            };
            _lvSims.Columns.Add("Simulation", (int)(220 * dpi));
            _lvSims.Columns.Add("Solver", (int)(80 * dpi));
            _lvSims.Columns.Add("Source", (int)(140 * dpi));
            _lvSims.Columns.Add("Wind Field", (int)(140 * dpi));
            _lvSims.Columns.Add("Status", (int)(90 * dpi));
            foreach (var s in _scene.Simulations.OrderBy(s => s.Name))
            {
                string srcName = _scene.TopLevelSources.FirstOrDefault(x => x.Id == s.SourceId)?.Name
                    ?? s.SnapshotSource?.Name ?? "(?)";
                string wfName = _scene.WindFieldScenarios.FirstOrDefault(w => w.Id == s.WindFieldId)?.Name
                    ?? "(?)";
                var lvi = new ListViewItem(s.Name ?? "(unnamed)") { Tag = s.Id };
                lvi.SubItems.Add(SolverCode.Of(s.SolverType));
                lvi.SubItems.Add(srcName);
                lvi.SubItems.Add(wfName);
                lvi.SubItems.Add(s.Status.ToString());
                if (s.Status != SimulationStatus.Completed)
                    lvi.ForeColor = SystemColors.GrayText;
                _lvSims.Items.Add(lvi);
            }
            simBox.Controls.Add(_lvSims);

            // Buttons — Cancel left, OK right (project convention).
            var btns = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, Padding = new Padding(0, (int)(8 * dpi), 0, 0) };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", AutoSize = true,
                Padding = new Padding(12, 2, 12, 2), DialogResult = DialogResult.Cancel };
            var btnOK = new Button { Text = "OK", AutoSize = true,
                Padding = new Padding(16, 2, 16, 2) };
            btnOK.Click += (s, e) => CommitAndClose();
            btns.Controls.Add(new Label(), 0, 0);
            btns.Controls.Add(btnCancel, 1, 0);
            btns.Controls.Add(btnOK, 2, 0);
            AcceptButton = btnOK;
            CancelButton = btnCancel;

            outer.Controls.Add(idBox, 0, 0);
            outer.Controls.Add(crBox, 0, 1);
            outer.Controls.Add(simBox, 0, 2);
            outer.Controls.Add(btns, 0, 3);
            Controls.Add(outer);

            UpdateUnitLabel();
        }

        private static Label MakeLabel(string text, float dpi) => new Label
        {
            Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
            Padding = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
        };

        /// <summary>Variant of <see cref="MakeLabel"/> tuned for labels sitting
        /// next to <see cref="ComboBox"/> / <see cref="NumericUpDown"/> on the
        /// same TableLayoutPanel row: uses <c>Margin</c> (table cell offset)
        /// instead of <c>Padding</c> (label internal offset) so the baseline
        /// lines up with the input controls' rendered text, independent of the
        /// row's auto-sized height.</summary>
        private static Label InlineLabel(string text, float dpi) => new Label
        {
            Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
            Margin = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
        };

        private void UpdateUnitLabel()
        {
            var q = _cmbQuantity.SelectedItem is ViewFieldProperty p ? p : ViewFieldProperty.PercentLFL;
            _lblUnit.Text = FieldTransform.UnitFor(q);
        }

        private void PopulateFromEditing()
        {
            if (_editing == null) return;
            _txtName.Text = _editing.Name ?? "";
            _txtDescription.Text = _editing.Description ?? "";
            _cmbQuantity.SelectedItem = _editing.DetectionQuantity;
            _nudThreshold.Value = (decimal)Math.Max(0, Math.Min(1_000_000_000, _editing.DetectionThreshold));
            var set = new HashSet<string>(_editing.SimulationIds ?? new List<string>(),
                StringComparer.Ordinal);
            foreach (ListViewItem it in _lvSims.Items)
                it.Checked = set.Contains((string)it.Tag);
            UpdateUnitLabel();
        }

        private void CommitAndClose()
        {
            var target = _editing ?? new DispersionStudy();
            target.Name = string.IsNullOrWhiteSpace(_txtName.Text) ? "Study" : _txtName.Text.Trim();
            target.Description = _txtDescription.Text ?? "";
            target.DetectionQuantity = _cmbQuantity.SelectedItem is ViewFieldProperty p
                ? p : ViewFieldProperty.PercentLFL;
            target.DetectionThreshold = (double)_nudThreshold.Value;
            target.SimulationIds.Clear();
            foreach (ListViewItem it in _lvSims.Items)
                if (it.Checked) target.SimulationIds.Add((string)it.Tag);
            Result = target;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
