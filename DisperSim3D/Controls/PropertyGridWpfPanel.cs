using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using HcPropertyGrid = HandyControl.Controls.PropertyGrid;
using HcPropertyItem = HandyControl.Controls.PropertyItem;
using SwfUserControl = System.Windows.Forms.UserControl;
using SwfDockStyle = System.Windows.Forms.DockStyle;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// WPF-based property grid hosted in a WinForms ElementHost using HandyControl's PropertyGrid.
    /// MIT license, modern look, native editors per type. Also renders the
    /// <c>[Description("…")]</c> of the focused property as a wrapping footer
    /// below the grid (VS-style help panel), so the docs that already exist on
    /// every model surface are discoverable without hover.
    /// </summary>
    public class PropertyGridWpfPanel : SwfUserControl
    {
        private static bool _resourcesLoaded;

        private readonly ElementHost _host;
        private readonly HcPropertyGrid _grid;
        private readonly TextBlock _descriptionTitle;
        private readonly TextBlock _descriptionBody;

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

            // HandyControl's PropertyGrid does not expose its own committed-value event,
            // so we route commits through several inner-editor events. All AddHandler
            // calls use handledEventsToo=true so the events still surface even after
            // the editor marks them handled. RefreshViews is idempotent, so firing more
            // often than strictly necessary is harmless.
            //
            // - LostKeyboardFocus + Enter/Tab: covers TextBox, NumericUpDown.
            // - ToggleButton.Checked/Unchecked: covers Toggle / Switch / CheckBox
            //   editors. These do NOT lose focus on click so they were silent before.
            // - Selector.SelectionChanged: covers ComboBox (enum dropdowns) which
            //   may not blur when the popup closes.
            _grid.AddHandler(System.Windows.UIElement.LostKeyboardFocusEvent,
                new System.Windows.Input.KeyboardFocusChangedEventHandler((s, e) =>
                {
                    PropertyValueChanged?.Invoke(this, EventArgs.Empty);
                }), true);
            _grid.AddHandler(System.Windows.UIElement.KeyDownEvent,
                new System.Windows.Input.KeyEventHandler((s, e) =>
                {
                    if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Tab)
                        PropertyValueChanged?.Invoke(this, EventArgs.Empty);
                }), true);
            _grid.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,
                new System.Windows.RoutedEventHandler((s, e) =>
                {
                    PropertyValueChanged?.Invoke(this, EventArgs.Empty);
                }), true);
            _grid.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,
                new System.Windows.RoutedEventHandler((s, e) =>
                {
                    PropertyValueChanged?.Invoke(this, EventArgs.Empty);
                }), true);
            _grid.AddHandler(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,
                new System.Windows.Controls.SelectionChangedEventHandler((s, e) =>
                {
                    PropertyValueChanged?.Invoke(this, EventArgs.Empty);
                }), true);

            // ── Description footer ─────────────────────────────────────────
            // Mimics the Visual Studio Properties pane: an always-visible
            // panel below the grid showing the [DisplayName] in bold and the
            // [Description("…")] of whichever property currently has focus.
            // Updated by walking the visual tree from the focused element up
            // to the nearest PropertyItem.
            var descBorder = new Border
            {
                BorderThickness = new Thickness(0, 1, 0, 0),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xCF, 0xCF)),
                Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF6)),
                Padding = new Thickness(8, 6, 8, 8),
                MinHeight = 56
            };
            DockPanel.SetDock(descBorder, System.Windows.Controls.Dock.Bottom);

            _descriptionTitle = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 2),
                Text = ""
            };
            _descriptionBody = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                Text = "Select a property to see its description."
            };
            var descStack = new StackPanel { Orientation = Orientation.Vertical };
            descStack.Children.Add(_descriptionTitle);
            descStack.Children.Add(_descriptionBody);
            descBorder.Child = descStack;

            var rootDock = new DockPanel { LastChildFill = true };
            rootDock.Children.Add(descBorder);   // docked Bottom
            rootDock.Children.Add(_grid);        // fills remainder

            // Focus events on the inner editors propagate up to the grid root.
            // Walk to the nearest PropertyItem ancestor and read its DisplayName
            // / Description so the help panel tracks the user's current focus
            // in the grid.
            _grid.AddHandler(System.Windows.UIElement.GotFocusEvent,
                new System.Windows.RoutedEventHandler((s, e) =>
                    UpdateDescriptionFromFocus(e.OriginalSource)), true);
            _grid.AddHandler(System.Windows.Input.Mouse.MouseEnterEvent,
                new System.Windows.Input.MouseEventHandler((s, e) =>
                    UpdateDescriptionFromFocus(e.OriginalSource)), true);

            _host = new ElementHost
            {
                Dock = SwfDockStyle.Fill,
                Child = rootDock
            };
            this.Controls.Add(_host);
        }

        /// <summary>Walks up the WPF visual tree from <paramref name="hit"/>
        /// looking for the enclosing <see cref="HcPropertyItem"/>, then mirrors
        /// its DisplayName + Description into the footer panel. Used to feed
        /// both focus and mouse-enter events — focus tracks keyboard/click
        /// navigation, mouse-enter gives instant feedback on hover without
        /// requiring the user to actually click into the editor.</summary>
        private void UpdateDescriptionFromFocus(object hit)
        {
            try
            {
                var dep = hit as System.Windows.DependencyObject;
                while (dep != null)
                {
                    if (dep is HcPropertyItem item)
                    {
                        string title = !string.IsNullOrEmpty(item.DisplayName)
                            ? item.DisplayName
                            : item.PropertyName ?? "";
                        string body = item.Description ?? "";
                        _descriptionTitle.Text = title;
                        _descriptionBody.Text = string.IsNullOrEmpty(body)
                            ? "(No description on this property.)"
                            : body;
                        return;
                    }
                    dep = VisualTreeHelper.GetParent(dep);
                }
            }
            catch
            {
                // If the visual tree shape changes between HandyControl
                // versions and the cast fails, just leave the footer alone —
                // the property grid still works.
            }
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
