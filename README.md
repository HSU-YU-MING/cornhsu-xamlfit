# XamlFit

**Static text-fit audit for XAML. Computes the pixel budget of every text slot without launching the app — export limits for translators before translation, fail CI on real overflow after.**

[![NuGet](https://img.shields.io/nuget/v/Cornhsu.XamlFit)](https://www.nuget.org/packages/Cornhsu.XamlFit)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

[繁體中文](README.zh-Hant.md)

```bash
dotnet tool install -g Cornhsu.XamlFit

xamlfit fonts export                      # once, on a machine that has your fonts
xamlfit budget ./src --csv budget.csv     # before translation: hand translators the real limits
xamlfit check  ./src --resx ./Resources   # after translation: fail CI on real overflow
```

> Requires the **.NET 10 runtime**. The analysis is pure arithmetic over XML and font tables —
> no WPF, no native dependencies, runs on Linux CI.

---

## What it does that nothing else does

Every pseudo-localization tool works the same way: **run the app, switch to German, look at
every screen**. Microsoft's own localization blog described the static version of this a decade
ago — *"it would be possible to build a check that reports any string that won't fit"* — and
nobody built it.

XamlFit builds it. It reads your XAML, works out how many pixels each piece of text actually
has, measures the real translated string against that number, and fails the build when it
doesn't fit.

One class of bug is **only** visible this way: a `TextBlock` with `TextWrapping="NoWrap"` and
`TextTrimming="None"` does not raise `IsTextTrimmedChanged` when its text is clipped. Runtime
detection cannot see it. Static measurement can.

## Honest coverage

**XamlFit can compute a number for roughly a quarter to a third of the text in a typical
project, and for 76–85% of the places where clipping is actually possible.**

Measured over 1,921 XAML files — three maintainer projects plus ScreenToGif, Playnite,
MaterialDesignInXamlToolkit, wpfui, ILSpy, HandyControl, MahApps.Metro and WinUI-Gallery:

| | all corpora | app screens only |
|---|---|---|
| localizable text sites | 9,149 | 7,074 |
| have a hard width bound (clipping is possible) | 35.9% | 33.0% |
| **of those, XamlFit computes a number for** | **75.9%** | **85.0%** |
| no declared width bound anywhere | 54.3% | 61.9% |

Per project the number ranges from **34.8%** (ScreenToGif, heavy `Auto`/`*` Grid use) to
**100%** (wpfui, ILSpy, WinUI-Gallery). Solving `Auto`/`*` Grid columns is the single biggest
remaining gap and the top item on the roadmap — it would take the figure to about 97%.

Every run prints its own `resolution` figure, and `check` exits **3** when it falls below
`--min-coverage`. The tool never says "looks fine" while only seeing a third of your UI.

**"No declared width bound" is not a safety guarantee.** It means the analysis found no bound
in the XAML — the window still has a width at runtime. Those sites are reported as
`no-declared-limit`, never as blank, and they are not counted as coverage.

## How measurement works

No SkiaSharp, no WPF, no native binaries. XamlFit reads five tables out of a font file
(`head`, `hhea`, `hmtx`, `cmap`, `OS/2`) and sums advance widths.

```
width = Σ advance(codepoint) × fontSize ÷ unitsPerEm
```

Validated against WPF's own `FormattedText` over 1,400 comparisons (9 face configurations ×
7 sizes × 28 strings in English, German, French, Chinese, Japanese and Korean):

| | |
|---|---|
| mean absolute error | **0.039%** |
| worst **under**-estimate (could miss an overflow) | **0.068%** |
| worst over-estimate (could report a false positive) | 1.43% |

The 1.43% is a single kerning pair in a Japanese font. Per-glyph advance widths match
`FormattedText` to within **0.001 px** — the entire error is kerning, which XamlFit does not
apply. Ignoring kerning systematically **over**-estimates width, so the tool errs toward
reporting a problem rather than missing one. The default 5% safety margin covers it three
times over.

### The metrics file

Fonts are not shipped and not needed on CI:

```bash
xamlfit fonts export --family "Segoe UI" --family "Microsoft JhengHei UI"
git add xamlfit.fonts.json
```

`xamlfit.fonts.json` contains **advance widths only** — no glyph outlines, no font software.
You generate it from fonts you already have a licence for, into your own repository. CI reads
numbers.

If a font has no metrics, the sites using it become `unmeasurable` and count against coverage.
They are never silently assumed to fit.

## `budget` — before translation

The unit is the **string**, not the UI slot, and:

> **a string's budget is the narrowest place it appears.**

`Resources.Save` may have 180 px on the settings page and 64 px in a toolbar. The budget is 64,
and the report names the slot that caused it, so you know what to widen.

```csv
key,limit_px,limit_chars,status,font,size,binding_site,source,source_px,headroom_px,fits_expanded,…
Save,64.0,10,measured,Segoe UI,12,MainToolbar.xaml:42,Save,31.4,32.6,yes,…
Unbounded,,,no-declared-limit,,,,…
```

`limit_px` is authoritative; `limit_chars` is an estimate, because glyph widths differ. The
column order puts the limit before the source string, which is what Smartling-style CSV imports
expect.

Text fields that start with `=`, `+`, `-`, `@`, a tab or a carriage return are prefixed with a
single quote, so a spreadsheet does not treat them as formulas. Real UI strings do start with
`+` and `-`, so if you feed the CSV straight into a translation API rather than opening it,
use `--csv-raw` to get byte-exact source strings — and do not open that file in Excel.

Translation platforms let you set a length limit per string — this is where the number comes
from, instead of someone guessing.

`headroom_px` is useful on its own, with no translations at all: a slot with 2 px of slack in
English is already counting down.

## `check` — after translation

```bash
xamlfit check ./src --resx ./Resources --baseline xamlfit-baseline.json
```

Each measurable slot is judged independently — the same string can overflow in one place and be
vertically clipped in another:

| wrapping | trimming | height | result | severity |
|---|---|---|---|---|
| `NoWrap` | `None` | — | `overflow` — text spills out | **fail** |
| `NoWrap` | ellipsis | — | `trimmed` — information lost | warn |
| `Wrap` | — | fixed | `vertical-clip` — lines don't fit | **fail** |
| `Wrap` | — | `Auto` | `reflow` — layout grows | info |
| any | any | — | `tight` — less than `--margin` slack | warn |

Without translations, `--pseudo` expands the source string using a length-bucketed table
(`< 10` chars → 200%, `11–20` → 180%, `21–30` → 160%, `31–50` → 130%, `50+` → 120%). A single
flat factor is wrong: short strings grow most, and button labels are the shortest and the most
likely to break. **Pseudo findings are always downgraded one level and never fail a build** —
a guess should not block someone's pull request.

### Exit codes

| | |
|---|---|
| 0 | pass |
| 1 | findings at or above the gate (`--fail-on fail` by default) |
| 2 | usage, config or input error |
| 3 | **result not trustworthy** — resolution below `--min-coverage`, or nothing measurable |

## Known limitations

| | effect | handling |
|---|---|---|
| `Auto` / `*` Grid columns | 21.6% of at-risk sites | `blocked`, counted; roadmap item 1 |
| `ControlTemplate` / `DataTemplate` contents | width comes from the consumer | `not-analysed` |
| `ComboBox`, `TextBox` chrome | content width does not propagate, so the chrome is unknown | sites inside are `unmeasurable` — **never** estimated from padding alone, which would under-count chrome and miss overflows |
| no kerning or shaping | ≤ 1.43% over-estimate | safety margin; never under-estimates |
| Arabic, Indic, Thai | advance widths do not sum | `unmeasurable`, not guessed |
| `TextFormattingMode="Display"` | slight width difference | measured as `Ideal` (the WPF default), noted in the report |
| custom `ControlTemplate` | the chrome table no longer applies | detected and marked |
| automatic font fallback | Windows font linking may pick a different face than the metrics file | measurement is flagged in the report |

## Configuration

Everything is a command-line flag today; a config file is a 0.x roadmap item.

Suppress a single slot with a reason — the reason is required, and suppressed slots are counted:

```xml
<!-- xamlfit-ignore: decorative, never translated -->
<TextBlock Width="40" Text="{x:Static p:Strings.Arrow}"/>
```

## Related tools

[XamlContrast](https://www.nuget.org/packages/Cornhsu.XamlContrast) audits WCAG contrast in the
same XAML, and [XamlKeys](https://www.nuget.org/packages/Cornhsu.XamlKeys) audits resource-key
scope. Same exit-code contract, same baseline shape, same mental model.

## Licence

MIT. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for bundled data.
