using System;
using System.ComponentModel;
using System.Globalization;
using mRemoteNG.Resources.Language;

namespace mRemoteNG.Tools
{
    public class YesNoTypeConverter : TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }
        public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType)
        {
            return destinationType == typeof(string) || base.CanConvertTo(context, destinationType);
        }

        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object? value)
        {
            if (value is not string stringValue)
            {
                // Ensure 'value' is not null before passing it to the base method
                return value != null
                    ? base.ConvertFrom(context, culture, value)
                    : throw new ArgumentNullException(nameof(value), "Value cannot be null.");
            }

            if (string.Equals(stringValue, Language.Yes, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }

            if (string.Equals(stringValue, Language.No, StringComparison.CurrentCultureIgnoreCase))
            {
                return false;
            }

            throw new Exception("Values must be \"Yes\" or \"No\"");
        }

        public override object ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string))
            {
                return Convert.ToBoolean(value) ? Language.Yes : Language.No;
            }

            return base.ConvertTo(context, culture, value, destinationType) ?? throw new InvalidOperationException("Base conversion returned null.");
        }

        public override bool GetStandardValuesSupported(ITypeDescriptorContext? context)
        {
            return true;
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext? context)
        {
            bool[] bools = { true, false };

            StandardValuesCollection svc = new(bools);

            return svc;
        }
    }
}
