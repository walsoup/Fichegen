using System.Text;
using Windows.ApplicationModel.DataTransfer;
using FicheGen.Core.Documents;

namespace FicheGen.Infrastructure.Export;

public static class ClipboardPackageBuilder
{
    public static string FormatCfHtml(string htmlFragment)
    {
        const string headerTemplate =
            "Version:1.0\r\n" +
            "StartHTML:0000000000\r\n" +
            "EndHTML:0000000000\r\n" +
            "StartFragment:0000000000\r\n" +
            "EndFragment:0000000000\r\n";
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
            "Version:1.0\r\n" +
            $"StartHTML:{startHtml:D10}\r\n" +
            $"EndHTML:{endHtml:D10}\r\n" +
            $"StartFragment:{startFragment:D10}\r\n" +
            $"EndFragment:{endFragment:D10}\r\n";

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
