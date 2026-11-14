using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

public class SymlinkTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"xamlfit-link-{Guid.NewGuid():N}");

    public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

    private void Write(string relative)
    {
        var p = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "<Root/>");
    }

    [Fact]
    public void A_directory_loop_made_of_symlinks_does_not_hang_the_scan()
    {
        Write("Views/Main.xaml");

        // ⚠ Windows 上建立符號連結需要開發人員模式或提升權限。
        //   建不起來時這個測試**沒有真的測到迴圈**——所以它會把這件事說出來，
        //   而不是靜靜地通過（xunit 2.x 沒有 Assert.Skip，不為此加套件）。
        var link = Path.Combine(_root, "Views", "loop");
        var linkCreated = true;
        try { Directory.CreateSymbolicLink(link, _root); }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            linkCreated = false;
            Console.Error.WriteLine(
                $"SymlinkTests: 這台機器建不了符號連結（{ex.GetType().Name}），" +
                "迴圈防線沒有被真的驗證到。要驗請在 Linux CI 上跑。");
        }

        // 沒有防線的話這裡會無限遞迴。正常情況是瞬間完成。
        var found = XamlScanner.EnumerateFiltered(_root, ".xaml", []).ToList();

        Assert.Single(found);
        Assert.Equal("Main.xaml", Path.GetFileName(found[0]));

        // 連結建得起來時，額外確認它真的沒被走進去
        if (linkCreated)
            Assert.DoesNotContain(found, f => f.Replace('\\', '/').Contains("/loop/"));
    }

    [Fact]
    public void The_scan_survives_a_directory_it_cannot_read()
    {
        // 權限不足或斷線的網路磁碟不該讓整個掃描失敗，只該跳過那一個目錄
        Write("Views/Main.xaml");
        Write("Other/Second.xaml");

        var found = XamlScanner.EnumerateFiltered(_root, ".xaml", [])
            .Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["Main.xaml", "Second.xaml"], found);
    }
}

public class LinkDetectionTests
{
    /// <summary>
    /// 連結偵測的正向驗證：拿 Windows 本身就有的 junction 來測。
    ///
    /// 這台開發機建不了符號連結（需要開發人員模式），但系統裡本來就有 junction，
    /// 所以**偵測這一半驗得到**；「迴圈真的不會無限遞迴」那一半仍然只有 Linux CI 驗得到。
    /// </summary>
    [Fact]
    public void Real_windows_junctions_are_recognised_as_links()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("LinkDetectionTests: 非 Windows，跳過（這個驗證針對 junction）。");
            return;
        }

        string[] knownJunctions =
        [
            @"C:\Users\All Users",
            @"C:\Documents and Settings",
            @"C:\Users\Default User",
        ];

        var checkedAny = false;
        foreach (var p in knownJunctions)
        {
            DirectoryInfo di;
            try { di = new DirectoryInfo(p); } catch { continue; }

            bool isLink;
            try { isLink = di.LinkTarget is not null || di.Attributes.HasFlag(FileAttributes.ReparsePoint); }
            catch { continue; }   // 讀不到屬性的情形由 IsLink 的 catch 保守處理

            checkedAny = true;
            Assert.True(isLink, $"{p} 應該被認成連結");
        }

        if (!checkedAny)
            Console.Error.WriteLine("LinkDetectionTests: 這台機器上找不到已知的 junction，偵測沒有被驗證到。");
    }
}
