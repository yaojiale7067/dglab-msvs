using System;
using System.Collections.Generic;
using System.Globalization;
using System.Web.Script.Serialization;

namespace DGLab.VS2026
{
    /// <summary>
    /// 轻量 JSON 辅助类。使用 .NET Framework 自带的 JavaScriptSerializer，
    /// 避免把第三方 JSON 程序集打进 VSIX 造成与 Visual Studio 宿主的版本冲突。
    /// </summary>
    internal static class Json
    {
        private static readonly object SyncRoot = new object();
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer
        {
            MaxJsonLength = int.MaxValue,
            RecursionLimit = 512
        };

        public static string Serialize(object value)
        {
            lock (SyncRoot)
            {
                return Serializer.Serialize(value);
            }
        }

        public static Dictionary<string, object> DeserializeObject(string json)
        {
            lock (SyncRoot)
            {
                return Serializer.Deserialize<Dictionary<string, object>>(json);
            }
        }

        public static string GetString(Dictionary<string, object> message, string key)
        {
            if (message != null && message.TryGetValue(key, out var value) && value != null)
            {
                return value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture);
            }

            return null;
        }

        public static int GetInt(Dictionary<string, object> message, string key, int defaultValue = 0)
        {
            if (message != null && message.TryGetValue(key, out var value) && value != null)
            {
                try
                {
                    return Convert.ToInt32(value, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public static string Truncate(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }

            return text.Substring(0, maxLength) + "...";
        }
    }
}
