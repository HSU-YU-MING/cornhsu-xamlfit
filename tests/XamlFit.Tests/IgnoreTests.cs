using System.Xml.Linq;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

public class IgnoreTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static TextSite Scan(string body)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xamlfit-ign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "T.xaml"), $"<Window {Ns} Width=\"300\">{body}</Window>");
            var scanner = new XamlScanner(ChromeTable.Builtin());
            return scanner.Scan(dir).Sites.Single();
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void Ignore_with_a_reason_suppresses_the_site()
    {
        var s = Scan("""
            <!-- xamlfit-ignore: decorative, never translated -->
            <TextBlock Width="40" Text="hello"/>
            """);
        Assert.Equal("decorative, never translated", s.SuppressedReason);
        Assert.False(s.InvalidIgnore);
    }

    [Fact]
    public void Ignore_without_a_reason_does_not_apply_and_is_flagged()
    {
        // 沒有理由的壓抑等於新開一條靜默退化的通道 —— 不生效，而且要讓使用者知道
        var s = Scan("""
            <!-- xamlfit-ignore -->
            <TextBlock Width="40" Text="hello"/>
            """);
        Assert.Null(s.SuppressedReason);
        Assert.True(s.InvalidIgnore);
    }

    [Fact]
    public void An_unrelated_comment_does_not_suppress()
    {
        var s = Scan("""
            <!-- just a note -->
            <TextBlock Width="40" Text="hello"/>
            """);
        Assert.Null(s.SuppressedReason);
        Assert.False(s.InvalidIgnore);
    }

    [Fact]
    public void Ignore_only_covers_the_next_element()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xamlfit-ign-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "T.xaml"), $"""
                <Window {Ns} Width="300">
                  <StackPanel>
                    <!-- xamlfit-ignore: first only -->
                    <TextBlock Width="40" Text="one"/>
                    <TextBlock Width="40" Text="two"/>
                  </StackPanel>
                </Window>
                """);
            var sites = new XamlScanner(ChromeTable.Builtin()).Scan(dir).Sites
                .OrderBy(s => s.Line).ToList();
            Assert.Equal(2, sites.Count);
            Assert.NotNull(sites[0].SuppressedReason);
            Assert.Null(sites[1].SuppressedReason);
        }
        finally { Directory.Delete(dir, true); }
    }
}
