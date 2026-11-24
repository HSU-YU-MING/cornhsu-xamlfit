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

    // ───────── 幾支工具的 ignore 疊在同一個元素上（XamlContrast、XamlKeys、XamlA11y 都有自己的）─────────

    private const string Text = "<TextBlock Width=\"40\" Text=\"hello\"/>";

    [Theory]
    [InlineData("<!-- xamlfit-ignore: f --><!-- xamlcontrast-ignore: c -->")]
    [InlineData("<!-- xamlcontrast-ignore: c --><!-- xamlfit-ignore: f -->")]
    [InlineData("<!-- xamla11y-ignore: a --><!-- xamlfit-ignore: f --><!-- xamlkeys-ignore: k --><!-- xamlcontrast-ignore: c -->")]
    [InlineData("<!-- xamlfit-ignore: f --><!-- just a note -->")]
    [InlineData("<!-- xamlfit-ignore: f -->\n<?hint x?>\n<!-- a note -->\n")]
    [InlineData("<!-- xamlcontrast-ignore: c; xamlfit-ignore: f -->")]
    [InlineData("<!-- xamlfit-ignore: f; xamlcontrast-ignore: c -->")]
    [InlineData("<!-- xamlfit-ignore f -->")]
    [InlineData("<!--xamlfit-ignore:f-->")]
    public void Ignore_is_found_among_other_comments(string comments)
    {
        var s = Scan(comments + Text);
        Assert.Equal("f", s.SuppressedReason);
        Assert.False(s.InvalidIgnore);
    }

    [Fact]
    public void The_nearest_of_two_ignores_wins() =>
        Assert.Equal("near", Scan("<!-- xamlfit-ignore: far --><!-- xamlcontrast-ignore: c --><!-- xamlfit-ignore: near -->" + Text).SuppressedReason);

    [Theory]
    [InlineData("<!-- xamlfit-ignore --><!-- xamlcontrast-ignore: c -->")]
    [InlineData("<!-- xamlfit-ignore: --><!-- a note -->")]
    [InlineData("<!-- xamlfit-ignore: ; xamlcontrast-ignore: c -->")]     // 理由是別支工具的
    public void Ignore_without_a_reason_is_flagged_behind_another_comment(string comments)
    {
        var s = Scan(comments + Text);
        Assert.Null(s.SuppressedReason);
        Assert.True(s.InvalidIgnore);
    }

    [Theory]
    [InlineData("<!-- TODO: remove the xamlfit-ignore: below once translated -->")]
    [InlineData("<!-- see xamlfit-ignore: decorative -->")]
    [InlineData("<!-- xamlfit-ignored: decorative -->")]
    [InlineData("<!-- xamlcontrast-ignore: same reason as the xamlfit-ignore above -->")]
    public void A_comment_that_only_mentions_the_ignore_does_not_count(string comment)
    {
        var s = Scan(comment + Text);
        Assert.Null(s.SuppressedReason);
        Assert.False(s.InvalidIgnore);
    }

    [Theory]
    [InlineData("<!-- xamlfit-ignore: r --><!-- <TextBlock Text=\"draft\"/> -->")]
    [InlineData("<!-- xamlfit-ignore: r -->\n<!--<Grid>-->\n<!-- a note -->\n<!-- xamlcontrast-ignore: c -->")]
    [InlineData("<!-- xamlfit-ignore: far --><!-- xamlfit-ignore: r --><!-- <TextBlock/> -->")]
    [InlineData("<!-- xamlfit-ignore --><!-- <TextBlock/> -->")]
    [InlineData("<!-- xamlfit-ignore: r -->text")]
    [InlineData("<!-- xamlfit-ignore: r -->\n text \n<!-- a note -->")]
    [InlineData("<!-- xamlfit-ignore: r --><![CDATA[text]]>")]
    [InlineData("<!-- xamlfit-ignore: r -->&#65;<?hint x?>")]
    public void Ignore_does_not_reach_past_a_commented_out_element_or_text_and_is_flagged(string comments)
    {
        // 把草稿註解掉，ignore 不能默默改壓正文；也不能沉默 —— 使用者看不出自己的註解為什麼沒生效
        var s = Scan(comments + Text);
        Assert.Null(s.SuppressedReason);
        Assert.True(s.InvalidIgnore);
    }

    [Fact]
    public void Ignore_between_the_commented_out_element_and_the_element_applies()
    {
        var s = Scan("<!-- xamlfit-ignore: draft --><!-- <TextBlock/> --><!-- xamlfit-ignore: f --><!-- a note -->" + Text);
        Assert.Equal("f", s.SuppressedReason);
        Assert.False(s.InvalidIgnore);
    }

    [Fact]
    public void Ignore_between_the_text_and_the_element_applies() =>
        Assert.Equal("f", Scan("<!-- xamlfit-ignore: draft -->text<!-- xamlfit-ignore: f --><?hint x?>" + Text).SuppressedReason);

    [Theory]
    [InlineData("<!-- xamlcontrast-ignore: c --><!-- <TextBlock/> -->")]
    [InlineData("<!-- xamlcontrast-ignore: c -->text")]
    public void A_commented_out_element_or_text_alone_is_not_flagged(string before)
    {
        var s = Scan(before + Text);
        Assert.Null(s.SuppressedReason);
        Assert.False(s.InvalidIgnore);
    }

    [Fact]
    public void The_nearest_ignore_counts_even_when_it_has_no_reason()
    {
        var s = Scan("<!-- xamlfit-ignore: far --><!-- xamlfit-ignore --><!-- a note -->" + Text);
        Assert.Null(s.SuppressedReason);
        Assert.True(s.InvalidIgnore);
    }

    [Fact]
    public void Ignore_does_not_reach_past_an_element() =>
        Assert.Null(Scan("<StackPanel><!-- xamlfit-ignore: border --><Border/><!-- a note -->" + Text + "</StackPanel>").SuppressedReason);
}
