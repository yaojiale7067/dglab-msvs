using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Task = System.Threading.Tasks.Task;

namespace DGLab.VS2026
{
    /// <summary>
    /// Visual Studio 2026 插件入口。
    /// 功能对应 lxyddice/DGLabVSCode：
    ///  - 开始连接 / 刷新配置 / 设置波形名称
    ///  - 二维码绑定
    ///  - 编辑代码、保存文件、调试开始/结束、调试报错、断点变化触发脉冲
    ///  - 心跳、强度渐增、A/B/AB 通道、后缀名黑名单
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("DGLabVSCode", "郊狼联动的 Visual Studio 2026 插件", "0.1.0")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(DGLabOptionsPage), "DGLab", "郊狼插件配置", 0, 0, true)]
    [ProvideToolWindow(typeof(QrCodeToolWindow), Style = VsDockStyle.Float, Width = 460, Height = 720)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuidString)]
    public sealed class DGLabPackage : AsyncPackage, IDGLabHost
    {
        public const string PackageGuidString = GuidList.PackageString;

        private const string QrUrlPrefix =
            "https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#wss://ws.dungeon-lab.cn/";

        private DGLabLog _log;
        private DGLabClient _client;
        private volatile DGLabSettings _settings;
        private DTE2 _dte;
        private DebuggerEvents _debuggerEvents;
        private DocumentEvents _documentEvents;
        private BreakpointWatcher _breakpointWatcher;
        private IVsStatusbar _statusBar;
        private QrCodeToolWindow _qrWindow;
        private string _lastQrUrl;
        private string _lastBlacklistLogPath;
        private bool _wasDebugging;
        private bool _disposed;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _lastClearChangeTicks;
        private int _changeCount;

        public static DGLabPackage Instance { get; private set; }

        public DGLabSettings Settings => _settings;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            Instance = this;

            var outputWindow = await GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;
            _log = new DGLabLog(outputWindow);
            _statusBar = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;

            ReloadSettings();
            _client = new DGLabClient(this);

            _dte = await GetServiceAsync(typeof(DTE)) as DTE2;
            HookDteEvents();

            if (_dte != null)
            {
                _breakpointWatcher = new BreakpointWatcher(_dte, TimeSpan.FromSeconds(1));
                _breakpointWatcher.Changed += OnBreakpointsChanged;
            }
            else
            {
                _log.Write("无法获取 DTE，调试/保存/断点事件不可用");
            }

            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            RegisterCommands(commandService);

            _log.Write("DGLabVSCode for Visual Studio 2026 已加载");
            _log.Write($"波形库: {_client.PulseNames.Count} 个；基础强度: {_settings.Strength}；波形: {_settings.PulseName}");
        }

        private void RegisterCommands(OleMenuCommandService commandService)
        {
            if (commandService == null)
            {
                _log?.Write("无法获取 IMenuCommandService，命令未注册");
                return;
            }

            commandService.AddCommand(new MenuCommand(ExecuteConnect, new CommandID(GuidList.CommandSet, CommandIds.Connect)));
            commandService.AddCommand(new MenuCommand(ExecuteRefresh, new CommandID(GuidList.CommandSet, CommandIds.Refresh)));
            commandService.AddCommand(new MenuCommand(ExecuteSetPulseName, new CommandID(GuidList.CommandSet, CommandIds.SetPulseName)));
            commandService.AddCommand(new MenuCommand(ExecuteShowQr, new CommandID(GuidList.CommandSet, CommandIds.ShowQr)));
        }

        private void HookDteEvents()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_dte == null)
            {
                return;
            }

            try
            {
                _debuggerEvents = _dte.Events.DebuggerEvents;
                _debuggerEvents.OnEnterRunMode += OnEnterRunMode;
                _debuggerEvents.OnEnterDesignMode += OnEnterDesignMode;
                _debuggerEvents.OnEnterBreakMode += OnEnterBreakMode;

                _documentEvents = _dte.Events.DocumentEvents;
                _documentEvents.DocumentSaved += OnDocumentSaved;
            }
            catch (Exception ex)
            {
                _log?.Write("挂接 DTE 事件失败: " + ex.Message);
            }
        }

        #region Commands

        private void ExecuteConnect(object sender, EventArgs e)
        {
            ExecuteConnectCommand();
        }

        internal void ExecuteConnectCommand()
        {
            if (_disposed || _client == null)
            {
                return;
            }

            _ = JoinableTaskFactory.RunAsync(ConnectAsync);
        }

        private async Task ConnectAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            try
            {
                _log.Write("开始连接 WebSocket...");
                var clientId = await _client.ConnectAsync(DisposalToken);
                if (string.IsNullOrEmpty(clientId))
                {
                    throw new InvalidOperationException("服务器没有返回 clientId");
                }

                var url = QrUrlPrefix + clientId;
                _lastQrUrl = url;
                await ShowQrWindowAsync(url);
                _log.Write("二维码已生成: " + url);
            }
            catch (OperationCanceledException) when (DisposalToken.IsCancellationRequested)
            {
                // Visual Studio 正在关闭。
            }
            catch (Exception ex)
            {
                _log.Write("WebSocket 连接失败: " + ex);
                ShowError("WebSocket 连接失败", ex.Message);
            }
        }

        private void ExecuteRefresh(object sender, EventArgs e)
        {
            _ = JoinableTaskFactory.RunAsync(RefreshAsync);
        }

        private async Task RefreshAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);

            if (_client.ConnectionCount == 0)
            {
                ShowError("DGLab", "还没有创建连接喵");
                return;
            }

            _log.Write("成功刷新配置");
            await _client.RefreshAsync();
        }

        private void ExecuteSetPulseName(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var dialog = new PromptDialog("设置波形名称", "请输入波形名称", _client.PulseName);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var pulseName = dialog.Value;
            if (string.IsNullOrEmpty(pulseName))
            {
                return;
            }

            if (!_client.SetPulseName(pulseName))
            {
                ShowError("设置波形失败", $"波形名称“{pulseName}”不存在。\n可用波形：{string.Join("、", _client.PulseNames)}");
                return;
            }

            try
            {
                var page = (DGLabOptionsPage)GetDialogPage(typeof(DGLabOptionsPage));
                page.PulseName = pulseName;
                page.SaveSettingsToStorage();
                ReloadSettings();
            }
            catch (Exception ex)
            {
                _log.Write("保存波形名称到配置失败: " + ex.Message);
            }

            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                if (_client.ConnectionCount > 0)
                {
                    await _client.ApplySettingsAsync();
                }
            });

            _log.Write($"成功设置波形为 {pulseName}");
        }

        private void ExecuteShowQr(object sender, EventArgs e)
        {
            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                if (string.IsNullOrEmpty(_lastQrUrl))
                {
                    ShowError("DGLab", "还没有创建连接喵");
                    return;
                }

                await ShowQrWindowAsync(_lastQrUrl);
            });
        }

        private async Task ShowQrWindowAsync(string url)
        {
            try
            {
                var window = await ShowToolWindowAsync(typeof(QrCodeToolWindow), 0, true, DisposalToken);
                if (window is QrCodeToolWindow qrWindow)
                {
                    _qrWindow = qrWindow;
                    qrWindow.SetUrl(url, _client);
                }
            }
            catch (Exception ex)
            {
                _log.Write("显示二维码窗口失败: " + ex.Message);
                ShowError("显示二维码窗口失败", ex.Message);
            }
        }

        #endregion

        #region Configuration

        public void OnOptionsChanged()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ReloadSettings();
            _client?.RestartHeartbeat();
            _log?.Write("配置已更新");
        }

        private void ReloadSettings()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                var page = (DGLabOptionsPage)GetDialogPage(typeof(DGLabOptionsPage));
                var settings = page.ToSettings();
                _settings = settings;

                if (settings.HeartbeatInterval < 10)
                {
                    _log?.Write("心跳间隔过短，可能导致快速被电（");
                }
            }
            catch (Exception ex)
            {
                _log?.Write("读取配置失败: " + ex.Message);
                _settings = new DGLabSettings(5, "快速按捏", 30, 20, 15, 15, 20, 10, 12, 20, ChannelMode.AB, null);
            }
        }

        #endregion

        #region Text / Document events

        internal void OnTextBufferChanged(IWpfTextView textView, string filePath, TextContentChangedEventArgs e)
        {
            if (_disposed || textView == null || e?.Changes == null || e.Changes.Count == 0)
            {
                return;
            }

            if (!ThreadHelper.CheckAccess())
            {
                return;
            }

            var settings = _settings;
            var client = _client;
            if (settings == null || client == null || client.ConnectionCount == 0)
            {
                return;
            }

            // 只处理当前拥有键盘焦点的编辑器，对应 VSCode 的 activeTextEditor。
            if (!textView.HasAggregateFocus)
            {
                return;
            }

            if (!string.IsNullOrEmpty(filePath) && settings.IsBlacklistedPath(filePath))
            {
                if (!string.Equals(_lastBlacklistLogPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastBlacklistLogPath = filePath;
                    _log.Write($"文件 {filePath} 在黑名单中，忽略编辑事件");
                }

                return;
            }

            _lastBlacklistLogPath = null;

            foreach (var change in e.Changes)
            {
                var now = _clock.ElapsedMilliseconds;
                if (now - _lastClearChangeTicks < 20000 && _changeCount > settings.CodingMaxFrequency)
                {
                    _ = client.ClearAllWaveAsync();
                    _changeCount = 0;
                    _lastClearChangeTicks = now;
                }

                _changeCount++;
                _ = client.SendWaveAsync("this", 0.3);
            }
        }

        private void OnDocumentSaved(Document document)
        {
            if (_disposed)
            {
                return;
            }

            var settings = _settings;
            var client = _client;
            if (settings == null || client == null || client.ConnectionCount == 0)
            {
                return;
            }

            var path = GetDocumentPath(document);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (settings.IsBlacklistedPath(path))
            {
                _log.Write($"文件 {path} 在黑名单中，忽略保存事件");
                return;
            }

            var activePath = GetActiveDocumentPath();
            if (!string.IsNullOrEmpty(activePath) && settings.IsBlacklistedPath(activePath))
            {
                _log.Write($"文件 {activePath} 在黑名单中，忽略保存事件");
                return;
            }

            _log.Write($"保存文件: {path}");
            _ = client.SendFireAsync(settings.OnSaveDocument, 2);
        }

        #endregion

        #region Debug events

        private void OnEnterRunMode(dbgEventReason reason)
        {
            if (_wasDebugging)
            {
                return;
            }

            _wasDebugging = true;
            FireDebugEvent(_settings?.OnStartDebugSession ?? 0, "调试开始");
        }

        private void OnEnterDesignMode(dbgEventReason reason)
        {
            if (!_wasDebugging)
            {
                return;
            }

            _wasDebugging = false;
            FireDebugEvent(_settings?.OnTerminateDebugSession ?? 0, "调试结束");
        }

        private void OnEnterBreakMode(dbgEventReason reason, ref dbgExecutionAction executionAction)
        {
            if (reason != dbgEventReason.dbgEventReasonExceptionThrown &&
                reason != dbgEventReason.dbgEventReasonExceptionNotHandled)
            {
                return;
            }

            FireDebugEvent(_settings?.OnDebugError ?? 0, "调试报错");
        }

        private void OnBreakpointsChanged(object sender, EventArgs e)
        {
            var settings = _settings;
            var client = _client;
            if (settings == null || client == null || client.ConnectionCount == 0)
            {
                return;
            }

            if (IsActiveDocumentBlacklisted(settings))
            {
                return;
            }

            _log.Write("断点发生变化");
            _ = client.SendFireAsync(settings.OnBreakpointsChanged, 0.6);
        }

        private void FireDebugEvent(int strength, string reason)
        {
            var settings = _settings;
            var client = _client;
            if (settings == null || client == null || client.ConnectionCount == 0)
            {
                return;
            }

            if (IsActiveDocumentBlacklisted(settings))
            {
                _log.Write($"{reason}，但当前文件在黑名单中，忽略");
                return;
            }

            _log.Write($"{reason}，发送一键开火，强度 {strength}");
            _ = client.SendFireAsync(strength, 0.6);
        }

        private bool IsActiveDocumentBlacklisted(DGLabSettings settings)
        {
            var activePath = GetActiveDocumentPath();
            return !string.IsNullOrEmpty(activePath) && settings.IsBlacklistedPath(activePath);
        }

        private string GetActiveDocumentPath()
        {
            try
            {
                return _dte?.ActiveDocument?.FullName;
            }
            catch
            {
                return null;
            }
        }

        private static string GetDocumentPath(Document document)
        {
            try
            {
                return document?.FullName;
            }
            catch
            {
                return null;
            }
        }

        #endregion

        #region IDGLabHost

        DGLabSettings IDGLabHost.Settings => _settings;

        void IDGLabHost.Log(string message)
        {
            _log?.Write(message);
        }

        void IDGLabHost.Notify(string message)
        {
            _log?.Write(message);
            SetStatus(message);
        }

        void IDGLabHost.NotifyError(string message)
        {
            _log?.Write("错误: " + message);
            ShowError("DGLab", message);
        }

        void IDGLabHost.OnConnectionStateChanged(bool connected)
        {
            if (_disposed)
            {
                return;
            }

            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                _statusBar?.SetText(connected ? "DGLab: 已连接" : "DGLab: 未连接");
                _qrWindow?.UpdateStatus(connected
                    ? "WebSocket 已连接，等待郊狼 App 扫码绑定..."
                    : "WebSocket 已断开。使用“开始连接”重新生成二维码。");
            });
        }

        void IDGLabHost.OnDeviceConnected(string clientId, string targetId)
        {
            _log?.Write($"成功连接到 ClientId:{clientId}，targetId:{targetId}");

            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                _statusBar?.SetText($"DGLab: 已绑定设备 {targetId}");
                _qrWindow?.UpdateStatus($"已连接设备：{targetId}\nClientId：{clientId}");
            });
        }

        private void SetStatus(string message)
        {
            if (_disposed)
            {
                return;
            }

            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                _statusBar?.SetText("DGLab: " + message);
            });
        }

        private void ShowError(string title, string message)
        {
            if (_disposed)
            {
                return;
            }

            _ = JoinableTaskFactory.RunAsync(async () =>
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
                VsShellUtilities.ShowMessageBox(
                    this,
                    message,
                    title,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            });
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                Instance = null;

                if (_breakpointWatcher != null)
                {
                    _breakpointWatcher.Changed -= OnBreakpointsChanged;
                    _breakpointWatcher.Dispose();
                    _breakpointWatcher = null;
                }

                if (_debuggerEvents != null)
                {
                    try
                    {
                        // DTE 事件必须在 UI 线程退订；包销毁正常发生在 UI 线程。
#pragma warning disable VSTHRD010
                        _debuggerEvents.OnEnterRunMode -= OnEnterRunMode;
                        _debuggerEvents.OnEnterDesignMode -= OnEnterDesignMode;
                        _debuggerEvents.OnEnterBreakMode -= OnEnterBreakMode;
#pragma warning restore VSTHRD010
                    }
                    catch
                    {
                        // 忽略。
                    }

                    _debuggerEvents = null;
                }

                if (_documentEvents != null)
                {
                    try
                    {
#pragma warning disable VSTHRD010
                        _documentEvents.DocumentSaved -= OnDocumentSaved;
#pragma warning restore VSTHRD010
                    }
                    catch
                    {
                        // 忽略。
                    }

                    _documentEvents = null;
                }

                _client?.Dispose();
                _client = null;
                _log?.Dispose();
                _log = null;
            }

            base.Dispose(disposing);
        }
    }
}
