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

Horizontal chrome, fonts and text casing of standard WPF controls under MahApps.Metro 2.4.11 and
MaterialDesignThemes 5.3.0, **measured on the maintainer's machine** by loading each theme into a WPF
application and placing content of known width inside each control (see `prototype/ThemeChrome`).
It holds numbers and the themes' public style keys (`MahApps.Styles.Button.Square`…), not their XAML.
Both libraries are MIT licensed (MahApps.Metro: Copyright © .NET Foundation and Contributors;
MaterialDesignThemes: Copyright James Willock/Mulholland Software Ltd, as stated in their NuGet packages); no part of them is
redistributed.

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

`xamlfit.fonts.json` is **not** shipped with the package. Users generate it themselves with
`xamlfit fonts export`, from fonts already installed and licensed on their own machine, into
their own repository.

The file contains **integer numbers only**: an advance width per codepoint, pair kerning
adjustments, and for Arabic-script letters the width of each letter form and of adjacent letters,
worked out at export time from the font's own substitution and positioning rules. It contains no
glyph outlines, hinting instructions, substitution or positioning tables, or any other part of
the font software.

> **Before 1.0:** confirm the position of the relevant font EULAs on extracting these metrics
> (advance widths, kerning, letter-form widths) and committing them to a repository. The maintainer's reading is that metrics are facts
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
