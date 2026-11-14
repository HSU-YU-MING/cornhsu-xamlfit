# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [Unreleased]

Everything below is the initial implementation; nothing has been released yet.

### Added

- **`xamlfit fonts export`** — extracts advance widths from font files into
  `xamlfit.fonts.json` by reading five OpenType tables (`head`, `hhea`, `hmtx`, `cmap`,
  `OS/2`). Pure managed code, no native dependencies, runs anywhere .NET runs.
  The file contains numbers only: no glyph outlines, no font software.
- **`xamlfit budget`** — exports a per-string pixel budget for translators, as CSV or JSON.
  A string's budget is the narrowest slot it appears in, and the report names that slot.
- **`xamlfit check`** — measures real `.resx` translations against those budgets and fails
  CI. `--pseudo` simulates expansion when translations do not exist yet.
- Baseline (`--baseline` / `--write-baseline`) and `<!-- xamlfit-ignore: reason -->`.
  The reason is **required**: an ignore without one does not apply and is reported.
- CSV formula neutralisation by default (`--csv-raw` opts out for API pipelines).
- A deliberately broken demo project with CI count assertions.

### Verified

- Measurement validated against WPF `FormattedText` over 1,400 comparisons:
  mean error **0.039%**, worst under-estimate **0.068%**, worst over-estimate 1.43%
  (a single kerning pair). Per-glyph advance widths match to within 0.001 px.
- Coverage measured over 1,921 XAML files from three maintainer projects and eight
  public ones. See `docs/M0-可量測比例調查.md`.
