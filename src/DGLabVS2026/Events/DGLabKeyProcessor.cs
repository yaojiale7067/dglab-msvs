using System.ComponentModel.Composition;
using System.Windows.Input;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;

namespace DGLab.VS2026
{

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
