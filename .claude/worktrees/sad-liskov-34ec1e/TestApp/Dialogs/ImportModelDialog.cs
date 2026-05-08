using System;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using HelixToolkit.Wpf;

namespace TestApp.Dialogs
{
    public class ImportModelDialog : Form
    {
        private NumericUpDown nudPosX, nudPosY, nudPosZ;
        private NumericUpDown nudRotX, nudRotY, nudRotZ;
        private NumericUpDown nudScale;
        private TrackBar trackScale;
        private Label lblInfo;
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

        public ImportModelDialog(Model3DGroup model, string fileName)
        {
            _model = model;
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
            this.ClientSize = new System.Drawing.Size(750, 520);
            this.MinimumSize = new System.Drawing.Size(650, 450);
            this.Padding = new Padding(8);

            var bounds = _model.Bounds;
            double maxExt = Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ));
            double defaultScale = maxExt > 0.001 ? 5.0 / maxExt : 1.0;

            // Left panel: controls in a TableLayoutPanel
            var leftPanel = new Panel
            {
                Dock = DockStyle.Left,
                Width = 260,
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
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));

            int row = 0;

            // Info label spanning both columns
            lblInfo = new Label
            {
                Text = string.Format("Triangles: {0}\nSize: {1:F1} x {2:F1} x {3:F1}\nAuto scale: {4:F4}",
                    CountTriangles(_model), bounds.SizeX, bounds.SizeY, bounds.SizeZ, defaultScale),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 8)
            };
            table.SetColumnSpan(lblInfo, 2);
            table.Controls.Add(lblInfo, 0, row++);

            // Position header
            var lblPos = new Label
            {
                Text = "Position",
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

            // Rotation header
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

            // Scale header
            var lblScl = new Label
            {
                Text = "Scale",
                AutoSize = true,
                Font = new System.Drawing.Font(this.Font, System.Drawing.FontStyle.Bold),
                Margin = new Padding(0, 4, 0, 2)
            };
            table.SetColumnSpan(lblScl, 2);
            table.Controls.Add(lblScl, 0, row++);

            nudScale = MakeNud(0.001m, 100m, (decimal)defaultScale, 4);
            nudScale.Increment = 0.01m;
            AddRow(table, row++, "Value:", nudScale);

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

            // Wire up value changes
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
                UpdatePreview();
            };

            // Buttons
            var buttonRow = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 8, 0, 0)
            };

            var btnResetView = new Button { Text = "Reset View", AutoSize = true };
            btnResetView.Click += (s, e) =>
            {
                _previewViewport.Camera.Position = new Point3D(10, 10, 8);
                _previewViewport.Camera.LookDirection = new Vector3D(-10, -10, -8);
                _previewViewport.Camera.UpDirection = new Vector3D(0, 0, 1);
            };

            var btnOK = new Button { Text = "Insert", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

            buttonRow.Controls.Add(btnResetView);
            buttonRow.Controls.Add(btnOK);
            buttonRow.Controls.Add(btnCancel);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            table.SetColumnSpan(buttonRow, 2);
            table.Controls.Add(buttonRow, 0, row++);

            leftPanel.Controls.Add(table);

            // Right panel: 3D preview
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
            _previewViewport.Camera.Position = new Point3D(10, 10, 8);
            _previewViewport.Camera.LookDirection = new Vector3D(-10, -10, -8);
            _previewViewport.Camera.UpDirection = new Vector3D(0, 0, 1);

            _previewViewport.Children.Add(new DefaultLights());

            var grid = new GridLinesVisual3D
            {
                Width = 40,
                Length = 40,
                MinorDistance = 1,
                MajorDistance = 5,
                Thickness = 0.01,
                Fill = Brushes.LightGray
            };
            _previewViewport.Children.Add(grid);

            _modelVisual = new ModelVisual3D();
            _previewViewport.Children.Add(_modelVisual);

            _previewHost.Child = _previewViewport;
            rightPanel.Controls.Add(_previewHost);

            this.Controls.Add(rightPanel);
            this.Controls.Add(leftPanel);
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

        private int ScaleToTrack(double scale)
        {
            double log = Math.Log10(scale);
            int val = (int)((log + 3) * 40);
            return Math.Max(1, Math.Min(200, val));
        }

        private double TrackToScale(int track)
        {
            double log = track / 40.0 - 3.0;
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
