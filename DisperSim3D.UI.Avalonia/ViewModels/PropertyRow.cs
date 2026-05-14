#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DisperSim3D.UI.Avalonia.ViewModels
{
    /// <summary>
    /// What kind of editor control to render for a row. The
    /// <see cref="PropertyEditor"/> view picks one per row at build time based
    /// on the property's CLR type. Adding a new kind is a 3-step process:
    /// add the enum value, add a branch in <c>PropertyRow.For</c>, add the
    /// matching XAML row in <c>PropertyEditor.axaml</c>.
    /// </summary>
    public enum PropertyEditorKind
    {
        None,        // unknown / fallback — shows ToString() in a read-only TextBox
        Text,        // string
        Integer,     // int / long / short
        Number,      // double / float / decimal
        Boolean,     // bool
        EnumChoice,  // any enum
        Point3D,     // DisperSim3D.Geometry.Point3D (3 numeric fields)
        Vector3D,    // DisperSim3D.Geometry.Vector3D (3 numeric fields)
        Color,       // DisperSim3D.Geometry.Color (swatch + hex + RGB flyout)
        FilePath,    // string property with [FilePathEditor] — combo presets + Browse dialog
        ReadOnly     // collections, complex objects — shows a short summary
    }

    /// <summary>
    /// One editable row in the inspector pane. Owns the lambdas that read and
    /// write the underlying property via reflection, so the view never touches
    /// <see cref="PropertyInfo"/> directly.
    /// </summary>
    public sealed class PropertyRow
    {
        public string Name { get; }
        public string Category { get; }
        public string Description { get; }
        public PropertyEditorKind Kind { get; }
        public bool IsReadOnly { get; }

        /// <summary>List of enum value names (only populated for
        /// <see cref="PropertyEditorKind.EnumChoice"/>).</summary>
        public IReadOnlyList<string> EnumOptions { get; }

        public string? FileFilter { get; }
        public IReadOnlyList<string>? FilePresets { get; }
        public IReadOnlyList<string>? FilePresetLabels { get; }

        /// <summary>Reads the current value from the target object.</summary>
        public Func<object?> Getter { get; }

        /// <summary>Writes a new value back to the target object. May throw if
        /// the value is incompatible — callers should catch and revert.</summary>
        public Action<object?> Setter { get; }

        private PropertyRow(string name, string category, string description,
            PropertyEditorKind kind, bool readOnly,
            IReadOnlyList<string> enumOptions,
            string? fileFilter, IReadOnlyList<string>? filePresets,
            IReadOnlyList<string>? filePresetLabels,
            Func<object?> getter, Action<object?> setter)
        {
            Name = name; Category = category; Description = description;
            Kind = kind; IsReadOnly = readOnly;
            EnumOptions = enumOptions;
            FileFilter = fileFilter; FilePresets = filePresets;
            FilePresetLabels = filePresetLabels;
            Getter = getter; Setter = setter;
        }

        /// <summary>Reflects one property on <paramref name="target"/> and
        /// builds the matching row. Returns null when the property is indexed
        /// or otherwise unusable.</summary>
        public static PropertyRow? For(object target, PropertyInfo prop)
        {
            if (prop.GetIndexParameters().Length > 0) return null;
            if (!prop.CanRead) return null;

            string category = prop.GetCustomAttribute<System.ComponentModel.CategoryAttribute>()?.Category ?? "General";
            string desc = prop.GetCustomAttribute<System.ComponentModel.DescriptionAttribute>()?.Description ?? "";
            bool readOnly = !prop.CanWrite;

            Type t = prop.PropertyType;
            Type ut = Nullable.GetUnderlyingType(t) ?? t;

            PropertyEditorKind kind;
            IReadOnlyList<string> enumOpts = Array.Empty<string>();
            string? fileFilter = null;
            IReadOnlyList<string>? filePresets = null;
            IReadOnlyList<string>? filePresetLabels = null;

            bool isFilePath = false;
            if (ut == typeof(string))
            {
                var fpAttr = prop.GetCustomAttribute<DisperSim3D.Models.FilePathEditorAttribute>();
                if (fpAttr != null)
                {
                    isFilePath = true;
                    fileFilter = fpAttr.Filter;
                    filePresets = fpAttr.Presets;
                    filePresetLabels = fpAttr.PresetLabels;
                }
                else
                {
                    foreach (var a in prop.GetCustomAttributes(true))
                    {
                        if (a.GetType().FullName == "DisperSim3D.Models.FilePathEditorAttribute")
                        {
                            isFilePath = true;
                            var at = a.GetType();
                            fileFilter = at.GetProperty("Filter")?.GetValue(a) as string;
                            filePresets = at.GetProperty("Presets")?.GetValue(a) as string[];
                            filePresetLabels = at.GetProperty("PresetLabels")?.GetValue(a) as string[];
                            break;
                        }
                    }
                }
            }

            if (isFilePath) kind = PropertyEditorKind.FilePath;
            else if (ut == typeof(string)) kind = PropertyEditorKind.Text;
            else if (ut == typeof(bool)) kind = PropertyEditorKind.Boolean;
            else if (ut == typeof(int) || ut == typeof(long) || ut == typeof(short) ||
                     ut == typeof(uint) || ut == typeof(ulong) || ut == typeof(ushort) ||
                     ut == typeof(byte) || ut == typeof(sbyte)) kind = PropertyEditorKind.Integer;
            else if (ut == typeof(double) || ut == typeof(float) || ut == typeof(decimal))
                kind = PropertyEditorKind.Number;
            else if (ut.IsEnum)
            {
                kind = PropertyEditorKind.EnumChoice;
                enumOpts = Enum.GetNames(ut);
            }
            else if (ut == typeof(Geometry.Point3D)) kind = PropertyEditorKind.Point3D;
            else if (ut == typeof(Geometry.Vector3D)) kind = PropertyEditorKind.Vector3D;
            else if (ut == typeof(Geometry.Color)) kind = PropertyEditorKind.Color;
            else kind = PropertyEditorKind.ReadOnly;

            return new PropertyRow(
                prop.Name, category, desc, kind, readOnly, enumOpts,
                fileFilter, filePresets, filePresetLabels,
                getter: () => { try { return prop.GetValue(target); } catch { return null; } },
                setter: v =>
                {
                    if (!prop.CanWrite) return;
                    object? coerced = Coerce(v, t);
                    prop.SetValue(target, coerced);
                });
        }

        /// <summary>Converts a UI-supplied value (typically string / double /
        /// bool / int from an editor control) into the property's CLR type.
        /// Returns null on failure — callers should swallow and revert.</summary>
        private static object? Coerce(object? raw, Type targetType)
        {
            if (raw == null) return null;
            Type ut = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (ut.IsInstanceOfType(raw)) return raw;
            if (ut == typeof(string)) return raw.ToString();
            if (ut.IsEnum && raw is string s) return Enum.Parse(ut, s);
            if (ut == typeof(bool) && raw is bool b) return b;
            try
            {
                return Convert.ChangeType(raw, ut, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                return null;
            }
        }
    }
}
