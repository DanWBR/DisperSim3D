using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HandyControl.Controls;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// HandyControl PropertyGrid editor for <see cref="System.Windows.Media.Color"/> properties.
    /// Shows a colour swatch button that opens a popup with HC's <see cref="ColorPicker"/>.
    /// Without this the grid falls back to the plain-text editor and renders the colour as
    /// the string "Color [A=255, R=…, G=…, B=…]" with no editing affordance.
    /// </summary>
    public class ColorSwatchPicker : Button
    {
        public static readonly DependencyProperty SelectedColorProperty =
            DependencyProperty.Register(nameof(SelectedColor), typeof(Color), typeof(ColorSwatchPicker),
                new FrameworkPropertyMetadata(Colors.Gray,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnSelectedColorChanged));

        public Color SelectedColor
        {
            get => (Color)GetValue(SelectedColorProperty);
            set => SetValue(SelectedColorProperty, value);
        }

        public ColorSwatchPicker()
        {
            HorizontalContentAlignment = HorizontalAlignment.Stretch;
            VerticalContentAlignment = VerticalAlignment.Stretch;
            Padding = new Thickness(2);
            MinHeight = 22;
            UpdateContent();
            Click += OnClick;
        }

        private static void OnSelectedColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ColorSwatchPicker)d).UpdateContent();

        private void UpdateContent()
        {
            var c = SelectedColor;
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            var swatch = new System.Windows.Shapes.Rectangle
            {
                Width = 18, Height = 14,
                Fill = new SolidColorBrush(c),
                Stroke = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                StrokeThickness = 1,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(swatch, 0);
            var label = new TextBlock
            {
                Text = string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B),
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
            var picker = new ColorPicker();
            try { picker.SelectedBrush = new SolidColorBrush(SelectedColor); } catch { }

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
                    SelectedColor = picker.SelectedBrush.Color;
                popup.IsOpen = false;
            };
            picker.Canceled += (s, args) => popup.IsOpen = false;
        }
    }

    /// <summary>HandyControl PropertyEditor that returns a <see cref="ColorSwatchPicker"/>.</summary>
    public class ColorPickerPropertyEditor : PropertyEditorBase
    {
        public override DependencyProperty GetDependencyProperty()
            => ColorSwatchPicker.SelectedColorProperty;

        public override FrameworkElement CreateElement(PropertyItem propertyItem)
            => new ColorSwatchPicker { IsEnabled = !propertyItem.IsReadOnly };
    }

    /// <summary>
    /// Custom <see cref="PropertyResolver"/> that returns <see cref="ColorPickerPropertyEditor"/>
    /// for any property of type <see cref="Color"/>; delegates to the base resolver otherwise.
    /// </summary>
    // Apply via [Editor(typeof(ColorPickerPropertyEditor), typeof(PropertyEditorBase))]
    // on any System.Windows.Media.Color property. HandyControl's default PropertyResolver
    // reads the EditorAttribute and instantiates our editor automatically — no subclassing
    // of PropertyGrid required (which broke the visual template).
}
