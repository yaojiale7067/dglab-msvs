using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DGLab.VS2026;
using QRCoder;

namespace DGLab.Smoke
{
    internal static class Program
    {
        private static int Main()
        {
            var failures = new List<string>();

            CheckPulseLibrary(failures);
            CheckSettings(failures);
            CheckQrCode(failures);
            CheckWebSocket(failures);

            Console.WriteLine();
            if (failures.Count == 0)
            {
                Console.WriteLine("SMOKE TEST PASSED");
                return 0;
            }

            Console.WriteLine("SMOKE TEST FAILED:");
            foreach (var failure in failures)
            {
                Console.WriteLine("  - " + failure);
            }

            return 1;
        }

        private static void CheckPulseLibrary(List<string> failures)
        {
            Console.WriteLine("== 波形库 ==");
            try
            {
                var library = PulseLibrary.Load();
                Check(library.Count == 16, $"波形数量应为 16，实际 {library.Count}", failures);
                Check(library.Contains("快速按捏"), "缺少默认波形“快速按捏”", failures);
                Check(library.Contains("呼吸"), "缺少波形“呼吸”", failures);

                var hex = library.GetHexWave("呼吸");
                Check(hex != null && hex.Length == 10, "呼吸波形应有 10 帧", failures);
                Check(hex != null && hex[0] == "0A0A0A0A00000000", $"呼吸第 1 帧编码错误: {hex?[0]}", failures);
                Check(hex != null && hex.All(x => x.Length == 16), "每帧应编码为 16 个十六进制字符", failures);

                Console.WriteLine($"  波形数: {library.Count}");
                Console.WriteLine($"  呼吸第 1 帧: {hex?[0]}");
            }
            catch (Exception ex)
            {
                failures.Add("波形库异常: " + ex);
            }
        }

        private static void CheckSettings(List<string> failures)
        {
            Console.WriteLine("== 配置/黑名单 ==");
            var settings = new DGLabSettings(
                5, "快速按捏", 30, 20, 15, 15, 20, 10, 12, 20, ChannelMode.AB,
                new[] { "json", " .md ", "txt" });

            Check(settings.Strength == 5, "基础强度错误", failures);
            Check(settings.HeartbeatInterval == 30, "心跳间隔错误", failures);
            Check(settings.IsBlacklistedPath(@"C:\a\b.json"), "json 应在黑名单", failures);
            Check(settings.IsBlacklistedPath(@"C:\a\b.MD"), "md 应大小写不敏感地命中黑名单", failures);
            Check(!settings.IsBlacklistedPath(@"C:\a\b.cs"), "cs 不应在黑名单", failures);
            Check(settings.Blacklist.Count == 3, $"黑名单应解析为 3 项，实际 {settings.Blacklist.Count}", failures);
            Console.WriteLine("  黑名单: " + string.Join(", ", settings.Blacklist));

            var converter = TypeDescriptor.GetConverter(typeof(ChannelMode));
            var text = converter.ConvertTo(ChannelMode.AB, typeof(string)) as string;
            Check(text == "AB一起输出", $"通道枚举显示文本错误: {text}", failures);
            var parsed = converter.ConvertFrom("仅A通道输出");
            Check(parsed is ChannelMode mode && mode == ChannelMode.AOnly, "通道枚举中文本反序列化错误", failures);
        }

        private static void CheckQrCode(List<string> failures)
        {
            Console.WriteLine("== 二维码 ==");
            try
            {
                const string url = "https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#wss://ws.dungeon-lab.cn/00000000-0000-0000-0000-000000000000";
                using (var generator = new QRCodeGenerator())
                using (var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
                using (var png = new PngByteQRCode(data))
                {
                    var bytes = png.GetGraphic(10);
                    Check(bytes.Length > 1000, "二维码 PNG 太小", failures);
                    Check(bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
                        "二维码不是合法 PNG", failures);
                    Console.WriteLine($"  PNG 大小: {bytes.Length} 字节");
                }
            }
            catch (Exception ex)
            {
                failures.Add("二维码异常: " + ex);
            }
        }

        private static void CheckWebSocket(List<string> failures)
        {
            Console.WriteLine("== WebSocket 连接 ==");
            var settings = new DGLabSettings(5, "快速按捏", 30, 20, 15, 15, 20, 10, 12, 20, ChannelMode.AB, null);
            var host = new ConsoleHost(settings);

            using (var client = new DGLabClient(host))
            {
                try
                {
                    var clientId = client.ConnectAsync(CancellationToken.None).GetAwaiter().GetResult();
                    Check(!string.IsNullOrWhiteSpace(clientId), "没有获得 clientId", failures);
                    Check(Guid.TryParse(clientId, out _), "clientId 不是 GUID: " + clientId, failures);

                    var url = "https://www.dungeon-lab.com/app-download.php#DGLAB-SOCKET#wss://ws.dungeon-lab.cn/" + clientId;
                    Console.WriteLine("  clientId: " + clientId);
                    Console.WriteLine("  绑定 URL: " + url);

                    // 等待一下，确认连接保持且不会异常关闭。
                    Thread.Sleep(1500);
                    Check(client.IsConnected, "连接在 1.5 秒内意外断开", failures);

                    client.DisconnectAsync().GetAwaiter().GetResult();
                    Check(!client.IsConnected, "断开后仍显示已连接", failures);
                }
                catch (Exception ex)
                {
                    failures.Add("WebSocket 连接异常: " + ex);
                }
            }
        }

        private static void Check(bool condition, string message, List<string> failures)
        {
            if (!condition)
            {
                failures.Add(message);
            }
        }

        private sealed class ConsoleHost : IDGLabHost
        {
            public ConsoleHost(DGLabSettings settings)
            {
                Settings = settings;
            }

            public DGLabSettings Settings { get; }

            public void Log(string message)
            {
                Console.WriteLine("  [log] " + message);
            }

            public void Notify(string message)
            {
                Console.WriteLine("  [info] " + message);
            }

            public void NotifyError(string message)
            {
                Console.WriteLine("  [error] " + message);
            }

            public void OnConnectionStateChanged(bool connected)
            {
                Console.WriteLine("  [state] connected=" + connected);
            }

            public void OnDeviceConnected(string clientId, string targetId)
            {
                Console.WriteLine($"  [device] {clientId} -> {targetId}");
            }
        }
    }
}
