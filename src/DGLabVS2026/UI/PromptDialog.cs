using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DGLab.VS2026
{
    /// <summary>
    /// 简单的文本输入对话框，替代 VSCode 的 showInputBox。
    /// </summary>
    public sealed class PromptDialog : Window
    {
        private readonly TextBox _textBox;

        public PromptDialog(string title, string prompt, string initialValue)
        {
            Title = title;
            Width = 480;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;

            _textBox = new TextBox
            {
                Text = initialValue ?? string.Empty,
                Margin = new Thickness(0, 8, 0, 16),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var okButton = new Button
            {
                Content = "确定",
                IsDefault = true,
                MinWidth = 88,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            okButton.Click += (sender, args) => DialogResult = true;

            var cancelButton = new Button
            {
                Content = "取消",
                IsCancel = true,
                MinWidth = 88,
                Padding = new Thickness(12, 4, 12, 4)
            };

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel
            {
                Margin = new Thickness(16)
            };
            panel.Children.Add(new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap
            });
            panel.Children.Add(_textBox);
            panel.Children.Add(buttons);

            Content = panel;

            Loaded += (sender, args) =>
            {
                _textBox.Focus();
                _textBox.SelectAll();
            };

            PreviewKeyDown += (sender, args) =>
            {
                if (args.Key == Key.Escape)
                {
                    DialogResult = false;
                }
            };
        }

        public string Value => _textBox.Text?.Trim() ?? string.Empty;
    }
}
