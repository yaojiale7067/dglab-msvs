using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;

namespace DGLab.VS2026
{
    /// <summary>
    /// 对应原 VSCode 配置 dglabvscode.messageSendOption。
    /// </summary>
    [TypeConverter(typeof(ChannelModeConverter))]
    public enum ChannelMode
    {
        [Description("AB一起输出")]
        AB = 0,

        [Description("仅A通道输出")]
        AOnly = 1,

        [Description("仅B通道输出")]
        BOnly = 2
    }

    /// <summary>
    /// 让属性网格显示中文通道名称，同时保持枚举持久化稳定。
    /// </summary>
    public sealed class ChannelModeConverter : EnumConverter
    {
        public ChannelModeConverter(Type type)
            : base(type)
        {
        }

        public override object ConvertTo(ITypeDescriptorContext context, CultureInfo culture, object value, Type destinationType)
        {
            if (destinationType == typeof(string) && value != null)
            {
                var description = GetDescription(value);
                if (description != null)
                {
                    return description;
                }
            }

            return base.ConvertTo(context, culture, value, destinationType);
        }

        public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
        {
            var text = value as string;
            if (text != null)
            {
                foreach (var field in EnumType.GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var description = field.GetCustomAttributes(typeof(DescriptionAttribute), false)
                                           .OfType<DescriptionAttribute>()
                                           .FirstOrDefault();
                    if (description != null && string.Equals(description.Description, text, StringComparison.CurrentCulture))
                    {
                        return field.GetValue(null);
                    }

                    if (string.Equals(field.Name, text, StringComparison.OrdinalIgnoreCase))
                    {
                        return field.GetValue(null);
                    }
                }
            }

            return base.ConvertFrom(context, culture, value);
        }

        private static string GetDescription(object value)
        {
            var field = value.GetType().GetField(value.ToString());
            var description = field == null
                ? null
                : field.GetCustomAttributes(typeof(DescriptionAttribute), false)
                       .OfType<DescriptionAttribute>()
                       .FirstOrDefault();
            return description?.Description;
        }
    }
}
