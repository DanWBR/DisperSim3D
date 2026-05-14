#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using HandyControl.Controls;
using Microsoft.Win32;

namespace DisperSim3D.Controls
{
    public class FilePathComboBox : System.Windows.Controls.ComboBox
    {
        public static readonly DependencyProperty FilePathValueProperty =
            DependencyProperty.Register(nameof(FilePathValue), typeof(string),
                typeof(FilePathComboBox),
                new FrameworkPropertyMetadata("",
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                    OnFilePathValueChanged));

        public string FilePathValue
        {
            get => (string)GetValue(FilePathValueProperty);
            set => SetValue(FilePathValueProperty, value);
        }

        internal PropertyItem? BoundPropertyItem { get; set; }

        private string[] _presetValues = Array.Empty<string>();
        private string[] _presetLabels = Array.Empty<string>();
        private string? _fileFilter;
        private bool _suppressSync;

        public FilePathComboBox() { }

        public void Configure(string? filter, string[]? presets, string[]? presetLabels)
        {
            _fileFilter = filter;
            _presetValues = presets ?? Array.Empty<string>();
            _presetLabels = presetLabels ?? Array.Empty<string>();
            RebuildItems();
        }

        private void RebuildItems()
        {
            _suppressSync = true;
            Items.Clear();

            for (int i = 0; i < _presetLabels.Length; i++)
                Items.Add(_presetLabels[i]);

            string current = FilePathValue ?? "";
            int customIdx = -1;
            if (!string.IsNullOrEmpty(current))
            {
                bool found = _presetValues.Contains(current);
                if (!found)
                {
                    string display;
                    try { display = System.IO.Path.GetFileName(current); }
                    catch { display = current; }
                    customIdx = Items.Count;
                    Items.Add(display);
                }
            }

            Items.Add("Browse…");

            if (customIdx >= 0)
                SelectedIndex = customIdx;
            else
            {
                for (int i = 0; i < _presetValues.Length; i++)
                    if (_presetValues[i] == current) { SelectedIndex = i; break; }
            }

            _suppressSync = false;
        }

        private void PushValue(string value)
        {
            _suppressSync = true;
            FilePathValue = value;
            try { if (BoundPropertyItem != null) BoundPropertyItem.Value = value; }
            catch { }
            _suppressSync = false;
        }

        protected override void OnSelectionChanged(SelectionChangedEventArgs e)
        {
            if (_suppressSync) { base.OnSelectionChanged(e); return; }

            int idx = SelectedIndex;
            if (idx >= 0)
            {
                int browseIdx = Items.Count - 1;
                if (idx == browseIdx)
                {
                    BrowseForFile();
                    return;
                }

                if (idx < _presetValues.Length)
                    PushValue(_presetValues[idx]);
            }

            base.OnSelectionChanged(e);
        }

        private void BrowseForFile()
        {
            var dlg = new OpenFileDialog();
            if (!string.IsNullOrEmpty(_fileFilter))
                dlg.Filter = _fileFilter;

            if (dlg.ShowDialog() == true)
            {
                PushValue(dlg.FileName);
                RebuildItems();
            }
            else
            {
                string current = FilePathValue ?? "";
                _suppressSync = true;
                for (int i = 0; i < _presetValues.Length; i++)
                    if (_presetValues[i] == current) { SelectedIndex = i; break; }
                _suppressSync = false;
            }
        }

        private static void OnFilePathValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var cbx = (FilePathComboBox)d;
            if (!cbx._suppressSync)
                cbx.RebuildItems();
        }
    }

    public class FilePathPropertyEditor : PropertyEditorBase
    {
        public override DependencyProperty GetDependencyProperty()
            => FilePathComboBox.FilePathValueProperty;

        public override FrameworkElement CreateElement(PropertyItem propertyItem)
        {
            var cbx = new FilePathComboBox
            {
                IsEnabled = !propertyItem.IsReadOnly,
                MinHeight = 26,
                BoundPropertyItem = propertyItem
            };

            string? filter = null;
            string[]? presets = null;
            string[]? presetLabels = null;

            try
            {
                var asm = typeof(DisperSim3D.Models.FilePathEditorAttribute).Assembly;
                foreach (var type in asm.GetExportedTypes())
                {
                    var prop = type.GetProperty(propertyItem.PropertyName,
                        BindingFlags.Public | BindingFlags.Instance);
                    if (prop == null) continue;

                    foreach (var cad in CustomAttributeData.GetCustomAttributes(prop))
                    {
                        if (cad.AttributeType.Name != "FilePathEditorAttribute") continue;
                        var args = cad.ConstructorArguments;
                        if (args.Count >= 1) filter = args[0].Value as string;
                        if (args.Count >= 2) presets = ReadStringArray(args[1]);
                        if (args.Count >= 3) presetLabels = ReadStringArray(args[2]);
                        break;
                    }
                    if (filter != null || presets != null) break;
                }
            }
            catch { }

            cbx.Configure(filter, presets, presetLabels);
            return cbx;
        }

        private static string[]? ReadStringArray(CustomAttributeTypedArgument arg)
        {
            if (arg.Value is IReadOnlyCollection<CustomAttributeTypedArgument> elems)
                return elems.Select(e => e.Value as string ?? "").ToArray();
            return null;
        }
    }
}
