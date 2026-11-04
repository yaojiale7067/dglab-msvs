using Microsoft.VisualStudio.Shell;

namespace DGLab.VS2026
{
    /// <summary>
    /// 显示二维码的 Visual Studio 工具窗口。
    /// </summary>
    public sealed class QrCodeToolWindow : ToolWindowPane
    {
        private readonly QrCodeControl _control;

        public QrCodeToolWindow()
            : base(null)
        {
            Caption = "DGLab 二维码";
            _control = new QrCodeControl();
            Content = _control;
        }

        public void SetUrl(string url, DGLabClient client)
        {
            _control.SetUrl(url, client);
        }

        public void UpdateStatus(string status)
        {
            _control.UpdateStatus(status);
        }
    }
}
