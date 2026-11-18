using System.Text.RegularExpressions;

namespace XamlFit.Tests;

/// <summary>
/// action.yml 的契約（照 XamlKeys 的 FrozenContractTests、XamlContrast 的 ActionContractTests）。
/// 0.4.0 起 Action 的形狀跟 XamlContrast 一樣：inputs 的集合、每個 input 都寫在兩份 README 裡、
/// 第三方 action 一律釘 40 碼 commit SHA、最後一步才依退出碼擋。
/// 改 inputs 是對使用者的承諾變動：要同時改 README、CHANGELOG 的 Upgrading，再來改這裡的清單。
/// </summary>
public class ActionContractTests
{
    private static readonly string[] Inputs =
    [
        "root", "working-directory", "version", "resx", "pseudo", "metrics", "config", "baseline",
        "fail-on", "min-coverage", "upload-report", "sarif", "artifact-name",
    ];

    private static string? Read(string relative) =>
        DataFileTests.RepoRoot() is { } root ? File.ReadAllText(Path.Combine(root, relative)) : null;

    private static IReadOnlyList<string> DeclaredInputs(string yaml)
    {
        // inputs: 區塊裡縮排兩格的鍵（不用 YAML 函式庫：測試不多帶相依）
        var block = yaml[yaml.IndexOf("inputs:", StringComparison.Ordinal)..yaml.IndexOf("runs:", StringComparison.Ordinal)];
        return Regex.Matches(block, @"^  ([a-z][a-z-]*):\s*$", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void The_inputs_are_frozen()
    {
        if (Read("action.yml") is not { } yaml) return;
        Assert.Equal(Inputs, DeclaredInputs(yaml));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("README.zh-Hant.md")]
    public void Every_input_is_documented(string readme)
    {
        if (Read(readme) is not { } text) return;
        foreach (var input in Inputs)
            Assert.True(text.Contains($"`{input}`", StringComparison.Ordinal), $"{readme} does not mention the action input `{input}`");
    }

    [Fact]
    public void Third_party_actions_are_pinned_to_commit_shas()
    {
        // 移動 tag（@v4）會讓別人的新版默默進來；2026-10 稽核後六個 repo 都釘 SHA
        if (Read("action.yml") is not { } yaml) return;
        var uses = Regex.Matches(yaml, @"^\s+uses:\s*(\S+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(uses);
        foreach (var u in uses)
            Assert.Matches(@"^[^@]+@[0-9a-f]{40}$", u);
    }

    [Fact]
    public void The_shape_matches_the_xamlcontrast_action()
    {
        if (Read("action.yml") is not { } yaml) return;
        // 先驗再裝、輸出包在 stop-commands 裡、root 在 -- 後面、最後一步才擋
        Assert.Contains("- name: Validate inputs", yaml);
        Assert.Contains("::stop-commands::", yaml);
        Assert.Contains("args+=(-- \"$XF_ROOT\")", yaml);
        Assert.EndsWith("run: exit \"$XF_EXIT\"", yaml.TrimEnd());
        // inputs 一律經由 env 進 shell，run: 裡不准直接內插
        var runBlocks = Regex.Matches(yaml, @"run: \|((?:\n(?: {8}|\s*$).*)+)", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
        foreach (var block in runBlocks)
            Assert.DoesNotContain("${{ inputs.", block);
    }
}
