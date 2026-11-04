using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.VisualStudio.Shell;
using QRCoder;

namespace DGLab.VS2026
{
    /// <summary>
    /// 二维码工具窗口内容。对应原 VSCode 版的 Webview：显示二维码和绑定 URL。
    /// </summary>
    public sealed class QrCodeControl : UserControl
    {
        private readonly Image _qrImage;
        private readonly TextBox _urlBox;
        private readonly TextBlock _status;
        private string _url = string.Empty;

        public QrCodeControl()
        {
            SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);

            var title = new TextBlock
            {
                Text = "使用dglab app 扫描二维码",
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 8)
            };

            var hint = new TextBlock
            {
                Text = "",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8,
                Margin = new Thickness(0, 0, 0, 16)
            };

            _qrImage = new Image
            {
                Width = 320,
                Height = 320,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };

            var qrBorder = new Border
            {
                Background = Brushes.White,
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 16),
                Child = _qrImage
            };

            var urlLabel = new TextBlock
            {
                Text = "绑定链接",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };

            _urlBox = new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(6, 4, 6, 4)
            };
            _urlBox.SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
            _urlBox.SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            var copyButton = new Button
            {
                Content = "复制链接",
                Padding = new Thickness(12, 4, 12, 4),
                MinWidth = 96,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            copyButton.Click += OnCopyClicked;

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 12)
            };
            buttons.Children.Add(copyButton);

            _status = new TextBlock
            {
                Text = "等待生成二维码...",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.9
            };

            var panel = new StackPanel
            {
                Margin = new Thickness(20),
                MaxWidth = 760,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            panel.Children.Add(title);
            panel.Children.Add(hint);
            panel.Children.Add(qrBorder);
            panel.Children.Add(urlLabel);
            panel.Children.Add(_urlBox);
            panel.Children.Add(buttons);
            panel.Children.Add(_status);

            Content = new ScrollViewer
            {
                Content = panel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
        }

        public void SetUrl(string url, DGLabClient client)
        {
            _url = url ?? string.Empty;
            _urlBox.Text = _url;

            try
            {
                _qrImage.Source = CreateQrImage(_url);
                _status.Text = client != null && client.ConnectionCount > 0
                    ? "设备已连接；再次扫描可绑定新设备。"
                    : "等待郊狼 App 扫描二维码...";
            }
            catch (Exception ex)
            {
                _qrImage.Source = null;
                _status.Text = "生成二维码失败: " + ex.Message;
            }
        }

        public void UpdateStatus(string status)
        {
            if (!string.IsNullOrEmpty(status))
            {
                _status.Text = status;
            }
        }

        private void OnCopyClicked(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_url))
            {
                return;
            }

            try
            {
                Clipboard.SetText(_url);
                UpdateStatus("链接已复制到剪贴板。");
            }
            catch (Exception ex)
            {
                UpdateStatus("复制失败: " + ex.Message);
            }
        }

        private static BitmapSource CreateQrImage(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            using (var generator = new QRCodeGenerator())
            using (var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (var png = new PngByteQRCode(data))
            {
                var bytes = png.GetGraphic(10);
                using (var stream = new MemoryStream(bytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
        }
    }
}
