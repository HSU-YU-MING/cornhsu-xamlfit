using XamlFit.Core.Analysis;
using XamlFit.Core.Report;

namespace XamlFit.Tests;

public class ExpansionTableTests
{
    [Theory]
    [InlineData(4, 2.00)]
    [InlineData(10, 2.00)]
    [InlineData(11, 1.80)]
    [InlineData(25, 1.60)]
    [InlineData(40, 1.30)]
    [InlineData(200, 1.20)]
    public void Short_strings_expand_most(int length, double factor) =>
        Assert.Equal(factor, ExpansionTable.FactorFor(length));

    [Fact]
    public void Single_factor_would_be_wrong_for_button_labels()
    {
        // "Save" → "Speichern" 是 +150%。用單一 1.4 會嚴重低估最常壞掉的那一類。
        Assert.True(ExpansionTable.FactorFor("Save") > 1.4);
    }
}

public class ResxReaderTests
{
    [Theory]
    [InlineData("Strings.resx", "(neutral)")]
    [InlineData("Strings.de.resx", "de")]
    [InlineData("Strings.de-DE.resx", "de-DE")]
    [InlineData("Strings.zh-Hant.resx", "zh-Hant")]
    [InlineData("My.App.Strings.resx", "(neutral)")]
    public void Culture_comes_from_the_file_name(string file, string expected) =>
        Assert.Equal(expected, ResxReader.CultureOf(file));

    [Fact]
    public void Reads_string_data_and_skips_binary_entries()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xamlfit-test-{Guid.NewGuid():N}.de.resx");
        File.WriteAllText(path, """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Save"><value>Speichern</value></data>
              <data name="Logo" type="System.Drawing.Bitmap, System.Drawing"><value>logo.png</value></data>
              <data name="Blob" mimetype="application/x-microsoft.net.object.binary.base64"><value>AAAA</value></data>
            </root>
            """);

        try
        {
            var r = ResxReader.ReadFile(path);
            Assert.NotNull(r);
            Assert.Equal("de", r!.Culture);
            Assert.Single(r.Values);
            Assert.Equal("Speichern", r.Values["Save"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_broken_file_returns_null_instead_of_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"xamlfit-test-{Guid.NewGuid():N}.resx");
        File.WriteAllText(path, "<root><data name=");
        try { Assert.Null(ResxReader.ReadFile(path)); }
        finally { File.Delete(path); }
    }
}

public class PseudoTests
{
    [Fact]
    public void Pseudoized_text_reaches_the_target_length()
    {
        var s = CheckAnalyzer.Pseudoize("Save", 2.0);
        Assert.Equal(8, s.Length);
        Assert.StartsWith("Save", s);
    }

    [Fact]
    public void A_factor_of_one_leaves_the_string_alone() =>
        Assert.Equal("Save", CheckAnalyzer.Pseudoize("Save", 1.0));
}

public class BaselineTests
{
    private static FitFinding Finding(string key, string site, FitKind kind = FitKind.Overflow) => new()
    {
        Kind = kind,
        Severity = FitSeverity.Fail,
        Key = key,
        Culture = "de",
        Text = "x",
        Site = site,
        LimitPx = 10,
        ActualPx = 20,
    };

    [Fact]
    public void Baseline_key_ignores_the_line_number()
    {
        // 行號進了鍵，一次格式化就讓整份 baseline 失效，然後使用者就把它關掉了
        var a = Finding("Save", "Views/Main.xaml:42");
        var b = Finding("Save", "Views/Main.xaml:900");
        Assert.Equal(a.BaselineKey, b.BaselineKey);
    }

    [Fact]
    public void Baseline_key_ignores_the_pixel_values()
    {
        // 像素值會隨 fonts export 重產而變
        var a = Finding("Save", "Views/Main.xaml:42");
        var b = a with { LimitPx = 999, ActualPx = 1234 };
        Assert.Equal(a.BaselineKey, b.BaselineKey);
    }

    [Fact]
    public void Different_kinds_at_the_same_site_are_different_entries()
    {
        var a = Finding("Save", "Views/Main.xaml:42", FitKind.Overflow);
        var b = Finding("Save", "Views/Main.xaml:42", FitKind.VerticalClip);
        Assert.NotEqual(a.BaselineKey, b.BaselineKey);
    }

    [Fact]
    public void Roundtrips()
    {
        var bl = Baseline.FromFindings([Finding("Save", "a.xaml:1"), Finding("Del", "b.xaml:2")],
            new DateOnly(2026, 11, 14));
        var back = Baseline.Parse(bl.ToJson());
        Assert.Equal(2, back.Known.Count);
        Assert.Equal(bl.AsSet(), back.AsSet());
    }

    [Fact]
    public void Rejects_a_newer_key_version_instead_of_misreading_it()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            Baseline.Parse("""{"keyVersion":99,"known":[]}"""));
        Assert.Contains("upgrade xamlfit", ex.Message);
    }
}

public class CsvTests
{
    [Fact]
    public void Budget_report_never_writes_a_blank_status()
    {
        // 在地化負責人看到空白會當成「沒限制」然後讓譯者自由發揮
        foreach (BudgetStatus s in Enum.GetValues<BudgetStatus>())
            Assert.NotEmpty(BudgetReport.StatusSlug(s));
    }
}

public class LetterFilterTests
{
    [Theory]
    [InlineData("Save", true)]
    [InlineData("儲存", true)]
    [InlineData("→", false)]
    [InlineData("▲", false)]
    [InlineData("00", false)]
    [InlineData(":", false)]
    [InlineData("x²", true)]
    public void Only_strings_with_letters_are_translation_targets(string text, bool expected) =>
        Assert.Equal(expected, BudgetAnalyzer.HasLetter(text));
}
