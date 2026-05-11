using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Media.Media3D;

namespace DisperSim3D.Core
{
    /// <summary>
    /// Converts <see cref="Point3D"/> to/from "X;Y;Z" strings using invariant culture.
    /// The default WPF Point3DConverter expects "X,Y,Z" but on pt-BR (comma = decimal
    /// separator) that breaks — "5,5,0" gets parsed as two values 5.5 and 0 instead of
    /// three. This converter uses ';' as the unambiguous separator and accepts ',' /
    /// space as fallback.
    /// </summary>
    public class Point3DStringConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            => sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);

        public override bool CanConvertTo(ITypeDescriptorContext context, Type destinationType)
            => destinationType == typeof(string) || base.CanConvertTo(context, destinationType);

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            if (value is string s)
            {
                // Accept ';', ',' and whitespace as separators. Invariant culture for the
                // parse so "5.5" works regardless of OS locale.
                var parts = s.Split(new[] { ';', ',', ' ', '\t' },
                    StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 3 &&
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) &&
                    double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                {
                    return new Point3D(x, y, z);
                }
                throw new FormatException(
                    "Position must be three numbers separated by ';' (or ',' / space). Got: " + s);
            }
            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture,
            object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is Point3D p)
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "{0:G}; {1:G}; {2:G}", p.X, p.Y, p.Z);
            }
            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
