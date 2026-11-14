using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

public class EnumerationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"xamlfit-enum-{Guid.NewGuid():N}");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Write(string relative, string content = "<Root/>")
    {
        var p = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, content);
    }

    private string[] Found(string? subRoot = null) =>
        [.. XamlScanner.EnumerateFiltered(subRoot ?? _root, ".xaml", [])
            .Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];

    [Fact]
    public void Build_output_and_dot_directories_are_excluded()
    {
        Write("Views/Main.xaml");
        Write("bin/Release/Main.xaml");
        Write("obj/Debug/Main.xaml");
        Write(".claude/worktrees/copy/Main.xaml");
        Write("node_modules/pkg/Main.xaml");

        Assert.Equal(["Main.xaml"], Found());
    }

    [Fact]
    public void A_scan_root_inside_a_dot_directory_still_finds_its_files()
    {
        // ⚠ 用完整路徑比對時，這個情形會排除掉**每一個**檔案，
        //   然後吐 exit 3 說「找不到任何文字位置」而不解釋為什麼。
        //   實際會中招的路徑：C:\Users\x\.config\app、/home/u/.local/src/app
        Write(".config/myapp/Views/Main.xaml");
        var subRoot = Path.Combine(_root, ".config", "myapp");

        Assert.Equal(["Main.xaml"], Found(subRoot));
    }

    [Fact]
    public void A_scan_root_under_a_folder_named_bin_still_finds_its_files()
    {
        Write("bin/myproject/Views/Main.xaml");
        var subRoot = Path.Combine(_root, "bin", "myproject");

        Assert.Equal(["Main.xaml"], Found(subRoot));
    }

    [Fact]
    public void Extensions_match_regardless_of_case()
    {
        // Windows 的 *.xaml 會配到 .XAML，Linux 不會 —— 同一個專案在 CI 上掃到的檔案
        // 比本機少，而且是靜默的。CI 看起來比本機乾淨，那正是謊報健康。
        Write("Views/Lower.xaml");
        Write("Views/Upper.XAML");
        Write("Views/Mixed.Xaml");

        Assert.Equal(["Lower.xaml", "Mixed.Xaml", "Upper.XAML"], Found());
    }

    [Fact]
    public void A_file_whose_name_merely_contains_the_extension_is_not_matched()
    {
        Write("Views/notes.xaml.txt");
        Write("Views/Real.xaml");

        Assert.Equal(["Real.xaml"], Found());
    }

    [Fact]
    public void Explicit_excludes_apply_to_the_relative_path()
    {
        Write("Generated/Auto.xaml");
        Write("Views/Main.xaml");

        var found = XamlScanner.EnumerateFiltered(_root, ".xaml", ["/Generated/"])
            .Select(Path.GetFileName).ToList();
        Assert.Equal(["Main.xaml"], found);
    }
}
