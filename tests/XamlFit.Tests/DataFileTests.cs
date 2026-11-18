using System.Text.RegularExpressions;

namespace XamlFit.Tests;

/// <summary>repo 裡不是程式碼、但會影響工具行為的檔案。</summary>
public class DataFileTests
{
    /// <summary>repo 的根目錄；不是從 repo 裡跑（例如只拿到測試組件）時是 null。</summary>
    internal static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "XamlFit.slnx"))) dir = dir.Parent;
        return dir?.FullName;
    }

    /// <summary>
    /// data/ 底下的表與 XamlFit.Core 裡嵌入的副本必須一樣。
    /// 工具讀的是嵌入的那份；只改 data/ 的話，文件說的跟工具做的就不一樣了。
    /// </summary>
    [Theory]
    [InlineData("control-chrome.json")]
    [InlineData("font-fallback.json")]
    [InlineData("theme-chrome.json")]
    public void The_embedded_copy_matches_data(string name)
    {
        if (RepoRoot() is not { } root) return;
        var data = File.ReadAllText(Path.Combine(root, "data", name)).ReplaceLineEndings();
        var embedded = File.ReadAllText(Path.Combine(root, "src", "XamlFit.Core", name)).ReplaceLineEndings();
        Assert.True(data == embedded, $"data/{name} and src/XamlFit.Core/{name} differ: copy data/ over the embedded file");
    }

    /// <summary>
    /// ⚠ action.yml 的輸入一律經過 env: 傳給腳本。直接把 ${{ inputs.x }} 插進 run: 的腳本裡，
    ///   一個接到 PR 作者控制的值的輸入就能在 runner 上執行任意指令。
    /// </summary>
    [Fact]
    public void The_action_never_interpolates_an_input_into_a_shell_script()
    {
        if (RepoRoot() is not { } root) return;
        // 只看 run: 腳本的內容：if:、with:、working-directory: 是 runner 的表達式與參數，不經過 shell（XamlContrast 的 Action 也這樣用）。
        // run: | 的區塊從那一行起，到縮排回到 run: 的層級為止
        var lines = File.ReadAllLines(Path.Combine(root, "action.yml"));
        var offenders = new List<string>();
        var runIndent = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var indent = line.Length - line.TrimStart().Length;
            if (runIndent >= 0 && line.Trim().Length > 0 && indent <= runIndent) runIndent = -1;
            if (runIndent < 0)
            {
                var m = Regex.Match(line, @"^(\s+)run:\s*(\|?)\s*(.*)$");
                if (!m.Success) continue;
                if (m.Groups[2].Value == "|") { runIndent = m.Groups[1].Length; continue; }
                if (m.Groups[3].Value.Contains("${{", StringComparison.Ordinal)) offenders.Add($"action.yml:{i + 1}: {line.Trim()}");
                continue;
            }
            if (line.Contains("${{", StringComparison.Ordinal) && !line.TrimStart().StartsWith('#'))
                offenders.Add($"action.yml:{i + 1}: {line.Trim()}");
        }
        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
