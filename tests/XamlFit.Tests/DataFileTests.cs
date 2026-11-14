namespace XamlFit.Tests;

/// <summary>
/// data/ 底下的表與 XamlFit.Core 裡嵌入的副本必須一樣。
/// 工具讀的是嵌入的那份；只改 data/ 的話，文件說的跟工具做的就不一樣了。
/// </summary>
public class DataFileTests
{
    [Theory]
    [InlineData("control-chrome.json")]
    [InlineData("font-fallback.json")]
    public void The_embedded_copy_matches_data(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "XamlFit.slnx"))) dir = dir.Parent;
        if (dir is null) return;   // 不是從 repo 裡跑（例如只拿到測試組件）

        var data = File.ReadAllText(Path.Combine(dir.FullName, "data", name)).ReplaceLineEndings();
        var embedded = File.ReadAllText(Path.Combine(dir.FullName, "src", "XamlFit.Core", name)).ReplaceLineEndings();
        Assert.True(data == embedded, $"data/{name} and src/XamlFit.Core/{name} differ: copy data/ over the embedded file");
    }
}
