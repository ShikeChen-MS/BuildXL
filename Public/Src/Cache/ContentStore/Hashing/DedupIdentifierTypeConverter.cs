using System;
using System.ComponentModel;
using System.Globalization;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Hashing
{
    public class DedupIdentifierTypeConverter: TypeConverter
    {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType)
        {
            if (sourceType == typeof(string))
            {
                return true;
            }

            return base.CanConvertFrom(context, sourceType);
        }

        // Overrides the ConvertFrom method of TypeConverter.
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value)
        {
            if (value is string valueAsString)
            {
                return DedupIdentifier.Create(valueAsString);
            }

            return base.ConvertFrom(context, culture, value);
        }

        // Overrides the ConvertTo method of TypeConverter.
        public override object? ConvertTo(ITypeDescriptorContext? context, CultureInfo? culture, object? value, Type destinationType)
        {
            if (destinationType == typeof(string) && value is not null)
            {
                return ((DedupIdentifier)value).ValueString;
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }
    }
}
