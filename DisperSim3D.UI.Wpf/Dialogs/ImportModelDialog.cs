using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using DisperSim3D.Core;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace DisperSim3D.Dialogs
{
    public class ImportModelDialog : Form
    {
        private NumericUpDown nudPosX, nudPosY, nudPosZ;
        private NumericUpDown nudRotX, nudRotY, nudRotZ;
        private NumericUpDown nudScale;
        private TrackBar trackScale;
        private ComboBox cboUnit;
        private Label lblInfo;
        private bool _suppressUnitSync;
        private ElementHost _previewHost;
        private HelixViewport3D _previewViewport;
        private Model3DGroup _model;
        private ModelVisual3D _modelVisual;

        public double PosX => (double)nudPosX.Value;
        public double PosY => (double)nudPosY.Value;
        public double PosZ => (double)nudPosZ.Value;
        public double RotX => (double)nudRotX.Value;
        public double RotY => (double)nudRotY.Value;
        public double RotZ => (double)nudRotZ.Value;
        public double ModelScale => (double)nudScale.Value;

        private double _groundSize = 200.0;

        public ImportModelDialog(Model3DGroup model, string fileName, double groundSize = 200.0)
        {
            _model = model;
            _groundSize = groundSize > 0 ? groundSize : 200.0;
            BuildUI(fileName);
            UpdatePreview();
        }

        private void BuildUI(string fileName)
        {
            this.Text = "Import 3D Model - " + System.IO.Path.GetFileName(fileName);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            var dpi = DeviceDpi / 96f;
            this.ClientSize = new System.Drawing.Size((int)(750 * dpi), (int)(520 * dpi));
            this.MinimumSize = new System.Drawing.Size((int)(650 * dpi), (int)(450 * dpi));
            this.Padding = new Padding((int)(8 * dpi));

            var bounds = _model.Bounds;
            double maxExt = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));

            // The model arrives at whatever size its file says, and the scene works in
            // metres. STL and OBJ carry no unit, so the number in the file could be
            // millimetres, metres or anything else — and authors get it wrong often
            // enough that guessing silently is worse than asking. The dialog picks the
            // likeliest unit and the user confirms it.
            double defaultScale = ModelUnits.FactorFor(ModelUnits.Guess(maxExt));

            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Vertical,
                SplitterDistance = (int)(260 * dpi),
                FixedPanel = FixedPanel.Panel1
            };

            var leftPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(4)
            };

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 15
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(130 * dpi)));

            int row = 0;

            lblInfo = new Label
            {
                Text = BuildInfoText(defaultScale),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.SetColumnSpan(lblInfo, 2);
            table.Controls.Add(lblInfo, 0, row++);

            var lblIntro = new Label
            {
                Text = "Position, rotate and scale the model in scene units (meters).\nUse the preview on the right to see the result.",
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Regular),
                Margin = new Padding(0, 0, 0, 6)
            };
            table.SetColumnSpan(lblIntro, 2);
            table.Controls.Add(lblIntro, 0, row++);

            var lblPos = new Label
            {
                Text = "Position (meters)",
                AutoSize = true,
                Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 2)
            };
            table.SetColumnSpan(lblPos, 2);
            table.Controls.Add(lblPos, 0, row++);

            nudPosX = MakeNud(-500m, 500m, 0m, 1);
            AddRow(table, row++, "X:", nudPosX);
            nudPosY = MakeNud(-500m, 500m, 0m, 1);
            AddRow(table, row++, "Y:", nudPosY);
            nudPosZ = MakeNud(-500m, 500m, 0m, 1);
            AddRow(table, row++, "Z:", nudPosZ);

            var lblRot = new Label
            {
                Text = "Rotation (degrees)",
                AutoSize = true,
                Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 2)
            };
            table.SetColumnSpan(lblRot, 2);
            table.Controls.Add(lblRot, 0, row++);

            nudRotX = MakeNud(-360m, 360m, 0m, 0);
            AddRow(table, row++, "X:", nudRotX);
            nudRotY = MakeNud(-360m, 360m, 0m, 0);
            AddRow(table, row++, "Y:", nudRotY);
            nudRotZ = MakeNud(-360m, 360m, 0m, 0);
            AddRow(table, row++, "Z:", nudRotZ);

            var lblScl = new Label
            {
                Text = "Scale",
                AutoSize = true,
                Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 2)
            };
            table.SetColumnSpan(lblScl, 2);
            table.Controls.Add(lblScl, 0, row++);

            cboUnit = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cboUnit.Items.AddRange(ModelUnits.Labels);
            cboUnit.SelectedIndex = (int)ModelUnits.Guess(maxExt);
            AddRow(table, row++, "Model unit:", cboUnit);

            nudScale = MakeNud(0.000001m, 100000m, (decimal)defaultScale, 6);
            nudScale.Increment = 0.01m;
            AddRow(table, row++, "Value:", nudScale);

            cboUnit.SelectedIndexChanged += (s, e) =>
            {
                var picked = (ModelUnit)cboUnit.SelectedIndex;
                if (picked == ModelUnit.Custom) return;  // the user drives the value directly
                _suppressUnitSync = true;
                nudScale.Value = (decimal)ModelUnits.FactorFor(picked);
                _suppressUnitSync = false;
            };

            trackScale = new TrackBar
            {
                Dock = DockStyle.Fill,
                Minimum = 1,
                Maximum = 200,
                Value = ScaleToTrack(defaultScale),
                TickFrequency = 20
            };
            trackScale.ValueChanged += (s, e) =>
            {
                double sv = TrackToScale(trackScale.Value);
                nudScale.Value = (decimal)sv;
            };
            table.SetColumnSpan(trackScale, 2);
            table.Controls.Add(trackScale, 0, row++);

            nudPosX.ValueChanged += (s, e) => UpdatePreview();
            nudPosY.ValueChanged += (s, e) => UpdatePreview();
            nudPosZ.ValueChanged += (s, e) => UpdatePreview();
            nudRotX.ValueChanged += (s, e) => UpdatePreview();
            nudRotY.ValueChanged += (s, e) => UpdatePreview();
            nudRotZ.ValueChanged += (s, e) => UpdatePreview();
            nudScale.ValueChanged += (s, e) =>
            {
                int tv = ScaleToTrack((double)nudScale.Value);
                if (trackScale.Value != tv) trackScale.Value = tv;
                // Dialling the value away from a named unit means the user is no longer
                // asserting that unit, so the combo drops to Custom rather than lying.
                if (!_suppressUnitSync)
                    cboUnit.SelectedIndex = (int)ModelUnits.Match((double)nudScale.Value);
                lblInfo.Text = BuildInfoText((double)nudScale.Value);
                UpdatePreview();
            };

            var buttonRow = new TableLayoutPanel
            {
                AutoSize = true, Dock = DockStyle.Fill,
                ColumnCount = 4, RowCount = 1,
                Padding = new Padding(4), Margin = new Padding(0, 8, 0, 0)
            };
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttonRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnResetView = new Button { Text = "Reset View", AutoSize = true };
            btnResetView.Click += (s, e) =>
            {
                _previewViewport.Camera.Position = new Point3D(10, 10, 8);
                _previewViewport.Camera.LookDirection = new Vector3D(-10, -10, -8);
                _previewViewport.Camera.UpDirection = new Vector3D(0, 0, 1);
            };

            var btnOK = new Button { Text = "Insert", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

            buttonRow.Controls.Add(btnResetView, 0, 0);
            buttonRow.Controls.Add(new Label(), 1, 0);
            buttonRow.Controls.Add(btnCancel, 2, 0);
            buttonRow.Controls.Add(btnOK, 3, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            table.SetColumnSpan(buttonRow, 2);
            table.Controls.Add(buttonRow, 0, row++);

            leftPanel.Controls.Add(table);
            splitContainer.Panel1.Controls.Add(leftPanel);

            var rightPanel = new Panel { Dock = DockStyle.Fill };

            _previewHost = new ElementHost
            {
                Dock = DockStyle.Fill,
                BackColor = System.Drawing.Color.White
            };

            _previewViewport = new HelixViewport3D
            {
                Background = Brushes.White,
                ShowCoordinateSystem = true,
                ShowViewCube = true,
                CameraRotationMode = CameraRotationMode.Turntable
            };
            // Camera distance and grid size mirror the editor's ground plane so the preview
            // shows the model at its true relative scale (a model that fills the dialog
            // grid will fill the editor grid too — they are the same ground reference).
            double camDist = _groundSize * 0.7;
            _previewViewport.Camera.Position = new Point3D(camDist, camDist, camDist * 0.8);
            _previewViewport.Camera.LookDirection = new Vector3D(-camDist, -camDist, -camDist * 0.8);
            _previewViewport.Camera.UpDirection = new Vector3D(0, 0, 1);

            _previewViewport.Children.Add(new DefaultLights());

            var grid = new GridLinesVisual3D
            {
                Width = _groundSize,
                Length = _groundSize,
                MinorDistance = Math.Max(1, _groundSize / 40),
                MajorDistance = Math.Max(5, _groundSize / 8),
                Thickness = _groundSize * 0.0001,
                Fill = Brushes.LightGray
            };
            _previewViewport.Children.Add(grid);

            _modelVisual = new ModelVisual3D();
            _previewViewport.Children.Add(_modelVisual);

            _previewHost.Child = _previewViewport;
            rightPanel.Controls.Add(_previewHost);
            splitContainer.Panel2.Controls.Add(rightPanel);

            this.Controls.Add(splitContainer);
            this.ApplyDpiScaling();
        }

        private void UpdatePreview()
        {
            if (_model == null || _modelVisual == null) return;

            var bounds = _model.Bounds;
            double cx = bounds.X + bounds.SizeX * 0.5;
            double cy = bounds.Y + bounds.SizeY * 0.5;
            double cz = bounds.Z;

            var group = new Transform3DGroup();
            group.Children.Add(new TranslateTransform3D(-cx, -cy, -cz));

            double sc = (double)nudScale.Value;
            group.Children.Add(new ScaleTransform3D(sc, sc, sc));

            double rx = (double)nudRotX.Value;
            double ry = (double)nudRotY.Value;
            double rz = (double)nudRotZ.Value;
            if (rz != 0) group.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 0, 1), rz)));
            if (ry != 0) group.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(0, 1, 0), ry)));
            if (rx != 0) group.Children.Add(new RotateTransform3D(
                new AxisAngleRotation3D(new Vector3D(1, 0, 0), rx)));

            group.Children.Add(new TranslateTransform3D(
                (double)nudPosX.Value, (double)nudPosY.Value, (double)nudPosZ.Value));

            _modelVisual.Content = _model;
            _modelVisual.Transform = group;
        }

        /// <summary>
        /// Triangle count plus the size the model will actually have in the scene, so
        /// the user can sanity-check the unit against something they recognise.
        /// </summary>
        private string BuildInfoText(double scale)
        {
            var b = _model.Bounds;
            return string.Format(
                "Triangles: {0}\nFile size: {1:F1} x {2:F1} x {3:F1} units\nIn the scene: {4:F1} x {5:F1} x {6:F1} m",
                CountTriangles(_model), b.SizeX, b.SizeY, b.SizeZ,
                b.SizeX * scale, b.SizeY * scale, b.SizeZ * scale);
        }

        // The slider spans 1e-4 to 1e4 metres per unit, wide enough to reach kilometres
        // at one end and to rescue a model authored in thousandths at the other.
        private int ScaleToTrack(double scale)
        {
            if (scale <= 0) return 1;
            double log = Math.Log10(scale);
            int val = (int)((log + 4) * 25);
            return Math.Max(1, Math.Min(200, val));
        }

        private double TrackToScale(int track)
        {
            double log = track / 25.0 - 4.0;
            return Math.Pow(10, log);
        }

        private int CountTriangles(Model3DGroup model)
        {
            int count = 0;
            foreach (var child in model.Children)
            {
                if (child is GeometryModel3D gm && gm.Geometry is MeshGeometry3D mesh)
                    count += mesh.TriangleIndices.Count / 3;
            }
            return count;
        }

        private static void AddRow(TableLayoutPanel table, int row, string label, Control control)
        {
            var lbl = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) };
            table.Controls.Add(lbl, 0, row);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            table.Controls.Add(control, 1, row);
        }

        private static NumericUpDown MakeNud(decimal min, decimal max, decimal value, int decimals)
        {
            var nud = new NumericUpDown
            {
                Minimum = min, Maximum = max, Value = value, DecimalPlaces = decimals,
                Dock = DockStyle.Fill
            };
            nud.Increment = decimals > 0 ? (decimal)Math.Pow(10, -Math.Min(decimals, 2)) : 1;
            return nud;
        }
    }
}
