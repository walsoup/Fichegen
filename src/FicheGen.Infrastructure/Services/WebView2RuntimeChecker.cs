using Microsoft.Win32;

namespace FicheGen.Infrastructure.Services;

public class WebView2RuntimeChecker
{
    public const string EvergreenBootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const string WebView2Guid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    public virtual bool IsWebView2Available()
    {
        try
        {
            static bool CheckRegistryKey(RegistryKey root, string subPath)
            {
                try
                {
                    using var key = root.OpenSubKey(subPath);
                    if (key == null) return false;
                    var pv = key.GetValue("pv") as string;
                    return !string.IsNullOrWhiteSpace(pv) && pv != "0.0.0.0";
                }
                catch
                {
                    return false;
                }
            }

            return CheckRegistryKey(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{WebView2Guid}")
                || CheckRegistryKey(Registry.LocalMachine, $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{WebView2Guid}")
                || CheckRegistryKey(Registry.CurrentUser, $@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{WebView2Guid}")
                || CheckRegistryKey(Registry.LocalMachine, $@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\ClientState\{WebView2Guid}")
                || CheckRegistryKey(Registry.LocalMachine, $@"SOFTWARE\Microsoft\EdgeUpdate\ClientState\{WebView2Guid}")
                || CheckRegistryKey(Registry.CurrentUser, $@"SOFTWARE\Microsoft\EdgeUpdate\ClientState\{WebView2Guid}");
        }
        catch
        {
            return false;
        }
    }

    public string GetDownloadUrl() => EvergreenBootstrapperUrl;
}
