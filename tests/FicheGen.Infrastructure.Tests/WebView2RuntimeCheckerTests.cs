using FicheGen.Infrastructure.Services;
using Xunit;

namespace FicheGen.Infrastructure.Tests;

public class WebView2RuntimeCheckerTests
{
    [Fact]
    public void GetDownloadUrl_ReturnsOfficialEvergreenLink()
    {
        var checker = new WebView2RuntimeChecker();
        var url = checker.GetDownloadUrl();

        Assert.NotNull(url);
        Assert.Contains("LinkId=2124703", url);
    }
}
