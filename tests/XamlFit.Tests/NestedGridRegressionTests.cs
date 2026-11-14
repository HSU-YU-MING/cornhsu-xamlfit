using System.Xml.Linq;
using XamlFit.Core.Layout;

namespace XamlFit.Tests;

public class NestedGridRegressionTests
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                              "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    private static AvailableWidth Resolve(string body)
    {
        var doc = XDocument.Parse($"<Root {Ns}>{body}</Root>", LoadOptions.SetLineInfo);
        var probe = doc.Descendants().First(e => e.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml"))?.Value == "probe");
        return new WidthResolver(ChromeTable.Builtin()).Resolve(probe);
    }

    [Fact]
    public void An_unsolved_inner_grid_is_not_papered_over_by_an_outer_fixed_column()
    {
        // 內層的星號欄解不開時，真正的寬度比外層的 300 窄。
        // 拿 300 當答案會高估可用寬度 → 漏報溢出。
        var w = Resolve("""
            <Grid Width="600">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="300"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Grid Grid.Column="0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <MyCustomChart Grid.Column="0"/>
                <TextBlock x:Name="probe" Grid.Column="1" Text="A"/>
              </Grid>
            </Grid>
            """);
        Assert.Equal(WidthStatus.NeedsGrid, w.Status);
    }
}
