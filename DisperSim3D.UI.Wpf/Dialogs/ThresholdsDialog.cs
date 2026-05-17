using System;
using System.Collections.Generic;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class ThresholdsDialog : Form
    {
        private DataGridView _grid;

        public List<DispersionThreshold> Result { get; private set; }

        /// <summary>LFL used as the seed for the 3 default layers when
        /// <paramref name="existing"/> is null / empty. Pass the active gas's
        /// LFL so the dialog matches what's drawn in the viewport; default
        /// 0.033 (methane) when unknown.</summary>
        public ThresholdsDialog(List<DispersionThreshold> existing = null, double defaultLfl = 0.033)
        {
            BuildUI();
            if (existing != null && existing.Count > 0)
            {
                foreach (var t in existing)
                    AddThresholdRow(t);
            }
            else
            {
                // Seed with the same 3-layer LFL set the renderer falls back
                // to, so what the user sees on screen ≡ what's in the dialog.
                foreach (var t in DispersionThreshold.BuildDefaultLflLayers(defaultLfl))
                    AddThresholdRow(t);
            }
        }

        private void BuildUI()
        {
            this.Text = "Dispersion Thresholds";
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            var dpi = DeviceDpi / 96f;
            this.ClientSize = new System.Drawing.Size((int)(620 * dpi), (int)(440 * dpi));
            this.MinimumSize = new System.Drawing.Size((int)(500 * dpi), (int)(380 * dpi));
            this.Padding = new Padding((int)(10 * dpi));

            var outerLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            outerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            outerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var help = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText,
                Font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Regular),
                MaximumSize = new System.Drawing.Size((int)(560 * dpi), 0),
                Margin = new Padding(0, 0, 0, 6),
                Text = "Each threshold becomes a 3D isosurface in the visualization.\n" +
                       "Preview: chequer-backed swatch — double-click to open the colour picker.\n" +
                       "Name: label shown in the legend (e.g. Low, Medium, High, LFL).\n" +
                       "Concentration: kg/m³ value defining the isosurface.\n" +
                       "Color (ARGB hex): 8 hex digits, e.g. 64FF0000 = semi-transparent red.\n" +
                       "Opacity: 0 = invisible, 1 = solid (the alpha in Color also blends in).\n" +
                       "Visible: uncheck to hide without deleting.\n" +
                       "Pick color... opens the system colour picker for the selected row (keeps the existing alpha)."
            };
            outerLayout.Controls.Add(help, 0, 0);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };

            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Concentration", "Concentration (kg/m³)");
            // Hidden hex storage column (the swatch column reads from it).
            // Kept visible too so advanced users can still type an ARGB hex
            // value directly; hidden swatch column on the left of it shows
            // the live preview.
            var swatchCol = new DataGridViewImageColumn {
                Name = "Swatch", HeaderText = "Preview",
                Width = (int)(64 * dpi),
                ImageLayout = DataGridViewImageCellLayout.Stretch
            };
            _grid.Columns.Add(swatchCol);
            _grid.Columns.Add("ColorHex", "Color (ARGB hex)");
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Opacity", HeaderText = "Opacity (0-1)" });
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Visible", HeaderText = "Visible" });

            // Double-click the swatch cell → opens a colour picker for that row.
            _grid.CellDoubleClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (e.ColumnIndex == _grid.Columns["Swatch"].Index)
                    PickColorForRow(e.RowIndex);
            };
            // Re-paint swatch whenever the hex cell is edited.
            _grid.CellEndEdit += (s, e) =>
            {
                if (e.ColumnIndex == _grid.Columns["ColorHex"].Index)
                    RefreshSwatch(e.RowIndex);
            };

            outerLayout.Controls.Add(_grid, 0, 1);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 5, RowCount = 1, Padding = new Padding(4),
                Margin = new Padding(0, 6, 0, 0)
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var btnAdd = new Button { Text = "Add", AutoSize = true };
            btnAdd.Click += (s, e) =>
            {
                AddThresholdRow(new DispersionThreshold
                {
                    Name = "Custom",
                    Type = DispersionThresholdType.Custom,
                    ConcentrationValue = 0.01,
                    Color = System.Windows.Media.Color.FromArgb(100, 0, 200, 0),
                    Opacity = 0.2,
                    Visible = true
                });
            };

            var btnRemove = new Button { Text = "Remove", AutoSize = true };
            btnRemove.Click += (s, e) =>
            {
                if (_grid.SelectedRows.Count > 0)
                    _grid.Rows.RemoveAt(_grid.SelectedRows[0].Index);
            };

            var btnPickColor = new Button { Text = "Pick color...", AutoSize = true };
            btnPickColor.Click += (s, e) =>
            {
                int row = _grid.SelectedRows.Count > 0
                    ? _grid.SelectedRows[0].Index
                    : (_grid.CurrentRow?.Index ?? -1);
                if (row >= 0) PickColorForRow(row);
            };
            var btnResetDefaults = new Button { Text = "Reset to 3 default LFL layers", AutoSize = true };
            btnResetDefaults.Click += (s, e) =>
            {
                _grid.Rows.Clear();
                foreach (var t in DispersionThreshold.BuildDefaultLflLayers(0.033))
                    AddThresholdRow(t);
            };

            var btnOK = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            btnOK.Click += BtnOK_Click;

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };

            // Layout: Add | Remove | Pick color | Reset | (spacer) | Cancel | OK
            buttons.ColumnCount = 7;
            buttons.ColumnStyles.Clear();
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            buttons.Controls.Add(btnAdd, 0, 0);
            buttons.Controls.Add(btnRemove, 1, 0);
            buttons.Controls.Add(btnPickColor, 2, 0);
            buttons.Controls.Add(btnResetDefaults, 3, 0);
            buttons.Controls.Add(new Label(), 4, 0);
            buttons.Controls.Add(btnCancel, 5, 0);
            buttons.Controls.Add(btnOK, 6, 0);

            this.AcceptButton = btnOK;
            this.CancelButton = btnCancel;

            outerLayout.Controls.Add(buttons, 0, 2);
            this.Controls.Add(outerLayout);
            this.ApplyDpiScaling();
        }

        private void AddThresholdRow(DispersionThreshold t)
        {
            string colorHex = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}", t.Color.A, t.Color.R, t.Color.G, t.Color.B);
            // Swatch column index = 2 (Name, Concentration, Swatch, ColorHex, Opacity, Visible)
            _grid.Rows.Add(t.Name, t.ConcentrationValue.ToString("G6"),
                MakeSwatch(t.Color.R, t.Color.G, t.Color.B, t.Color.A),
                colorHex, t.Opacity.ToString("F2"), t.Visible);
        }

        private static System.Drawing.Image MakeSwatch(byte r, byte g, byte b, byte a)
        {
            // 48×16 PNG-like in-memory bitmap. The alpha channel is rendered
            // against a chequer-pattern background so the user can SEE the
            // transparency, not just the colour.
            var bmp = new System.Drawing.Bitmap(48, 16);
            using (var gx = System.Drawing.Graphics.FromImage(bmp))
            {
                // Chequer base.
                using (var dark  = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(200, 200, 200)))
                using (var light = new System.Drawing.SolidBrush(System.Drawing.Color.White))
                {
                    for (int y = 0; y < 16; y += 4)
                        for (int x = 0; x < 48; x += 4)
                            gx.FillRectangle(((x / 4 + y / 4) & 1) == 0 ? light : dark, x, y, 4, 4);
                }
                using (var fg = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(a, r, g, b)))
                    gx.FillRectangle(fg, 0, 0, 48, 16);
            }
            return bmp;
        }

        private void RefreshSwatch(int row)
        {
            if (row < 0 || row >= _grid.Rows.Count) return;
            string hex = _grid.Rows[row].Cells["ColorHex"].Value?.ToString() ?? "64FF0000";
            if (hex.Length != 8) return;
            try
            {
                byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                _grid.Rows[row].Cells["Swatch"].Value = MakeSwatch(r, g, b, a);
            }
            catch { /* leave previous swatch */ }
        }

        private void PickColorForRow(int row)
        {
            if (row < 0 || row >= _grid.Rows.Count) return;
            string hex = _grid.Rows[row].Cells["ColorHex"].Value?.ToString() ?? "64FF0000";
            byte aIn = 100, rIn = 255, gIn = 0, bIn = 0;
            if (hex.Length == 8)
            {
                try {
                    aIn = Convert.ToByte(hex.Substring(0, 2), 16);
                    rIn = Convert.ToByte(hex.Substring(2, 2), 16);
                    gIn = Convert.ToByte(hex.Substring(4, 2), 16);
                    bIn = Convert.ToByte(hex.Substring(6, 2), 16);
                } catch { }
            }
            using var dlg = new ColorDialog
            {
                Color = System.Drawing.Color.FromArgb(rIn, gIn, bIn),
                AllowFullOpen = true, FullOpen = true,
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            // ColorDialog has no alpha channel — preserve the existing alpha.
            string newHex = string.Format("{0:X2}{1:X2}{2:X2}{3:X2}",
                aIn, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            _grid.Rows[row].Cells["ColorHex"].Value = newHex;
            RefreshSwatch(row);
        }

        private void BtnOK_Click(object sender, EventArgs e)
        {
            Result = new List<DispersionThreshold>();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.IsNewRow) continue;

                string name = row.Cells["Name"].Value?.ToString() ?? "Threshold";
                double conc = 0.01;
                double.TryParse(row.Cells["Concentration"].Value?.ToString(), out conc);

                double opacity = 0.3;
                double.TryParse(row.Cells["Opacity"].Value?.ToString(), out opacity);

                bool visible = true;
                if (row.Cells["Visible"].Value is bool v) visible = v;

                var color = System.Windows.Media.Color.FromArgb(100, 255, 0, 0);
                string hex = row.Cells["ColorHex"].Value?.ToString() ?? "64FF0000";
                if (hex.Length == 8)
                {
                    try
                    {
                        byte a = Convert.ToByte(hex.Substring(0, 2), 16);
                        byte r = Convert.ToByte(hex.Substring(2, 2), 16);
                        byte g = Convert.ToByte(hex.Substring(4, 2), 16);
                        byte b = Convert.ToByte(hex.Substring(6, 2), 16);
                        color = System.Windows.Media.Color.FromArgb(a, r, g, b);
                    }
                    catch { }
                }

                Result.Add(new DispersionThreshold
                {
                    Name = name,
                    Type = DispersionThresholdType.Custom,
                    ConcentrationValue = conc,
                    Color = color,
                    Opacity = opacity,
                    Visible = visible
                });
            }
        }
    }
}
