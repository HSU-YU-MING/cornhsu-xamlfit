using System.Xml.Linq;
using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>「寧可說沒有這支字型的度量，也不要拿別支字型去量」。</summary>
public class StrictFontResolutionTests
{
    private static FontLookup Resolve(FontResolver fonts, string family, int weight = 400,
        FontStyleKind style = FontStyleKind.Normal) => fonts.Resolve(family, weight, style, "Mono");

    [Fact]
    public void Bold_is_not_measured_with_the_regular_face()
    {
        // ⚠ 第一版取「最接近的字重」而且沒有距離上限：度量檔只有 Regular 時，粗體文字用 Regular 量。
        //   粗體比較寬（連 WPF 的模擬粗體也是）—— 低估文字寬度就是漏報。
        var lookup = Resolve(Fonts(Mono(weight: 400)), "Mono", weight: 700);
        Assert.Empty(lookup.Chain);
        Assert.Equal(FontMissReason.NoSuchFace, lookup.Miss);
    }

    [Theory]
    [InlineData(600, 700)]   // SemiBold 沒有 → 用 Bold（WPF 也是）
    [InlineData(500, 400)]
    [InlineData(300, 400)]
    public void A_neighbouring_weight_is_accepted(int requested, int expected)
    {
        var lookup = Resolve(Fonts(Mono(weight: 400), Mono(weight: 700)), "Mono", requested);
        Assert.Equal(expected, lookup.Chain[0].Weight);
    }

    [Fact]
    public void A_variable_font_only_answers_for_its_default_weight()
    {
        // 可變字型的檔案裡只有預設軸位置的寬度
        var fonts = Fonts(Mono(weight: 400, variable: true));
        Assert.Single(Resolve(fonts, "Mono", 400).Chain);
        Assert.Empty(Resolve(fonts, "Mono", 500).Chain);
    }

    [Fact]
    public void A_condensed_face_is_not_used_for_normal_text()
    {
        // 「Arial Narrow」在字型家族模型裡屬於 Arial
        var fonts = Fonts(Mono(stretch: "Condensed"));
        Assert.Empty(Resolve(fonts, "Mono").Chain);

        var both = Fonts(Mono(stretch: "Condensed"), Mono());
        Assert.Equal("Normal", Resolve(both, "Mono").Chain[0].Stretch);
    }

    [Fact]
    public void An_italic_request_falls_back_to_upright_but_not_the_reverse()
    {
        // WPF 會做合成斜體，前進寬度不變；要正體卻只有斜體則沒有可用的字面
        Assert.Single(Resolve(Fonts(Mono()), "Mono", style: FontStyleKind.Italic).Chain);
        Assert.Empty(Resolve(Fonts(Mono(style: FontStyleKind.Italic)), "Mono").Chain);
    }

    [Fact]
    public void The_legacy_family_name_selects_its_face()
    {
        // WPF 兩種寫法都認：FontFamily="Segoe UI" FontWeight="SemiBold" 與 FontFamily="Segoe UI Semibold"
        var fonts = Fonts(Mono("Segoe UI", 400), Mono("Segoe UI", 600, legacy: "Segoe UI Semibold"));
        var lookup = Resolve(fonts, "Segoe UI Semibold");
        Assert.Equal(600, lookup.Chain[0].Weight);
    }

    [Fact]
    public void A_localized_family_name_finds_the_same_font()
    {
        var fonts = Fonts(Mono("Microsoft JhengHei", aliases: ["微軟正黑體"]));
        Assert.Equal("Microsoft JhengHei", Resolve(fonts, "微軟正黑體").Chain[0].Family);
    }

    [Fact]
    public void Characters_the_font_lacks_are_drawn_with_WPFs_choice()
    {
        // ⚠ CorpusRender 實測：WPF 補日文假名用 Yu Gothic UI（元素語言是預設的 en-US 時），
        //   補韓文用 Malgun Gothic —— 照它自己的 GlobalUserInterface.CompositeFont。
        //   第一版依字母順序挑度量檔裡的字型，日文量寬了 40%。
        var fonts = Fonts(
            Mono("Latin", cjk: false),
            Mono("Microsoft JhengHei UI"),                                   // 字母順序在前，但 WPF 不拿它補假名
            KanaFont("Yu Gothic UI", 700),
            KanaFont("Malgun Gothic", 900));

        var lookup = fonts.Resolve("Latin", 400, FontStyleKind.Normal, "x");
        Assert.Equal("Yu Gothic UI", lookup.Fallback!(0x30A2)!.Value.Face.Family);   // カタカナ
        Assert.Equal(7, TextMeasure.Measure("ア", lookup, 10).WidthPx, 3);
        Assert.True(TextMeasure.Measure("ア", lookup, 10).UsedAutoFallback);
    }

    private static FontMetrics Face(string family, int weight, int start, int end, int advance) => new()
    {
        Family = family, Weight = weight, UnitsPerEm = 1000, Ascender = 800, Descender = -200, DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, 500), new AdvanceRange(start, end, advance)],
    };

    [Theory]
    [InlineData(400)]
    [InlineData(700)]
    public void A_character_the_face_lacks_comes_from_another_face_of_the_same_family_first(int weight)
    {
        // ⚠ LayoutOracle 實測：微軟正黑體 UI 的 Regular 與 Bold 都沒有西里爾字母，Light 有 ——
        //   WPF 用 Light 的寬度（粗體文字也一樣），不是先跳到 Segoe UI。
        //   寫成 "正黑體, Segoe UI" 也一樣：同家族的其他字面排在清單的下一個家族前面。
        var fonts = Fonts(
            Face("Hei", 400, 0x4E00, 0x9FFF, 1000),
            Face("Hei", 700, 0x4E00, 0x9FFF, 1000),
            Face("Hei", 290, 0x0400, 0x04FF, 640),
            Face("Segoe UI", 400, 0x0400, 0x04FF, 600),
            Face("Segoe UI", 700, 0x0400, 0x04FF, 620));

        foreach (var family in new[] { "Hei", "Hei, Segoe UI" })
        {
            var m = TextMeasure.Measure("а", fonts.Resolve(family, weight, FontStyleKind.Normal, "x"), 10);
            Assert.True(m.IsReliable);
            Assert.False(m.UsedAutoFallback);
            Assert.Equal(6.4, m.WidthPx, 3);
        }
    }

    [Fact]
    public void The_closest_face_that_has_the_character_wins()
    {
        // Segoe UI Black 缺的字，WPF 用 Bold 的寬度（不是 Regular）
        var fonts = Fonts(
            Face("S", 900, 0x4E00, 0x4E00, 1000),
            Face("S", 400, 0x0370, 0x0373, 500),
            Face("S", 700, 0x0370, 0x0373, 540));
        Assert.Equal(5.4, TextMeasure.Measure("Ͱ", fonts.Resolve("S", 900, FontStyleKind.Normal, "x"), 10).WidthPx, 3);
    }

    [Fact]
    public void The_element_language_changes_the_fallback()
    {
        var fonts = Fonts(Mono("Latin", cjk: false), KanaFont("Yu Gothic UI", 700), KanaFont("Microsoft JhengHei UI", 1000));
        Assert.Equal("Yu Gothic UI", fonts.Resolve("Latin", 400, FontStyleKind.Normal, "x", "en-US").Fallback!(0x30A2)!.Value.Face.Family);
        Assert.Equal("Yu Gothic UI", fonts.Resolve("Latin", 400, FontStyleKind.Normal, "x", "ja-JP").Fallback!(0x30A2)!.Value.Face.Family);
        Assert.Equal("Microsoft JhengHei UI", fonts.Resolve("Latin", 400, FontStyleKind.Normal, "x", "zh-TW").Fallback!(0x30A2)!.Value.Face.Family);
    }

    [Theory]
    [InlineData("zh-TW", "zh-Hant", true)]
    [InlineData("zh-HK", "zh-HK", true)]
    [InlineData("zh-HK", "zh-Hant", true)]
    [InlineData("zh-CN", "zh-Hans", true)]
    [InlineData("zh-Hant-TW", "zh-Hant", true)]
    [InlineData("ja-JP", "ja", true)]
    [InlineData("ko-KR", "ko", true)]
    [InlineData("en-US", "ja", false)]
    [InlineData("zh-TW", "zh-Hans", false)]
    public void Language_matching_does_not_depend_on_the_culture_database(string element, string map, bool expected)
    {
        // Linux 的 CI 常開 globalization-invariant 模式，那裡查不到任何文化資料
        Assert.Equal(expected, FontFallback.LanguageMatches(map, element));
    }

    [Fact]
    public void Without_WPFs_fallback_font_the_character_is_unmeasurable()
    {
        // WPF 會用 Yu Gothic UI 補，而度量檔裡沒有它：量不到，不是拿別支代替
        var fonts = Fonts(Mono("Latin", cjk: false), Mono("Microsoft JhengHei UI"));
        var m = TextMeasure.Measure("ア", fonts.Resolve("Latin", 400, FontStyleKind.Normal, "x"), 10);
        Assert.False(m.IsReliable);
        Assert.Equal(0x30A2, m.FirstMissing);
    }

    private static FontMetrics KanaFont(string family, int kanaAdvance) => new()
    {
        Family = family, UnitsPerEm = 1000, Ascender = 800, Descender = -200, DefaultAdvance = 500,
        Ranges = [new AdvanceRange(0x20, 0x7E, 500), new AdvanceRange(0x3040, 0x30FF, kanaAdvance), new AdvanceRange(0x4E00, 0x9FFF, 1000)],
    };

    [Theory]
    [InlineData("Bold", true, 700)]
    [InlineData("650", true, 650)]
    [InlineData(null, true, 400)]
    [InlineData("Chunky", false, 400)]
    [InlineData("{Binding W}", false, 400)]
    public void Weight_parsing_reports_what_it_cannot_read(string? text, bool ok, int weight)
    {
        Assert.Equal(ok, FontResolver.TryParseWeight(text, out var w));
        Assert.Equal(weight, w);
    }

    [Fact]
    public void Measurement_names_the_first_uncovered_character()
    {
        var m = TextMeasure.Measure("AB☃C", [Mono(cjk: false)], 10);
        Assert.Equal(1, m.MissingGlyphs);
        Assert.Equal(0x2603, m.FirstMissing);
        Assert.False(m.IsReliable);
    }

    [Theory]
    [InlineData(0xFE0F)]   // ☀️ 的變體選擇符
    [InlineData(0x200B)]   // 零寬空白
    [InlineData(0x200D)]   // 零寬連接符
    [InlineData(0xFEFF)]   // BOM
    [InlineData(0x2060)]   // word joiner
    public void Invisible_characters_have_no_width_and_are_not_missing(int cp)
    {
        // LayoutOracle 實測：WPF 給這些字的寬度都是 0
        var text = "AA" + char.ConvertFromUtf32(cp);
        var m = TextMeasure.Measure(text, [Mono(cjk: false)], 10);
        Assert.True(m.IsReliable);
        Assert.Equal(10, m.WidthPx, 3);
    }

    [Theory]
    [InlineData(0x0E01)]   // 泰文
    [InlineData(0x0E81)]   // 寮文
    [InlineData(0x1000)]   // 緬甸文
    [InlineData(0x0750)]   // 阿拉伯文補充
    [InlineData(0x1820)]   // 蒙古文
    public void Scripts_that_reshape_in_context_are_flagged(int codepoint) =>
        Assert.True(TextMeasure.IsComplexScript(codepoint));
}

public class FontSpecTests
{
    private static FontSpec Read(string attributes, string container = "")
    {
        var doc = XDocument.Parse($"<StackPanel {Ns} {container}><TextBlock x:Name=\"probe\" {attributes}/></StackPanel>");
        return FontSpec.Read(new StyleIndex(), Probe(doc), "Mono", 12);
    }

    [Fact]
    public void Defaults_apply_when_nothing_is_set()
    {
        var f = Read("");
        Assert.Null(f.Unreadable);
        Assert.Equal("Mono", f.Family);
        Assert.Equal(12, f.Size);
        Assert.Equal(400, f.Weight);
    }

    [Theory]
    [InlineData("FontFamily=\"{StaticResource UiFont}\"", "FontFamily")]
    [InlineData("FontSize=\"{Binding Size}\"", "FontSize")]
    [InlineData("FontWeight=\"{Binding W}\"", "FontWeight")]
    public void An_unreadable_font_property_is_carried_out_not_defaulted(string attributes, string property)
    {
        // ⚠ 語料實測 FontFamily 用資源引用寫的比字面多一倍；第一版默默換成預設字型去量
        var f = Read(attributes);
        Assert.NotNull(f.Unreadable);
        Assert.Contains(property, f.Unreadable);
    }

    [Fact]
    public void Display_formatting_mode_cannot_be_measured_from_metrics()
    {
        // LayoutOracle 實測：Display 模式的寬度跟線性加總差到 −37% ~ +45%
        Assert.Contains("Display", Read("", "TextOptions.TextFormattingMode=\"Display\"").Unreadable);
        Assert.Null(Read("", "TextOptions.TextFormattingMode=\"Ideal\"").Unreadable);
    }

    [Fact]
    public void A_non_normal_stretch_selects_a_face_we_do_not_measure()
    {
        Assert.Contains("FontStretch", Read("FontStretch=\"Condensed\"").Unreadable);
        Assert.Null(Read("FontStretch=\"Normal\"").Unreadable);
    }

    [Theory]
    [InlineData("14", 14)]
    [InlineData("14px", 14)]
    [InlineData("9pt", 12)]
    [InlineData("0.5in", 48)]
    public void Font_size_units_are_converted(string text, double px)
    {
        Assert.True(FontSpec.TryFontSize(text, out var size));
        Assert.Equal(px, size, 3);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("big")]
    public void A_nonsense_font_size_is_rejected(string text) => Assert.False(FontSpec.TryFontSize(text, out _));
}

public class TextRulesTests
{
    [Theory]
    [InlineData("Button", "_Save", "Save")]
    [InlineData("Button", "Save _As", "Save As")]
    [InlineData("Label", "A__B", "A_B")]
    [InlineData("MenuItem", "_File", "File")]
    [InlineData("CheckBox", "_One _Two", "One _Two")]     // 只有第一個是快速鍵標記
    [InlineData("TextBlock", "_Save", "_Save")]            // TextBlock 會把底線畫出來
    [InlineData("ListBoxItem", "_Save", "_Save")]
    [InlineData("ComboBoxItem", "_Save", "_Save")]
    public void Access_key_underscores_are_not_drawn(string element, string text, string shown)
    {
        // LayoutOracle 實測：Content="_Save" 的 Button 跟 Content="Save" 一樣寬
        Assert.Equal(shown, TextRules.Display(element, text));
    }

    [Fact]
    public void Explicit_line_breaks_split_the_text() =>
        Assert.Equal(["one", "two", "three"], TextRules.Lines("one\r\ntwo\nthree"));

    [Theory]
    [InlineData("Deleted {0} files", true)]
    [InlineData("{0:N2} MB", true)]
    [InlineData("Hello {name}", true)]
    [InlineData("%d items", true)]
    [InlineData("%1$s of %2$s", true)]
    [InlineData("Save", false)]
    [InlineData("50% discount", false)]
    [InlineData("{{literal}}", false)]
    [InlineData("a { b } c", false)]
    public void Format_placeholders_are_recognised(string text, bool expected) =>
        Assert.Equal(expected, TextRules.HasPlaceholder(text));

    [Fact]
    public void Element_content_collapses_whitespace_unless_preserved()
    {
        // XAML 把元素內容裡的連續空白（含換行與縮排）收成一個空格
        var collapsed = XElement.Parse("<T>\n      Hello\n      World\n    </T>");
        Assert.Equal("Hello World", TextRules.ElementText(collapsed));

        // ⚠ 實查 ScreenToGif：xml:space="preserve" 寫在字典的根元素上，字串裡用 &#x0d; 換行
        var preserved = XElement.Parse("<R xml:space=\"preserve\"><T>line one&#x0d;line two</T></R>").Elements().Single();
        Assert.Equal(2, TextRules.Lines(TextRules.ElementText(preserved)).Length);
    }

    [Theory]
    [InlineData("M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z", true)]
    [InlineData("M 0 0 L 10 10 Z", true)]
    [InlineData("Move items", false)]
    [InlineData("Menu", false)]
    [InlineData("M1", false)]
    public void Vector_path_data_is_not_text(string text, bool expected) =>
        Assert.Equal(expected, TextRules.IsPathData(text));
}

public class CultureFontsTests
{
    [Theory]
    [InlineData("zh-Hant", "Microsoft JhengHei UI")]
    [InlineData("zh-TW", "Microsoft JhengHei UI")]
    [InlineData("zh-Hant-HK", "Microsoft JhengHei UI")]
    [InlineData("zh", "Microsoft YaHei UI")]
    [InlineData("zh-CN", "Microsoft YaHei UI")]
    [InlineData("ja", "Yu Gothic UI")]
    [InlineData("ja-JP", "Yu Gothic UI")]
    [InlineData("ko-KR", "Malgun Gothic")]
    public void East_asian_windows_uses_its_own_ui_font(string culture, string font)
    {
        // ⚠ WPF 的預設字型是「系統訊息字型」，跟著 Windows 的語言走。
        //   LayoutOracle 在繁中系統上實測是 Microsoft JhengHei UI，拉丁字母比 Segoe UI 寬 7%。
        Assert.Equal(font, CultureFonts.DefaultFor(culture));
    }

    [Theory]
    [InlineData("de")]
    [InlineData("fr-CA")]
    [InlineData("ru")]
    [InlineData("zhx")]   // 不是 zh
    public void Other_languages_use_the_common_default(string culture) =>
        Assert.Null(CultureFonts.DefaultFor(culture));

    [Fact]
    public void Config_overrides_win_and_the_longest_prefix_is_used()
    {
        var overrides = new Dictionary<string, string> { ["th"] = "Leelawadee UI", ["zh"] = "A", ["zh-Hant"] = "B" };
        Assert.Equal("Leelawadee UI", CultureFonts.DefaultFor("th-TH", overrides));
        Assert.Equal("B", CultureFonts.DefaultFor("zh-Hant-TW", overrides));
        Assert.Equal("A", CultureFonts.DefaultFor("zh-CN", overrides));
    }
}

public class ChromeTableRulesTests
{
    private static XName Wpf(string name) => XName.Get(name, "http://schemas.microsoft.com/winfx/2006/xaml/presentation");

    [Fact]
    public void Panels_and_text_have_no_template()
    {
        foreach (var name in new[] { "Grid", "StackPanel", "Border", "TextBlock", "ContentPresenter", "ScrollContentPresenter" })
            Assert.Equal(ChromeKind.NoTemplate, Chrome.Lookup(Wpf(name)).Kind);
    }

    [Fact]
    public void The_table_carries_the_measured_values()
    {
        Assert.Equal(6, Chrome.Lookup(Wpf("TabControl")).Chrome);
        Assert.Equal(0, Chrome.Lookup(Wpf("UserControl")).Chrome);
        Assert.Equal(9, Chrome.HeaderChrome("GroupBox", wpf: true));
        Assert.Equal(27, Chrome.HeaderChrome("Expander", wpf: true));
        Assert.Equal(10, Chrome.HeaderChrome("DataGridColumnHeader", wpf: true));
        Assert.Null(Chrome.HeaderChrome("GroupBox", wpf: false));
        Assert.Null(Chrome.HeaderChrome("GroupBox", wpf: true, externalTheme: "T"));
        Assert.True(Chrome.ScrollBarWidth >= 17);
    }

    [Fact]
    public void Inset_modes_say_what_an_explicit_value_does()
    {
        var button = Chrome.Lookup(Wpf("Button"));
        Assert.Equal(38, button.PaddingDelta(40));          // 取代預設的 2
        var content = Chrome.Lookup(Wpf("ContentControl"));
        Assert.Equal(0, content.PaddingDelta(40));          // 預設範本根本不用 Padding
        var combo = Chrome.Lookup(Wpf("ComboBox"));
        Assert.Equal(40, combo.PaddingDelta(40));           // 沒實測過：保守地加上去
    }

    [Fact]
    public void Template_defaults_differ_from_effective_padding_for_CheckBox()
    {
        // 預設範本靠觸發器把 Padding 設成 4；樣式本身是 0（自訂範本拿到的是 0）
        Assert.Equal(0, Chrome.DefaultsOf(Wpf("CheckBox"))!.Value.Padding);
        Assert.Equal(4, Chrome.Lookup(Wpf("CheckBox")).DefaultPadding);
    }

    [Fact]
    public void A_header_override_uses_the_dotted_key()
    {
        var table = Chrome.WithOverrides(new Dictionary<string, double> { ["Card.Header"] = 12 });
        Assert.Equal(12, table.HeaderChrome("Card", wpf: false));
    }
}
