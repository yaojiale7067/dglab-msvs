using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace DGLab.VS2026
{
    /// <summary>
    /// 同时写入 Visual Studio 输出窗口和本地日志文件，便于排查问题。
    /// </summary>
    public sealed class DGLabLog : IDisposable
    {
        private static readonly object FileLock = new object();
        private readonly IVsOutputWindowPane _pane;
        private readonly string _filePath;
        private bool _disposed;

        public DGLabLog(IVsOutputWindow outputWindow)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            IVsOutputWindowPane pane = null;
            var paneGuid = new Guid(GuidList.OutputPaneString);
            try
            {
                outputWindow?.CreatePane(ref paneGuid, "DGLabVSCode", 1, 0);
                outputWindow?.GetPane(ref paneGuid, out pane);
            }
            catch
            {
                pane = null;
            }

            _pane = pane;

            try
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "DGLabVS2026");
                Directory.CreateDirectory(directory);
                _filePath = Path.Combine(directory, "extension.log");
            }
            catch
            {
                _filePath = null;
            }
        }

        public void Write(string message)
        {
            if (_disposed)
            {
                return;
            }

            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            try
            {
                // OutputStringThreadSafe 明确允许从任意线程调用。
#pragma warning disable VSTHRD010
                _pane?.OutputStringThreadSafe(line + Environment.NewLine);
#pragma warning restore VSTHRD010
            }
            catch
            {
                // 输出窗口可能已经关闭。
            }

            if (_filePath != null)
            {
                try
                {
                    lock (FileLock)
                    {
                        var info = new FileInfo(_filePath);
                        if (info.Exists && info.Length > 4 * 1024 * 1024)
                        {
                            File.Delete(_filePath);
                        }

                        File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch
                {
                    // 日志失败不影响插件功能。
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
