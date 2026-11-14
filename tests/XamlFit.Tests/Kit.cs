using System.Xml.Linq;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;

namespace XamlFit.Tests;

/// <summary>
/// 測試共用的小工具。
///
/// 字型用直接組出來的度量（不經過字型檔）：等寬、每個 ASCII 字元 0.5 em ——
/// 字級 10 時每個字 5 px，預期值心算得出來。
/// </summary>
internal static class Kit
{
    public const string Ns =
        "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
        "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    public static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    public static readonly ChromeTable Chrome = ChromeTable.Builtin();

    public const string MonoFamily = "Mono";
    public const double MonoSize = 10;

    /// <summary>等寬字型：ASCII 可列印字元都是 500／1000 em；中日韓統一表意文字 1000／1000 em。</summary>
    public static FontMetrics Mono(
        string family = MonoFamily, int weight = 400, FontStyleKind style = FontStyleKind.Normal,
        string stretch = "Normal", bool variable = false, string? legacy = null, string[]? aliases = null,
        bool cjk = true) =>
        new()
        {
            Family = family,
            Weight = weight,
            Style = style,
            Stretch = stretch,
            Variable = variable,
            LegacyFamily = legacy,
            Aliases = aliases ?? [],
            UnitsPerEm = 1000,
            Ascender = 800,
            Descender = -200,
            LineGap = 0,
            DefaultAdvance = 500,
            Ranges = cjk
                ? [new AdvanceRange(0x20, 0x7E, 500), new AdvanceRange(0x4E00, 0x9FFF, 1000)]
                : [new AdvanceRange(0x20, 0x7E, 500)],
        };

    public static FontResolver Fonts(params FontMetrics[] metrics) =>
        new(metrics.Length > 0 ? metrics : [Mono()]);

    public static XElement Probe(XDocument doc) =>
        doc.Descendants().First(e => (string?)e.Attribute(X + "Name") == "probe");

    /// <summary>解析一份完整的 XAML（要自己寫根元素），回傳名為 probe 的元素的可用寬度。</summary>
    /// <param name="dictionaries">額外的資源字典檔（檔名, 內容），會進全域索引。</param>
    public static AvailableWidth Resolve(
        string xaml,
        bool measurer = true,
        IReadOnlyDictionary<string, double>? chrome = null,
        string? theme = null,
        (string File, string Xaml)[]? dictionaries = null,
        string file = "Page.xaml")
    {
        var doc = XDocument.Parse(xaml, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);   // 跟 XamlScanner 讀檔一樣保留空白
        doc.AddAnnotation(new SourceFile(file));
        if (theme is not null) doc.AddAnnotation(new ExternalTheme(theme));

        var table = chrome is null ? Chrome : Chrome.WithOverrides(chrome);
        var styles = new StyleIndex { SystemScrollBarWidth = table.ScrollBarWidth };
        styles.Add(doc, file);
        foreach (var (name, content) in dictionaries ?? [])
            styles.Add(XDocument.Parse(content), name);

        IElementMeasurer? m = measurer
            ? new ElementMeasurer(Fonts(), styles, table, MonoFamily, MonoSize)
            : null;

        return new WidthResolver(table, styles, m).Resolve(Probe(doc));
    }

    /// <summary>把片段包在一個沒有欄定義的 Grid 裡（透明容器：不吃寬度、也不提供上界）。</summary>
    public static AvailableWidth In(string body, bool measurer = true, IReadOnlyDictionary<string, double>? chrome = null) =>
        Resolve($"<Grid {Ns} xmlns:ui=\"http://example.com/ui\">{body}</Grid>", measurer, chrome);

    /// <summary>建一個暫存目錄、寫入檔案，回傳路徑。用完記得 <see cref="Cleanup"/>。</summary>
    public static string TempProject(params (string Path, string Content)[] files)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"xamlfit-test-{Guid.NewGuid():N}");
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(dir, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }
        return dir;
    }

    public static void Cleanup(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
