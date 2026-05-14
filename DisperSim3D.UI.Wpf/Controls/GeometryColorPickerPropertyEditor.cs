#nullable enable
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HandyControl.Controls;
using GeoColor = DisperSim3D.Geometry.Color;

namespace DisperSim3D.Controls
{
    public class GeometryColorSwatchPicker : Button
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(GeoColor), typeof(GeometryColorSwatchPicker),
                new FrameworkPropertyMetadata(GeoColor.FromRgb(128, 128, 128),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public GeoColor SelectedColor
        {
            get => (GeoColor)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        internal PropertyItem? BoundPropertyItem { get; set; }
        private bool _suppressSync;

        public GeometryColorSwatchPicker()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Padding = new Thickness(2);
            MinHeight = 22;
            UpdateContent();
            Click += OnClick;
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var picker = (GeometryColorSwatchPicker)d;
            picker.UpdateContent();
            if (!picker._suppressSync && picker.BoundPropertyItem != null)
            {
                try { picker.BoundPropertyItem.Value = (GeoColor)e.NewValue; }
                catch { }
            }
        }

        private void UpdateContent()
        {
            var c = SelectedColor;
            var wpfColor = Color.FromArgb(c.A, c.R, c.G, c.B);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 18, Height = 14,
                Fill = new SolidColorBrush(wpfColor),
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(swatch, 0);
            var label = new TextBlock
            {
                Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 0, 0)
            };
            Grid.SetColumn(label, 1);
            grid.Children.Add(swatch);
            grid.Children.Add(label);
            Content = grid;
        }

        private void OnClick(object sender, RoutedEventArgs e)
        {
            var c = SelectedColor;
            var picker = new ColorPicker();
            try { picker.SelectedBrush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B)); } catch { }

            var popup = new Popup
            {
                Child = picker,
                PlacementTarget = this,
                Placement = PlacementMode.Bottom,
                StaysOpen = false,
                AllowsTransparency = true,
                IsOpen = true
            };
            picker.Confirmed += (s, args) =>
            {
                if (picker.SelectedBrush != null)
                {
                    var wpf = picker.SelectedBrush.Color;
                    _suppressSync = true;
                    SelectedColor = GeoColor.FromArgb(wpf.A, wpf.R, wpf.G, wpf.B);
                    _suppressSync = false;
                    if (BoundPropertyItem != null)
                    {
                        try { BoundPropertyItem.Value = SelectedColor; }
                        catch { }
                    }
                }
                popup.IsOpen = false;
            };
            picker.Canceled += (s, args) => popup.IsOpen = false;
        }
    }

    public class GeometryColorPickerPropertyEditor : PropertyEditorBase
    {
        public override DependencyProperty GetDependencyProperty()
            => GeometryColorSwatchPicker.SelectedColorProperty;

        public override FrameworkElement CreateElement(PropertyItem propertyItem)
            => new GeometryColorSwatchPicker
            {
                IsEnabled = !propertyItem.IsReadOnly,
                BoundPropertyItem = propertyItem
            };
    }
}
