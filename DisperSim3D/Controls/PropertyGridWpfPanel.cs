using System;
using System.Windows;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using HcPropertyGrid = HandyControl.Controls.PropertyGrid;
using SwfUserControl = System.Windows.Forms.UserControl;
using SwfDockStyle = System.Windows.Forms.DockStyle;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// WPF-based property grid hosted in a WinForms ElementHost using HandyControl's PropertyGrid.
    /// MIT license, modern look, native editors per type.
    /// </summary>
    public class PropertyGridWpfPanel : SwfUserControl
    {
        private static bool _resourcesLoaded;

        private readonly ElementHost _host;
        private readonly HcPropertyGrid _grid;

        public event EventHandler PropertyValueChanged;

        public PropertyGridWpfPanel()
        {
            EnsureHandyControlResources();

            _grid = new HcPropertyGrid
            {
                Background = Brushes.White,
                ShowSortButton = true,
                MinTitleWidth = 120,
                MaxTitleWidth = 240
            };

            ApplyReadOnlyShading(_grid);

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
            var current = _grid.SelectedObject;
            _grid.SelectedObject = null;
            _grid.SelectedObject = current;
            PropertyValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void ApplyReadOnlyShading(HcPropertyGrid grid)
        {
            try
            {
                var shadeBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
                shadeBrush.Freeze();

                var style = new System.Windows.Style(typeof(HandyControl.Controls.PropertyItem));
                var trigger = new System.Windows.DataTrigger
                {
                    Binding = new System.Windows.Data.Binding("IsReadOnly")
                    {
                        RelativeSource = new System.Windows.Data.RelativeSource(
                            System.Windows.Data.RelativeSourceMode.Self)
                    },
                    Value = true
                };
                trigger.Setters.Add(new System.Windows.Setter(
                    System.Windows.Controls.Control.BackgroundProperty, shadeBrush));
                style.Triggers.Add(trigger);

                grid.Resources[typeof(HandyControl.Controls.PropertyItem)] = style;
            }
            catch
            {
                // If style fails (API change), the grid still works without shading.
            }
        }

        private static void EnsureHandyControlResources()
        {
            if (_resourcesLoaded) return;
            try
            {
                if (Application.Current == null)
                    new Application();

                var themes = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml", UriKind.Absolute)
                };
                var theme = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml", UriKind.Absolute)
                };
                Application.Current.Resources.MergedDictionaries.Add(themes);
                Application.Current.Resources.MergedDictionaries.Add(theme);

                // Force English UI strings for HandyControl built-in headers (e.g. Misc/Other)
                try
                {
                    HandyControl.Properties.Langs.Lang.Culture =
                        new System.Globalization.CultureInfo("en-US");
                }
                catch { }

                _resourcesLoaded = true;
            }
            catch
            {
                // If resources fail to load, the grid will still work but unstyled.
            }
        }
    }
}
