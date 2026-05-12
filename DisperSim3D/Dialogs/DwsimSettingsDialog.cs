using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using DisperSim3D.Core;

namespace DisperSim3D.Dialogs
{
    /// <summary>
    /// Application-level DWSIM configuration: install directory + default property
    /// package. Mirrors <see cref="CfdSettingsDialog"/> for OpenFOAM — both are
    /// reached from the Dispersion menu and persist to <see cref="AppSettings"/>.
    /// Layout follows the project WinForms convention: AutoSize containers that grow
    /// to fit content, Cancel on the left and OK on the right of the button row.
    /// </summary>
    public class DwsimSettingsDialog : Form
    {
        private TextBox _txtInstallPath;
        private Button _btnBrowse;
        private Button _btnTest;
        private ComboBox _cmbPropertyPackage;
        private Label _lblStatus;

        // Fallback list when DWSIM hasn't loaded yet (so the combo isn't empty).
        private static readonly string[] _commonPackages =
        {
            "Peng-Robinson 1978 (PR78)",
            "Peng-Robinson (PR)",
            "Peng-Robinson 1978 Advanced",
            "Peng-Robinson-Stryjek-Vera 2 (PRSV2-M)",
            "Peng-Robinson-Stryjek-Vera 2 (PRSV2-VL)",
            "Soave-Redlich-Kwong (SRK)",
            "Soave-Redlich-Kwong Advanced",
            "Lee-Kesler-Plöcker",
            "Chao-Seader",
            "Grayson-Streed",
            "Raoult's Law",
            "NRTL",
            "UNIQUAC",
            "Wilson",
            "UNIFAC",
            "Modified UNIFAC (Dortmund)",
            "Steam Tables (IAPWS-IF97)",
            "CoolProp",
            "GERG-2008",
            "PC-SAFT"
        };

        public DwsimSettingsDialog()
        {
            Text = "DWSIM Settings (Application)";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96F, 96F);
            // Let the form expand to fit its contents instead of being clipped to a
            // hard-coded size that doesn't account for DPI / theme padding.
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(640, 0);
            Padding = new Padding(0);

            BuildUI();
            PopulatePackages();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(10 * dpi)),
                ColumnCount = 1,
                RowCount = 4
            };
            outer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // install group
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // property package group
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // status label
            outer.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // button row

            // ── DWSIM install path group ───────────────────────────────
            var installGroup = new GroupBox
            {
                Text = "DWSIM Installation",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi))
            };
            var ig = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 2
            };
            ig.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ig.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            ig.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            ig.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            ig.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            ig.Controls.Add(new Label
            {
                Text = "Install directory:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
            }, 0, 0);
            _txtInstallPath = new TextBox
            {
                Dock = DockStyle.Fill,
                Text = AppSettings.Instance.DwsimInstallPath ?? "",
                MinimumSize = new Size((int)(360 * dpi), 0)
            };
            ig.Controls.Add(_txtInstallPath, 1, 0);
            _btnBrowse = new Button { Text = "Browse...", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            _btnBrowse.Click += (s, e) => Browse();
            ig.Controls.Add(_btnBrowse, 2, 0);

            var hint = new Label
            {
                Text = "Folder containing DWSIM.Automation.FluentAPI.dll.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, (int)(2 * dpi), 0, 0)
            };
            ig.SetColumnSpan(hint, 3);
            ig.Controls.Add(hint, 0, 1);
            installGroup.Controls.Add(ig);

            // ── Property package group ─────────────────────────────────
            var ppGroup = new GroupBox
            {
                Text = "Default Property Package",
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding((int)(8 * dpi))
            };
            var pp = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 2
            };
            pp.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pp.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pp.Controls.Add(new Label
            {
                Text = "Property Package:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Padding = new Padding(0, (int)(6 * dpi), (int)(6 * dpi), 0)
            }, 0, 0);
            _cmbPropertyPackage = new ComboBox
            {
                Dock = DockStyle.Fill,
                DropDownStyle = ComboBoxStyle.DropDownList,
                MinimumSize = new Size((int)(420 * dpi), 0)
            };
            pp.Controls.Add(_cmbPropertyPackage, 1, 0);
            var ppHint = new Label
            {
                Text = "Used by every mixture flash. Peng-Robinson 1978 is the default for gas dispersion.",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Padding = new Padding(0, (int)(2 * dpi), 0, 0)
            };
            pp.SetColumnSpan(ppHint, 2);
            pp.Controls.Add(ppHint, 0, 1);
            ppGroup.Controls.Add(pp);

            // ── Status row ─────────────────────────────────────────────
            _lblStatus = new Label
            {
                Text = "",
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Dock = DockStyle.Fill,
                Padding = new Padding((int)(4 * dpi))
            };

            // ── Button row — Test on left, then spacer, then Cancel and OK on right.
            //    Project convention (memory: feedback_winforms_button_order): Cancel
            //    BEFORE OK in column order so Cancel sits to the left of OK.
            var btns = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, (int)(4 * dpi), 0, 0)
            };
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btns.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _btnTest = new Button { Text = "Test connection", AutoSize = true, Padding = new Padding(8, 2, 8, 2) };
            _btnTest.Click += (s, e) => TestConnection();
            var btnCancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(12, 2, 12, 2), DialogResult = DialogResult.Cancel };
            var btnOK = new Button { Text = "OK", AutoSize = true, Padding = new Padding(16, 2, 16, 2) };
            btnOK.Click += (s, e) => CommitAndClose();
            btns.Controls.Add(_btnTest, 0, 0);
            btns.Controls.Add(new Label { Dock = DockStyle.Fill }, 1, 0);
            btns.Controls.Add(btnCancel, 2, 0);
            btns.Controls.Add(btnOK, 3, 0);
            AcceptButton = btnOK;
            CancelButton = btnCancel;

            outer.Controls.Add(installGroup, 0, 0);
            outer.Controls.Add(ppGroup, 0, 1);
            outer.Controls.Add(_lblStatus, 0, 2);
            outer.Controls.Add(btns, 0, 3);
            Controls.Add(outer);
        }

        private void PopulatePackages()
        {
            _cmbPropertyPackage.Items.Clear();
            // Try to read the live list from DWSIM if it's already initialised; fall
            // back to the static list so the combo isn't empty when the user hasn't
            // pointed at an install yet.
            var live = DwsimThermo.AvailablePropertyPackages();
            var packages = live.Count > 0 ? live.ToArray() : _commonPackages;
            foreach (var p in packages) _cmbPropertyPackage.Items.Add(p);
            string current = AppSettings.Instance.DwsimPropertyPackage;
            int idx = -1;
            if (!string.IsNullOrEmpty(current))
            {
                for (int i = 0; i < _cmbPropertyPackage.Items.Count; i++)
                    if (string.Equals(_cmbPropertyPackage.Items[i].ToString(), current, StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                if (idx < 0)
                {
                    _cmbPropertyPackage.Items.Insert(0, current);
                    idx = 0;
                }
            }
            if (idx < 0) idx = 0;
            _cmbPropertyPackage.SelectedIndex = idx;
        }

        private void Browse()
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select the DWSIM install directory (contains DWSIM.Automation.FluentAPI.dll)";
                if (Directory.Exists(_txtInstallPath.Text)) fbd.SelectedPath = _txtInstallPath.Text;
                if (fbd.ShowDialog(this) != DialogResult.OK) return;
                _txtInstallPath.Text = fbd.SelectedPath;
            }
        }

        private void TestConnection()
        {
            Cursor = Cursors.WaitCursor;
            _lblStatus.Text = "Loading DWSIM.Automation.FluentAPI...";
            Application.DoEvents();
            try
            {
                if (DwsimThermo.Initialize(_txtInstallPath.Text))
                {
                    int n = DwsimThermo.AvailableCompounds().Count;
                    _lblStatus.Text = string.Format(
                        "Connected — {0} compounds, {1} property packages.",
                        n, DwsimThermo.AvailablePropertyPackages().Count);
                    _lblStatus.ForeColor = Color.DarkGreen;
                    PopulatePackages();
                }
                else
                {
                    _lblStatus.Text = "Failed: " + DwsimThermo.LastError;
                    _lblStatus.ForeColor = Color.Firebrick;
                }
            }
            finally { Cursor = Cursors.Default; }
        }

        private void CommitAndClose()
        {
            AppSettings.Instance.DwsimInstallPath = (_txtInstallPath.Text ?? "").Trim();
            AppSettings.Instance.DwsimPropertyPackage =
                _cmbPropertyPackage.SelectedItem?.ToString() ?? "Peng-Robinson 1978 (PR78)";
            AppSettings.Instance.Save();
            // Invalidate the cached flowsheet so the next flash uses the new path / PP.
            DwsimThermo.ResetFlowsheetCache();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
