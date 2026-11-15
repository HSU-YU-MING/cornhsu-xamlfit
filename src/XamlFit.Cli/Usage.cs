namespace XamlFit.Cli;

internal static class Usage
{
    public static void Print(TextWriter w)
    {
        w.WriteLine("""
            xamlfit — static text-fit audit for WPF XAML.

            Computes the pixel budget of every text slot without launching the app.

            USAGE
              xamlfit fonts export [options]      Extract font metrics into xamlfit.fonts.json
              xamlfit fonts list [--metrics <f>]  List the families a metrics file covers
              xamlfit budget <root> [options]     Export per-string pixel budgets (before translation)
              xamlfit check  <root> [options]     Measure real translations, gate CI (after translation)

            FONTS EXPORT
              --from <path>        A font file (.ttf/.otf/.ttc) or a directory of them.
                                   Repeatable. Defaults to the system font directory.
              --out <path>         Output file (default: xamlfit.fonts.json)
              --family <name>      Only export these families. Repeatable.
              --merge              Merge into an existing output file instead of replacing it.

              The metrics file contains numbers only (advance widths, kerning, Arabic letter-
              form widths) — no glyph outlines and no font software. Generate it on a machine that has the fonts, commit it, and CI needs
              no fonts installed.

            BUDGET
              --csv <path>         Write the budget table as CSV (for translation platforms)
              --csv-raw            Do not prefix formula-like text with ' (byte-exact source
                                   strings for API pipelines; unsafe to open in a spreadsheet)
              --json <path>        Write the full report as JSON
              --sites <path>       Write one CSV row per text site: status, bound and the reason
                                   it is blocked. The summary says how many; this says which.
              --resx <path>        Directory holding .resx files, so resource strings get their
                                   current (source-language) width too. Repeatable.
              --metrics <path>     Metrics file (default: xamlfit.fonts.json next to <root>)
              --min-coverage <n>   Resolution floor, 0-100. Below it: exit 3.
                                   Default 0 for budget (an export, not a gate), 60 for check.
              --config <path>      Config file (default: xamlfit.config.json next to <root>).
                                   Command-line flags always win over the file.
              --default-font <s>   Font family assumed where XAML sets none (default: Segoe UI)
              --default-size <n>   Font size assumed where XAML sets none (default: 12)
              --source-culture <c> The language the app is written in. WPF's default font follows
                                   the Windows display language (zh-Hant: Microsoft JhengHei UI,
                                   ja: Yu Gothic UI, ...), so this picks --default-font for you.
              --assume-default-theme
                                   An external theme was detected, but it does not change control
                                   padding: keep using the built-in chrome table.

            CHECK
              All BUDGET options, plus:
              --resx <path>        Directory holding .resx files. Repeatable. XAML string
                                   dictionaries (<sys:String x:Key=...>) under <root> are found
                                   on their own.
              --culture <name>     Only check these cultures. Repeatable. Default: all found.
              --source-only        Check only the source language (hard-coded text and neutral
                                   resources). Needs no translations.
              --pseudo             No translations: simulate expansion instead (reports only)
              --margin <n>         Safety margin percent before calling it tight (default 5)
              --fail-on <level>    'fail' (default) or 'warn'
              --baseline <path>    Only report findings not in this baseline
              --write-baseline <p> Write current findings as a baseline and exit 0
              --sarif <path>       Write SARIF 2.1.0 (GitHub code scanning)
              --msbuild            Also print MSBuild diagnostics on stdout, so Visual Studio
                                   lists them in the Error List (XF0001 gated, XF0002 warning,
                                   XF0003 result not trustworthy)

            WHAT THE TOOL CANNOT SEE, AND HOW TO TELL IT (xamlfit.config.json)
              "chrome":   { "Card": 16 }        horizontal padding of a control it does not know
              "treatAs":  { "MyGrid": "Grid" }  a subclass that lays out exactly like its base
              "gridPanels": { "GridEx": { "columnCount": "ColumnCount", "starColumns": "StarColumns",
                                          "autoLayoutColumns": "AutoLayoutColumns" } }
                                   a Grid subclass that generates its columns from properties
              "controlWidths": { "IntegerUpDown": 100 }   the widest a control sized at run time
                                   (a number box, a drop-down filled from code) ever gets
              "hiddenScrollBars": ["ActionScrollViewer"]   x:Name of a ScrollViewer or list
                                   whose vertical bar never shows (content never taller)
              "displayScales": [1, 1.25]   display scalings TextFormattingMode="Display" text
                                   must fit at (default 1, 1.25, 1.5, 1.75, 2)
              "absentFonts": ["Segoe UI Variable"]   skip a family target machines do not have
              "defaultFontByCulture": { "th": "Leelawadee UI" }
              "elementLanguage": "en-US"   the Language WPF uses to pick fallback fonts
                                   (WPF's default is en-US on any Windows; "culture" = your
                                   app sets Language to the UI culture)
              "assumeDefaultTheme": true

            EXIT CODES
              0  pass
              1  findings at or above the gate
              2  usage, config or input error
              3  result not trustworthy (resolution below the floor, nothing measured, or a
                 whole culture that could not be measured)
            """);
    }
}
