using System;
using System.Text;
using System.Windows.Threading;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace DGLab.VS2026
{
    /// <summary>
    /// Visual Studio 的 EnvDTE 没有公开断点集合变化事件，因此以低频率轮询断点签名。
    /// 仅在签名（数量、位置、启用状态、条件）变化时触发一次事件。
    /// </summary>
    public sealed class BreakpointWatcher : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly DispatcherTimer _timer;
        private string _lastSignature;
        private bool _disposed;

        public BreakpointWatcher(DTE2 dte, TimeSpan interval)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _lastSignature = CaptureSignature();

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = interval
            };
            _timer.Tick += OnTick;
            _timer.Start();
        }

        public event EventHandler Changed;

        private void OnTick(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_disposed)
            {
                return;
            }

            var signature = CaptureSignature();
            if (!string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                _lastSignature = signature;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private string CaptureSignature()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var builder = new StringBuilder();

            try
            {
                var breakpoints = _dte.Debugger?.Breakpoints;
                if (breakpoints == null)
                {
                    return string.Empty;
                }

                foreach (Breakpoint breakpoint in breakpoints)
                {
                    try
                    {
                        builder.Append(breakpoint.Enabled).Append('|');
                        builder.Append(breakpoint.LocationType).Append('|');

                        if (breakpoint.LocationType == dbgBreakpointLocationType.dbgBreakpointLocationTypeFile)
                        {
                            builder.Append(breakpoint.File).Append('|');
                            builder.Append(breakpoint.FileLine).Append('|');
                            builder.Append(breakpoint.FileColumn).Append('|');
                        }
                        else
                        {
                            builder.Append(breakpoint.FunctionName).Append('|');
                        }

                        builder.Append(breakpoint.Condition).Append('|');
                        builder.Append(breakpoint.Name).Append(';');
                    }
                    catch
                    {
                        builder.Append("?;");
                    }
                }
            }
            catch
            {
                // 调试器尚未初始化或正在关闭，忽略本轮。
            }

            return builder.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;
        }
    }
}
