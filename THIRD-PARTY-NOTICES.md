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

### `data/theme-chrome.json`

Horizontal chrome, fonts and text casing of standard WPF controls under MahApps.Metro 2.4.11,
MaterialDesignThemes 5.3.0, HandyControl 3.5.1 and WPF-UI 4.3.0, **measured on the maintainer's machine** by loading each theme into a WPF
application and placing content of known width inside each control (see `prototype/ThemeChrome`).
It holds numbers and the themes' public style keys (`MahApps.Styles.Button.Square`…), not their XAML.
All four libraries are MIT licensed (MahApps.Metro: Copyright © .NET Foundation and Contributors;
MaterialDesignThemes: Copyright James Willock/Mulholland Software Ltd; HandyControl: Copyright (c) 2018-2024 HandyOrg;
WPF-UI: Copyright (C) 2021-2026 Leszek Pomianowski and WPF UI Contributors — as stated in their NuGet packages); no part of
them is redistributed.

### `data/font-fallback.json`

The fallback table WPF applies to characters the requested font cannot draw, transcribed from
WPF's own composite font `GlobalUserInterface.CompositeFont` (the `Windows10RS5` section),
which ships as a resource of PresentationCore in Microsoft.WindowsDesktop.App 10.0.10.
WPF is published by the .NET Foundation at <https://github.com/dotnet/wpf> under the MIT
licence:

> Copyright (c) .NET Foundation and Contributors
>
> Permission is hereby granted, free of charge, to any person obtaining a copy of this software
> and associated documentation files (the "Software"), to deal in the Software without
> restriction, including without limitation the rights to use, copy, modify, merge, publish,
> distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the
> Software is furnished to do so, subject to the following conditions:
>
> The above copyright notice and this permission notice shall be included in all copies or
> substantial portions of the Software.
>
> THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING
> BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
> NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM,
> DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
> OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

Only the Unicode ranges, language tags, scale factors and font family names are kept; no font
data is included.

## Not bundled

### Font metrics

`xamlfit.fonts.json` (or `.json.gz`) is **not** shipped with the package. Users generate it
themselves with `xamlfit fonts export`, from fonts already installed and licensed on their own
machine, into their own repository. The demo project's metrics are for a synthetic font made by
`prototype/DemoMetrics`, not taken from any real font.

What the file holds, per font face (family, weight, style, stretch). Everything is a number or a
name, keyed by Unicode code point or OpenType script/language tag; glyph IDs are not kept:

- **Naming:** the family name in English, the legacy family name, and the family names in the
  font's other languages (`微軟正黑體`), as stored in its `name` table.
- **Vertical metrics:** units per em, ascender, descender, line gap.
- **Advance widths:** per code point (from `cmap` + `hmtx`), as ranges of equal widths, plus the
  default advance. For a variable font, per named instance (`fvar` + `HVAR`).
- **Kerning:** the pair adjustments of the font's GPOS `kern` lookups, **keeping their structure**:
  per lookup and subtable either explicit pairs, or the first glyph's coverage, the first- and
  second-class definitions and the non-zero cells of the class matrix — all re-keyed from glyphs
  to code points — and which script/language systems each lookup is attached to. Variable fonts
  add the `GDEF` variation deltas applied to each instance.
- **Widths worked out by running the font's own GSUB/GPOS rules** at export time: Arabic letters
  in each joining form and the change in width next to one or two neighbours, per language system;
  Indic, Khmer, Myanmar, Mongolian and other complex-script clusters as the difference from the sum
  of their letters; the width a letter takes when a language substitutes it (`locl`) or when a
  combining mark follows it (`ccmp`); punctuation drawn differently inside Mongolian text.
- **Hinted widths** (with `--hinted`): for 8–48 px, the whole-pixel width at each size where it
  differs from the rounded linear width, from the font's `hdmx` table or from Windows GDI.
- **Installed family names** (`installedFamilies`): only the names that resolving a font name the
  way WPF does needs — the exported families, and installed families whose names begin with one of
  them followed by a space (`Segoe UI Historic` next to `Segoe UI`), so that `Segoe UI Variable` is
  read as Segoe UI but a real font missing from the file is not. Files exported before 0.6.0 list
  every family the export saw, that is, what was installed on the machine that ran it.

Segoe UI Variable is not exported unless the user names it with `--family`: Microsoft's font FAQ
says it is not available for licensing or use outside Microsoft products.

It contains no glyph outlines, hinting instructions, glyph IDs, or copies of the font's tables or
software. The kerning section is, however, a transcription of the positioning data of part of the
GPOS table into another key space, and several sections are the results of executing the font's
substitution and positioning rules — that is what a legal review should be told, not just
"widths".

> **Before 1.0:** confirm the position of the relevant font EULAs on extracting these metrics
> (all of the above) and committing them to a repository. The maintainer's reading is that metrics are facts
> rather than the creative expression a font licence protects, but that reading has not been
> verified by counsel. Until it is, XamlFit ships metrics for **no** proprietary font.
>
> What has been checked so far: Microsoft's font redistribution FAQ forbids redistributing or
> converting the fonts and says nothing about extracting metrics; the U.S. Copyright Office's
> 1988 position, as summarised by Richard Kinch, is that font metrics are not copyrightable;
> metric-compatible fonts (Liberation and others) exist. None of this is legal advice.

### Corpora

The coverage figures in `README.md` and `docs/M0-可量測比例調查.md` were produced by running a
throwaway survey tool over publicly available repositories (ScreenToGif, Playnite,
MaterialDesignInXamlToolkit, wpfui, ILSpy, HandyControl, MahApps.Metro, WinUI-Gallery). Only
aggregate statistics are reproduced. **No source from those projects is included in this
repository**, and baselines must never embed their XAML.
