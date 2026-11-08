#nullable enable
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DisperSim3D.Extensibility;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Edits the options a simulation's extender solver offers, for any simulation on such a
    /// solver (a release or a fire). Returns true when the values were changed.
    /// </summary>
    public sealed class SolverOptionsDialog : Window
    {
        private readonly Simulation _sim;
        private readonly SolverOptionsPanel _panel = new SolverOptionsPanel();
        private readonly TextBlock _error = new TextBlock { Foreground = new SolidColorBrush(Color.Parse("#A40000")), IsVisible = false, TextWrapping = TextWrapping.Wrap };

        public SolverOptionsDialog() : this(new Simulation(), null) { }

        public SolverOptionsDialog(Simulation sim, ISolverExtender? solver)
        {
            _sim = sim;
            Title = "Solver Options — " + (sim.Name ?? "Simulation");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 700;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _panel.Show(solver, sim.ExtenderOptions);

            var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true, Background = new SolidColorBrush(Color.Parse("#0078D4")), Foreground = Brushes.White, Name = "BtnOK" };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true, Name = "BtnCancel" };
            var reset = new Button { Content = "Defaults", MinWidth = 80, Name = "BtnDefaults" };
            ok.Click += (_, _) =>
            {
                string? e = _panel.Validate();
                if (e != null) { _error.Text = e; _error.IsVisible = true; return; }
                _sim.ExtenderOptions = _panel.Values();
                Close(true);
            };
            cancel.Click += (_, _) => Close(false);
            reset.Click += (_, _) => _panel.Show(solver, null);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new global::Avalonia.Thickness(0, 10, 0, 0) };
            buttons.Children.Add(reset); buttons.Children.Add(cancel); buttons.Children.Add(ok);
            var header = new TextBlock
            {
                Text = (solver?.DisplayName ?? "This solver") + (_panel.Count == 0 ? " offers no options." : ": settings for this simulation's runs. A value left at its default follows the solver's default."),
                TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#444")), Margin = new global::Avalonia.Thickness(0, 0, 0, 6),
            };
            var body = new StackPanel { Margin = new global::Avalonia.Thickness(12) };
            body.Children.Add(header);
            body.Children.Add(_error);
            body.Children.Add(new ScrollViewer { Content = _panel, MaxHeight = 520, HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
            body.Children.Add(buttons);
            Content = body;
        }
    }
}
