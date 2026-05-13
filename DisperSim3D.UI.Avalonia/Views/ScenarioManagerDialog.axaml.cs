#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>ScenarioManagerDialog</c>.
    /// Manages the list of <see cref="DispersionScenario"/> entries on a
    /// project: list on the left with New / Rename / Duplicate / Delete;
    /// properties for the selected scenario on the right (timing, domain,
    /// meteorology, wind-field link). Returns the updated list and the new
    /// active index via <see cref="Scenarios"/> and <see cref="SelectedIndex"/>.
    /// </summary>
    public partial class ScenarioManagerDialog : Window
    {
        private readonly List<WindFieldScenario> _windFields;
        private readonly ObservableCollection<ScenarioRow> _rows = new();

        public List<DispersionScenario> Scenarios { get; private set; }
        public int SelectedIndex { get; private set; }

        public ScenarioManagerDialog() : this(new List<DispersionScenario>(), 0, null) { }

        public ScenarioManagerDialog(List<DispersionScenario> scenarios, int activeIndex,
            List<WindFieldScenario>? windFieldScenarios = null)
        {
            Scenarios = new List<DispersionScenario>(scenarios);
            SelectedIndex = activeIndex;
            _windFields = windFieldScenarios ?? new List<WindFieldScenario>();

            InitializeComponent();

            LstScenarios.ItemsSource = _rows;
            LstScenarios.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<ScenarioRow>(
                (row, _) => new TextBlock
                {
                    [!TextBlock.TextProperty] = new global::Avalonia.Data.Binding(nameof(ScenarioRow.Display)),
                    Margin = new global::Avalonia.Thickness(4, 1)
                });

            RefreshWindFieldCombo();
            RefreshList();

            if (SelectedIndex >= 0 && SelectedIndex < _rows.Count)
                LstScenarios.SelectedIndex = SelectedIndex;
        }

        private void RefreshList()
        {
            int prev = LstScenarios.SelectedIndex;
            _rows.Clear();
            for (int i = 0; i < Scenarios.Count; i++)
                _rows.Add(new ScenarioRow(Scenarios[i],
                    Scenarios[i].Name ?? ("Scenario " + (i + 1))));
            if (prev >= 0 && prev < _rows.Count) LstScenarios.SelectedIndex = prev;
        }

        private void RefreshWindFieldCombo()
        {
            CmbWindField.Items.Clear();
            CmbWindField.Items.Add(new ComboBoxItem { Content = "(none — required)" });
            foreach (var wf in _windFields)
                CmbWindField.Items.Add(new ComboBoxItem
                {
                    Content = string.Format("{0} [{1}]", wf.Name ?? "(unnamed)", wf.Status)
                });
        }

        private void LstScenarios_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => LoadSelectedScenario();

        private void LoadSelectedScenario()
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count)
            {
                PropsPanel.IsEnabled = false;
                return;
            }
            PropsPanel.IsEnabled = true;
            var sc = Scenarios[idx];

            NudDuration.Value    = Clamp(sc.SimulationDurationS, NudDuration);
            NudTimeStep.Value    = Clamp(sc.TimeStepS,           NudTimeStep);
            NudDomainSize.Value  = Clamp(sc.DomainSizeM,         NudDomainSize);
            NudGridRes.Value     = Clamp(sc.GridResolution,      NudGridRes);

            if (sc.Meteo != null)
            {
                NudWindSpeed.Value     = Clamp(sc.Meteo.WindSpeed,             NudWindSpeed);
                NudWindDir.Value       = Clamp(sc.Meteo.WindDirectionDeg,      NudWindDir);
                NudAmbientTemp.Value   = Clamp(sc.Meteo.AmbientTemperature - 273.15, NudAmbientTemp);
                CmbStability.SelectedIndex = (int)sc.Meteo.StabilityClass;
            }

            // Find the matching wind-field combo entry (entry 0 = "(none)").
            int wfIdx = 0;
            if (!string.IsNullOrEmpty(sc.WindFieldScenarioId))
                for (int i = 0; i < _windFields.Count; i++)
                    if (_windFields[i].Id == sc.WindFieldScenarioId) { wfIdx = i + 1; break; }
            CmbWindField.SelectedIndex = Math.Min(wfIdx, CmbWindField.Items.Count - 1);
        }

        private static decimal Clamp(double v, NumericUpDown nud)
            => (decimal)Math.Max((double)nud.Minimum, Math.Min((double)nud.Maximum, v));

        private void SaveCurrentScenario()
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count) return;
            var sc = Scenarios[idx];

            sc.SimulationDurationS = (double)(NudDuration.Value ?? 300m);
            sc.TimeStepS           = (double)(NudTimeStep.Value ?? 0.5m);
            sc.DomainSizeM         = (double)(NudDomainSize.Value ?? 200m);
            sc.GridResolution      = (int)(NudGridRes.Value ?? 80m);

            sc.Meteo ??= new MeteorologicalConditions();
            sc.Meteo.WindSpeed           = (double)(NudWindSpeed.Value ?? 3m);
            sc.Meteo.WindDirectionDeg    = (double)(NudWindDir.Value ?? 270m);
            sc.Meteo.AmbientTemperature  = (double)(NudAmbientTemp.Value ?? 20m) + 273.15;
            sc.Meteo.StabilityClass      = (PasquillStabilityClass)Math.Max(0, CmbStability.SelectedIndex);

            // Combo index 0 is the "(none)" sentinel; the actual wind-field
            // entries live at index 1..N.
            int wfIdx = CmbWindField.SelectedIndex;
            sc.WindFieldScenarioId = (wfIdx <= 0 || wfIdx - 1 >= _windFields.Count)
                ? null
                : _windFields[wfIdx - 1].Id;

            _rows[idx].UpdateDisplay(sc.Name ?? ("Scenario " + (idx + 1)));
        }

        // ── New / Rename / Duplicate / Delete ────────────────────────────────
        private void BtnNew_Click(object? sender, RoutedEventArgs e)
        {
            SaveCurrentScenario();
            var sc = new DispersionScenario { Name = "Scenario " + (Scenarios.Count + 1) };
            Scenarios.Add(sc);
            RefreshList();
            LstScenarios.SelectedIndex = Scenarios.Count - 1;
        }

        private async void BtnRename_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0) return;
            string? name = await PromptInputAsync("Rename Scenario",
                "Enter new name:", Scenarios[idx].Name ?? "");
            if (string.IsNullOrEmpty(name)) return;
            Scenarios[idx].Name = name;
            _rows[idx].UpdateDisplay(name);
        }

        private void BtnDuplicate_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0) return;
            SaveCurrentScenario();
            var orig = Scenarios[idx];
            var copy = new DispersionScenario
            {
                Name                = (orig.Name ?? "Scenario") + " (copy)",
                SimulationDurationS = orig.SimulationDurationS,
                TimeStepS           = orig.TimeStepS,
                DomainSizeM         = orig.DomainSizeM,
                GridResolution      = orig.GridResolution,
                Meteo = new MeteorologicalConditions
                {
                    WindSpeed          = orig.Meteo?.WindSpeed ?? 3,
                    WindDirectionDeg   = orig.Meteo?.WindDirectionDeg ?? 270,
                    StabilityClass     = orig.Meteo?.StabilityClass ?? PasquillStabilityClass.D,
                    AmbientTemperature = orig.Meteo?.AmbientTemperature ?? 293.15,
                    AmbientPressure    = orig.Meteo?.AmbientPressure ?? 101325
                }
            };
            // Deep-copy the source list and thresholds so editing the copy
            // doesn't mutate the original.
            foreach (var src in orig.Sources)
                copy.Sources.Add(new ReleaseSource3D
                {
                    Name                = src.Name,
                    Position            = src.Position,
                    Gas                 = src.Gas,
                    ReleaseRateKgPerS   = src.ReleaseRateKgPerS,
                    PuffIntervalS       = src.PuffIntervalS,
                    ReleaseHeightOffset = src.ReleaseHeightOffset,
                    AttachedUnitId      = src.AttachedUnitId
                });
            foreach (var t in orig.Thresholds)
                copy.Thresholds.Add(new DispersionThreshold
                {
                    Name               = t.Name,
                    Type               = t.Type,
                    ConcentrationValue = t.ConcentrationValue,
                    Color              = t.Color,
                    Opacity            = t.Opacity,
                    Visible            = t.Visible
                });
            Scenarios.Add(copy);
            RefreshList();
            LstScenarios.SelectedIndex = Scenarios.Count - 1;
        }

        private void BtnDelete_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            // Refuse to remove the last scenario — the engine always expects
            // at least one to exist on Scene3D.
            if (idx < 0 || Scenarios.Count <= 1) return;
            Scenarios.RemoveAt(idx);
            if (SelectedIndex >= Scenarios.Count) SelectedIndex = Scenarios.Count - 1;
            RefreshList();
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            SaveCurrentScenario();
            SelectedIndex = Math.Max(0, LstScenarios.SelectedIndex);
            Close(true);
        }

        /// <summary>Tiny input prompt for the Rename button (Avalonia has no
        /// built-in InputBox).</summary>
        private async Task<string?> PromptInputAsync(string title, string prompt, string defaultValue)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 360,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon
            };
            var tb = new TextBox { Text = defaultValue ?? "", Margin = new global::Avalonia.Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "OK", MinWidth = 80, IsDefault = true,
                Background = global::Avalonia.Media.Brushes.DodgerBlue,
                Foreground = global::Avalonia.Media.Brushes.White };
            var cancel = new Button { Content = "Cancel", MinWidth = 80, IsCancel = true };
            string? result = null;
            ok.Click += (_, _) => { result = tb.Text; dlg.Close(); };
            cancel.Click += (_, _) => { result = null; dlg.Close(); };

            var btnRow = new StackPanel
            {
                Orientation = global::Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8,
                Margin = new global::Avalonia.Thickness(0, 12, 0, 0)
            };
            btnRow.Children.Add(cancel);
            btnRow.Children.Add(ok);
            var root = new StackPanel { Margin = new global::Avalonia.Thickness(12) };
            root.Children.Add(new TextBlock { Text = prompt });
            root.Children.Add(tb);
            root.Children.Add(btnRow);
            dlg.Content = root;

            await dlg.ShowDialog(this);
            return result;
        }

        /// <summary>List wrapper carrying both the engine scenario and the
        /// display string. Renaming pokes Display via UpdateDisplay so the
        /// listbox row refreshes without rebuilding the whole collection.</summary>
        private sealed class ScenarioRow : global::System.ComponentModel.INotifyPropertyChanged
        {
            public DispersionScenario Scenario { get; }
            public string Display { get; private set; }
            public ScenarioRow(DispersionScenario s, string display)
            {
                Scenario = s;
                Display = display;
            }
            public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            public void UpdateDisplay(string s)
            {
                Display = s;
                PropertyChanged?.Invoke(this,
                    new global::System.ComponentModel.PropertyChangedEventArgs(nameof(Display)));
            }
        }
    }
}
