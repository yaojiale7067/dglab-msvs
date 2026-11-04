using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DGLab.VS2026
{
    /// <summary>
    /// 插件运行时使用的不可变配置快照。
    /// 对应原 VSCode 插件的 dglabvscode.* 与 extension.onDidReceiveDebugSessionCustomEvent 配置。
    /// </summary>
    public sealed class DGLabSettings
    {
        private readonly HashSet<string> _blacklist;

        public DGLabSettings(
            int strength,
            string pulseName,
            int heartbeatInterval,
            int maxAddInterval,
            int onStartDebugSession,
            int onTerminateDebugSession,
            int onDebugError,
            int onBreakpointsChanged,
            int onSaveDocument,
            int codingMaxFrequency,
            ChannelMode channelMode,
            IEnumerable<string> blacklist,
            double editOutputDuration = 0.3,
            double eventOutputDuration = 0.6,
            double saveOutputDuration = 2.0,
            double fireStrengthHoldDuration = 0.6)
        {
            Strength = Math.Max(0, strength);
            PulseName = string.IsNullOrWhiteSpace(pulseName) ? "快速按捏" : pulseName.Trim();
            HeartbeatInterval = Math.Max(1, heartbeatInterval);
            MaxAddInterval = Math.Max(0, maxAddInterval);
            OnStartDebugSession = Math.Max(0, onStartDebugSession);
            OnTerminateDebugSession = Math.Max(0, onTerminateDebugSession);
            OnDebugError = Math.Max(0, onDebugError);
            OnBreakpointsChanged = Math.Max(0, onBreakpointsChanged);
            OnSaveDocument = Math.Max(0, onSaveDocument);
            CodingMaxFrequency = Math.Max(0, codingMaxFrequency);
            ChannelMode = channelMode;
            EditOutputDuration = ClampDuration(editOutputDuration, 0.3);
            EventOutputDuration = ClampDuration(eventOutputDuration, 0.6);
            SaveOutputDuration = ClampDuration(saveOutputDuration, 2.0);
            FireStrengthHoldDuration = ClampDuration(fireStrengthHoldDuration, 0.6);
            _blacklist = new HashSet<string>(
                (blacklist ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(NormalizeExtension),
                StringComparer.OrdinalIgnoreCase);
        }

        public int Strength { get; }

        public string PulseName { get; }

        public int HeartbeatInterval { get; }

        public int MaxAddInterval { get; }

        public int OnStartDebugSession { get; }

        public int OnTerminateDebugSession { get; }

        /// <summary>原 onDidReceiveDebugSessionCustomEvent：调试输出 stderr / 调试报错时开火。</summary>
        public int OnDebugError { get; }

        public int OnBreakpointsChanged { get; }

        public int OnSaveDocument { get; }

        public int CodingMaxFrequency { get; }

        public ChannelMode ChannelMode { get; }

        /// <summary>编辑代码时单次波形的持续时间（秒）。</summary>
        public double EditOutputDuration { get; }

        /// <summary>调试开始/结束、调试报错、断点变化时的波形持续时间（秒）。</summary>
        public double EventOutputDuration { get; }

        /// <summary>保存文件时的波形持续时间（秒）。</summary>
        public double SaveOutputDuration { get; }

        /// <summary>一键开火时临时强度保持多久后恢复（秒）。</summary>
        public double FireStrengthHoldDuration { get; }

        public IReadOnlyCollection<string> Blacklist => _blacklist;

        public bool IsBlacklistedPath(string path)
        {
            var extension = GetExtension(path);
            return extension.Length > 0 && _blacklist.Contains(extension);
        }

        public bool IsBlacklistedExtension(string extension)
        {
            return !string.IsNullOrEmpty(extension) && _blacklist.Contains(NormalizeExtension(extension));
        }

        public static string GetExtension(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var fileName = Path.GetFileName(path);
            var dot = fileName.LastIndexOf('.');
            if (dot < 0 || dot == fileName.Length - 1)
            {
                return string.Empty;
            }

            return fileName.Substring(dot + 1);
        }

        public static string NormalizeExtension(string extension)
        {
            if (extension == null)
            {
                return string.Empty;
            }

            return extension.Trim().TrimStart('.');
        }

        private static double ClampDuration(double value, double fallback)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            {
                return fallback;
            }

            return Math.Min(30.0, Math.Max(0.05, value));
        }
    }
}
