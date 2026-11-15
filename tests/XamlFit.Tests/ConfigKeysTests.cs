using XamlFit.Core.Analysis;
using XamlFit.Core.Fonts;
using XamlFit.Core.OpenType;

namespace XamlFit.Tests;

public class ConfigKeysTests
{
    private static (FitConfig? Config, IReadOnlyList<FitConfig.Problem> Problems) Load(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"xamlfit-config-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try { return FitConfig.Load(path); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void The_keys_for_declaring_what_the_tool_cannot_see_are_accepted()
    {
        var (config, problems) = Load("""
            {
              "chrome": { "Card": 16, "GroupBox.Header": 9 },
              "treatAs": { "GridEx": "Grid" },
              "absentFonts": ["Segoe UI Variable"],
              "sourceCulture": "zh-Hant",
              "defaultFontByCulture": { "th": "Leelawadee UI" },
              "assumeDefaultTheme": true,
              "displayScales": [1, 1.25]
            }
            """);

        Assert.Empty(problems);
        Assert.Equal(16, config!.Chrome!["Card"]);
        Assert.Equal("Grid", config.TreatAs!["GridEx"]);
        Assert.Equal("zh-Hant", config.SourceCulture);
        Assert.True(config.AssumeDefaultTheme);
        Assert.Equal([1.0, 1.25], config.DisplayScales);
    }

    [Theory]
    [InlineData("{ \"chrome\": { \"ui:Card\": 16 } }", "chrome")]
    [InlineData("{ \"chrome\": { \"Card\": -1 } }", "chrome")]
    [InlineData("{ \"treatAs\": { \"c:GridEx\": \"Grid\" } }", "treatAs")]
    [InlineData("{ \"treatAs\": { \"GridEx\": \"\" } }", "treatAs")]
    [InlineData("{ \"defaultFontByCulture\": { \"th\": \"\" } }", "defaultFontByCulture")]
    [InlineData("{ \"sourceCulture\": \"\" }", "sourceCulture")]
    [InlineData("{ \"displayScales\": [] }", "displayScales")]
    [InlineData("{ \"displayScales\": [0.1] }", "displayScales")]
    public void A_bad_value_names_its_field(string json, string field)
    {
        var (_, problems) = Load(json);
        Assert.Contains(problems, p => p.Field.StartsWith(field, StringComparison.Ordinal));
    }

    [Fact]
    public void A_key_that_does_nothing_is_rejected()
    {
        // ⚠ 第一版接受 "usedAtRuntime" 卻完全沒有用到它 —— 寫了以為有生效，跟打錯字被默默吞掉是同一種問題
        var (_, problems) = Load("{ \"usedAtRuntime\": [\"Icon*\"] }");
        Assert.NotEmpty(problems);
    }
}

public class FontNameTests
{
    [Fact]
    public void The_english_family_name_is_preferred_and_other_languages_become_aliases()
    {
        // ⚠ msjh.ttc 的 name 表第一筆是繁體中文的「微軟正黑體」。第一版拿第一筆，
        //   於是 XAML 裡寫的 FontFamily="Microsoft JhengHei" 對不上，整支字型變成「沒有度量」。
        // 只有裝了這支字型的 Windows 上才驗得到；其他環境這個測試不做任何斷言。
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "msjh.ttc");
        if (!File.Exists(path)) return;

        var faces = FontMetricsExtractor.FromFile(path);
        var family = Assert.Single(faces, f => f.Family == "Microsoft JhengHei");
        Assert.Contains(family.Aliases, a => a.Any(c => c > 0x2E80));
        Assert.Contains(faces, f => f.Family == "Microsoft JhengHei UI");

        var resolver = new FontResolver(faces);
        Assert.NotEmpty(resolver.Resolve("Microsoft JhengHei", 400, FontStyleKind.Normal, "x").Chain);
    }

    [Fact]
    public void Aliases_legacy_names_and_the_variable_flag_survive_the_metrics_file()
    {
        var metrics = Kit.Mono("Segoe UI", 600, legacy: "Segoe UI Semibold", aliases: ["別名"], variable: true);
        var json = MetricsFile.FromMetrics([metrics], "test", new DateOnly(2026, 11, 14)).ToJson();
        var back = MetricsFile.Parse(json).ToMetrics().Single();

        Assert.Equal("Segoe UI Semibold", back.LegacyFamily);
        Assert.Equal(["別名"], back.Aliases);
        Assert.True(back.Variable);
    }

    [Fact]
    public void A_plain_face_writes_none_of_the_optional_fields()
    {
        var json = MetricsFile.FromMetrics([Kit.Mono()], "test", new DateOnly(2026, 11, 14)).ToJson();
        Assert.DoesNotContain("legacyFamily", json);
        Assert.DoesNotContain("variable", json);
        Assert.DoesNotContain("aliases", json);
    }
}
