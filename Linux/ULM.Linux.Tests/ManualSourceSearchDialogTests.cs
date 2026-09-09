using ULM.Linux.Views;
using Xunit;
namespace ULM.Linux.Tests;
public class ManualSourceSearchDialogTests
{
    [Fact]
    public void BrowserQueryEscapesUserInput()
    {
        Assert.Equal("https://duckduckgo.com/?q=Linux%20%26%20rescue%20download", ManualSourceSearchDialog.BuildBrowserSearchUrl("Linux & rescue"));
    }
}
