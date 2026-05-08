using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.Dialogs
{
    public class CfdSettingsDialog : Form
    {
        private ComboBox _cmbEnvType;
        private TextBox _txtPath;
        private Button _btnBrowse;
        private TextBox _txtWslDistro;
        private Label _lblWslDistro;
        private Label _lblEnvStatus;
        private TextBox _txtDiffusivity;
        private TextBox _txtWriteInterval;
        private NumericUpDown _nudGridRes;
        private NumericUpDown _nudProcessors;
        private Label _lblCellEstimate;
        private TextBox _txtSolverTolerance;
        private ComboBox _cmbScheme;
        private CheckBox _chkAdjustableDt;
        private TextBox _txtMaxCourant;
        private NumericUpDown _nudPurgeWrite;
        private CheckBox _chkSubgrid;
        private TextBox _txtSubgridMargin;
        private CheckBox _chkWindField;
        private Button _btnOk;
        private Button _btnCancel;
        private Button _btnTest;

        private ToolTip _tip;
        private OpenFoamEnvironment _environment;
        public CfdConfiguration Result { get; private set; }

        public CfdSettingsDialog(CfdConfiguration config, OpenFoamEnvironment env)
        {
            _environment = env ?? new OpenFoamEnvironment();
            Result = config ?? new CfdConfiguration();

            InitializeComponent();

            this.Text = "CFD Settings (OpenFOAM)";
            this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            var dpi = DeviceDpi / 96f;

            BuildUI();
            this.ApplyDpiScaling();
            LoadFromConfig();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;

            _tip = new ToolTip
            {
                AutoPopDelay = 15000,
                InitialDelay = 400,
                ReshowDelay = 200,
                ShowAlways = true
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                AutoSize = true,
                Margin = new Padding((int)(12 * dpi)),
                Padding = new Padding((int)(12 * dpi)),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.None
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(140 * dpi)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            // --- Environment section ---
            AddSectionHeader(layout, row++, "OpenFOAM Environment");

            AddLabel(layout, row, "Environment:");
            _cmbEnvType = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Dock = DockStyle.Fill
            };
            _cmbEnvType.Items.Add("None");
            _cmbEnvType.Items.Add("Native Windows");
            _cmbEnvType.Items.Add("WSL2");
            _cmbEnvType.Items.Add("Docker");
            _cmbEnvType.Items.Add("BlueCFD");
            _cmbEnvType.SelectedIndexChanged += (s, e) => OnEnvTypeChanged();
            layout.Controls.Add(_cmbEnvType, 1, row++);

            AddLabel(layout, row, "Path / Image:");
            var pathPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(2),
                Padding = new Padding(2)
            };
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pathPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtPath = new TextBox { Dock = DockStyle.Fill, WordWrap = true };
            _btnBrowse = new Button { Text = "...", AutoSize = true, Padding = new Padding(4) };
            _btnBrowse.Click += BtnBrowse_Click;
            pathPanel.Controls.Add(_txtPath, 0, 0);
            pathPanel.Controls.Add(_btnBrowse, 1, 0);
            layout.Controls.Add(pathPanel, 1, row++);

            _lblWslDistro = new Label
            {
                Text = "WSL Distro:",
                AutoSize = true,
                Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 4, 0)
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(_lblWslDistro, 0, row);
            _txtWslDistro = new TextBox { Dock = DockStyle.Fill, Text = "Ubuntu" };
            layout.Controls.Add(_txtWslDistro, 1, row++);

            AddLabel(layout, row, "Status:");
            var statusPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                AutoSize = true, Margin = new Padding(0), Padding = new Padding(0)
            };
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            statusPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            statusPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _lblEnvStatus = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.Gray,
                Padding = new Padding(4)
            };
            _btnTest = new Button { Text = "Test", AutoSize = true, Padding = new Padding(4) };
            _btnTest.Click += BtnTest_Click;
            statusPanel.Controls.Add(_lblEnvStatus, 0, 0);
            statusPanel.Controls.Add(_btnTest, 1, 0);
            layout.Controls.Add(statusPanel, 1, row++);

            // --- Solver section ---
            AddSectionHeader(layout, row++, "Solver Settings");

            AddLabel(layout, row, "Diffusivity:");
            var diffPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            diffPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            diffPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            diffPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtDiffusivity = new TextBox
            {
                Width = (int)(80 * dpi),
                Text = Result.DiffusivityM2PerS.ToString("E2", System.Globalization.CultureInfo.InvariantCulture)
            };
            _tip.SetToolTip(_txtDiffusivity,
                "Molecular diffusivity of the transported scalar (DT).\n" +
                "Typical values:\n" +
                "  Air at 25 °C: ~2E-05 m²/s\n" +
                "  Heavy gases (Cl₂, NH₃): ~1E-05 m²/s\n" +
                "Higher values spread the plume faster.");
            diffPanel.Controls.Add(_txtDiffusivity, 0, 0);
            diffPanel.Controls.Add(new Label
            {
                Text = "m²/s", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(diffPanel, 1, row++);

            AddLabel(layout, row, "Write Interval:");
            var writePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            writePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            writePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            writePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtWriteInterval = new TextBox
            {
                Width = (int)(80 * dpi),
                Text = Result.WriteIntervalS > 0
                    ? Result.WriteIntervalS.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)
                    : "auto"
            };
            _tip.SetToolTip(_txtWriteInterval,
                "How often OpenFOAM writes result files to disk.\n" +
                "'auto' = duration/100 (one file per 1% of simulation).\n" +
                "Lower values give finer time resolution but use more disk space.\n" +
                "For a 300 s simulation, 'auto' writes every 3 s.");
            writePanel.Controls.Add(_txtWriteInterval, 0, 0);
            writePanel.Controls.Add(new Label
            {
                Text = "s (or 'auto')", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(writePanel, 1, row++);

            AddLabel(layout, row, "Grid Resolution:");
            var gridPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 2,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            gridPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gridPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            gridPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            gridPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _nudGridRes = new NumericUpDown
            {
                Minimum = 10,
                Maximum = 500,
                Increment = 10,
                Value = Result.GridResolution,
                Width = (int)(80 * dpi)
            };
            _tip.SetToolTip(_nudGridRes,
                "Number of cells along each axis (X and Y).\n" +
                "Z axis uses half this value.\n" +
                "  20-40: coarse, fast (seconds)\n" +
                "  50-80: moderate detail (minutes)\n" +
                "  100+: fine detail, slow (may need parallel CPUs)\n" +
                "Total cells = N × N × (N/2). Doubling this value\n" +
                "increases cell count by ~8× and runtime similarly.");
            _nudGridRes.ValueChanged += (s, e) => UpdateCellEstimate();
            gridPanel.Controls.Add(_nudGridRes, 0, 0);
            gridPanel.Controls.Add(new Label
            {
                Text = "cells per axis (10-500)", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            _lblCellEstimate = new Label
            {
                AutoSize = true,
                ForeColor = Color.Gray,
                Margin = new Padding(0, 2, 0, 0)
            };
            gridPanel.SetColumnSpan(_lblCellEstimate, 2);
            gridPanel.Controls.Add(_lblCellEstimate, 0, 1);
            UpdateCellEstimate();
            layout.Controls.Add(gridPanel, 1, row++);

            AddLabel(layout, row, "Parallel CPUs:");
            var cpuPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            cpuPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cpuPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cpuPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _nudProcessors = new NumericUpDown
            {
                Minimum = 1,
                Maximum = Environment.ProcessorCount,
                Increment = 1,
                Value = Math.Min(Result.NumberOfProcessors, Environment.ProcessorCount),
                Width = (int)(80 * dpi)
            };
            _tip.SetToolTip(_nudProcessors,
                "Number of CPU cores for parallel solving (MPI).\n" +
                "1 = serial mode (no decomposition).\n" +
                "2+ = domain is split via scotch method,\n" +
                "solved in parallel, then reconstructed.\n" +
                "Recommended: half of available cores (" +
                (Environment.ProcessorCount / 2) + " on this machine).\n" +
                "Using all cores may slow down the OS.");
            cpuPanel.Controls.Add(_nudProcessors, 0, 0);
            cpuPanel.Controls.Add(new Label
            {
                Text = string.Format("1-{0} (1 = serial)", Environment.ProcessorCount),
                AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(cpuPanel, 1, row++);

            AddLabel(layout, row, "Solver Tolerance:");
            var tolPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                AutoSize = true, Margin = new Padding(0), Padding = new Padding(0)
            };
            tolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tolPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtSolverTolerance = new TextBox
            {
                Width = (int)(80 * dpi),
                Text = Result.SolverTolerance.ToString("E1", System.Globalization.CultureInfo.InvariantCulture)
            };
            _tip.SetToolTip(_txtSolverTolerance,
                "Convergence tolerance for the linear solver (PBiCGStab).\n" +
                "The solver iterates until the residual drops below this value.\n" +
                "  1E-06: fast, less accurate (screening runs)\n" +
                "  1E-08: default, good balance\n" +
                "  1E-10: very tight, slower (final/validation runs)\n" +
                "Too loose may produce noisy concentration fields.");
            tolPanel.Controls.Add(_txtSolverTolerance, 0, 0);
            tolPanel.Controls.Add(new Label
            {
                Text = "(e.g. 1E-08)", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(tolPanel, 1, row++);

            AddLabel(layout, row, "Num. Scheme:");
            _cmbScheme = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = (int)(160 * dpi)
            };
            _cmbScheme.Items.Add("linearUpwind");
            _cmbScheme.Items.Add("upwind");
            _cmbScheme.Items.Add("linear");
            _cmbScheme.Items.Add("limitedLinear 1");
            _cmbScheme.Items.Add("vanLeer");
            _cmbScheme.SelectedItem = Result.NumericalScheme ?? "linearUpwind";
            if (_cmbScheme.SelectedIndex < 0) _cmbScheme.SelectedIndex = 0;
            _tip.SetToolTip(_cmbScheme,
                "Numerical discretization scheme for the convection term div(phi,T).\n\n" +
                "linearUpwind (default): 2nd order, good accuracy with\n" +
                "  mild numerical diffusion. Best for most cases.\n" +
                "upwind: 1st order, very stable but diffusive.\n" +
                "  Use for difficult cases or initial runs.\n" +
                "linear: 2nd order central, no numerical diffusion\n" +
                "  but may oscillate — can produce negative concentrations.\n" +
                "limitedLinear: bounded 2nd order, prevents oscillations.\n" +
                "vanLeer: TVD limiter, monotone and stable.\n" +
                "  Good compromise between accuracy and stability.");
            layout.Controls.Add(_cmbScheme, 1, row++);

            _chkAdjustableDt = new CheckBox
            {
                Text = "Adjustable Time Step",
                AutoSize = true,
                Checked = Result.AdjustableTimeStep,
                Margin = new Padding(0, 6, 0, 0)
            };
            _tip.SetToolTip(_chkAdjustableDt,
                "When enabled, OpenFOAM automatically adjusts deltaT\n" +
                "at each iteration to keep the Courant number below\n" +
                "the Max Courant limit. This improves stability for\n" +
                "cases with varying velocities (e.g., jet releases).\n" +
                "When disabled, deltaT remains fixed at the initial value.\n" +
                "Recommended: ON for transient dispersion simulations.");
            _chkAdjustableDt.CheckedChanged += (s, e) => _txtMaxCourant.Enabled = _chkAdjustableDt.Checked;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.SetColumnSpan(_chkAdjustableDt, 2);
            layout.Controls.Add(_chkAdjustableDt, 0, row++);

            AddLabel(layout, row, "Max Courant:");
            var coPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                AutoSize = true, Margin = new Padding(0), Padding = new Padding(0)
            };
            coPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            coPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            coPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtMaxCourant = new TextBox
            {
                Width = (int)(80 * dpi),
                Text = Result.MaxCourantNumber.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                Enabled = Result.AdjustableTimeStep
            };
            _tip.SetToolTip(_txtMaxCourant,
                "Maximum Courant number (Co = U·Δt/Δx).\n" +
                "Controls how far information travels per time step.\n" +
                "  Co > 1: unstable — solver may diverge.\n" +
                "  0.5 (default): safe for most dispersion cases.\n" +
                "  0.2-0.3: more conservative, slower but very stable.\n" +
                "  0.8-1.0: faster but may introduce oscillations.\n" +
                "Only used when Adjustable Time Step is enabled.");
            coPanel.Controls.Add(_txtMaxCourant, 0, 0);
            coPanel.Controls.Add(new Label
            {
                Text = "(0.1–1.0, lower = safer)", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(coPanel, 1, row++);

            AddLabel(layout, row, "Purge Write:");
            var purgePanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                AutoSize = true, Margin = new Padding(0), Padding = new Padding(0)
            };
            purgePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            purgePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            purgePanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _nudPurgeWrite = new NumericUpDown
            {
                Minimum = 0, Maximum = 1000, Increment = 1,
                Value = Result.PurgeWrite,
                Width = (int)(80 * dpi)
            };
            _tip.SetToolTip(_nudPurgeWrite,
                "Number of time step directories to keep on disk.\n" +
                "0 (default): keep ALL written timesteps.\n" +
                "N > 0: only the last N timesteps are kept,\n" +
                "  older ones are automatically deleted.\n" +
                "Useful to save disk space on long simulations.\n" +
                "For post-processing the full timeline, keep 0.\n" +
                "For monitoring-only runs, try 5-10.");
            purgePanel.Controls.Add(_nudPurgeWrite, 0, 0);
            purgePanel.Controls.Add(new Label
            {
                Text = "(0 = keep all timesteps)", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(purgePanel, 1, row++);

            // --- Optimization section ---
            AddSectionHeader(layout, row++, "Optimization");

            _chkSubgrid = new CheckBox
            {
                Text = "Use Gaussian Plume subgrid",
                AutoSize = true,
                Checked = Result.UseGaussianSubgrid,
                Margin = new Padding(0, 6, 0, 0)
            };
            _tip.SetToolTip(_chkSubgrid,
                "When enabled, a fast Gaussian Plume estimate runs first\n" +
                "to predict where the gas cloud will be. The CFD mesh is\n" +
                "then created only in that region, drastically reducing\n" +
                "the number of cells and computation time.\n" +
                "Disable if the plume estimate doesn't match the CFD result\n" +
                "(e.g., complex terrain or recirculation zones).");
            _chkSubgrid.CheckedChanged += (s, e) => _txtSubgridMargin.Enabled = _chkSubgrid.Checked;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.SetColumnSpan(_chkSubgrid, 2);
            layout.Controls.Add(_chkSubgrid, 0, row++);

            AddLabel(layout, row, "Subgrid Margin:");
            var marginPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
                AutoSize = true, Margin = new Padding(0), Padding = new Padding(0)
            };
            marginPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            marginPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            marginPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _txtSubgridMargin = new TextBox
            {
                Width = (int)(80 * dpi),
                Text = Result.SubgridMarginFactor.ToString("F1", System.Globalization.CultureInfo.InvariantCulture),
                Enabled = Result.UseGaussianSubgrid
            };
            _tip.SetToolTip(_txtSubgridMargin,
                "How much extra space to add around the estimated plume.\n" +
                "1.0 = exact plume bounds (tight, may clip edges)\n" +
                "1.5 = 50% margin (default, good safety)\n" +
                "2.0 = 100% margin (very conservative)\n" +
                "Higher values give more safety but reduce the speed benefit.");
            marginPanel.Controls.Add(_txtSubgridMargin, 0, 0);
            marginPanel.Controls.Add(new Label
            {
                Text = "factor (1.0-3.0)", AutoSize = true,
                Margin = new Padding(4, 4, 0, 0), ForeColor = Color.Gray
            }, 1, 0);
            layout.Controls.Add(marginPanel, 1, row++);

            _chkWindField = new CheckBox
            {
                Text = "Pre-compute wind field for Gaussian Puff",
                AutoSize = true,
                Checked = Result.UseWindField,
                Margin = new Padding(0, 6, 0, 0)
            };
            _tip.SetToolTip(_chkWindField,
                "When enabled, a steady-state CFD simulation (simpleFoam)\n" +
                "runs first to compute the wind field around obstacles.\n" +
                "The Gaussian Puff model then uses local wind vectors\n" +
                "instead of uniform wind, producing more realistic\n" +
                "dispersion around buildings and equipment.\n" +
                "Requires OpenFOAM to be available.");
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.SetColumnSpan(_chkWindField, 2);
            layout.Controls.Add(_chkWindField, 0, row++);

            // --- Buttons ---
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            row++;

            // --- Buttons ---
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var sep = new Label { Height = (int)(16 * dpi) };
            layout.SetColumnSpan(sep, 2);
            layout.Controls.Add(sep, 0, row++);

            var btnTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                Margin = new Padding(0),
                Padding = new Padding(4)
            };
            btnTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            btnTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _btnOk = new Button { Text = "OK", Width = (int)(60*dpi), Height = (int)(24*dpi) };
            _btnOk.Click += BtnOk_Click;
            _btnCancel = new Button { Text = "Cancel", Width = (int)(60*dpi), Height = (int)(24*dpi), DialogResult = DialogResult.Cancel };
            btnTable.Controls.Add(new Label(), 0, 0);
            btnTable.Controls.Add(_btnCancel, 1, 0);
            btnTable.Controls.Add(_btnOk, 2, 0);
            layout.SetColumnSpan(btnTable, 2);
            layout.Controls.Add(btnTable, 0, row++);

            this.Controls.Add(layout);
            this.AcceptButton = _btnOk;
            this.CancelButton = _btnCancel;
        }

        private void LoadFromConfig()
        {
            _txtPath.Text = Result.OpenFoamPath ?? "";
            _txtWslDistro.Text = Result.WslDistroName ?? "Ubuntu";

            switch (Result.DetectedEnvironment)
            {
                case OpenFoamEnvironmentType.NativeWindows: _cmbEnvType.SelectedIndex = 1; break;
                case OpenFoamEnvironmentType.WSL2: _cmbEnvType.SelectedIndex = 2; break;
                case OpenFoamEnvironmentType.Docker: _cmbEnvType.SelectedIndex = 3; break;
                case OpenFoamEnvironmentType.BlueCFD: _cmbEnvType.SelectedIndex = 4; break;
                default: _cmbEnvType.SelectedIndex = 0; break;
            }

            OnEnvTypeChanged();
        }

        private void OnEnvTypeChanged()
        {
            bool isWsl = _cmbEnvType.SelectedIndex == 2;
            _lblWslDistro.Visible = isWsl;
            _txtWslDistro.Visible = isWsl;

            bool isDocker = _cmbEnvType.SelectedIndex == 3;
            _btnBrowse.Visible = !isDocker;

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var type = GetSelectedType();
            string path = _txtPath.Text.Trim();

            if (type == OpenFoamEnvironmentType.None || string.IsNullOrEmpty(path))
            {
                _lblEnvStatus.Text = "No OpenFOAM environment configured.";
                _lblEnvStatus.ForeColor = Color.Gray;
                return;
            }

            _environment.Configure(path, type, _txtWslDistro.Text.Trim());
            _lblEnvStatus.Text = _environment.StatusMessage ?? "Configured";
            _lblEnvStatus.ForeColor = _environment.IsAvailable ? Color.DarkGreen : Color.Red;
        }

        private OpenFoamEnvironmentType GetSelectedType()
        {
            switch (_cmbEnvType.SelectedIndex)
            {
                case 1: return OpenFoamEnvironmentType.NativeWindows;
                case 2: return OpenFoamEnvironmentType.WSL2;
                case 3: return OpenFoamEnvironmentType.Docker;
                case 4: return OpenFoamEnvironmentType.BlueCFD;
                default: return OpenFoamEnvironmentType.None;
            }
        }

        private void BtnBrowse_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Select OpenFOAM installation folder";
                if (!string.IsNullOrEmpty(_txtPath.Text) && System.IO.Directory.Exists(_txtPath.Text))
                    fbd.SelectedPath = _txtPath.Text;
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    _txtPath.Text = fbd.SelectedPath;
                    UpdateStatus();
                }
            }
        }

        private void BtnOk_Click(object sender, EventArgs e)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double diff;
            if (double.TryParse(_txtDiffusivity.Text, System.Globalization.NumberStyles.Float, inv, out diff))
                Result.DiffusivityM2PerS = diff;

            if (_txtWriteInterval.Text.Trim().ToLower() == "auto")
                Result.WriteIntervalS = -1;
            else
            {
                double wi;
                if (double.TryParse(_txtWriteInterval.Text, System.Globalization.NumberStyles.Float, inv, out wi))
                    Result.WriteIntervalS = wi;
            }

            Result.GridResolution = (int)_nudGridRes.Value;
            Result.NumberOfProcessors = (int)_nudProcessors.Value;

            double tol;
            if (double.TryParse(_txtSolverTolerance.Text, System.Globalization.NumberStyles.Float, inv, out tol))
                Result.SolverTolerance = tol;

            Result.NumericalScheme = _cmbScheme.SelectedItem?.ToString() ?? "linearUpwind";
            Result.AdjustableTimeStep = _chkAdjustableDt.Checked;

            double co;
            if (double.TryParse(_txtMaxCourant.Text, System.Globalization.NumberStyles.Float, inv, out co))
                Result.MaxCourantNumber = co;

            Result.PurgeWrite = (int)_nudPurgeWrite.Value;

            Result.UseGaussianSubgrid = _chkSubgrid.Checked;
            double margin;
            if (double.TryParse(_txtSubgridMargin.Text, System.Globalization.NumberStyles.Float, inv, out margin))
                Result.SubgridMarginFactor = Math.Max(1.0, Math.Min(3.0, margin));
            Result.UseWindField = _chkWindField.Checked;

            var type = GetSelectedType();
            string path = _txtPath.Text.Trim();

            _environment.Configure(path, type, _txtWslDistro.Text.Trim());

            Result.DetectedEnvironment = type;
            Result.OpenFoamPath = path;
            Result.WslDistroName = _txtWslDistro.Text.Trim();

            if (type == OpenFoamEnvironmentType.Docker)
                Result.DockerImageName = path;
            else if (type == OpenFoamEnvironmentType.BlueCFD)
                Result.BlueCfdPath = path;

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BtnTest_Click(object sender, EventArgs e)
        {
            var type = GetSelectedType();
            string path = _txtPath.Text.Trim();

            if (type == OpenFoamEnvironmentType.None || string.IsNullOrEmpty(path))
            {
                MessageBox.Show(this, "Select an environment type and path first.",
                    "Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _environment.Configure(path, type, _txtWslDistro.Text.Trim());
            if (!_environment.IsAvailable)
            {
                _lblEnvStatus.Text = _environment.StatusMessage;
                _lblEnvStatus.ForeColor = Color.Red;
                return;
            }

            _btnTest.Enabled = false;
            _lblEnvStatus.Text = "Testing...";
            _lblEnvStatus.ForeColor = Color.Gray;
            Application.DoEvents();

            try
            {
                string tempCase = Path.Combine(Path.GetTempPath(), "DisperSim_OF_test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                Directory.CreateDirectory(Path.Combine(tempCase, "system"));
                File.WriteAllText(Path.Combine(tempCase, "system", "controlDict"), "");

                var proc = _environment.StartCommand(tempCase, "blockMesh -help");
                string stdout = proc.StandardOutput.ReadToEnd();
                string stderr = proc.StandardError.ReadToEnd();
                proc.WaitForExit(15000);

                try { Directory.Delete(tempCase, true); } catch { }

                bool success = stdout.Contains("blockMesh") || stdout.Contains("OpenFOAM");
                if (success)
                {
                    _lblEnvStatus.Text = "OK — blockMesh responded. " + _environment.StatusMessage;
                    _lblEnvStatus.ForeColor = Color.DarkGreen;
                }
                else
                {
                    string msg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : "No response from blockMesh";
                    if (msg.Length > 120) msg = msg.Substring(0, 120) + "...";
                    _lblEnvStatus.Text = "FAILED: " + msg;
                    _lblEnvStatus.ForeColor = Color.Red;
                }
            }
            catch (Exception ex)
            {
                _lblEnvStatus.Text = "Error: " + ex.Message;
                _lblEnvStatus.ForeColor = Color.Red;
            }
            finally
            {
                _btnTest.Enabled = true;
            }
        }

        private void UpdateCellEstimate()
        {
            int n = (int)_nudGridRes.Value;
            long cells = (long)n * n * n;
            string category;
            Color clr;
            if (cells <= 1_000_000)
            {
                category = "Simple / Fast";
                clr = Color.DarkGreen;
            }
            else if (cells <= 10_000_000)
            {
                category = "Moderate";
                clr = Color.DarkGoldenrod;
            }
            else if (cells <= 50_000_000)
            {
                category = "Heavy — may take several minutes";
                clr = Color.OrangeRed;
            }
            else
            {
                category = "Very heavy — requires powerful hardware";
                clr = Color.Red;
            }
            _lblCellEstimate.Text = string.Format("≈ {0:N0} cells ({1}³)  —  {2}", cells, n, category);
            _lblCellEstimate.ForeColor = clr;
        }

        private static void AddLabel(TableLayoutPanel table, int row, string text)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = text, AutoSize = true, Anchor = AnchorStyles.Left, TextAlign = ContentAlignment.TopLeft,
                Margin = new Padding(0, 6, 4, 6), Dock = DockStyle.Top
            }, 0, row);
        }

        private void AddSectionHeader(TableLayoutPanel table, int row, string text)
        {
            var dpi = DeviceDpi / 96f;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var lbl = new Label
            {
                Text = text, Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
                Dock = DockStyle.Fill, Height = (int)(28 * dpi), TextAlign = ContentAlignment.MiddleLeft
            };
            table.SetColumnSpan(lbl, 2);
            table.Controls.Add(lbl, 0, row);
        }

        private void InitializeComponent()
        {
            this.SuspendLayout();
            // 
            // CfdSettingsDialog
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(192F, 192F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
            this.ClientSize = new System.Drawing.Size(1151, 1124);
            this.Margin = new System.Windows.Forms.Padding(6, 6, 6, 6);
            this.Name = "CfdSettingsDialog";
            this.Load += new System.EventHandler(this.CfdSettingsDialog_Load);
            this.ResumeLayout(false);

        }

        private void CfdSettingsDialog_Load(object sender, EventArgs e)
        {
        }
    }
}
