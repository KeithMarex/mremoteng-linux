using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;

namespace mRemoteNG.Tools
{
    public class TabColorConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(string) || sourceType == typeof(Color) || base.CanConvertFrom(context, sourceType);
        }

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        {
            return destinationType == typeof(string) || destinationType == typeof(Color) || base.CanConvertTo(context, destinationType);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
        {
            if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
            {
                return string.Empty;
            }

            if (value is string stringValue)
            {
                return stringValue;
            }

            if (value is Color colorValue)
            {
                // An empty color means "no color set" and must stay an empty string.
                if (colorValue.IsEmpty)
                {
                    return string.Empty;
                }

                // Convert Color to string representation
                // Use named color if it's a known color, otherwise use hex format
                if (colorValue.IsNamedColor)
                {
                    return colorValue.Name;
                }
                else
                {
                    // Return hex format without alpha if fully opaque, otherwise include alpha
                    if (colorValue.A == 255)
                    {
                        return $"#{colorValue.R:X2}{colorValue.G:X2}{colorValue.B:X2}";
                    }
                    else
                    {
                        return $"#{colorValue.A:X2}{colorValue.R:X2}{colorValue.G:X2}{colorValue.B:X2}";
                    }
                }
            }

            return base.ConvertFrom(context, culture, value);
        }

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string))
            {
                if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
                {
                    return string.Empty;
                }
                return value.ToString() ?? string.Empty;
            }

            if (destinationType == typeof(Color))
            {
                if (value == null || (value is string str && string.IsNullOrWhiteSpace(str)))
                {
                    return Color.Empty;
                }

                if (value is string stringValue)
                {
                    try
                    {
                        ColorConverter converter = new ColorConverter();
                        return converter.ConvertFromString(stringValue) ?? Color.Empty;
                    }
                    catch
                    {
                        return Color.Empty;
                    }
                }
            }

            return base.ConvertTo(context, culture, value, destinationType) ?? throw new InvalidOperationException("Base conversion returned null.");
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            // Provide a list of common colors for the dropdown. The values must be
            // strings: the annotated properties are strings, and the grid commits the
            // picked standard value without running it through this converter.
            Color[] colors =
            [
                Color.Red,
                Color.Orange,
                Color.Yellow,
                Color.Green,
                Color.Blue,
                Color.Purple,
                Color.Pink,
                Color.Brown,
                Color.Black,
                Color.White,
                Color.Gray,
                Color.LightGray,
                Color.DarkGray,
                Color.Cyan,
                Color.Magenta,
                Color.Lime,
                Color.Navy,
                Color.Teal,
                Color.Maroon,
                Color.Olive
            ];

            return new StandardValuesCollection(Array.ConvertAll(colors, color => color.Name));
        }

        public override bool GetStandardValuesExclusive(ITypeDescriptorContext? context)
        {
            // Return false to allow custom values (hex codes or other color names)
            return false;
        }
    }
}
