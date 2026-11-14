# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

Nothing has been released yet. The entries below describe the implementation as it stands
after a from-scratch audit (`docs/M6-全面重讀.md`), which replaced several earlier behaviours
that over-estimated the available width.

### Added

- **`xamlfit fonts export`** — extracts advance widths from font files into
  `xamlfit.fonts.json`. Pure managed code, no native dependencies. The file contains numbers
  only: no glyph outlines, no font software. Family names are taken in English, with other
  languages kept as aliases; legacy family names (`Segoe UI Semibold`) and variable fonts are
  recorded.
- **`xamlfit budget`** — exports a per-string pixel budget for translators, as CSV or JSON.
  The budget is the site where the string runs out of room first, and the report names it.
  `--sites` writes one row per text site with the reason it is blocked.
- **`xamlfit check`** — measures real translations against each site and fails CI.
  Translations come from `.resx` and from XAML string dictionaries
  (`<sys:String x:Key>` with `{DynamicResource}`), which are discovered automatically.
  The source language is checked too. `--source-only` needs no translations; `--pseudo`
  simulates expansion and never fails a build.
- Each culture is laid out with its own strings and its own default UI font: a row of
  translated buttons shares its width, and WPF's default font follows the Windows display
  language.
- **Chrome from the project's own templates.** When a control's `ControlTemplate` is replaced
  inside the project, its horizontal chrome is computed from that template instead of being
  assumed or given up on.
- `Auto` / `*` Grid column solving, with `MinWidth`/`MaxWidth` redistribution; horizontal
  `StackPanel`, `DockPanel`, `UniformGrid`, `WrapPanel`, `BulletDecorator`; window frames;
  header chrome for `GroupBox`, `Expander` and column headers.
- Things the tool cannot see are declared in `xamlfit.config.json`: `chrome`, `treatAs`,
  `absentFonts`, `sourceCulture`, `defaultFontByCulture`, `assumeDefaultTheme`.
  Every run lists the controls whose chrome it could not determine.
- Detection of non-WPF XAML (WinUI/UWP, Avalonia, MAUI) and of external themes merged from
  other assemblies; both are reported instead of being measured with the wrong assumptions.
- Baseline (`--baseline` / `--write-baseline`) and `<!-- xamlfit-ignore: reason -->`.
  The reason is required.
- SARIF 2.1.0 output and MSBuild diagnostics (`XF0001`–`XF0003`).
- CSV formula neutralisation by default (`--csv-raw` opts out).
- A deliberately broken demo project with CI count assertions, and a CLI smoke test over
  42 command paths and their exit codes.

### Verified

- **Text width** against WPF `FormattedText`, 1,400 comparisons: mean error 0.039%, worst
  under-estimate 0.068%, worst over-estimate 1.43% (a single kerning pair).
- **Layout** against real off-screen WPF windows, 138 cases: 113 exact, 25 conservative,
  0 over-estimates (`prototype/LayoutOracle`).
- **False positives** against shipped software: 958 source-language measurements over ten
  projects, 0 overflows.
- 453 unit tests.

### Not verified

- Execution on Linux, the release workflow and the composite GitHub Action: the repository is
  not published, so none of them has ever run.
- The default UI fonts for Japanese, Korean and Simplified Chinese Windows: taken from
  Windows' font configuration, not measured on those systems.
- The overflows reported on the public corpus: computed, not confirmed by running the apps.
