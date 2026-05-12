using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Media.Media3D;
using DisperSim3D.Models;
using DisperSim3D.Core;

namespace DisperSim3D.Controls
{
    public enum AddItemType
    {
        GasLeakOrEmission,
        HighPressureGasLeak,
        JetFire,
        PoolFire,
        GasDetector,
        MonitorPoint,
        DispersionSimulation
    }

    public class AddItemPanel : UserControl
    {
        private ComboBox _cmbItemType;
        private Panel _propertiesContainer;
        private TextBox _txtName;
        private TextBox _txtPosX, _txtPosY, _txtPosZ;
        private Button _btnPick;
        private Button _btnAddItem;
        private Button _btnCancel;
        private CheckBox _chkPreview;
        private Label _lblCoords;
        private bool _pickActive;

        // Gas Leak fields
        private ComboBox _cmbGas;
        private TextBox _txtReleaseRate, _txtDuration, _txtPuffInterval, _txtHeightOffset;

        // HP Leak fields
        private TextBox _txtHPPressure, _txtHPTemperature, _txtHPOrifice, _txtHPVolume, _txtHPGamma, _txtHPMolarMass;
        private TextBox _txtHPDischargeCoeff;
        private Label _lblHPFlowRate, _lblHPChoked;

        // Fire fields
        private TextBox _txtFireMassFlow, _txtFireOrifice, _txtFireHeatComb, _txtFireRadFrac;
        private TextBox _txtPoolDiameter, _txtPoolBurnRate;

        // Detector fields
        private TextBox _txtThreshold;

        // Orientation fields
        private TextBox _txtAngleNorth, _txtElevation;

        // Dispersion Simulation fields
        private TextBox _txtWindSpeed, _txtWindDirection;
        private ComboBox _cmbStability;
        private TextBox _txtDomainSize, _txtGridResolution, _txtSimDuration, _txtSimTimestep;
        private CheckBox _chkTransientWind;
        private CheckBox _chkAutoRun;
        private ComboBox _cmbInflowSource;

        // Section panels for show/hide
        private Panel _positionSection;
        private Panel _orientationSection;

        // External data for populating dropdowns
        private System.Collections.Generic.List<ReleaseSource3D> _existingSources;

        public event EventHandler<Point3D> PickRequested;
        public event EventHandler<AddItemEventArgs> ItemAdded;
        public event EventHandler Cancelled;

        public AddItemPanel()
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.AutoScaleDimensions = new SizeF(96F, 96F);
            BuildUI();
        }

        private void BuildUI()
        {
            var dpi = DeviceDpi / 96f;
            this.BackColor = SystemColors.Control;
            this.Padding = new Padding(0);

            var headerLabel = new Label
            {
                Text = "Add Item",
                Dock = DockStyle.Top,
                Height = (int)(28 * dpi),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(50, 50, 55),
                ForeColor = Color.White,
                Padding = new Padding((int)(8 * dpi), 0, 0, 0)
            };

            var scrollPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding((int)(8 * dpi), (int)(4 * dpi), (int)(8 * dpi), (int)(4 * dpi))
            };

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                Width = (int)(250 * dpi)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(100 * dpi)));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;

            AddLabel(layout, row, "Select Item:");
            _cmbItemType = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbItemType.Items.AddRange(new object[]
            {
                "Gas Leak or Emission",
                "High Pressure Gas Leak",
                "Jet Fire",
                "Pool Fire",
                "Gas Detector",
                "Monitor Point",
                "Dispersion Simulation"
            });
            _cmbItemType.SelectedIndexChanged += (s, e) => RebuildProperties();
            layout.Controls.Add(_cmbItemType, 1, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var sep1 = new Label { Height = (int)(8 * dpi), Dock = DockStyle.Fill };
            layout.SetColumnSpan(sep1, 2);
            layout.Controls.Add(sep1, 0, row++);

            AddLabel(layout, row, "Name:");
            _txtName = new TextBox { Dock = DockStyle.Fill, Text = "Item 01" };
            layout.Controls.Add(_txtName, 1, row++);

            _propertiesContainer = new Panel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.SetColumnSpan(_propertiesContainer, 2);
            layout.Controls.Add(_propertiesContainer, 0, row++);

            // --- Position section (wrapped in panel for visibility toggle) ---
            _positionSection = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var posLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2
            };
            posLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(100 * dpi)));
            posLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int posRow = 0;
            posLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var posHeader = new Label
            {
                Text = "Position",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Height = (int)(24 * dpi),
                TextAlign = ContentAlignment.BottomLeft
            };
            posLayout.SetColumnSpan(posHeader, 2);
            posLayout.Controls.Add(posHeader, 0, posRow++);

            posLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var posDesc = new Label
            {
                Text = "Set by direct input or by using the Pick Tool",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.Gray,
                Font = new Font("Segoe UI", 8f)
            };
            posLayout.SetColumnSpan(posDesc, 2);
            posLayout.Controls.Add(posDesc, 0, posRow++);

            posLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var posPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 7, RowCount = 1, Padding = new Padding(4),
                Margin = new Padding(0)
            };
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            posPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            posPanel.Controls.Add(new Label { Text = "x:", AutoSize = true, Margin = new Padding(0, 4, 2, 0) }, 0, 0);
            _txtPosX = new TextBox { Width = (int)(50 * dpi), Text = "0" };
            posPanel.Controls.Add(_txtPosX, 1, 0);
            posPanel.Controls.Add(new Label { Text = "y:", AutoSize = true, Margin = new Padding(4, 4, 2, 0) }, 2, 0);
            _txtPosY = new TextBox { Width = (int)(50 * dpi), Text = "0" };
            posPanel.Controls.Add(_txtPosY, 3, 0);
            posPanel.Controls.Add(new Label { Text = "z:", AutoSize = true, Margin = new Padding(4, 4, 2, 0) }, 4, 0);
            _txtPosZ = new TextBox { Width = (int)(50 * dpi), Text = "0" };
            posPanel.Controls.Add(_txtPosZ, 5, 0);

            _btnPick = new Button
            {
                Text = "📌",
                Width = (int)(28 * dpi), Height = (int)(24 * dpi),
                FlatStyle = FlatStyle.Flat,
                Margin = new Padding(4, 1, 0, 0),
                Font = new Font("Segoe UI", 9f)
            };
            _btnPick.FlatAppearance.BorderColor = Color.Gray;
            _btnPick.Click += (s, e) => TogglePick();
            posPanel.Controls.Add(_btnPick, 6, 0);

            posLayout.SetColumnSpan(posPanel, 2);
            posLayout.Controls.Add(posPanel, 0, posRow++);

            _lblCoords = new Label
            {
                Text = "",
                Dock = DockStyle.Fill,
                AutoSize = true,
                ForeColor = Color.FromArgb(0, 120, 210),
                Font = new Font("Segoe UI", 8f)
            };
            posLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            posLayout.SetColumnSpan(_lblCoords, 2);
            posLayout.Controls.Add(_lblCoords, 0, posRow++);

            _positionSection.Controls.Add(posLayout);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.SetColumnSpan(_positionSection, 2);
            layout.Controls.Add(_positionSection, 0, row++);

            // --- Orientation section (wrapped in panel for visibility toggle) ---
            _orientationSection = new Panel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
            var orientLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2
            };
            orientLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(100 * dpi)));
            orientLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int orientRow = 0;
            orientLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var orientHeader = new Label
            {
                Text = "Orientation",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Height = (int)(24 * dpi),
                TextAlign = ContentAlignment.BottomLeft
            };
            orientLayout.SetColumnSpan(orientHeader, 2);
            orientLayout.Controls.Add(orientHeader, 0, orientRow++);

            AddLabel(orientLayout, orientRow, "Angle from North:");
            _txtAngleNorth = new TextBox { Dock = DockStyle.Fill, Text = "0" };
            orientLayout.Controls.Add(_txtAngleNorth, 1, orientRow++);

            AddLabel(orientLayout, orientRow, "Elevation:");
            _txtElevation = new TextBox { Dock = DockStyle.Fill, Text = "0" };
            orientLayout.Controls.Add(_txtElevation, 1, orientRow++);

            _orientationSection.Controls.Add(orientLayout);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.SetColumnSpan(_orientationSection, 2);
            layout.Controls.Add(_orientationSection, 0, row++);

            // --- Preview + buttons ---
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var sep2 = new Label { Height = (int)(12 * dpi), Dock = DockStyle.Fill };
            layout.SetColumnSpan(sep2, 2);
            layout.Controls.Add(sep2, 0, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _chkPreview = new CheckBox { Text = "Preview", AutoSize = true, Checked = true };
            layout.SetColumnSpan(_chkPreview, 2);
            layout.Controls.Add(_chkPreview, 0, row++);

            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var btnPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 2, RowCount = 1, Padding = new Padding(4)
            };
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            btnPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _btnCancel = new Button { Text = "Cancel", Width = (int)(70 * dpi), Height = (int)(30 * dpi) };
            _btnCancel.Click += (s, e) => { DeactivatePick(); Cancelled?.Invoke(this, EventArgs.Empty); };
            btnPanel.Controls.Add(_btnCancel, 0, 0);

            _btnAddItem = new Button
            {
                Text = "Add Item",
                Width = (int)(90 * dpi), Height = (int)(30 * dpi),
                BackColor = Color.FromArgb(0, 120, 210),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold)
            };
            _btnAddItem.FlatAppearance.BorderSize = 0;
            _btnAddItem.Click += (s, e) => DoAddItem();
            btnPanel.Controls.Add(_btnAddItem, 1, 0);

            layout.SetColumnSpan(btnPanel, 2);
            layout.Controls.Add(btnPanel, 0, row++);

            scrollPanel.Controls.Add(layout);
            this.Controls.Add(scrollPanel);
            this.Controls.Add(headerLabel);

            _cmbItemType.SelectedIndex = 0;
        }

        private void RebuildProperties()
        {
            _propertiesContainer.Controls.Clear();
            var dpi = DeviceDpi / 96f;

            var table = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, (int)(100 * dpi)));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            int row = 0;
            var type = GetSelectedType();

            bool isSceneWide = (type == AddItemType.DispersionSimulation);
            if (_positionSection != null) _positionSection.Visible = !isSceneWide;
            if (_orientationSection != null) _orientationSection.Visible = !isSceneWide;

            if (isSceneWide)
                _btnAddItem.Text = "Add & Run";
            else
                _btnAddItem.Text = "Add Item";

            switch (type)
            {
                case AddItemType.GasLeakOrEmission:
                    _txtName.Text = "Release 01";
                    AddLabel(table, row, "Gas:");
                    _cmbGas = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                    _cmbGas.Items.AddRange(new[] { "METHANE", "PROPANE", "HYDROGEN", "AMMONIA", "H2S", "CO", "ETHYLENE", "Custom" });
                    _cmbGas.SelectedIndex = 0;
                    table.Controls.Add(_cmbGas, 1, row++);

                    AddLabel(table, row, "Release Rate:");
                    _txtReleaseRate = new TextBox { Dock = DockStyle.Fill, Text = "1.0" };
                    table.Controls.Add(MakeUnitPanel(_txtReleaseRate, "kg/s"), 1, row++);

                    AddLabel(table, row, "Duration:");
                    _txtDuration = new TextBox { Dock = DockStyle.Fill, Text = "60" };
                    table.Controls.Add(MakeUnitPanel(_txtDuration, "s"), 1, row++);

                    AddLabel(table, row, "Puff Interval:");
                    _txtPuffInterval = new TextBox { Dock = DockStyle.Fill, Text = "1.0" };
                    table.Controls.Add(MakeUnitPanel(_txtPuffInterval, "s"), 1, row++);

                    AddLabel(table, row, "Height Offset:");
                    _txtHeightOffset = new TextBox { Dock = DockStyle.Fill, Text = "2.0" };
                    table.Controls.Add(MakeUnitPanel(_txtHeightOffset, "m"), 1, row++);
                    break;

                case AddItemType.HighPressureGasLeak:
                    _txtName.Text = "HP Release 01";
                    AddLabel(table, row, "Gas:");
                    _cmbGas = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                    _cmbGas.Items.AddRange(new[] { "METHANE", "PROPANE", "HYDROGEN", "AMMONIA", "H2S", "CO", "Custom" });
                    _cmbGas.SelectedIndex = 0;
                    table.Controls.Add(_cmbGas, 1, row++);

                    AddLabel(table, row, "Upstream P:");
                    _txtHPPressure = new TextBox { Dock = DockStyle.Fill, Text = "10" };
                    table.Controls.Add(MakeUnitPanel(_txtHPPressure, "bar(g)"), 1, row++);

                    AddLabel(table, row, "Upstream T:");
                    _txtHPTemperature = new TextBox { Dock = DockStyle.Fill, Text = "10" };
                    table.Controls.Add(MakeUnitPanel(_txtHPTemperature, "°C"), 1, row++);

                    AddLabel(table, row, "Discharge Cd:");
                    _txtHPDischargeCoeff = new TextBox { Dock = DockStyle.Fill, Text = "0.85" };
                    table.Controls.Add(_txtHPDischargeCoeff, 1, row++);

                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var holeSec = new Label
                    {
                        Text = "Hole Size, Location and Orientation",
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill, Height = (int)(26 * dpi), TextAlign = ContentAlignment.BottomLeft
                    };
                    table.SetColumnSpan(holeSec, 2);
                    table.Controls.Add(holeSec, 0, row++);

                    AddLabel(table, row, "Diameter:");
                    _txtHPOrifice = new TextBox { Dock = DockStyle.Fill, Text = "25" };
                    _txtHPOrifice.TextChanged += (s, e) => UpdateHPCalc();
                    table.Controls.Add(MakeUnitPanel(_txtHPOrifice, "mm"), 1, row++);

                    AddLabel(table, row, "Vessel Volume:");
                    _txtHPVolume = new TextBox { Dock = DockStyle.Fill, Text = "10" };
                    table.Controls.Add(MakeUnitPanel(_txtHPVolume, "m³"), 1, row++);

                    AddLabel(table, row, "Gamma (Cp/Cv):");
                    _txtHPGamma = new TextBox { Dock = DockStyle.Fill, Text = "1.4" };
                    table.Controls.Add(_txtHPGamma, 1, row++);

                    AddLabel(table, row, "Molar Mass:");
                    _txtHPMolarMass = new TextBox { Dock = DockStyle.Fill, Text = "0.016" };
                    table.Controls.Add(MakeUnitPanel(_txtHPMolarMass, "kg/mol"), 1, row++);

                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var calcSec = new Label
                    {
                        Text = "Expanded Conditions",
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill, Height = (int)(26 * dpi), TextAlign = ContentAlignment.BottomLeft
                    };
                    table.SetColumnSpan(calcSec, 2);
                    table.Controls.Add(calcSec, 0, row++);

                    AddLabel(table, row, "Flow regime:");
                    _lblHPChoked = new Label { Dock = DockStyle.Fill, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
                    table.Controls.Add(_lblHPChoked, 1, row++);

                    AddLabel(table, row, "Mass Flow Rate:");
                    _lblHPFlowRate = new Label { Dock = DockStyle.Fill, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
                    table.Controls.Add(_lblHPFlowRate, 1, row++);

                    _txtHPPressure.TextChanged += (s, e) => UpdateHPCalc();
                    _txtHPTemperature.TextChanged += (s, e) => UpdateHPCalc();
                    _txtHPGamma.TextChanged += (s, e) => UpdateHPCalc();
                    _txtHPMolarMass.TextChanged += (s, e) => UpdateHPCalc();
                    UpdateHPCalc();
                    break;

                case AddItemType.JetFire:
                    _txtName.Text = "JetFire 01";
                    AddLabel(table, row, "Mass Flow:");
                    _txtFireMassFlow = new TextBox { Dock = DockStyle.Fill, Text = "1.0" };
                    table.Controls.Add(MakeUnitPanel(_txtFireMassFlow, "kg/s"), 1, row++);

                    AddLabel(table, row, "Orifice Dia:");
                    _txtFireOrifice = new TextBox { Dock = DockStyle.Fill, Text = "0.02" };
                    table.Controls.Add(MakeUnitPanel(_txtFireOrifice, "m"), 1, row++);

                    AddLabel(table, row, "Heat Combust.:");
                    _txtFireHeatComb = new TextBox { Dock = DockStyle.Fill, Text = "50000000" };
                    table.Controls.Add(MakeUnitPanel(_txtFireHeatComb, "J/kg"), 1, row++);

                    AddLabel(table, row, "Rad. Fraction:");
                    _txtFireRadFrac = new TextBox { Dock = DockStyle.Fill, Text = "0.2" };
                    table.Controls.Add(_txtFireRadFrac, 1, row++);
                    break;

                case AddItemType.PoolFire:
                    _txtName.Text = "PoolFire 01";
                    AddLabel(table, row, "Pool Diameter:");
                    _txtPoolDiameter = new TextBox { Dock = DockStyle.Fill, Text = "5.0" };
                    table.Controls.Add(MakeUnitPanel(_txtPoolDiameter, "m"), 1, row++);

                    AddLabel(table, row, "Burn Rate:");
                    _txtPoolBurnRate = new TextBox { Dock = DockStyle.Fill, Text = "0.05" };
                    table.Controls.Add(MakeUnitPanel(_txtPoolBurnRate, "kg/m²/s"), 1, row++);

                    AddLabel(table, row, "Heat Combust.:");
                    _txtFireHeatComb = new TextBox { Dock = DockStyle.Fill, Text = "50000000" };
                    table.Controls.Add(MakeUnitPanel(_txtFireHeatComb, "J/kg"), 1, row++);

                    AddLabel(table, row, "Rad. Fraction:");
                    _txtFireRadFrac = new TextBox { Dock = DockStyle.Fill, Text = "0.2" };
                    table.Controls.Add(_txtFireRadFrac, 1, row++);
                    break;

                case AddItemType.GasDetector:
                    _txtName.Text = "Detector 01";
                    AddLabel(table, row, "Threshold:");
                    _txtThreshold = new TextBox { Dock = DockStyle.Fill, Text = "0.01" };
                    table.Controls.Add(MakeUnitPanel(_txtThreshold, "kg/m³"), 1, row++);
                    break;

                case AddItemType.MonitorPoint:
                    _txtName.Text = "Monitor 01";
                    break;

                case AddItemType.DispersionSimulation:
                    _txtName.Text = "Dispersion Scenario 01";

                    // --- Ventilation section ---
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var ventSec = new Label
                    {
                        Text = "Ventilation (Wind)",
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill, Height = (int)(26 * dpi), TextAlign = ContentAlignment.BottomLeft
                    };
                    table.SetColumnSpan(ventSec, 2);
                    table.Controls.Add(ventSec, 0, row++);

                    AddLabel(table, row, "Wind Speed:");
                    _txtWindSpeed = new TextBox { Dock = DockStyle.Fill, Text = "5.0" };
                    table.Controls.Add(MakeUnitPanel(_txtWindSpeed, "m/s"), 1, row++);

                    AddLabel(table, row, "Direction:");
                    _txtWindDirection = new TextBox { Dock = DockStyle.Fill, Text = "270" };
                    table.Controls.Add(MakeUnitPanel(_txtWindDirection, "° from N"), 1, row++);

                    AddLabel(table, row, "Stability:");
                    _cmbStability = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                    _cmbStability.Items.AddRange(new object[] { "A - Very Unstable", "B - Unstable", "C - Slightly Unstable",
                        "D - Neutral", "E - Slightly Stable", "F - Stable" });
                    _cmbStability.SelectedIndex = 3;
                    table.Controls.Add(_cmbStability, 1, row++);

                    // --- Inflow section ---
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var inflowSec = new Label
                    {
                        Text = "Inflow Source",
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill, Height = (int)(26 * dpi), TextAlign = ContentAlignment.BottomLeft
                    };
                    table.SetColumnSpan(inflowSec, 2);
                    table.Controls.Add(inflowSec, 0, row++);

                    AddLabel(table, row, "Source:");
                    _cmbInflowSource = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
                    _cmbInflowSource.Items.Add("(all existing sources)");
                    if (_existingSources != null)
                    {
                        foreach (var src in _existingSources)
                            _cmbInflowSource.Items.Add(src.Name ?? "Source");
                    }
                    _cmbInflowSource.SelectedIndex = 0;
                    table.Controls.Add(_cmbInflowSource, 1, row++);

                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var inflowDesc = new Label
                    {
                        Text = "Select a specific source or use all sources in the scenario.",
                        Dock = DockStyle.Fill, AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8f)
                    };
                    table.SetColumnSpan(inflowDesc, 2);
                    table.Controls.Add(inflowDesc, 0, row++);

                    // --- Domain section ---
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var domainSec = new Label
                    {
                        Text = "Domain",
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill, Height = (int)(26 * dpi), TextAlign = ContentAlignment.BottomLeft
                    };
                    table.SetColumnSpan(domainSec, 2);
                    table.Controls.Add(domainSec, 0, row++);

                    AddLabel(table, row, "Domain Size:");
                    _txtDomainSize = new TextBox { Dock = DockStyle.Fill, Text = "200" };
                    table.Controls.Add(MakeUnitPanel(_txtDomainSize, "m"), 1, row++);

                    AddLabel(table, row, "Grid Resolution:");
                    _txtGridResolution = new TextBox { Dock = DockStyle.Fill, Text = "40" };
                    table.Controls.Add(MakeUnitPanel(_txtGridResolution, "cells"), 1, row++);

                    AddLabel(table, row, "Duration:");
                    _txtSimDuration = new TextBox { Dock = DockStyle.Fill, Text = "300" };
                    table.Controls.Add(MakeUnitPanel(_txtSimDuration, "s"), 1, row++);

                    AddLabel(table, row, "Time Step:");
                    _txtSimTimestep = new TextBox { Dock = DockStyle.Fill, Text = "0.5" };
                    table.Controls.Add(MakeUnitPanel(_txtSimTimestep, "s"), 1, row++);

                    // --- Transient / Advanced ---
                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    var advSec = new Label
                    {
                        Text = "Advanced",
                        Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                        Dock = DockStyle.Fill, Height = (int)(26 * dpi), TextAlign = ContentAlignment.BottomLeft
                    };
                    table.SetColumnSpan(advSec, 2);
                    table.Controls.Add(advSec, 0, row++);

                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _chkTransientWind = new CheckBox { Text = "Enable Transient Wind Profile", AutoSize = true };
                    table.SetColumnSpan(_chkTransientWind, 2);
                    table.Controls.Add(_chkTransientWind, 0, row++);

                    table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    _chkAutoRun = new CheckBox { Text = "Start simulation after adding", AutoSize = true, Checked = true };
                    table.SetColumnSpan(_chkAutoRun, 2);
                    table.Controls.Add(_chkAutoRun, 0, row++);
                    break;
            }

            _propertiesContainer.Controls.Add(table);
        }

        private void UpdateHPCalc()
        {
            if (_lblHPChoked == null || _lblHPFlowRate == null) return;
            try
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                double pressureBarG = double.Parse(_txtHPPressure.Text, inv);
                double tempC = double.Parse(_txtHPTemperature.Text, inv);
                double orificeMm = double.Parse(_txtHPOrifice.Text, inv);
                double gamma = double.Parse(_txtHPGamma.Text, inv);
                double molarMass = double.Parse(_txtHPMolarMass.Text, inv);

                var p = new HighPressureLeakParams
                {
                    VesselPressurePa = (pressureBarG + 1.01325) * 1e5,
                    VesselTemperatureK = tempC + 273.15,
                    OrificeDiameterM = orificeMm / 1000.0,
                    GasGamma = gamma,
                    GasMolarMassKgMol = molarMass
                };

                bool choked = HighPressureLeakModel.IsChoked(p);
                double mdot = HighPressureLeakModel.MassFlowRate(p);

                _lblHPChoked.Text = choked ? "CHOKED" : "Unchoked";
                _lblHPChoked.ForeColor = choked ? Color.Red : Color.DarkGreen;
                _lblHPFlowRate.Text = mdot.ToString("F3") + " kg/s";
            }
            catch
            {
                _lblHPChoked.Text = "-";
                _lblHPFlowRate.Text = "-";
            }
        }

        public void SetPickedPosition(Point3D point)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            _txtPosX.Text = point.X.ToString("F2", inv);
            _txtPosY.Text = point.Y.ToString("F2", inv);
            _txtPosZ.Text = point.Z.ToString("F2", inv);
            _lblCoords.Text = string.Format(inv, "[{0:F2}, {1:F2}, {2:F2}] meters", point.X, point.Y, point.Z);
        }

        public bool IsPickActive => _pickActive;

        public AddItemType GetSelectedType()
        {
            switch (_cmbItemType.SelectedIndex)
            {
                case 0: return AddItemType.GasLeakOrEmission;
                case 1: return AddItemType.HighPressureGasLeak;
                case 2: return AddItemType.JetFire;
                case 3: return AddItemType.PoolFire;
                case 4: return AddItemType.GasDetector;
                case 5: return AddItemType.MonitorPoint;
                case 6: return AddItemType.DispersionSimulation;
                default: return AddItemType.GasLeakOrEmission;
            }
        }

        private void TogglePick()
        {
            _pickActive = !_pickActive;
            _btnPick.BackColor = _pickActive ? Color.FromArgb(0, 120, 210) : SystemColors.Control;
            _btnPick.ForeColor = _pickActive ? Color.White : SystemColors.ControlText;
            if (_pickActive)
                _lblCoords.Text = "Click in the 3D viewport to pick a position...";
            PickRequested?.Invoke(this, GetCurrentPosition());
        }

        public void ActivatePick()
        {
            if (!_pickActive) TogglePick();
        }

        public void DeactivatePick()
        {
            if (_pickActive) TogglePick();
        }

        public void SetExistingSources(System.Collections.Generic.List<ReleaseSource3D> sources)
        {
            _existingSources = sources;
        }

        private Point3D GetCurrentPosition()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            double x, y, z;
            double.TryParse(_txtPosX.Text, System.Globalization.NumberStyles.Float, inv, out x);
            double.TryParse(_txtPosY.Text, System.Globalization.NumberStyles.Float, inv, out y);
            double.TryParse(_txtPosZ.Text, System.Globalization.NumberStyles.Float, inv, out z);
            return new Point3D(x, y, z);
        }

        private double ParseDouble(TextBox txt, double fallback)
        {
            double v;
            if (double.TryParse(txt.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v))
                return v;
            return fallback;
        }

        private void DoAddItem()
        {
            var pos = GetCurrentPosition();
            var type = GetSelectedType();
            var args = new AddItemEventArgs { Type = type, Name = _txtName.Text, Position = pos };

            double angleNorth = ParseDouble(_txtAngleNorth, 0);
            double elevation = ParseDouble(_txtElevation, 0);
            double dirRad = angleNorth * Math.PI / 180.0;
            double elevRad = elevation * Math.PI / 180.0;
            args.Direction = new Vector3D(
                Math.Sin(dirRad) * Math.Cos(elevRad),
                Math.Cos(dirRad) * Math.Cos(elevRad),
                Math.Sin(elevRad));

            switch (type)
            {
                case AddItemType.GasLeakOrEmission:
                    args.ReleaseSource = new ReleaseSource3D
                    {
                        Name = _txtName.Text,
                        Position = pos,
                        ReleaseRateKgPerS = ParseDouble(_txtReleaseRate, 1.0),
                        PuffIntervalS = ParseDouble(_txtPuffInterval, 1.0),
                        ReleaseHeightOffset = ParseDouble(_txtHeightOffset, 2.0),
                        Gas = new GasProperties { Name = _cmbGas?.SelectedItem?.ToString() ?? "METHANE" }
                    };
                    break;

                case AddItemType.HighPressureGasLeak:
                    double pressureBarG = ParseDouble(_txtHPPressure, 10);
                    double tempC = ParseDouble(_txtHPTemperature, 10);
                    double orificeMm = ParseDouble(_txtHPOrifice, 25);
                    var hpParams = new HighPressureLeakParams
                    {
                        VesselPressurePa = (pressureBarG + 1.01325) * 1e5,
                        VesselTemperatureK = tempC + 273.15,
                        OrificeDiameterM = orificeMm / 1000.0,
                        VesselVolumeM3 = ParseDouble(_txtHPVolume, 10),
                        GasGamma = ParseDouble(_txtHPGamma, 1.4),
                        GasMolarMassKgMol = ParseDouble(_txtHPMolarMass, 0.016)
                    };
                    double mdot = HighPressureLeakModel.MassFlowRate(hpParams);
                    args.ReleaseSource = new ReleaseSource3D
                    {
                        Name = _txtName.Text,
                        Position = pos,
                        ReleaseRateKgPerS = mdot,
                        PuffIntervalS = 1.0,
                        ReleaseHeightOffset = 0,
                        HighPressureLeak = hpParams,
                        Gas = new GasProperties { Name = _cmbGas?.SelectedItem?.ToString() ?? "METHANE" }
                    };
                    break;

                case AddItemType.JetFire:
                    args.FireSource = new FireSource
                    {
                        Name = _txtName.Text,
                        Position = pos,
                        Direction = args.Direction,
                        MassFlowRateKgS = ParseDouble(_txtFireMassFlow, 1.0),
                        OrificeDiameterM = ParseDouble(_txtFireOrifice, 0.02),
                        HeatOfCombustionJKg = ParseDouble(_txtFireHeatComb, 50e6),
                        RadiativeFraction = ParseDouble(_txtFireRadFrac, 0.2),
                        IsPoolFire = false
                    };
                    break;

                case AddItemType.PoolFire:
                    args.FireSource = new FireSource
                    {
                        Name = _txtName.Text,
                        Position = pos,
                        Direction = new Vector3D(0, 0, 1),
                        IsPoolFire = true,
                        PoolDiameterM = ParseDouble(_txtPoolDiameter, 5.0),
                        PoolBurnRateKgM2S = ParseDouble(_txtPoolBurnRate, 0.05),
                        HeatOfCombustionJKg = ParseDouble(_txtFireHeatComb, 50e6),
                        RadiativeFraction = ParseDouble(_txtFireRadFrac, 0.2),
                        MassFlowRateKgS = ParseDouble(_txtPoolBurnRate, 0.05)
                            * Math.PI * 0.25 * Math.Pow(ParseDouble(_txtPoolDiameter, 5.0), 2)
                    };
                    break;

                case AddItemType.GasDetector:
                    args.GasDetector = new GasDetector3D
                    {
                        Name = _txtName.Text,
                        Position = pos,
                        ThresholdKgM3 = ParseDouble(_txtThreshold, 0.01)
                    };
                    break;

                case AddItemType.MonitorPoint:
                    args.MonitorPoint = new MonitorPoint3D
                    {
                        Name = _txtName.Text,
                        Position = pos
                    };
                    break;

                case AddItemType.DispersionSimulation:
                    var stabilityMap = new[] {
                        PasquillStabilityClass.A, PasquillStabilityClass.B, PasquillStabilityClass.C,
                        PasquillStabilityClass.D, PasquillStabilityClass.E, PasquillStabilityClass.F
                    };
                    var stability = _cmbStability != null && _cmbStability.SelectedIndex >= 0
                        ? stabilityMap[_cmbStability.SelectedIndex]
                        : PasquillStabilityClass.D;

                    var scenario = new DispersionScenario
                    {
                        Name = _txtName.Text,
                        Meteo = new MeteorologicalConditions
                        {
                            WindSpeed = ParseDouble(_txtWindSpeed, 5.0),
                            WindDirectionDeg = ParseDouble(_txtWindDirection, 270),
                            StabilityClass = stability
                        },
                        DomainSizeM = ParseDouble(_txtDomainSize, 200),
                        GridResolution = (int)ParseDouble(_txtGridResolution, 40),
                        SimulationDurationS = ParseDouble(_txtSimDuration, 300),
                        TimeStepS = ParseDouble(_txtSimTimestep, 0.5)
                    };

                    if (_chkTransientWind != null && _chkTransientWind.Checked)
                        scenario.TransientWind.Enabled = true;

                    if (_cmbInflowSource != null && _cmbInflowSource.SelectedIndex > 0 && _existingSources != null)
                    {
                        int srcIdx = _cmbInflowSource.SelectedIndex - 1;
                        if (srcIdx < _existingSources.Count)
                            scenario.Sources.Add(_existingSources[srcIdx]);
                    }

                    args.Scenario = scenario;
                    args.AutoRun = _chkAutoRun != null && _chkAutoRun.Checked;
                    break;
            }

            DeactivatePick();
            ItemAdded?.Invoke(this, args);
        }

        private static void AddLabel(TableLayoutPanel table, int row, string text)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label
            {
                Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
                Margin = new Padding(0, 6, 4, 0)
            }, 0, row);
        }

        private Panel MakeUnitPanel(TextBox txt, string unit)
        {
            var dpi = DeviceDpi / 96f;
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoSize = true,
                ColumnCount = 2, RowCount = 1,
                Margin = new Padding(0), Padding = new Padding(4)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            txt.Width = (int)(70 * dpi);
            panel.Controls.Add(txt, 0, 0);
            panel.Controls.Add(new Label
            {
                Text = unit, AutoSize = true,
                Margin = new Padding(2, 4, 0, 0),
                ForeColor = Color.Gray
            }, 1, 0);
            return panel;
        }
    }

    public class AddItemEventArgs : EventArgs
    {
        public AddItemType Type { get; set; }
        public string Name { get; set; }
        public Point3D Position { get; set; }
        public Vector3D Direction { get; set; }
        public ReleaseSource3D ReleaseSource { get; set; }
        public FireSource FireSource { get; set; }
        public GasDetector3D GasDetector { get; set; }
        public MonitorPoint3D MonitorPoint { get; set; }
        public DispersionScenario Scenario { get; set; }
        public bool AutoRun { get; set; }
    }
}
