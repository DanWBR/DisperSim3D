using System;

namespace DisperSim3D.Models
{
    [AttributeUsage(AttributeTargets.Property)]
    public class FilePathEditorAttribute : Attribute
    {
        public string Filter { get; }
        public string[] Presets { get; }
        public string[] PresetLabels { get; }

        public FilePathEditorAttribute(string filter, string[] presets, string[] presetLabels)
        {
            Filter = filter;
            Presets = presets;
            PresetLabels = presetLabels;
        }
    }
}
