using System;
using System.ComponentModel;
using System.Globalization;

namespace mRemoteNG.Tools
{
    public class EnumTypeConverter(Type type) : EnumConverter(type)
    {
        private readonly Type _enumType = type;

        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destType)
        {
            return destType == typeof(string);
        }

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type? destType)
        {
            if (value == null) return string.Empty;

            string? enumName = Enum.GetName(_enumType, value);
            if (enumName == null)
            {
                throw new ArgumentException("Invalid enum value provided.");
            }

            System.Reflection.FieldInfo? fi = _enumType.GetField(enumName);
            if (fi == null)
            {
                throw new ArgumentException("FieldInfo could not be retrieved for the provided enum value.");
            }

            DescriptionAttribute? dna = (DescriptionAttribute?)Attribute.GetCustomAttribute(fi, typeof(DescriptionAttribute));
            return dna?.Description ?? value.ToString() ?? string.Empty;
        }

        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type? srcType)
        {
            return srcType == typeof(string);
        }

        public override object ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
        {
            if (value is string stringValue)
            {
                foreach (System.Reflection.FieldInfo fi in _enumType.GetFields())
                {
                    DescriptionAttribute? dna = (DescriptionAttribute?)Attribute.GetCustomAttribute(fi, typeof(DescriptionAttribute));

                    if (dna != null && string.Equals(stringValue, dna.Description, StringComparison.Ordinal))
                    {
                        return Enum.Parse(_enumType, fi.Name);
                    }
                }

                return Enum.Parse(_enumType, stringValue);
            }

            throw new ArgumentNullException(nameof(value), "Value cannot be null.");
        }
    }
}
