using System;
using System.Linq;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Wizard for creating a new <see cref="View"/>: pick a Simulation, kind, field property, and time mode.
    /// Fine-tuning (iso value, color, plane position, color map, etc.) happens via the PropertyGrid.
    /// </summary>
    public class ViewEditorDialog : Form
    {
        /// <summary>Combo entry that displays the field label and carries the enum.</summary>
        private sealed class FieldChoice
        {
            public ViewFieldProperty Value { get; }
            public FieldChoice(ViewFieldProperty value) { Value = value; }
            public override string ToString() => FieldTransform.DisplayName(Value);
        }

        private readonly Scene3D _scene;
        private TextBox txtName;
        private ComboBox cmbSimulation;
        private ComboBox cmbKind;
        private ComboBox cmbField;
        private ComboBox cmbTimeMode;

        public DisperSim3D.Models.View Result { get; private set; }

        public ViewEditorDialog(Scene3D scene)
        {
            _scene = scene;
            BuildUI();
            PopulateLists();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.Text = "New View";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoSize = true;
            this.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            this.Padding = new Padding((int)(10 * dpi));

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 2
            };
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var table = new TableLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(280 * dpi)));

            int row = 0;
            txtName = new TextBox { Dock = DockStyle.Fill, Text = "View " + (_scene.Views.Count + 1) };
            DialogHelpers.AddRowWithHelp(table, ref row, "Name:", txtName,
                "Display name shown in the project tree.");

            cmbKind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbKind.Items.AddRange(new object[] {
                "Isosurface (3D)",
                "Contour XY (horizontal slice at z = PlanePosition)",
                "Contour XZ (vertical slice at y = PlanePosition)",
                "Contour YZ (vertical slice at x = PlanePosition)"
            });
            cmbKind.SelectedIndex = 0;
            DialogHelpers.AddRowWithHelp(table, ref row, "Kind:", cmbKind,
                "Isosurface extracts a 3D surface at a threshold; contour planes show a 2D coloured slice.");

            cmbSimulation = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            DialogHelpers.AddRowWithHelp(table, ref row, "Simulation:", cmbSimulation,
                "Completed simulation whose results this view samples.");

            // Built from the enum, not a hardcoded list: the list used to stop at
            // the seventh of nineteen values, so %LFL, ppm, thermal radiation, the
            // flash-fire fields and the dose fields were unreachable here and had
            // to be set through the property grid.
            cmbField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            foreach (ViewFieldProperty p in Enum.GetValues(typeof(ViewFieldProperty)))
                cmbField.Items.Add(new FieldChoice(p));
            cmbField.SelectedIndex = 0;
            DialogHelpers.AddRowWithHelp(table, ref row, "Field:", cmbField,
                "What the view renders. Most entries sample the simulation result; "
                + "thermal radiation, dose and fatality come from the scene's fire sources, "
                + "and the flash-fire entries burn the cloud from the scene's ignition.");

            cmbTimeMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
            cmbTimeMode.Items.AddRange(new object[] {
                "Peak Over Time (per-cell maximum)",
                "Final Snapshot (last timestep)",
                "Specific Time (set seconds in PropertyGrid)"
            });
            cmbTimeMode.SelectedIndex = 0;
            DialogHelpers.AddRowWithHelp(table, ref row, "Time Mode:", cmbTimeMode,
                "How transient timesteps are collapsed to a single field.");

            outer.Controls.Add(table, 0, 0);

            var btns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 3, RowCount = 1, Padding = new Padding(4)
            };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;
            btns.Controls.Add(new Label(), 0, 0);
            btns.Controls.Add(btnCancel, 1, 0);
            btns.Controls.Add(btnOK, 2, 0);
            outer.Controls.Add(btns, 0, 1);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;
            this.Controls.Add(outer);
            this.ApplyDpiScaling();
        }

        private void PopulateLists()
        {
            cmbSimulation.Items.Clear();
            var completed = _scene.Simulations
                .Where(s => s.Status == SimulationStatus.Completed)
                .ToList();
            if (completed.Count == 0)
            {
                cmbSimulation.Items.Add("(no completed simulations)");
                cmbSimulation.SelectedIndex = 0;
                cmbSimulation.Enabled = false;
                return;
            }
            foreach (var s in completed)
                cmbSimulation.Items.Add(s.Name);
            cmbSimulation.SelectedIndex = 0;
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            var completed = _scene.Simulations
                .Where(s => s.Status == SimulationStatus.Completed)
                .ToList();
            if (completed.Count == 0)
            {
                MessageBox.Show(this,
                    "No completed simulations available. Run a simulation first.",
                    "Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                this.DialogResult = DialogResult.None;
                return;
            }

            var sim = completed[Math.Max(0, cmbSimulation.SelectedIndex)];

            ViewKind kind;
            switch (cmbKind.SelectedIndex)
            {
                case 1: kind = ViewKind.ContourXY; break;
                case 2: kind = ViewKind.ContourXZ; break;
                case 3: kind = ViewKind.ContourYZ; break;
                default: kind = ViewKind.Isosurface; break;
            }
            var field = (cmbField.SelectedItem as FieldChoice)?.Value ?? ViewFieldProperty.Concentration;
            ViewTimeMode timeMode = (ViewTimeMode)cmbTimeMode.SelectedIndex;

            Result = new DisperSim3D.Models.View
            {
                Name = string.IsNullOrEmpty(txtName.Text) ? "View" : txtName.Text,
                Kind = kind,
                SimulationId = sim.Id,
                FieldProperty = field,
                TimeMode = timeMode
            };
        }
    }
}
