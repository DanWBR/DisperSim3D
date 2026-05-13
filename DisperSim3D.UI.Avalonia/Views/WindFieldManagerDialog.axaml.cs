#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Avalonia equivalent of the WPF / WinForms <c>WindFieldManagerDialog</c>.
    /// Library manager for <see cref="WindFieldScenario"/> entries: list on the
    /// left with New / Rename / Duplicate / Delete buttons; properties for the
    /// selected scenario on the right; a Run button at the bottom kicks off
    /// the <see cref="WindFieldRunner"/> against the application-level CFD
    /// config and reports progress through an indeterminate-friendly bar.
    /// On OK the dialog exposes <see cref="Scenarios"/> for the caller to
    /// merge back into <see cref="Scene3D.WindFieldScenarios"/>.
    /// </summary>
    public partial class WindFieldManagerDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly OpenFoamEnvironment _env;
        private readonly ObservableCollection<ScenarioRow> _rows = new();

        public List<WindFieldScenario> Scenarios { get; private set; }

        public WindFieldManagerDialog() : this(new Scene3D(), new OpenFoamEnvironment(), null) { }

        public WindFieldManagerDialog(Scene3D scene, OpenFoamEnvironment env, string? preselectedId = null)
        {
            _scene = scene;
            _env = env ?? new OpenFoamEnvironment();
            Scenarios = new List<WindFieldScenario>(scene.WindFieldScenarios);

            InitializeComponent();

            LstScenarios.ItemsSource = _rows;
            // Show "{Name} [{Status}]" so users see at a glance which scenarios
            // are Ready vs Pending vs Failed. Item display reads from a row
            // wrapper that we refresh on Save / Run.
            LstScenarios.ItemTemplate = new global::Avalonia.Controls.Templates.FuncDataTemplate<ScenarioRow>(
                (row, _) => new TextBlock
                {
                    [!TextBlock.TextProperty] = new global::Avalonia.Data.Binding(nameof(ScenarioRow.Display)),
                    Margin = new global::Avalonia.Thickness(4, 1)
                });

            RefreshList();

            if (!string.IsNullOrEmpty(preselectedId))
                for (int i = 0; i < _rows.Count; i++)
                    if (_rows[i].Scenario.Id == preselectedId)
                    {
                        LstScenarios.SelectedIndex = i;
                        break;
                    }
        }

        // ── List housekeeping ────────────────────────────────────────────────
        private void RefreshList()
        {
            int sel = LstScenarios.SelectedIndex;
            _rows.Clear();
            foreach (var wf in Scenarios) _rows.Add(new ScenarioRow(wf));
            if (sel >= 0 && sel < _rows.Count) LstScenarios.SelectedIndex = sel;
        }

        private void LstScenarios_SelectionChanged(object? sender, SelectionChangedEventArgs e)
            => LoadSelected();

        private void LoadSelected()
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count)
            {
                PropsPanel.IsEnabled = false;
                return;
            }
            PropsPanel.IsEnabled = true;
            var wf = Scenarios[idx];

            TxtName.Text         = wf.Name ?? "";
            NudDomainSize.Value  = Clamp(wf.DomainSizeM,     NudDomainSize);
            NudDomainHeight.Value= Clamp(wf.DomainHeightM,   NudDomainHeight);
            NudGridRes.Value     = Clamp(wf.GridResolution,  NudGridRes);

            if (wf.Meteo != null)
            {
                NudWindSpeed.Value     = Clamp(wf.Meteo.WindSpeed,             NudWindSpeed);
                NudWindDir.Value       = Clamp(wf.Meteo.WindDirectionDeg,      NudWindDir);
                CmbStability.SelectedIndex = (int)wf.Meteo.StabilityClass;
                // Stored in Kelvin on the engine model; display as °C.
                NudTemperature.Value   = Clamp(wf.Meteo.AmbientTemperature - 273.15, NudTemperature);
            }

            LblStatus.Text = "Status: " + wf.Status
                + (string.IsNullOrEmpty(wf.StatusMessage) ? "" : " — " + wf.StatusMessage);
        }

        private static decimal Clamp(double v, NumericUpDown nud)
            => (decimal)Math.Max((double)nud.Minimum, Math.Min((double)nud.Maximum, v));

        private void SaveCurrent()
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0 || idx >= Scenarios.Count) return;
            var wf = Scenarios[idx];
            wf.Name           = TxtName.Text ?? "";
            wf.DomainSizeM    = (double)(NudDomainSize.Value ?? 200m);
            wf.DomainHeightM  = (double)(NudDomainHeight.Value ?? 100m);
            wf.GridResolution = (int)(NudGridRes.Value ?? 40m);
            wf.Meteo ??= new MeteorologicalConditions();
            wf.Meteo.WindSpeed          = (double)(NudWindSpeed.Value ?? 5m);
            wf.Meteo.WindDirectionDeg   = (double)(NudWindDir.Value ?? 270m);
            wf.Meteo.AmbientTemperature = (double)(NudTemperature.Value ?? 20m) + 273.15;
            wf.Meteo.StabilityClass     = (PasquillStabilityClass)Math.Max(0, CmbStability.SelectedIndex);
            // Refresh the list so the new name / status surface in the listbox.
            _rows[idx].UpdateDisplay();
        }

        // ── New / Rename / Duplicate / Delete ────────────────────────────────
        private void BtnNew_Click(object? sender, RoutedEventArgs e)
        {
            SaveCurrent();
            var wf = new WindFieldScenario { Name = "Wind Field " + (Scenarios.Count + 1) };
            wf.CfdConfig ??= new CfdConfiguration();
            CfdConfigurationPresets.ApplyForSolver(
                wf.CfdConfig, CfdSolverType.ScalarSimpleFoam, null, wf.Meteo);
            Scenarios.Add(wf);
            RefreshList();
            LstScenarios.SelectedIndex = Scenarios.Count - 1;
        }

        private async void BtnRename_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0) return;
            string? name = await PromptInputAsync("Rename Wind Field",
                "Enter new name:", Scenarios[idx].Name ?? "");
            if (string.IsNullOrEmpty(name)) return;
            Scenarios[idx].Name = name;
            _rows[idx].UpdateDisplay();
            LoadSelected();
        }

        private void BtnDuplicate_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0) return;
            SaveCurrent();
            var orig = Scenarios[idx];
            var copy = new WindFieldScenario
            {
                Name           = (orig.Name ?? "Wind Field") + " (copy)",
                DomainSizeM    = orig.DomainSizeM,
                DomainHeightM  = orig.DomainHeightM,
                GridResolution = orig.GridResolution,
                Meteo = new MeteorologicalConditions
                {
                    WindSpeed          = orig.Meteo?.WindSpeed ?? 5,
                    WindDirectionDeg   = orig.Meteo?.WindDirectionDeg ?? 270,
                    StabilityClass     = orig.Meteo?.StabilityClass ?? PasquillStabilityClass.D,
                    AmbientTemperature = orig.Meteo?.AmbientTemperature ?? 293.15,
                    AmbientPressure    = orig.Meteo?.AmbientPressure ?? 101325
                }
            };
            Scenarios.Add(copy);
            RefreshList();
            LstScenarios.SelectedIndex = Scenarios.Count - 1;
        }

        private void BtnDelete_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0) return;
            Scenarios.RemoveAt(idx);
            RefreshList();
        }

        // ── Run ──────────────────────────────────────────────────────────────
        private async void BtnRun_Click(object? sender, RoutedEventArgs e)
        {
            int idx = LstScenarios.SelectedIndex;
            if (idx < 0) return;
            SaveCurrent();
            var wf = Scenarios[idx];

            wf.CfdConfig = AppSettings.Instance.CreateCfdConfig();

            var obstacles = new List<BoundingBox>();
            if (_scene.Decorations != null)
                foreach (var deco in _scene.Decorations)
                    if (deco.BoundingBox != null) obstacles.Add(deco.BoundingBox);

            BtnRun.IsEnabled = false;
            ProgressBar.IsVisible = true;
            ProgressBar.Value = 0;
            LblStatus.Text = "Running...";

            try
            {
                // Off the UI thread — the runner shells out to OpenFOAM. Progress
                // messages come back via Dispatcher.UIThread.Post so the bar can
                // update without cross-thread access exceptions.
                await Task.Run(() =>
                {
                    var runner = new WindFieldRunner(_env);
                    runner.Run(wf, obstacles, (frac, msg) =>
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            ProgressBar.Value = Math.Max(0, Math.Min(100, frac * 100));
                            LblStatus.Text = msg ?? "";
                        });
                    });
                });
                LblStatus.Text = "Status: " + wf.Status
                    + (string.IsNullOrEmpty(wf.StatusMessage) ? "" : " — " + wf.StatusMessage);
            }
            catch (Exception ex)
            {
                LblStatus.Text = "Failed: " + ex.Message;
            }
            finally
            {
                BtnRun.IsEnabled = true;
                ProgressBar.IsVisible = false;
                _rows[idx].UpdateDisplay();
            }
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────
        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            SaveCurrent();
            Close(true);
        }

        /// <summary>Modal text-input prompt. Avalonia doesn't ship a built-in
        /// equivalent of WinForms' InputBox, so we host a small Window with a
        /// single TextBox + OK/Cancel buttons and await its dialog result.</summary>
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

        /// <summary>List row wrapper. Keeps the listbox text in sync with the
        /// underlying scenario's Name / Status without us having to rebuild
        /// the whole collection on every edit.</summary>
        private sealed class ScenarioRow : global::System.ComponentModel.INotifyPropertyChanged
        {
            public WindFieldScenario Scenario { get; }
            public string Display => string.Format("{0} [{1}]",
                Scenario.Name ?? "(unnamed)", Scenario.Status);
            public ScenarioRow(WindFieldScenario s) { Scenario = s; }
            public event global::System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            public void UpdateDisplay()
                => PropertyChanged?.Invoke(this,
                    new global::System.ComponentModel.PropertyChangedEventArgs(nameof(Display)));
        }
    }
}
