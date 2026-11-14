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
  `absentFonts`, `sourceCulture`, `defaultFontByCulture`, `elementLanguage`,
  `assumeDefaultTheme`. Every run lists the controls whose chrome it could not determine.
- Characters the requested font cannot draw are measured with the font WPF would use: another
  face of the same family first, then WPF's own composite fallback table
  (`data/font-fallback.json`, from dotnet/wpf, MIT), chosen by the element's `Language`.
- `Run`s are measured line by line instead of given up on, and project header styles for
  `DataGridColumnHeader` that keep the template are understood (Padding replaces the
  template's own 5,0,5,0 unless all zero; BorderThickness does not count). The header's font
  comes from the header style or the grid, not from the column.
- `gridPanels` in `xamlfit.config.json`: declare a Grid subclass that generates its columns
  from properties (Playnite's GridEx). Never built in: another project's class with the same
  name may lay out differently.
- A `Width="0"` panel opened by a storyboard in the same file takes the smallest non-zero
  literal `To` of the animations that open it (ScreenToGif's editor panel; 21.9% → 40.0%).
- Items of an `ItemsControl`/`ComboBox` with an `ItemTemplate` are drawn through the template,
  so their own text is not measured as written.
- A project ScrollBar style that only replaces the template keeps the system width (WPF's default
  style still sets Width and MinWidth); with `OverridesDefaultStyle` the template's fixed root width
  decides. Playnite 32.9% → 34.3% without configuration, 41.8% with `gridPanels`.
- Templates copied out to compute chrome look up styles from the control's own position, so a
  ScrollBar style in a window's or grid's resources is no longer missed (a 24 px bar was taken as
  the system width: 6.5 px over).
- Runs report the files that turn on `UseLayoutRounding`: WPF can shave about one pixel off text
  sized to its content, which XamlFit does not model.
- `check` and `budget` reject each other's options (`check --sites` used to be accepted and
  ignored).
- Console output is UTF-8: translated text was garbled through pipes and in CI on a
  Traditional Chinese Windows.
- Hardening for untrusted input (see `SECURITY.md`): files nested more than 512 elements
  deep are parse errors instead of a hang, and `##vso[` / `##[` CI log commands in scanned
  text are defused.
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
- **Layout** against real off-screen WPF windows, 156 cases: 127 exact, 29 conservative,
  0 over-estimates (`prototype/LayoutOracle`).
- **Real project windows** drawn by WPF with their own themes and translations
  (`prototype/CorpusRender`): Playnite, 2,841 comparisons — 71 clipped by both, 4 false
  alarms, **0 missed**; ScreenToGif, 2,657 comparisons including the editor's side panel and
  drop-downs, same font on both sides — 12 clipped by both, 0 false alarms, and 100 headers that
  WPF draws 0.5–1.2 px short because of `UseLayoutRounding` (not modelled; not a translation
  length issue). Eleven bugs found this way are fixed (see `docs/M6-全面重讀.md` §2.6), among
  them: rows now share width in document order; side tab strips and project `Window`
  subclasses are no longer over-estimated; characters a font lacks are drawn as WPF draws
  them (other faces of the same family first, then WPF's composite fallback table, chosen by
  the element's `Language`); invisible characters have no width.
- **Window frame and scroll bar widths** per display scaling, from `AdjustWindowRectExForDpi`
  (`prototype/DpiProbe`): the table's 16 / 20 / 17.5 DIP are the maxima across scalings.
- **Globalization-invariant mode** (common on Linux CI): all tests pass, and two full corpus
  runs give identical results.
- **Shipped software**: 984 source-language measurements over ten projects. The only two
  overflows are in the maintainer's app and are real (drawn by WPF).
- 538 unit tests.

### Not verified

- nuget.org installation and Trusted Publishing: nothing has been released. The release steps
  and the Action's script were rehearsed locally. (CI on Linux and Windows runs and passes.)
- The default UI fonts for Japanese, Korean and Simplified Chinese Windows: taken from
  Windows' font configuration, not measured on those systems.
- Text in drop-down lists, data templates and hidden panels: the render harness cannot draw
  it, so those judgements have not been compared against WPF.
