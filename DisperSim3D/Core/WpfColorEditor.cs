using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using WpfColor = System.Windows.Media.Color;
using DrawColor = System.Drawing.Color;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Bridges WPF <see cref="WpfColor"/> to the WinForms PropertyGrid by opening the
    /// standard ColorDialog when the property is clicked. Without this the grid shows
    /// the WPF colour as plain text and offers no editor.
    /// </summary>
    public class WpfColorEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
            => UITypeEditorEditStyle.Modal;

        public override bool GetPaintValueSupported(ITypeDescriptorContext context) => true;

        public override void PaintValue(PaintValueEventArgs e)
        {
            if (e.Value is WpfColor c)
            {
                using (var brush = new System.Drawing.SolidBrush(DrawColor.FromArgb(c.A, c.R, c.G, c.B)))
                    e.Graphics.FillRectangle(brush, e.Bounds);
            }
        }

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            using (var dlg = new ColorDialog { FullOpen = true, AnyColor = true })
            {
                if (value is WpfColor c)
                    dlg.Color = DrawColor.FromArgb(c.A, c.R, c.G, c.B);
                if (dlg.ShowDialog() == DialogResult.OK)
                    return WpfColor.FromArgb(dlg.Color.A, dlg.Color.R, dlg.Color.G, dlg.Color.B);
            }
            return value;
        }
    }

    /// <summary>
    /// Converts a WPF <see cref="WpfColor"/> to/from a "#AARRGGBB" string so the
    /// PropertyGrid displays something readable next to the colour swatch.
    /// </summary>
    public class WpfColorConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
            {
                try { return (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(s); }
                catch { }
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture,
            object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is WpfColor c)
                return string.Format("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B);
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
