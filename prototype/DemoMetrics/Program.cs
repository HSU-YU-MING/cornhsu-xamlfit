// 產生 samples/demo/xamlfit.fonts.json —— 示範專案與 CI 用的度量檔。
//
// 為什麼不用真的字型：
//   - CI 跑在 Linux，沒有 Segoe UI；拿「機器上剛好有的字型」會讓斷言隨機器變動。
//   - 商用字型的度量不散布（見 THIRD-PARTY-NOTICES.md）。
//
// 所以這裡造一支叫 "XamlFit Demo Sans" 的字型，涵蓋 Latin-1（含德文的 ä ö ü ß），
// 寬度用一張固定的表，確定性、跨平台、沒有授權問題。
//
// 重產：dotnet run --project prototype/DemoMetrics -- samples/demo/xamlfit.fonts.json

using XamlFit.Core.Fonts;

var outPath = args.Length > 0 ? args[0] : "xamlfit.fonts.json";

const int UnitsPerEm = 1000;

// 每個字符的前進寬度（千分之一 em）。數字刻意模仿一般無襯線 UI 字型的比例：
// i/l/t 窄、m/w/W 寬、數字等寬、CJK 全形。
static int AdvanceOf(int cp) => cp switch
{
    ' ' => 260,
    'i' or 'j' or 'l' or '!' or '|' or '.' or ',' or ':' or ';' or '\'' => 240,
    'f' or 'r' or 't' or '(' or ')' or '[' or ']' or '/' => 330,
    'I' => 280,
    'm' => 870,
    'w' or 'M' or 'W' => 800,
    >= '0' and <= '9' => 560,
    >= 'a' and <= 'z' => 540,
    >= 'A' and <= 'Z' => 620,
    0x00DF => 560,                        // ß
    >= 0x00C0 and <= 0x00DE => 620,       // 大寫附加符號
    >= 0x00E0 and <= 0x00FF => 540,       // 小寫附加符號
    >= 0x2000 and <= 0x206F => 400,       // 一般標點（— 「 」 …）
    >= 0x4E00 and <= 0x9FFF => 1000,      // 中日韓統一表意文字：全形
    >= 0x3000 and <= 0x303F => 1000,      // 中日韓標點
    _ => 500,
};

var ranges = new List<AdvanceRange>();
int rs = -1, re = -1, ra = -1;

void Flush() { if (rs >= 0) ranges.Add(new AdvanceRange(rs, re, ra)); }

// 涵蓋：ASCII、Latin-1 補充、一般標點、常用中日韓
foreach (var (from, to) in new[] { (0x20, 0xFF), (0x2010, 0x203A), (0x3000, 0x303F), (0x4E00, 0x9FFF) })
{
    for (var cp = from; cp <= to; cp++)
    {
        var adv = AdvanceOf(cp);
        if (rs >= 0 && cp == re + 1 && adv == ra) { re = cp; continue; }
        Flush();
        rs = re = cp;
        ra = adv;
    }
}
Flush();

var faces = new List<FontMetrics>();
foreach (var (weight, style) in new (int, FontStyleKind)[]
         {
             (400, FontStyleKind.Normal), (600, FontStyleKind.Normal),
             (700, FontStyleKind.Normal), (400, FontStyleKind.Italic),
         })
{
    faces.Add(new FontMetrics
    {
        Family = "XamlFit Demo Sans",
        Weight = weight,
        Style = style,
        UnitsPerEm = UnitsPerEm,
        Ascender = 800,
        Descender = -200,
        LineGap = 0,
        DefaultAdvance = 500,
        // 粗體略寬，斜體同寬 —— 跟真實字型的行為一致
        Ranges = weight >= 600
            ? [.. ranges.Select(r => r with { Advance = (int)(r.Advance * 1.04) })]
            : ranges,
    });
}

var file = MetricsFile.FromMetrics(faces, "demo-generator", DateOnly.FromDateTime(DateTime.Now));
file.Note = "Synthetic font metrics for the XamlFit demo and CI. Not derived from any real font; " +
            "no font software is involved. Regenerate with prototype/DemoMetrics.";

var dir = Path.GetDirectoryName(Path.GetFullPath(outPath));
if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
File.WriteAllText(outPath, file.ToJson());

Console.WriteLine($"wrote {outPath} ({faces.Count} faces, {ranges.Count} ranges, " +
                  $"{new FileInfo(outPath).Length / 1024.0:F0} KB)");
