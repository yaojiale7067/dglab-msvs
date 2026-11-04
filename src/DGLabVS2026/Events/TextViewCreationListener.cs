using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace DGLab.VS2026
{
    /// <summary>
    /// 监听所有可编辑文本视图的缓冲区变化，对应 VSCode 的 onDidChangeTextDocument。
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    public sealed class TextViewCreationListener : IWpfTextViewCreationListener
    {
        [Import]
        public ITextDocumentFactoryService TextDocumentFactoryService { get; set; }

        public void TextViewCreated(IWpfTextView textView)
        {
            // 始终订阅；插件包可能比第一个文本视图稍晚完成初始化。
            textView.TextBuffer.Changed += (sender, args) =>
            {
                var package = DGLabPackage.Instance;
                if (package == null)
                {
                    return;
                }

                string filePath = null;
                if (TextDocumentFactoryService != null &&
                    TextDocumentFactoryService.TryGetTextDocument(textView.TextBuffer, out var document))
                {
                    filePath = document.FilePath;
                }

                package.OnTextBufferChanged(textView, filePath, args);
            };
        }
    }
}
