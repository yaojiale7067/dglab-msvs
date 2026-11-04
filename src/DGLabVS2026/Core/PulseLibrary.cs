using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace DGLab.VS2026
{
    /// <summary>
    /// 波形库。移植自原项目 wave.json（PULSE_DATA）。
    /// 每一帧由两行四列 0-100 的强度值组成，发送前按原逻辑扁平化为 16 位大写十六进制字符串。
    /// </summary>
    public sealed class PulseLibrary
    {
        public const string ResourceName = "DGLab.VS2026.wave.json";

        private readonly Dictionary<string, int[][][]> _pulses;

        private PulseLibrary(Dictionary<string, int[][][]> pulses)
        {
            _pulses = pulses;
        }

        public IReadOnlyCollection<string> Names => _pulses.Keys;

        public int Count => _pulses.Count;

        public static PulseLibrary Load()
        {
            var assembly = typeof(PulseLibrary).GetTypeInfo().Assembly;
            using (var stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("未找到内嵌波形资源: " + ResourceName);
                }

                using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                {
                    return Parse(reader.ReadToEnd());
                }
            }
        }

        public static PulseLibrary Parse(string json)
        {
            var serializer = new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 512
            };

            var root = serializer.Deserialize<Dictionary<string, object>>(json);
            if (root == null || !root.TryGetValue("PULSE_DATA", out var pulseDataObject))
            {
                throw new InvalidDataException("wave.json 缺少 PULSE_DATA");
            }

            var pulseData = pulseDataObject as Dictionary<string, object>;
            if (pulseData == null)
            {
                throw new InvalidDataException("wave.json 的 PULSE_DATA 格式不正确");
            }

            var pulses = new Dictionary<string, int[][][]>(StringComparer.Ordinal);
            foreach (var pair in pulseData)
            {
                var frames = AsArray(pair.Value);
                if (frames == null)
                {
                    continue;
                }

                var converted = new int[frames.Length][][];
                for (var frameIndex = 0; frameIndex < frames.Length; frameIndex++)
                {
                    var rows = AsArray(frames[frameIndex]);
                    if (rows == null)
                    {
                        continue;
                    }

                    var convertedRows = new int[rows.Length][];
                    for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
                    {
                        var numbers = AsArray(rows[rowIndex]);
                        if (numbers == null)
                        {
                            continue;
                        }

                        convertedRows[rowIndex] = numbers
                            .Select(n => Convert.ToInt32(n))
                            .ToArray();
                    }

                    converted[frameIndex] = convertedRows;
                }

                pulses[pair.Key] = converted;
            }

            if (pulses.Count == 0)
            {
                throw new InvalidDataException("wave.json 中没有可用波形");
            }

            return new PulseLibrary(pulses);
        }

        private static object[] AsArray(object value)
        {
            if (value is object[] array)
            {
                return array;
            }

            if (value is string)
            {
                return null;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                var items = new List<object>();
                foreach (var item in enumerable)
                {
                    items.Add(item);
                }

                return items.ToArray();
            }

            return null;
        }

        public bool Contains(string name)
        {
            return !string.IsNullOrEmpty(name) && _pulses.ContainsKey(name);
        }

        /// <summary>
        /// 与 VSCode 版 convertToHexString 完全一致的十六进制编码。
        /// </summary>
        public string[] GetHexWave(string name)
        {
            if (!_pulses.TryGetValue(name, out var frames))
            {
                return null;
            }

            var result = new string[frames.Length];
            for (var i = 0; i < frames.Length; i++)
            {
                var frame = frames[i];
                if (frame == null)
                {
                    result[i] = string.Empty;
                    continue;
                }

                var builder = new StringBuilder(16);
                foreach (var row in frame)
                {
                    if (row == null)
                    {
                        continue;
                    }

                    foreach (var value in row)
                    {
                        builder.Append(value.ToString("X2"));
                    }
                }

                result[i] = builder.ToString();
            }

            return result;
        }
    }
}
