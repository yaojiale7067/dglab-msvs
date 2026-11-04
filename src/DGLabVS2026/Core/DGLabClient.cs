using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DGLab.VS2026
{
    /// <summary>
    /// 插件向客户端暴露的宿主能力，便于把网络协议层与 Visual Studio 解耦。
    /// </summary>
    public interface IDGLabHost
    {
        DGLabSettings Settings { get; }

        void Log(string message);

        void Notify(string message);

        void NotifyError(string message);

        void OnConnectionStateChanged(bool connected);

        void OnDeviceConnected(string clientId, string targetId);
    }

    /// <summary>
    /// 郊狼 WebSocket 协议客户端。行为移植自原项目 src/tools/wsConnect.ts：
    /// 连接 wss://ws.dungeon-lab.cn/、绑定 clientId、心跳、强度渐增、波形发送、
    /// 一键开火（临时提高强度并在 600ms 后恢复）以及 A/B/AB 通道输出。
    /// </summary>
    public sealed class DGLabClient : IDisposable
    {
        private static readonly Uri ServerUri = new Uri("wss://ws.dungeon-lab.cn/");
        private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(2);

        private readonly IDGLabHost _host;
        private readonly object _mapLock = new object();
        private readonly object _heartbeatLock = new object();
        private readonly Dictionary<string, string> _connectMap = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _fireGate = new SemaphoreSlim(1, 1);

        private PulseLibrary _pulseLibrary;
        private ClientWebSocket _ws;
        private CancellationTokenSource _receiveCts;
        private Task _receiveTask;
        private TaskCompletionSource<string> _bindTcs;
        private Timer _heartbeatTimer;
        private int _heartbeatPeriodSeconds;
        private int _addStrengthInterval;
        private volatile string _clientId = string.Empty;
        private volatile string _pulseName;
        private bool _disposed;

        public DGLabClient(IDGLabHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _pulseName = host.Settings?.PulseName ?? "快速按捏";

            try
            {
                _pulseLibrary = PulseLibrary.Load();
                _host.Log($"已加载波形库，共 {_pulseLibrary.Count} 个波形");
            }
            catch (Exception ex)
            {
                _pulseLibrary = null;
                _host.Log("加载波形库失败: " + ex);
            }
        }

        public event EventHandler<string> ClientIdChanged;

        public bool IsConnected
        {
            get
            {
                var ws = Volatile.Read(ref _ws);
                return ws != null && ws.State == WebSocketState.Open;
            }
        }

        public int ConnectionCount
        {
            get
            {
                lock (_mapLock)
                {
                    return _connectMap.Count;
                }
            }
        }

        public string ClientId => _clientId;

        public string PulseName => _pulseName;

        public IReadOnlyCollection<string> PulseNames =>
            _pulseLibrary == null ? Array.Empty<string>() : _pulseLibrary.Names.ToArray();

        /// <summary>
        /// 建立连接并等待服务器下发 bind 消息，返回 clientId。
        /// </summary>
        public async Task<string> ConnectAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            await DisconnectAsync().ConfigureAwait(false);

            var ws = new ClientWebSocket();
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);

            var bindTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _bindTcs = bindTcs;
            Volatile.Write(ref _ws, ws);
            _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            try
            {
                await ws.ConnectAsync(ServerUri, cancellationToken).ConfigureAwait(false);
                _host.Log("WebSocket 已连接");

                _receiveTask = Task.Run(() => ReceiveLoopAsync(ws, _receiveCts.Token));

                using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeoutCts.CancelAfter(BindTimeout);
                    using (timeoutCts.Token.Register(() => bindTcs.TrySetCanceled()))
                    {
                        return await bindTcs.Task.ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                await DisconnectAsync().ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                _host.Log("WebSocket 连接失败: " + ex);
                await DisconnectAsync().ConfigureAwait(false);
                throw;
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            var messageBuffer = new MemoryStream();

            try
            {
                while (!cancellationToken.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            break;
                        }

                        // 内存流写入是同步的 CPU 操作，不会阻塞 UI。
#pragma warning disable VSTHRD103
                        messageBuffer.Write(buffer, 0, result.Count);
#pragma warning restore VSTHRD103
                    }
                    while (!result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        try
                        {
                            await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
                        }
                        catch
                        {
                            // 服务器已经关闭，忽略。
                        }

                        break;
                    }

                    var text = Encoding.UTF8.GetString(messageBuffer.ToArray());
                    messageBuffer.SetLength(0);
                    ProcessMessage(ws, text);
                }
            }
            catch (OperationCanceledException)
            {
                // 正常断开。
            }
            catch (Exception ex)
            {
                if (!cancellationToken.IsCancellationRequested)
                {
                    _host.Log("WebSocket 接收失败: " + ex.Message);
                }
            }
            finally
            {
                messageBuffer.Dispose();
                OnSocketClosed(ws);
            }
        }

        private void ProcessMessage(ClientWebSocket ws, string text)
        {
            if (!ReferenceEquals(Volatile.Read(ref _ws), ws))
            {
                return;
            }

            try
            {
                var message = Json.DeserializeObject(text);
                var type = Json.GetString(message, "type");
                _host.Log("收到消息: " + Json.Truncate(text, 400));

                if (string.Equals(type, "bind", StringComparison.OrdinalIgnoreCase))
                {
                    var clientId = Json.GetString(message, "clientId");
                    var targetId = Json.GetString(message, "targetId");

                    if (!string.IsNullOrEmpty(clientId))
                    {
                        _clientId = clientId;
                        ClientIdChanged?.Invoke(this, clientId);
                    }

                    if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(targetId))
                    {
                        lock (_mapLock)
                        {
                            _connectMap[clientId] = targetId;
                        }

                        _host.OnDeviceConnected(clientId, targetId);
                        _ = ApplyBaseStrengthAsync();
                    }

                    _bindTcs?.TrySetResult(_clientId);
                    StartHeartbeat();
                }
            }
            catch (Exception ex)
            {
                _host.Log("消息解析失败: " + ex.Message + "，原始内容: " + Json.Truncate(text, 200));
            }
        }

        private void OnSocketClosed(ClientWebSocket ws)
        {
            if (!ReferenceEquals(Interlocked.CompareExchange(ref _ws, null, ws), ws))
            {
                return;
            }

            StopHeartbeat();
            lock (_mapLock)
            {
                _connectMap.Clear();
            }

            _clientId = string.Empty;
            _bindTcs?.TrySetCanceled();
            _host.Log("WebSocket 已关闭");
            _host.OnConnectionStateChanged(false);

            try
            {
                ws.Dispose();
            }
            catch
            {
                // 忽略关闭异常。
            }
        }

        public async Task DisconnectAsync()
        {
            var ws = Interlocked.Exchange(ref _ws, null);
            if (ws == null)
            {
                StopHeartbeat();
                return;
            }

            StopHeartbeat();

            var cts = Interlocked.Exchange(ref _receiveCts, null);
            try
            {
                cts?.Cancel();
            }
            catch
            {
                // 忽略。
            }

            lock (_mapLock)
            {
                _connectMap.Clear();
            }

            _clientId = string.Empty;
            _bindTcs?.TrySetCanceled();
            _host.OnConnectionStateChanged(false);

            try
            {
                if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                {
                    using (var closeCts = new CancellationTokenSource(CloseTimeout))
                    {
                        await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", closeCts.Token).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // 关闭超时或连接已断开，忽略。
            }
            finally
            {
                try
                {
                    ws.Dispose();
                }
                catch
                {
                    // 忽略。
                }

                cts?.Dispose();
            }

            if (_receiveTask != null)
            {
                try
                {
                    await Task.WhenAny(_receiveTask, Task.Delay(1000)).ConfigureAwait(false);
                }
                catch
                {
                    // 忽略。
                }

                _receiveTask = null;
            }
        }

        private void StartHeartbeat()
        {
            var settings = _host.Settings;
            if (settings == null || Volatile.Read(ref _ws) == null)
            {
                return;
            }

            lock (_heartbeatLock)
            {
                if (_heartbeatTimer != null && _heartbeatPeriodSeconds == settings.HeartbeatInterval)
                {
                    return;
                }

                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;

                _heartbeatPeriodSeconds = settings.HeartbeatInterval;
                var period = TimeSpan.FromSeconds(settings.HeartbeatInterval);
                _heartbeatTimer = new Timer(HeartbeatTick, null, period, period);
                _host.Log($"心跳已启动，间隔 {settings.HeartbeatInterval} 秒");
            }
        }

        private void StopHeartbeat()
        {
            lock (_heartbeatLock)
            {
                if (_heartbeatTimer != null)
                {
                    _heartbeatTimer.Dispose();
                    _heartbeatTimer = null;
                    _host.Log("心跳已停止");
                }

                _heartbeatPeriodSeconds = 0;
                _addStrengthInterval = 0;
            }
        }

        public void RestartHeartbeat()
        {
            lock (_heartbeatLock)
            {
                _heartbeatTimer?.Dispose();
                _heartbeatTimer = null;
                _heartbeatPeriodSeconds = 0;
            }

            if (IsConnected)
            {
                StartHeartbeat();
            }
        }

        private void HeartbeatTick(object state)
        {
            _ = HeartbeatAsync();
        }

        private async Task HeartbeatAsync()
        {
            var ws = Volatile.Read(ref _ws);
            if (ws == null || ws.State != WebSocketState.Open)
            {
                return;
            }

            await SendJsonAsync(new
            {
                type = "heartbeat",
                clientId = _clientId,
                message = "200"
            }).ConfigureAwait(false);

            var settings = _host.Settings;
            int addStrength;

            lock (_mapLock)
            {
                if (_connectMap.Count == 0 && _addStrengthInterval >= settings.MaxAddInterval)
                {
                    return;
                }

                _addStrengthInterval++;
                addStrength = _addStrengthInterval;
            }

            _host.Log($"基础强度 +{addStrength}");
            await SetStrengthAsync(1, 4, settings.Strength + addStrength).ConfigureAwait(false);
            await SetStrengthAsync(2, 4, settings.Strength + addStrength).ConfigureAwait(false);
        }

        public async Task<bool> SetStrengthAsync(int channel, int type, int strength)
        {
            var ws = Volatile.Read(ref _ws);
            if (ws == null || ws.State != WebSocketState.Open)
            {
                _host.Log("WebSocket 尚未连接");
                return false;
            }

            var targets = SnapshotTargets();
            if (targets.Count == 0)
            {
                _host.Log("未连接任何设备");
                return false;
            }

            foreach (var target in targets)
            {
                var message = CreateStrengthMessage(channel, type, strength, target.Key, target.Value);
                if (message != null)
                {
                    await SendJsonAsync(message).ConfigureAwait(false);
                }
            }

            return true;
        }

        private object CreateStrengthMessage(int channel, int type, int strength, string clientId, string targetId)
        {
            if (type >= 1 && type <= 3)
            {
                return new
                {
                    type,
                    channel,
                    clientId,
                    targetId,
                    message = "set channel",
                    strength
                };
            }

            if (type == 4)
            {
                return new
                {
                    type,
                    clientId,
                    targetId,
                    message = $"strength-{channel}+2+{strength}"
                };
            }

            _host.Log("设置强度类型错误: " + type);
            return null;
        }

        public async Task ClearAllWaveAsync()
        {
            foreach (var target in SnapshotTargets())
            {
                await SendJsonAsync(new
                {
                    type = 4,
                    clientId = target.Key,
                    targetId = target.Value,
                    message = "clear-1"
                }).ConfigureAwait(false);

                await SendJsonAsync(new
                {
                    type = 4,
                    clientId = target.Key,
                    targetId = target.Value,
                    message = "clear-2"
                }).ConfigureAwait(false);
            }

            _host.Log("已清除所有波形");
        }

        /// <summary>
        /// 发送波形。wave 为 "this" 时使用当前波形名称。
        /// </summary>
        public async Task SendWaveAsync(string wave, double time)
        {
            string[] hexWave;

            if (string.Equals(wave, "this", StringComparison.Ordinal))
            {
                hexWave = GetPulseDataByName(_pulseName);
            }
            else
            {
                hexWave = GetPulseDataByName(wave);
            }

            if (hexWave == null || hexWave.Length == 0)
            {
                _host.Log("波形数据为空，请先设置波形名称: " + wave);
                return;
            }

            var settings = _host.Settings;
            var waveJson = Json.Serialize(hexWave);
            var targets = SnapshotTargets();

            foreach (var target in targets)
            {
                if (settings.ChannelMode == ChannelMode.AOnly || settings.ChannelMode == ChannelMode.AB)
                {
                    await SendJsonAsync(new
                    {
                        type = "clientMsg",
                        channel = "A",
                        clientId = target.Key,
                        targetId = target.Value,
                        message = "A:" + waveJson,
                        time
                    }).ConfigureAwait(false);
                }

                if (settings.ChannelMode == ChannelMode.BOnly || settings.ChannelMode == ChannelMode.AB)
                {
                    await SendJsonAsync(new
                    {
                        type = "clientMsg",
                        channel = "B",
                        clientId = target.Key,
                        targetId = target.Value,
                        message = "B:" + waveJson,
                        time
                    }).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// 一键开火：清除旧波形，临时提升强度并发送波形，600ms 后恢复。
        /// </summary>
        public async Task SendFireAsync(double strength, double time)
        {
            // 串行化一键开火，避免多个事件同时开火时强度恢复顺序错乱。
            await _fireGate.WaitAsync().ConfigureAwait(false);
            try
            {
                await ClearAllWaveAsync().ConfigureAwait(false);

                var settings = _host.Settings;
                int addStrength;
                lock (_mapLock)
                {
                    addStrength = _addStrengthInterval;
                }

                var originalStrength = settings.Strength + addStrength;
                var fireStrength = (int)Math.Round(strength + originalStrength);

                await SetStrengthAsync(1, 4, fireStrength).ConfigureAwait(false);
                await SetStrengthAsync(2, 4, fireStrength).ConfigureAwait(false);
                await SendWaveAsync("this", time).ConfigureAwait(false);

                await Task.Delay(600).ConfigureAwait(false);

                await SetStrengthAsync(1, 4, originalStrength).ConfigureAwait(false);
                await SetStrengthAsync(2, 4, originalStrength).ConfigureAwait(false);

                _host.Log($"已发送一键开火，强度 {fireStrength}");
            }
            finally
            {
                _fireGate.Release();
            }
        }

        /// <summary>
        /// 应用当前配置：设置基础强度、设置波形并清空输出队列。
        /// 对应原命令 dglabvscode.serR 的 applySettings。
        /// </summary>
        public async Task ApplySettingsAsync()
        {
            await _fireGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var settings = _host.Settings;

                await SetStrengthAsync(1, 4, settings.Strength).ConfigureAwait(false);
                await SetStrengthAsync(2, 4, settings.Strength).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(settings.PulseName))
                {
                    var success = SetPulseName(settings.PulseName);
                    await ClearAllWaveAsync().ConfigureAwait(false);

                    if (success)
                    {
                        _host.Notify($"设置波形为 {settings.PulseName} 成功");
                    }
                    else
                    {
                        _host.NotifyError($"设置波形为 {settings.PulseName} 失败");
                    }
                }

                RestartHeartbeat();
            }
            finally
            {
                _fireGate.Release();
            }
        }

        /// <summary>
        /// 刷新配置：重置强度渐增并把配置重新下发到设备。
        /// </summary>
        public async Task RefreshAsync()
        {
            lock (_mapLock)
            {
                _addStrengthInterval = 0;
            }

            await ApplySettingsAsync().ConfigureAwait(false);
        }

        public bool SetPulseName(string pulseName)
        {
            if (_pulseLibrary == null)
            {
                _host.Log("波形数据未加载");
                return false;
            }

            if (!_pulseLibrary.Contains(pulseName))
            {
                _host.Log($"波形名称 {pulseName} 不存在");
                return false;
            }

            _pulseName = pulseName;
            _host.Log($"设置波形名称为 {pulseName}");
            return true;
        }

        public string[] GetPulseDataByName(string pulseName)
        {
            if (_pulseLibrary == null)
            {
                return null;
            }

            var data = _pulseLibrary.GetHexWave(pulseName);
            if (data == null)
            {
                _host.Log($"未找到波形数据: {pulseName}");
            }

            return data;
        }

        private async Task ApplyBaseStrengthAsync()
        {
            var settings = _host.Settings;
            await SetStrengthAsync(1, 4, settings.Strength).ConfigureAwait(false);
            await SetStrengthAsync(2, 4, settings.Strength).ConfigureAwait(false);
        }

        private List<KeyValuePair<string, string>> SnapshotTargets()
        {
            lock (_mapLock)
            {
                return _connectMap.ToList();
            }
        }

        private async Task SendJsonAsync(object message)
        {
            var text = Json.Serialize(message);
            var bytes = Encoding.UTF8.GetBytes(text);

            await _sendGate.WaitAsync().ConfigureAwait(false);
            try
            {
                var ws = Volatile.Read(ref _ws);
                if (ws == null || ws.State != WebSocketState.Open)
                {
                    _host.Log("WebSocket 尚未连接");
                    return;
                }

                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
                _host.Log("发送消息: " + Json.Truncate(text, 400));
            }
            catch (Exception ex)
            {
                _host.Log("发送消息失败: " + ex.Message);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DGLabClient));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                // DisconnectAsync 内部全部使用 ConfigureAwait(false)，不会捕获 UI 同步上下文。
#pragma warning disable VSTHRD002
                DisconnectAsync().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
            }
            catch
            {
                // 忽略。
            }

            StopHeartbeat();
            _sendGate.Dispose();
            _fireGate.Dispose();
            _receiveCts?.Dispose();
        }
    }
}
