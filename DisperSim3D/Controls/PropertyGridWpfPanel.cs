using System;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using XceedPropertyGrid = Xceed.Wpf.Toolkit.PropertyGrid.PropertyGrid;
using SwfUserControl = System.Windows.Forms.UserControl;
using SwfDockStyle = System.Windows.Forms.DockStyle;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// WPF-based property grid hosted in a WinForms ElementHost.
    /// Uses Xceed Extended WPF Toolkit's PropertyGrid for a modern, polished editor with
    /// nested object expansion, color/datetime/numeric editors, and inline validation.
    /// </summary>
    public class PropertyGridWpfPanel : SwfUserControl
    {
        private readonly ElementHost _host;
        private readonly XceedPropertyGrid _grid;

        /// <summary>Raised after a property value is committed.</summary>
        public event EventHandler PropertyValueChanged;

        public PropertyGridWpfPanel()
        {
            _grid = new XceedPropertyGrid
            {
                AutoGenerateProperties = true,
                ShowAdvancedOptions = false,
                ShowSearchBox = true,
                ShowSortOptions = false,
                ShowSummary = true,
                ShowTitle = false,
                IsCategorized = true,
                IsReadOnly = false,
                Background = Brushes.White
            };
            _grid.PropertyValueChanged += (s, e) =>
            {
                PropertyValueChanged?.Invoke(this, EventArgs.Empty);
            };

            _host = new ElementHost
            {
                Dock = SwfDockStyle.Fill,
                Child = _grid
            };
            this.Controls.Add(_host);
        }

        public object SelectedObject
        {
            get => _grid.SelectedObject;
            set
            {
                _grid.SelectedObject = value;
            }
        }

        public new void Refresh()
        {
            _grid.Update();
        }
    }
}
