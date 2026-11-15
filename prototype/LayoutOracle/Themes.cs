using System.Windows;
using XamlFit.Core.Layout;

/// <summary>
/// --theme mahapps|md2|md3：把控制項函式庫的佈景合併進 Application.Resources（跟專案在 App.xaml 裡做的一樣），
/// 同一段 XAML 給真的 WPF 視窗與解析器（解析器拿到 data/theme-chrome.json 的那份量測），逐案比對。
/// 驗證的是：佈景的 chrome、明寫 Padding 的換算、keyed 樣式、樣式設的字型與大小寫（排在同一排的鄰居）、佈景的捲軸寬、MinWidth。
/// </summary>
internal static class Themes
{
    public static ThemeProfile Load(Application app, string which)
    {
        string id;
        switch (which)
        {
            case "mahapps":
                id = "MahApps.Metro 2.4";
                foreach (var s in new[]
                         {
                             "pack://application:,,,/MahApps.Metro;component/Styles/Controls.xaml",
                             "pack://application:,,,/MahApps.Metro;component/Styles/Fonts.xaml",
                             "pack://application:,,,/MahApps.Metro;component/Styles/Themes/Light.Blue.xaml",
                         })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri(s) });
                break;
            case "md2":
            case "md3":
                id = $"MaterialDesignThemes 5.3 (MaterialDesign{which[2]}.Defaults)";
                app.Resources.MergedDictionaries.Add(new MaterialDesignThemes.Wpf.BundledTheme
                {
                    BaseTheme = MaterialDesignThemes.Wpf.BaseTheme.Light,
                    PrimaryColor = MaterialDesignColors.PrimaryColor.DeepPurple,
                    SecondaryColor = MaterialDesignColors.SecondaryColor.Lime,
                });
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"pack://application:,,,/MaterialDesignThemes.Wpf;component/Themes/MaterialDesign{which[2]}.Defaults.xaml"),
                });
                break;
            default:
                throw new ArgumentException(which);
        }
        return ThemeProfiles.Builtin().Single(p => p.Id == id);
    }

    /// <summary>每種佈景都跑的案例；keyed 樣式依佈景換名字。</summary>
    /// <param name="ns">未使用；保留跟其他案例一樣的呼叫方式。</param>
    /// <returns>Unknown：量測資料裡這個控制項的 chrome 是「量不到」（MaterialDesign 的 ToggleButton 是開關，內容寬度不往外傳），
    /// 解析器該回「量不到」。</returns>
    public static IEnumerable<(string Name, string Inner, int Width, bool Unknown)> Cases(string which, string ns, string longText)
    {
        var text = $"<TextBlock x:Name='probe' Text='{longText}'/>";
        var (keyedButton, keyedToggle) = which == "mahapps"
            ? ("MahApps.Styles.Button.Square", "MahApps.Styles.ToggleButton")
            : ("MaterialDesignOutlinedButton", "MaterialDesignFlatToggleButton");

        foreach (var control in new[] { "Button", "ToggleButton", "RepeatButton", "CheckBox", "RadioButton", "Label", "StatusBarItem" })
        {
            var unknown = which != "mahapps" && control == "ToggleButton";
            yield return ($"{control}", $"<{control} Width='200' HorizontalAlignment='Left'>{text}</{control}>", 700, unknown);
            yield return ($"{control} Padding=3", $"<{control} Width='200' Padding='3' HorizontalAlignment='Left'>{text}</{control}>", 700, unknown);
            yield return ($"{control} BorderThickness=4", $"<{control} Width='200' BorderThickness='4' HorizontalAlignment='Left'>{text}</{control}>", 700, unknown);
        }
        yield return ("GroupBox content", $"<GroupBox Width='250' HorizontalAlignment='Left' Header='H'>{text}</GroupBox>", 700, false);
        yield return ("ListBoxItem", $"<ListBox Width='250' HorizontalAlignment='Left' ScrollViewer.HorizontalScrollBarVisibility='Disabled'><ListBoxItem>{text}</ListBoxItem></ListBox>", 700, false);
        yield return ($"keyed {keyedButton}", $"<Button Style='{{StaticResource {keyedButton}}}' Width='200' HorizontalAlignment='Left'>{text}</Button>", 700, false);
        yield return ($"keyed {keyedToggle}", $"<ToggleButton Style='{{StaticResource {keyedToggle}}}' Width='200' HorizontalAlignment='Left'>{text}</ToggleButton>", 700, which != "mahapps");
        yield return ("project style BasedOn keyed",
            $"<Button Width='200' HorizontalAlignment='Left'><Button.Style><Style TargetType='Button' BasedOn='{{StaticResource {keyedButton}}}'><Setter Property='FontSize' Value='20'/></Style></Button.Style>{text}</Button>", 700, false);
        yield return ("ScrollViewer bar visible", $"<ScrollViewer Width='250' HorizontalAlignment='Left' VerticalScrollBarVisibility='Visible' HorizontalScrollBarVisibility='Disabled'>{text}</ScrollViewer>", 700, false);

        // 同一排的鄰居：佈景的字型、字級、粗細、大小寫，加上 chrome 與 MinWidth
        foreach (var label in new[] { "Save changes", "OK", "Apply all settings now" })
        {
            yield return ($"row: Button '{label}' + text",
                $"<StackPanel Orientation='Horizontal' Width='400' HorizontalAlignment='Left'><Button Content='{label}'/>{text}</StackPanel>", 700, false);
            yield return ($"row: keyed Button '{label}' + text",
                $"<StackPanel Orientation='Horizontal' Width='400' HorizontalAlignment='Left'><Button Style='{{StaticResource {keyedButton}}}' Content='{label}'/>{text}</StackPanel>", 700, false);
            yield return ($"row: CheckBox '{label}' + text",
                $"<StackPanel Orientation='Horizontal' Width='400' HorizontalAlignment='Left'><CheckBox Content='{label}'/>{text}</StackPanel>", 700, false);
            yield return ($"row: Label '{label}' + text",
                $"<StackPanel Orientation='Horizontal' Width='400' HorizontalAlignment='Left'><Label Content='{label}'/>{text}</StackPanel>", 700, false);
            yield return ($"grid Auto: Button '{label}' | text",
                $"<Grid Width='400' HorizontalAlignment='Left'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Button Content='{label}'/><TextBlock x:Name='probe' Grid.Column='1' Text='{longText}'/></Grid>", 700, false);
        }
        yield return ("row: Button casing turned off",
            "<StackPanel Orientation='Horizontal' Width='400' HorizontalAlignment='Left' xmlns:mah='http://metro.mahapps.com/winfx/xaml/controls'>" +
            $"<Button Content='Save changes' {(which == "mahapps" ? "mah:ControlsHelper.ContentCharacterCasing='Normal'" : "")}/>{text}</StackPanel>", 700, false);
    }
}
