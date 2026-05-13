#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>ViewEditorDialog</c>.
    /// Wizard for creating a <see cref="View"/>: pick a completed simulation,
    /// the view kind (isosurface / contour XY/XZ/YZ), the field to sample,
    /// and the time-collapse mode. Fine-tuning (iso value, color, plane
    /// position, color map, etc.) is done afterwards via the inspector.
    /// </summary>
    public partial class ViewEditorDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly List<Simulation> _completed;

        public View? Result { get; private set; }

        public ViewEditorDialog() : this(new Scene3D()) { }

        public ViewEditorDialog(Scene3D scene)
        {
            _scene = scene;
            InitializeComponent();

            TxtName.Text = "View " + (_scene.Views.Count + 1);

            // Default selections — match the WinForms behaviour.
            CmbKind.SelectedIndex = 0;
            CmbField.SelectedIndex = 0;
            CmbTimeMode.SelectedIndex = 0;

            _completed = _scene.Simulations
                .Where(s => s.Status == SimulationStatus.Completed)
                .ToList();

            if (_completed.Count == 0)
            {
                CmbSimulation.Items.Add(new ComboBoxItem
                {
                    Content = "(no completed simulations)"
                });
                CmbSimulation.SelectedIndex = 0;
                CmbSimulation.IsEnabled = false;
            }
            else
            {
                foreach (var s in _completed)
                    CmbSimulation.Items.Add(new ComboBoxItem
                    {
                        Content = string.IsNullOrEmpty(s.Name) ? "(unnamed)" : s.Name
                    });
                CmbSimulation.SelectedIndex = 0;
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            if (_completed.Count == 0)
            {
                ErrorText.Text = "No completed simulations available. Run a simulation first.";
                ErrorBanner.IsVisible = true;
                return;
            }

            var sim = _completed[Math.Max(0, CmbSimulation.SelectedIndex)];

            ViewKind kind = CmbKind.SelectedIndex switch
            {
                1 => ViewKind.ContourXY,
                2 => ViewKind.ContourXZ,
                3 => ViewKind.ContourYZ,
                _ => ViewKind.Isosurface
            };
            ViewFieldProperty field = (ViewFieldProperty)Math.Max(0, CmbField.SelectedIndex);
            ViewTimeMode timeMode = (ViewTimeMode)Math.Max(0, CmbTimeMode.SelectedIndex);

            Result = new View
            {
                Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "View" : TxtName.Text.Trim(),
                Kind = kind,
                SimulationId = sim.Id,
                FieldProperty = field,
                TimeMode = timeMode
            };
            Close(true);
        }
    }
}
