using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Edits a <see cref="FireStudy"/> and scores it in place. Membership is the two
    /// checked lists: jet and pool fires on the left, ignitions on the right. Evaluate
    /// ranks whatever is checked without closing the dialog, so the harm criterion and
    /// the grid can be tuned against the numbers they produce.
    /// </summary>
    public class FireStudyDialog : Form
    {
        private readonly Scene3D _scene;
        private readonly List<FireSource> _fireSources = new List<FireSource>();
        private readonly List<IgnitionEvent> _ignitions = new List<IgnitionEvent>();

        private TextBox txtName, txtDescription;
        private ComboBox cmbHarm;
        private NumericUpDown nudThreshold, nudHalf, nudGrid, nudIgnitionProbability;
        private CheckedListBox lstFireSources, lstIgnitions;
        private TextBox txtReport;

        /// <summary>The harm quantities offered, in the order of the combo.</summary>
        private static readonly ViewFieldProperty[] HarmQuantities =
        {
            ViewFieldProperty.FatalityProbability,
            ViewFieldProperty.ThermalDose,
            ViewFieldProperty.ThermalRadiationKwM2
        };

        public FireStudy Result { get; private set; }

        public FireStudyDialog(Scene3D scene, FireStudy existing = null)
        {
            _scene = scene;
            Result = existing ?? new FireStudy();
            BuildUI();
            LoadFrom(Result, existing != null);
        }

        private void BuildUI()
        {
            this.Text = "Fire Study";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            var dpi = DeviceDpi / 96f;
            this.ClientSize = new System.Drawing.Size((int)(780 * dpi), (int)(640 * dpi));
            this.MinimumSize = new System.Drawing.Size((int)(680 * dpi), (int)(560 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // fields
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));         // membership
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));         // report
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));            // buttons

            // ── Fields ──────────────────────────────────────────────────
            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4, RowCount = 4
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            txtName = new TextBox { Text = "Fire Study", Dock = DockStyle.Fill };
            AddPair(fields, 0, 0, "Name:", txtName);
            txtDescription = new TextBox { Dock = DockStyle.Fill };
            AddPair(fields, 0, 2, "Description:", txtDescription);

            cmbHarm = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbHarm.Items.AddRange(new object[]
            {
                "Fatality probability", "Thermal dose", "Thermal radiation (kW/m²)"
            });
            cmbHarm.SelectedIndex = 0;
            AddPair(fields, 1, 0, "Harm quantity:", cmbHarm);

            nudThreshold = MakeNud(0m, 1000000m, 0.01m, 4);
            AddPair(fields, 1, 2, "Threshold:", nudThreshold);

            nudHalf = MakeNud(10m, 10000m, 100m, 0);
            AddPair(fields, 2, 0, "Domain half-width (m):", nudHalf);

            nudGrid = MakeNud(8m, 200m, 40m, 0);
            AddPair(fields, 2, 2, "Grid resolution:", nudGrid);

            nudIgnitionProbability = MakeNud(0.0001m, 1m, 0.1m, 4);
            AddPair(fields, 3, 0, "Ignition probability:", nudIgnitionProbability);

            root.Controls.Add(fields, 0, 0);

            // ── Membership ──────────────────────────────────────────────
            var lists = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 8, 0, 0)
            };
            lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lists.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            lists.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            lists.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            lists.Controls.Add(new Label { Text = "Fire sources", AutoSize = true }, 0, 0);
            lists.Controls.Add(new Label { Text = "Ignitions (flash fires)", AutoSize = true }, 1, 0);

            lstFireSources = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            lstIgnitions = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true };
            lists.Controls.Add(lstFireSources, 0, 1);
            lists.Controls.Add(lstIgnitions, 1, 1);
            root.Controls.Add(lists, 0, 1);

            if (_scene?.FireScenario?.Sources != null)
            {
                foreach (var f in _scene.FireScenario.Sources)
                {
                    if (f == null) continue;
                    _fireSources.Add(f);
                    lstFireSources.Items.Add(
                        (string.IsNullOrEmpty(f.Name) ? "(fire)" : f.Name)
                        + (f.IsPoolFire ? "  - pool" : "  - jet"));
                }
            }
            if (_scene?.Ignitions != null)
            {
                foreach (var g in _scene.Ignitions)
                {
                    if (g == null) continue;
                    _ignitions.Add(g);
                    lstIgnitions.Items.Add(
                        (string.IsNullOrEmpty(g.Name) ? "(ignition)" : g.Name)
                        + $"  - t = {g.TimeS:0.#} s");
                }
            }

            // ── Report ──────────────────────────────────────────────────
            txtReport = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 8.5f),
                Text = "Check the scenarios and press Evaluate.",
                Margin = new Padding(0, 8, 0, 0)
            };
            root.Controls.Add(txtReport, 0, 2);

            // ── Buttons ─────────────────────────────────────────────────
            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 4, RowCount = 1, Padding = new Padding(4)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var btnEvaluate = new Button { Text = "Evaluate", AutoSize = true };
            btnEvaluate.Click += (s, e) => Evaluate();
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += (s, e) => Harvest();

            buttons.Controls.Add(btnEvaluate, 0, 0);
            buttons.Controls.Add(new Label(), 1, 0);
            buttons.Controls.Add(btnCancel, 2, 0);
            buttons.Controls.Add(btnOK, 3, 0);
            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
            root.Controls.Add(buttons, 0, 3);

            this.Controls.Add(root);
            this.ApplyDpiScaling();
        }

        private void LoadFrom(FireStudy study, bool isExisting)
        {
            if (!isExisting) return;

            txtName.Text = study.Name;
            txtDescription.Text = study.Description;
            nudThreshold.Value = Clamp(nudThreshold, (decimal)study.HarmThreshold);
            nudHalf.Value = Clamp(nudHalf, (decimal)study.DomainHalfM);
            nudGrid.Value = Clamp(nudGrid, study.GridResolution);
            nudIgnitionProbability.Value = Clamp(nudIgnitionProbability, (decimal)study.IgnitionProbability);

            int harmIndex = Array.IndexOf(HarmQuantities, study.HarmQuantity);
            cmbHarm.SelectedIndex = harmIndex >= 0 ? harmIndex : 0;

            for (int i = 0; i < _fireSources.Count; i++)
                if (study.FireSourceIds.Contains(_fireSources[i].Id))
                    lstFireSources.SetItemChecked(i, true);
            for (int i = 0; i < _ignitions.Count; i++)
                if (study.IgnitionIds.Contains(_ignitions[i].Id))
                    lstIgnitions.SetItemChecked(i, true);
        }

        /// <summary>Copies the dialog state onto <see cref="Result"/>. Shared by Evaluate
        /// and OK so the report always describes what OK would save.</summary>
        private void Harvest()
        {
            Result.Name = string.IsNullOrWhiteSpace(txtName.Text) ? "Fire Study" : txtName.Text.Trim();
            Result.Description = txtDescription.Text ?? "";
            Result.HarmQuantity = HarmQuantities[Math.Max(0, cmbHarm.SelectedIndex)];
            Result.HarmThreshold = (double)nudThreshold.Value;
            Result.DomainHalfM = (double)nudHalf.Value;
            Result.GridResolution = (int)nudGrid.Value;
            Result.IgnitionProbability = (double)nudIgnitionProbability.Value;

            Result.FireSourceIds.Clear();
            for (int i = 0; i < _fireSources.Count; i++)
                if (lstFireSources.GetItemChecked(i)) Result.FireSourceIds.Add(_fireSources[i].Id);

            Result.IgnitionIds.Clear();
            for (int i = 0; i < _ignitions.Count; i++)
                if (lstIgnitions.GetItemChecked(i)) Result.IgnitionIds.Add(_ignitions[i].Id);
        }

        private void Evaluate()
        {
            Harvest();
            if (Result.FireSourceIds.Count == 0 && Result.IgnitionIds.Count == 0)
            {
                txtReport.Text = "Check at least one fire source or ignition.";
                return;
            }

            Cursor = Cursors.WaitCursor;
            try
            {
                txtReport.Text = FireStudyEngine.Evaluate(_scene, Result).Format()
                    .Replace("\n", Environment.NewLine);
            }
            catch (Exception ex)
            {
                txtReport.Text = "Evaluation failed: " + ex.Message;
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private static void AddPair(TableLayoutPanel table, int row, int col, string label, Control control)
        {
            table.Controls.Add(new Label
            {
                Text = label, AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 8, 0)
            }, col, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            table.Controls.Add(control, col + 1, row);
        }

        private static decimal Clamp(NumericUpDown nud, decimal value)
            => value < nud.Minimum ? nud.Minimum : (value > nud.Maximum ? nud.Maximum : value);

        private static NumericUpDown MakeNud(decimal min, decimal max, decimal value, int decimals)
        {
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value,
                DecimalPlaces = decimals, Dock = DockStyle.Fill
            };
            nud.Increment = decimals > 0 ? (decimal)Math.Pow(10, -decimals) : 1;
            return nud;
        }
    }
}
