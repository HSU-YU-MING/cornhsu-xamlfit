namespace XamlFit.Cli;

internal static class Usage
{
    public static void Print(TextWriter w)
    {
        w.WriteLine("""
            xamlfit — static text-fit audit for XAML.

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

              The metrics file contains advance widths only — no glyph outlines and no font
              software. Generate it on a machine that has the fonts, commit it, and CI needs
              no fonts installed.

            BUDGET
              --csv <path>         Write the budget table as CSV (for translation platforms)
              --csv-raw            Do not prefix formula-like text with ' (byte-exact source
                                   strings for API pipelines; unsafe to open in a spreadsheet)
              --json <path>        Write the full report as JSON
              --metrics <path>     Metrics file (default: xamlfit.fonts.json next to <root>)
              --min-coverage <n>   Resolution floor, 0-100. Below it: exit 3.
                                   Default 0 for budget (an export, not a gate), 60 for check.
              --config <path>      Config file (default: xamlfit.config.json next to <root>).
                                   Command-line flags always win over the file.
              --default-font <s>   Font family assumed when XAML does not say (default: Segoe UI)
              --default-size <n>   Font size assumed when XAML does not say (default: 12)

            CHECK
              All BUDGET options, plus:
              --resx <path>        Directory holding .resx files. Repeatable.
              --culture <name>     Only check these cultures. Repeatable. Default: all found.
              --pseudo             No translations: simulate expansion instead (reports only)
              --margin <n>         Safety margin percent before calling it tight (default 5)
              --fail-on <level>    'fail' (default) or 'warn'
              --baseline <path>    Only report findings not in this baseline
              --write-baseline <p> Write current findings as a baseline and exit 0
              --sarif <path>       Write SARIF 2.1.0 (GitHub code scanning)
              --msbuild            Also print MSBuild diagnostics on stdout, so Visual Studio
                                   lists them in the Error List (XF0001 gated, XF0002 warning,
                                   XF0003 result not trustworthy)

            EXIT CODES
              0  pass
              1  findings at or above the gate
              2  usage, config or input error
              3  result not trustworthy (coverage below floor, or nothing measurable)
            """);
    }
}
