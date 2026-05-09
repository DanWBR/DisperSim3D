using System.Drawing;
using System.Windows.Forms;
using DisperSim3D.Core;
using WeifenLuo.WinFormsUI.Docking;

namespace DisperSim3D.Controls
{
    public class PropertiesDockPanel : DockContent
    {
        public PropertyGrid PropertyGrid { get; private set; }

        public PropertiesDockPanel()
        {
            Text = "Properties";
            if (FormExtensions.AppIcon != null) Icon = FormExtensions.AppIcon;
            HideOnClose = true;
            DockAreas = DockAreas.DockLeft | DockAreas.DockRight | DockAreas.Float;

            var font = new Font(SystemFonts.MessageBoxFont.FontFamily, 8f);
            PropertyGrid = new PropertyGrid
            {
                Dock = DockStyle.Fill,
                HelpVisible = true,
                ToolbarVisible = true,
                PropertySort = PropertySort.Categorized,
                Font = font,
                LargeButtons = false
            };

            Controls.Add(PropertyGrid);
        }
    }

    public class CfdSimulationsDockPanel : DockContent
    {
        public Dialogs.CfdSimulationsPanel SimPanel { get; private set; }

        public CfdSimulationsDockPanel(Dialogs.CfdSimulationsPanel panel)
        {
            Text = "CFD Simulations";
            if (FormExtensions.AppIcon != null) Icon = FormExtensions.AppIcon;
            HideOnClose = true;
            DockAreas = DockAreas.DockBottom | DockAreas.DockTop |
                        DockAreas.DockLeft | DockAreas.DockRight | DockAreas.Float;

            SimPanel = panel;
            panel.Dock = DockStyle.Fill;
            Controls.Add(panel);
        }
    }

    public class MonitorDockPanel : DockContent
    {
        public DataGridView MonitorGrid { get; private set; }

        public MonitorDockPanel()
        {
            Text = "Monitor Readings";
            if (FormExtensions.AppIcon != null) Icon = FormExtensions.AppIcon;
            HideOnClose = true;
            DockAreas = DockAreas.DockBottom | DockAreas.DockTop |
                        DockAreas.DockLeft | DockAreas.DockRight | DockAreas.Float;

            MonitorGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                BackgroundColor = System.Drawing.SystemColors.Window,
                BorderStyle = BorderStyle.None
            };
            MonitorGrid.Columns.Add("Name", "Name");
            MonitorGrid.Columns.Add("Type", "Type");
            MonitorGrid.Columns.Add("Position", "Position (m)");
            MonitorGrid.Columns.Add("Concentration", "Avg (kg/m³)");
            MonitorGrid.Columns.Add("MinMax", "Min / Max");

            Controls.Add(MonitorGrid);
        }
    }

    public class AddItemDockPanel : DockContent
    {
        public AddItemPanel ItemPanel { get; private set; }

        public AddItemDockPanel(AddItemPanel panel)
        {
            Text = "Add Item";
            if (FormExtensions.AppIcon != null) Icon = FormExtensions.AppIcon;
            HideOnClose = true;
            DockAreas = DockAreas.DockLeft | DockAreas.DockRight | DockAreas.Float;

            ItemPanel = panel;
            panel.Dock = DockStyle.Fill;
            Controls.Add(panel);
        }
    }

    public class SimulationManagerDockPanel : DockContent
    {
        public Dialogs.SimulationManagerPanel ManagerPanel { get; private set; }

        public SimulationManagerDockPanel(Dialogs.SimulationManagerPanel panel)
        {
            Text = "Simulation Manager";
            if (FormExtensions.AppIcon != null) Icon = FormExtensions.AppIcon;
            HideOnClose = true;
            DockAreas = DockAreas.DockBottom | DockAreas.DockTop |
                        DockAreas.DockLeft | DockAreas.DockRight | DockAreas.Float;

            ManagerPanel = panel;
            panel.Dock = DockStyle.Fill;
            Controls.Add(panel);
        }
    }

    public class ViewportDockPanel : DockContent
    {
        public ViewportDockPanel(Control viewportControl)
        {
            Text = "3D Viewport";
            if (FormExtensions.AppIcon != null) Icon = FormExtensions.AppIcon;
            DockAreas = DockAreas.Document;
            CloseButton = false;
            CloseButtonVisible = false;

            viewportControl.Dock = DockStyle.Fill;
            Controls.Add(viewportControl);
        }
    }
}
