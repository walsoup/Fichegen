using Microsoft.Win32;

namespace FicheGen.Infrastructure.Services;

public class WebView2RuntimeChecker
{
    public const string EvergreenBootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    public virtual bool IsWebView2Available()
    {
        try
        {
            // Check 64-bit & 32-bit registry keys for WebView2 Evergreen Runtime
            const string webView2Guid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
            using var key64 = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{webView2Guid}");
            using var key32 = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{webView2Guid}");
            using var keyUser = Registry.CurrentUser.OpenSubKey($@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{webView2Guid}");

            return key64 != null || key32 != null || keyUser != null;
        }
        catch
        {
            return true; // Fallback to true if registry check is restricted
        }
    }

    public string GetDownloadUrl() => EvergreenBootstrapperUrl;
}
