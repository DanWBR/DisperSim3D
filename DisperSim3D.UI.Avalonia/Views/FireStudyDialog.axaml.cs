#nullable enable
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using DisperSim3D.Core;
using DisperSim3D.Models;

namespace DisperSim3D.UI.Avalonia.Views
{
    /// <summary>
    /// Edits a <see cref="FireStudy"/> and scores it in place. Membership is the two
    /// multi-select lists: jet and pool fires on the left, ignitions on the right.
    /// Evaluate ranks whatever is selected without closing the dialog, so the harm
    /// criterion and the grid can be tuned against the numbers they produce.
    /// </summary>
    public partial class FireStudyDialog : Window
    {
        private readonly Scene3D _scene;
        private readonly List<FireSource> _fireSources = new();
        private readonly List<IgnitionEvent> _ignitions = new();

        /// <summary>The harm quantities offered, in the order of the combo.</summary>
        private static readonly ViewFieldProperty[] HarmQuantities =
        {
            ViewFieldProperty.FatalityProbability,
            ViewFieldProperty.ThermalDose,
            ViewFieldProperty.ThermalRadiationKwM2
        };

        public FireStudy Result { get; private set; } = new FireStudy();

        public FireStudyDialog() : this(new Scene3D(), null) { }

        public FireStudyDialog(Scene3D scene, FireStudy? existing)
        {
            _scene = scene;
            InitializeComponent();

            if (scene.FireScenario?.Sources != null)
            {
                foreach (var f in scene.FireScenario.Sources)
                {
                    if (f == null) continue;
                    _fireSources.Add(f);
                    LstFireSources.Items.Add(new ListBoxItem
                    {
                        Content = (string.IsNullOrEmpty(f.Name) ? "(fire)" : f.Name)
                                + (f.IsPoolFire ? "  — pool" : "  — jet")
                    });
                }
            }

            if (scene.Ignitions != null)
            {
                foreach (var g in scene.Ignitions)
                {
                    if (g == null) continue;
                    _ignitions.Add(g);
                    LstIgnitions.Items.Add(new ListBoxItem
                    {
                        Content = (string.IsNullOrEmpty(g.Name) ? "(ignition)" : g.Name)
                                + $"  — t = {g.TimeS:0.#} s"
                    });
                }
            }

            if (existing != null)
            {
                Result = existing;
                TxtName.Text = existing.Name;
                TxtDescription.Text = existing.Description;
                NudThreshold.Value = (decimal)existing.HarmThreshold;
                NudHalf.Value = (decimal)existing.DomainHalfM;
                NudGrid.Value = existing.GridResolution;
                NudIgnitionProbability.Value = (decimal)existing.IgnitionProbability;

                int harmIndex = System.Array.IndexOf(HarmQuantities, existing.HarmQuantity);
                CmbHarm.SelectedIndex = harmIndex >= 0 ? harmIndex : 0;

                SelectMembers(LstFireSources, _fireSources.ConvertAll(f => f.Id), existing.FireSourceIds);
                SelectMembers(LstIgnitions, _ignitions.ConvertAll(g => g.Id), existing.IgnitionIds);
            }
        }

        private static void SelectMembers(ListBox list, List<string> ids, List<string> selected)
        {
            for (int i = 0; i < ids.Count; i++)
                if (selected.Contains(ids[i])) list.SelectedItems?.Add(list.Items[i]);
        }

        /// <summary>Copies the dialog state onto <see cref="Result"/>. Shared by Evaluate
        /// and OK so the report always describes what OK would save.</summary>
        private void Harvest()
        {
            Result.Name = string.IsNullOrWhiteSpace(TxtName.Text) ? "Fire Study" : TxtName.Text.Trim();
            Result.Description = TxtDescription.Text ?? "";
            Result.HarmQuantity = HarmQuantities[System.Math.Max(0, CmbHarm.SelectedIndex)];
            Result.HarmThreshold = (double)(NudThreshold.Value ?? 0.01m);
            Result.DomainHalfM = (double)(NudHalf.Value ?? 100m);
            Result.GridResolution = (int)(NudGrid.Value ?? 40m);
            Result.IgnitionProbability = (double)(NudIgnitionProbability.Value ?? 0.1m);

            Result.FireSourceIds.Clear();
            foreach (var index in SelectedIndices(LstFireSources))
                Result.FireSourceIds.Add(_fireSources[index].Id);

            Result.IgnitionIds.Clear();
            foreach (var index in SelectedIndices(LstIgnitions))
                Result.IgnitionIds.Add(_ignitions[index].Id);
        }

        private static List<int> SelectedIndices(ListBox list)
        {
            var indices = new List<int>();
            if (list.SelectedItems == null) return indices;
            foreach (var item in list.SelectedItems)
            {
                int i = list.Items.IndexOf(item);
                if (i >= 0) indices.Add(i);
            }
            indices.Sort();
            return indices;
        }

        private void BtnEvaluate_Click(object? sender, RoutedEventArgs e)
        {
            Harvest();
            if (Result.FireSourceIds.Count == 0 && Result.IgnitionIds.Count == 0)
            {
                TxtReport.Text = "Select at least one fire source or ignition.";
                return;
            }

            BtnEvaluate.IsEnabled = false;
            TxtReport.Text = "Evaluating…";
            try
            {
                TxtReport.Text = FireStudyEngine.Evaluate(_scene, Result).Format();
            }
            catch (System.Exception ex)
            {
                TxtReport.Text = "Evaluation failed: " + ex.Message;
            }
            finally
            {
                BtnEvaluate.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object? sender, RoutedEventArgs e) => Close(false);

        private void BtnOK_Click(object? sender, RoutedEventArgs e)
        {
            Harvest();
            Close(true);
        }
    }
}
