# Third-party notices

XamlFit itself is MIT licensed. This file covers data and tooling bundled with, or used to
produce, the package.

## Bundled data

### `data/control-chrome.json`

How much horizontal space each WPF control consumes around its content. The numbers are
**measured on the maintainer's machine** by placing a `Border` of known width inside each
control and asking WPF to measure it (see `prototype/ChromeGen`). They are facts about the
default Aero2 theme's layout behaviour, not a copy of Microsoft's theme files.

No Microsoft source code, theme XAML, or binaries are redistributed.

## Not bundled

### Font metrics

`xamlfit.fonts.json` is **not** shipped with the package. Users generate it themselves with
`xamlfit fonts export`, from fonts already installed and licensed on their own machine, into
their own repository.

The file contains **advance widths only** — integer numbers per codepoint — and no glyph
outlines, hinting instructions, or any other part of the font software.

> **Before 1.0:** confirm the position of the relevant font EULAs on extracting advance widths
> and committing them to a repository. The maintainer's reading is that metrics are facts
> rather than the creative expression a font licence protects, but that reading has not been
> verified by counsel. Until it is, XamlFit ships metrics for **no** proprietary font.

### Corpora

The coverage figures in `README.md` and `docs/M0-可量測比例調查.md` were produced by running a
throwaway survey tool over publicly available repositories (ScreenToGif, Playnite,
MaterialDesignInXamlToolkit, wpfui, ILSpy, HandyControl, MahApps.Metro, WinUI-Gallery). Only
aggregate statistics are reproduced. **No source from those projects is included in this
repository**, and baselines must never embed their XAML.
