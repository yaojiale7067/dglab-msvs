using System.ComponentModel.Composition;
using System.Windows.Input;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace DGLab.VS2026
{
    /// <summary>
    /// 保证编辑器内的 Ctrl+U 能触发“开始连接”。
    /// Visual Studio 默认把 Ctrl+U 绑定到“转换为小写”，仅靠 VSCT 快捷键可能被内置命令抢占，
    /// 因此用键盘处理器在编辑器预览阶段拦截（与原 VSCode 插件 editorTextFocus 下的 Ctrl+U 行为一致）。
    /// </summary>
    [Export(typeof(IKeyProcessorProvider))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    [Name("DGLabCtrlUKeyProcessor")]
    [Order(Before = "DefaultKeyProcessor")]
    public sealed class DGLabKeyProcessorProvider : IKeyProcessorProvider
    {
        public KeyProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
        {
            return new DGLabKeyProcessor();
        }
    }

    internal sealed class DGLabKeyProcessor : KeyProcessor
    {
        public override void PreviewKeyDown(KeyEventArgs args)
        {
            var package = DGLabPackage.Instance;
            if (package == null)
            {
                // 包尚未加载时不要吞掉 Ctrl+U，保留 Visual Studio 的默认行为。
                return;
            }

            if (args.Key == Key.U && Keyboard.Modifiers == ModifierKeys.Control)
            {
                args.Handled = true;
                package.ExecuteConnectCommand();
            }
        }
    }
}
