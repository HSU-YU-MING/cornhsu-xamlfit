using XamlFit.Core.Layout;
using XamlFit.Core.Xaml;
using static XamlFit.Tests.Kit;

namespace XamlFit.Tests;

/// <summary>
/// 量過的控制項函式庫佈景（data/theme-chrome.json，prototype/ThemeChrome 用 WPF 量的）。
/// LayoutOracle --theme mahapps|md2|md3 拿真的 WPF 視窗對照：0 高估、0 錯。
/// 這裡驗證什麼時候套、什麼時候不套。
/// </summary>
public class ThemeProfileTests
{
    private const string MahApps =
        "<ResourceDictionary Source=\"pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml\"/>" +
        "<ResourceDictionary Source=\"pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml\"/>";

    private static string App(string merged) =>
        $"<Application {Ns} xmlns:md=\"http://materialdesigninxaml.net/winfx/xaml/themes\"><Application.Resources><ResourceDictionary>" +
        $"<ResourceDictionary.MergedDictionaries>{merged}</ResourceDictionary.MergedDictionaries></ResourceDictionary></Application.Resources></Application>";

    private static string Project(string package, string version) =>
        $"<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"{package}\" Version=\"{version}\" /></ItemGroup></Project>";

    private static string Window(string body) =>
        $"<Window {Ns} xmlns:mah=\"http://metro.mahapps.com/winfx/xaml/controls\" Width=\"400\">{body}</Window>";

    private static ScanResult Scan(params (string Path, string Content)[] files)
    {
        var dir = TempProject(files);
        try
        {
            // MahApps 的樣式把按鈕字型設成 Segoe UI：測試裡給一支等寬的替身
            return new XamlScanner(Chrome, new ScanOptions { DefaultFontFamily = MonoFamily, DefaultFontSize = MonoSize },
                Fonts(Mono(), Mono("Segoe UI"), Mono("Segoe UI", weight: 600), Mono("Segoe UI", weight: 700))).Scan(dir);
        }
        finally { Cleanup(dir); }
    }

    private static TextSite Site(ScanResult scan, string text) => scan.Sites.Single(s => s.Text == text);

    [Fact]
    public void The_builtin_profiles_load()
    {
        var profiles = ThemeProfiles.Builtin();
        Assert.Contains(profiles, p => p.Id == "MahApps.Metro 2.4" && p.MajorMinor == "2.4" && p.Controls.ContainsKey("Button"));
        Assert.Equal(2, profiles.Count(p => p.Package == "MaterialDesignThemes"));
        Assert.Contains(profiles, p => p.Id == "HandyControl 3.5" && p.Controls["CheckBox"].LayoutRounding);
        Assert.Contains(profiles, p => p.Id == "WPF-UI 4.3");
    }

    [Fact]
    public void A_measured_theme_with_the_same_version_gives_the_measured_chrome()
    {
        var scan = Scan(
            ("App.csproj", Project("MahApps.Metro", "2.4.11")),
            ("App.xaml", App(MahApps)),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));

        Assert.Equal("MahApps.Metro 2.4", scan.ExternalThemes.First().Profile);
        var site = Site(scan, "Save");
        Assert.True(site.Width.Status == WidthStatus.Known, site.Width.Reason);
        Assert.Equal(100 - 12, site.Width.Px, 3);          // MahApps 的 Button：chrome 12
        Assert.Equal(TextCasing.Upper, site.Casing);        // 而且字串畫成大寫
        Assert.Equal(10, site.FontSize, 3);                 // 10 粗體
        Assert.Equal(700, site.FontWeight);
    }

    [Fact]
    public void A_different_version_is_not_measured()
    {
        var scan = Scan(
            ("App.csproj", Project("MahApps.Metro", "1.6.5")),
            ("App.xaml", App(MahApps)),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));

        Assert.Null(scan.ExternalThemes.First().Profile);
        Assert.Contains("1.6.5", scan.ExternalThemes.First().ProfileNote);
        Assert.Equal(WidthStatus.Unmeasurable, Site(scan, "Save").Width.Status);
    }

    [Fact]
    public void Without_a_package_reference_the_version_is_unknown()
    {
        var scan = Scan(("App.xaml", App(MahApps)), ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));
        Assert.Null(scan.ExternalThemes.First().Profile);
        Assert.Equal(WidthStatus.Unmeasurable, Site(scan, "Save").Width.Status);
    }

    [Fact]
    public void Another_unmeasured_theme_in_the_same_scope_cancels_the_profile()
    {
        // 不知道另一個佈景改了哪些控制項
        var scan = Scan(
            ("App.csproj", Project("MahApps.Metro", "2.4.11")),
            ("App.xaml", App(MahApps + "<ResourceDictionary Source=\"pack://application:,,,/Other.Theme;component/Theme.xaml\"/>")),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));

        Assert.All(scan.ExternalThemes, t => Assert.Null(t.Profile));
        Assert.Equal(WidthStatus.Unmeasurable, Site(scan, "Save").Width.Status);
    }

    [Theory]
    [InlineData("MaterialDesign3.Defaults.xaml", "MaterialDesignThemes 5.3 (MaterialDesign3.Defaults)")]
    [InlineData("MaterialDesign2.Defaults.xaml", "MaterialDesignThemes 5.3 (MaterialDesign2.Defaults)")]
    [InlineData("MaterialDesignTheme.Defaults.xaml", null)]   // 舊的檔名：沒量過
    public void MaterialDesign_needs_the_defaults_file_that_was_measured(string defaults, string? profile)
    {
        var scan = Scan(
            ("App.csproj", Project("MaterialDesignThemes", "5.3.0")),
            ("App.xaml", App($"<md:BundledTheme BaseTheme=\"Light\"/><ResourceDictionary Source=\"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/{defaults}\"/>")),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));

        Assert.All(scan.ExternalThemes, t => Assert.Equal(profile, t.Profile));
        var site = Site(scan, "Save");
        if (profile is null) Assert.Equal(WidthStatus.Unmeasurable, site.Width.Status);
        else Assert.Equal(100 - 32, site.Width.Px, 3);       // MaterialDesign 的 Button：chrome 32
    }

    [Fact]
    public void A_project_style_based_on_a_theme_key_resolves_through_the_profile()
    {
        var scan = Scan(
            ("App.csproj", Project("MahApps.Metro", "2.4.11")),
            ("App.xaml", App(MahApps)),
            ("Styles.xaml", $"<ResourceDictionary {Ns}><Style x:Key=\"Mine\" TargetType=\"Button\" BasedOn=\"{{StaticResource MahApps.Styles.Button.Square}}\">" +
                            "<Setter Property=\"FontSize\" Value=\"20\"/></Style></ResourceDictionary>"),
            ("Main.xaml", Window("<Button Width=\"100\" Style=\"{StaticResource Mine}\" Content=\"Save\"/>")));

        var site = Site(scan, "Save");
        Assert.Equal(100 - 10, site.Width.Px, 3);            // Square：chrome 10
        Assert.Equal(20, site.FontSize, 3);                  // 專案樣式蓋掉字級
        Assert.Equal(TextCasing.Lower, site.Casing);         // Square 畫成小寫
    }

    [Fact]
    public void Casing_set_on_the_element_wins()
    {
        var scan = Scan(
            ("App.csproj", Project("MahApps.Metro", "[2.4.11]")),
            ("App.xaml", App(MahApps)),
            ("Main.xaml", Window("<Button Width=\"100\" mah:ControlsHelper.ContentCharacterCasing=\"Normal\" Content=\"Save\"/>")));
        Assert.Equal(TextCasing.AsIs, Site(scan, "Save").Casing);
    }

    [Fact]
    public void A_central_package_version_counts()
    {
        var scan = Scan(
            ("Directory.Packages.props", "<Project><ItemGroup><PackageVersion Include=\"MahApps.Metro\" Version=\"2.4.10\" /></ItemGroup></Project>"),
            ("App.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"MahApps.Metro\" /></ItemGroup></Project>"),
            ("App.xaml", App(MahApps)),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));
        Assert.Equal("MahApps.Metro 2.4", scan.ExternalThemes.First().Profile);
    }

    [Fact]
    public void WPF_UI_is_recognised_by_its_dictionary_elements()
    {
        var scan = Scan(
            ("App.csproj", Project("WPF-UI", "4.3.0")),
            ("App.xaml", $"<Application {Ns} xmlns:ui=\"http://schemas.lepo.co/wpfui/2022/xaml\"><Application.Resources><ResourceDictionary>" +
                         "<ResourceDictionary.MergedDictionaries><ui:ThemesDictionary Theme=\"Dark\"/><ui:ControlsDictionary/></ResourceDictionary.MergedDictionaries>" +
                         "</ResourceDictionary></Application.Resources></Application>"),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));
        Assert.All(scan.ExternalThemes, t => Assert.Equal("WPF-UI 4.3", t.Profile));
        Assert.Equal(100 - 24, Site(scan, "Save").Width.Px, 3);   // WPF-UI 的 Button：chrome 24
        Assert.Equal(14, Site(scan, "Save").FontSize, 3);
    }

    [Fact]
    public void HandyControl_is_recognised_by_its_pack_uris()
    {
        var scan = Scan(
            ("App.csproj", Project("HandyControl", "3.5.1")),
            ("App.xaml", App("<ResourceDictionary Source=\"pack://application:,,,/HandyControl;component/Themes/SkinDefault.xaml\"/>" +
                             "<ResourceDictionary Source=\"pack://application:,,,/HandyControl;component/Themes/Theme.xaml\"/>")),
            ("Main.xaml", Window("<Button Width=\"100\" Content=\"Save\"/>")));
        Assert.All(scan.ExternalThemes, t => Assert.Equal("HandyControl 3.5", t.Profile));
        Assert.Equal(100 - 22, Site(scan, "Save").Width.Px, 3);
    }

    [Fact]
    public void Uppercase_text_is_measured_in_capitals()
    {
        Assert.Equal("SAVE ÄNDERUNGEN", TextRules.Cased("Save Änderungen", TextCasing.Upper));
        Assert.Equal("save", TextRules.Cased("SAVE", TextCasing.Lower));
    }
}
