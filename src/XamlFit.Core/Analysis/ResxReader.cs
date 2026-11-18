using System.Xml.Linq;

namespace XamlFit.Core.Analysis;

/// <summary>一個文化的字串表。</summary>
public sealed record CultureStrings(string Culture, IReadOnlyDictionary<string, string> Values, string File)
{
    /// <summary>每個鍵定義在哪個檔案、第幾行（行號 0 = 不知道）。翻譯完整度的 finding 要指得出位置。
    /// null = 這份表沒記（測試直接建的表）。</summary>
    public IReadOnlyDictionary<string, (string File, int Line)>? Origins { get; init; }
}

/// <summary>
/// 讀 .resx。只取 &lt;data name="K"&gt;&lt;value&gt;V&lt;/value&gt;&lt;/data&gt;，不碰二進位資源。
///
/// 文化從檔名判斷：Strings.resx → 中性；Strings.de.resx → de；Strings.zh-Hant.resx → zh-Hant。
/// </summary>
public static class ResxReader
{
    public const string NeutralCulture = "(neutral)";

    public static IReadOnlyList<CultureStrings> ReadDirectory(string dir)
    {
        var result = new List<CultureStrings>();
        if (!Directory.Exists(dir)) return result;

        // ⚠ 排除規則比對的是「相對於 dir 的路徑」，副檔名不分大小寫 ——
        //   理由見 XamlScanner.EnumerateFiltered 的檔頭，兩個 bug 這裡原本也有。
        foreach (var file in Xaml.XamlScanner.EnumerateFiltered(dir, ".resx", []))
        {
            var one = ReadFile(file);
            if (one is not null) result.Add(one);
        }

        return result;
    }

    public static CultureStrings? ReadFile(string file)
    {
        XDocument doc;
        try { doc = Xaml.SafeXml.Load(file, lineInfo: true); }
        catch { return null; }
        if (doc.Root is null) return null;

        var path = file.Replace('\\', '/');
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var origins = new Dictionary<string, (string File, int Line)>(StringComparer.Ordinal);
        foreach (var data in doc.Root.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            // ⚠ type/mimetype 有值代表那是二進位或非字串資源，不是翻譯對象
            if (data.Attribute("type") is not null || data.Attribute("mimetype") is not null) continue;

            var value = data.Element("value")?.Value;
            if (value is null) continue;
            values[name] = value;
            origins[name] = (path, ((System.Xml.IXmlLineInfo)data).LineNumber);
        }

        return new CultureStrings(CultureOf(file), values, path) { Origins = origins };
    }

    /// <summary>Strings.de-DE.resx → de-DE；Strings.resx → 中性。</summary>
    internal static string CultureOf(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);   // Strings.de-DE
        var dot = name.LastIndexOf('.');
        if (dot < 0) return NeutralCulture;

        var tag = name[(dot + 1)..];

        // ⚠ 不能只檢查「全是字母」—— My.App.Strings.resx 的 "Strings" 會被當成文化標籤。
        //   交給 .NET 判定，而且要 predefinedOnly，否則 ICU 會替任意字串造一個自訂文化。
        try
        {
            System.Globalization.CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            return tag;
        }
        catch (System.Globalization.CultureNotFoundException)
        {
            return LooksLikeCultureTag(tag) ? tag : NeutralCulture;
        }
    }

    /// <summary>
    /// 形狀像語言代碼（<c>sr-SP</c>、<c>pt_BR</c>、<c>de</c>），即使 .NET 不認得它。
    ///
    /// ⚠ CorpusRender 對照時抓到的：Playnite 的 <c>sr_SP.xaml</c> 用的是已經廢止的「塞爾維亞與蒙特內哥羅」代碼，
    ///   .NET 不認得，於是那份譯文被當成**來源語言**併進去，整個塞爾維亞文默默沒有檢查。
    ///   一份看起來是譯文的檔案寧可當成一個不認得的語言，也不要當成來源語言。
    /// </summary>
    internal static bool LooksLikeCultureTag(string tag) =>
        System.Text.RegularExpressions.Regex.IsMatch(tag, "^[a-z]{2,3}([-_]([A-Z]{2}|[A-Z][a-z]{3}|[0-9]{3}))?([-_]([A-Z]{2}|[0-9]{3}))?$");
}
