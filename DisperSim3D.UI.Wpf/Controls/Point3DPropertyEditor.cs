using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using HandyControl.Controls;

namespace DisperSim3D.Controls
{
    /// <summary>
    /// HandyControl PropertyGrid editor for <see cref="Point3D"/> — exposes three
    /// <see cref="NumericUpDown"/> spinners (X / Y / Z) instead of a single text field.
    /// Apply via <c>[Editor(typeof(Point3DPropertyEditor), typeof(PropertyEditorBase))]</c>
    /// on any <see cref="Point3D"/> property.
    /// </summary>
    public class Point3DEditor : System.Windows.Controls.UserControl
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(Point3D), typeof(Point3DEditor),
                new FrameworkPropertyMetadata(default(Point3D),
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnValueChanged));

        public Point3D Value
        {
            get => (Point3D)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        private readonly NumericUpDown _x;
        private readonly NumericUpDown _y;
        private readonly NumericUpDown _z;
        private bool _suppressEvents;

        public Point3DEditor()
        {
            _x = MakeSpinner();
            _y = MakeSpinner();
            _z = MakeSpinner();

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            grid.Children.Add(WithLabel("X", _x, 0));
            grid.Children.Add(WithLabel("Y", _y, 1));
            grid.Children.Add(WithLabel("Z", _z, 2));

            Content = grid;
            MinHeight = 28;

            _x.ValueChanged += OnSpinnerChanged;
            _y.ValueChanged += OnSpinnerChanged;
            _z.ValueChanged += OnSpinnerChanged;
        }

        private static NumericUpDown MakeSpinner()
        {
            return new NumericUpDown
            {
                Minimum = -1_000_000,
                Maximum = 1_000_000,
                Value = 0,
                DecimalPlaces = 3,
                Increment = 1,
                Margin = new Thickness(1, 0, 1, 0),
                MinWidth = 60
            };
        }

        private static UIElement WithLabel(string label, NumericUpDown spinner, int column)
        {
            var dp = new DockPanel { LastChildFill = true, Margin = new Thickness(0) };
            var lbl = new TextBlock
            {
                Text = label,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2, 0, 4, 0),
                FontWeight = FontWeights.SemiBold
            };
            DockPanel.SetDock(lbl, Dock.Left);
            dp.Children.Add(lbl);
            dp.Children.Add(spinner);
            Grid.SetColumn(dp, column);
            return dp;
        }

        private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ed = (Point3DEditor)d;
            if (ed._suppressEvents) return;
            var p = (Point3D)e.NewValue;
            ed._suppressEvents = true;
            ed._x.Value = p.X;
            ed._y.Value = p.Y;
            ed._z.Value = p.Z;
            ed._suppressEvents = false;
        }

        private void OnSpinnerChanged(object sender, HandyControl.Data.FunctionEventArgs<double> e)
        {
            if (_suppressEvents) return;
            _suppressEvents = true;
            Value = new Point3D(_x.Value, _y.Value, _z.Value);
            _suppressEvents = false;
        }
    }

    /// <summary>HandyControl PropertyGrid editor returning a <see cref="Point3DEditor"/>.</summary>
    public class Point3DPropertyEditor : PropertyEditorBase
    {
        public override DependencyProperty GetDependencyProperty()
            => Point3DEditor.ValueProperty;

        public override FrameworkElement CreateElement(PropertyItem propertyItem)
            => new Point3DEditor { IsEnabled = !propertyItem.IsReadOnly };
    }
}
