using System.Xml.Linq;

namespace XamlFit.Core.Analysis;

/// <summary>一個文化的字串表。</summary>
public sealed record CultureStrings(string Culture, IReadOnlyDictionary<string, string> Values, string File);

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
        try { doc = XDocument.Load(file); }
        catch { return null; }
        if (doc.Root is null) return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var data in doc.Root.Elements("data"))
        {
            var name = data.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            // ⚠ type/mimetype 有值代表那是二進位或非字串資源，不是翻譯對象
            if (data.Attribute("type") is not null || data.Attribute("mimetype") is not null) continue;

            var value = data.Element("value")?.Value;
            if (value is null) continue;
            values[name] = value;
        }

        return new CultureStrings(CultureOf(file), values, file.Replace('\\', '/'));
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
            return NeutralCulture;
        }
    }
}
