using System.Text;
using Windows.ApplicationModel.DataTransfer;
using FicheGen.Core.Documents;

namespace FicheGen.Infrastructure.Export;

public static class ClipboardPackageBuilder
{
    public static string FormatCfHtml(string htmlFragment)
    {
        const string headerTemplate =
@"Version:1.0
StartHTML:0000000000
EndHTML:0000000000
StartFragment:0000000000
EndFragment:0000000000
";
        const string htmlPrefix = "<!DOCTYPE html><html><body><!--StartFragment-->";
        const string htmlSuffix = "<!--EndFragment--></body></html>";

        var headerBytesCount = Encoding.UTF8.GetByteCount(headerTemplate);
        var prefixBytesCount = Encoding.UTF8.GetByteCount(htmlPrefix);
        var fragmentBytesCount = Encoding.UTF8.GetByteCount(htmlFragment);
        var suffixBytesCount = Encoding.UTF8.GetByteCount(htmlSuffix);

        var startHtml = headerBytesCount;
        var startFragment = startHtml + prefixBytesCount;
        var endFragment = startFragment + fragmentBytesCount;
        var endHtml = endFragment + suffixBytesCount;

        var header =
$@"Version:1.0
StartHTML:{startHtml:D10}
EndHTML:{endHtml:D10}
StartFragment:{startFragment:D10}
EndFragment:{endFragment:D10}
";

        return $"{header}{htmlPrefix}{htmlFragment}{htmlSuffix}";
    }

    public static DataPackage BuildClipboardPackage(GeneratedDocument doc, string htmlFragment, string rtfString)
    {
        var package = new DataPackage();
        package.RequestedOperation = DataPackageOperation.Copy;

        // 1. Plain text
        package.SetText(doc.ToPlainText());

        // 2. CF_HTML format
        var cfHtml = FormatCfHtml(htmlFragment);
        package.SetHtmlFormat(cfHtml);

        // 3. CF_RTF format
        if (!string.IsNullOrEmpty(rtfString))
        {
            package.SetRtf(rtfString);
        }

        return package;
    }
}
