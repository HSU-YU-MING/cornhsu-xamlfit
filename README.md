# XamlFit

**Static text-fit audit for WPF XAML. Computes the pixel budget of every text slot without launching the app — export limits for translators before translation, fail CI on real overflow after.**

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
> no WPF and no native dependencies.
>
> ⚠ It has **not yet been executed on Linux**: the CI workflow that would prove it has never
> run, because the repository is not published. What could be checked without Linux was: the
> `linux-x64` build depends on nothing but `Microsoft.NETCore.App`, and the whole test suite
> and two full corpus runs give identical results in globalization-invariant mode, which many
> Linux CI images use. See `docs/M5b-跨平台與CI驗證.md` and `docs/M6-全面重讀.md`.
>
> **WPF only.** WinUI/UWP, Avalonia and MAUI files are recognised and reported as *not
> analysed* — their chrome, default font and layout rules are different, and numbers computed
> with WPF's would simply be wrong.

---

## What it does that nothing else does

Every pseudo-localization tool works the same way: **run the app, switch to German, look at
every screen**. Microsoft's own localization blog described the static version a decade ago —
*"it would be possible to build a check that reports any string that won't fit"* — and nobody
built it.

XamlFit reads your XAML, works out how many pixels each piece of text actually has, measures
the real translated string against that number, and fails the build when it does not fit.

One class of bug is **only** visible this way: a `TextBlock` with `TextWrapping="NoWrap"` and
`TextTrimming="None"` does not raise `IsTextTrimmedChanged` when its text is clipped. Runtime
detection cannot see it. Static measurement can.

## The one rule

> **Say "cannot measure" rather than compute a number that is too generous.**

An available width that is over-estimated turns "does not fit" into "fits". That is the worst
thing this tool can do, so everything it cannot read is *unknown*, never *assumed default*:
a bound `Width`, a style defined in another assembly, a third-party control's template, a font
that comes from the hosting window. Each of those blocks the site and says why.

## What it can see, honestly

**Resolution** = of the text sites that have a width bound and therefore *should* yield a
number, the share that actually did. It is printed on every run and gated by `--min-coverage`.

Measured out of the box, no configuration, on eleven projects:

| project | localizable sites | measured | blocked | no width bound | resolution |
|---|---:|---:|---:|---:|---:|
| Cornea (maintainer app) | 68 | 59 | 0 | 9 | **100%** |
| QuillNest (maintainer app) | 685 | 487 | 55 | 116 | **89.9%** |
| Kindling (maintainer app) | 150 | 57 | 18 | 75 | **76.0%** |
| Playnite | 1,107 | 136 | 278 | 506 | **32.9%** |
| ScreenToGif | 1,277 | 99 | 375 | 790 | **20.9%** |
| HandyControl | 210 | 11 | 61 | 123 | 15.3% |
| MahApps.Metro | 1,613 | 53 | 749 | 718 | 6.6% |
| MaterialDesignInXamlToolkit | 2,339 | 0 | 867 | 1,432 | 0% |
| wpfui | 440 | 0 | 15 | 405 | 0% |
| WinUI-Gallery | — | — | — | — | not WPF |

Read that table as two groups.

**Applications** range from 21% to 100%. What blocks the rest is concrete and printed:
a custom panel the tool has never heard of (Playnite's `GridEx`), side panels collapsed to
`Width="0"` and opened by animation (ScreenToGif, 318 sites), a row whose neighbour is
data-bound, `SharedSizeGroup` columns.

**Control libraries** are near zero, and that is the correct answer: in those repositories the
controls *are* the theme. Their demos take fonts from a markup extension and wrap content in
controls whose templates rebind everything. An application that *uses* one of them is in the
same position unless it tells the tool what the theme does — see
[What you have to tell it](#what-you-have-to-tell-it).

Earlier drafts of this README claimed 77–100% for the same projects. Those figures were
produced by an engine that applied default-theme padding to restyled controls, ignored the
window frame, and counted WinUI files as WPF. **A high number built on wrong assumptions is
worse than a low honest one.** The full account is in `docs/M6-全面重讀.md`.

**"No width bound" is not a safety guarantee.** It means the analysis found no bound in the
XAML — the window still has a width at runtime. Those sites are reported as
`no-declared-limit`, never as blank, and they are not counted as measured.

## How it knows

Three independent checks, each against something that is not this tool.

**Text width, against WPF's `FormattedText`.** 1,400 comparisons (9 faces × 7 sizes × 28
strings in English, German, French, Chinese, Japanese and Korean): mean error **0.039%**,
worst under-estimate **0.068%**, worst over-estimate 1.43% (one kerning pair). XamlFit does not
apply kerning, which makes text measure slightly *wide* — it errs toward reporting.

**Layout, against real WPF windows.** `prototype/LayoutOracle` feeds 138 XAML snippets to an
actual off-screen `Window` and to the resolver, and compares what is really visible:
**113 exact, 25 conservative, 0 over-estimates.** The conservative ones are deliberate — an
assumed vertical scroll bar, the window frame at its widest display scaling. This harness
found most of what the last audit fixed; it needs Windows, so its conclusions are restated as
portable unit tests.

**Real projects, against real WPF windows.** `prototype/CorpusRender` loads a real project's
windows into WPF — with the project's own `App.xaml` dictionaries and each language's strings —
and records how wide every text needs to be and how much of it is actually visible. Playnite,
9 windows, 2,025 comparisons: **71 clipped by both, 4 false alarms, 0 missed**, 1,950 fit by
both. Two of the false alarms are within 0.5 px; the other two are the same button, 0.7 and 7.7 px on the safe side.
Where both agree, XamlFit's text width is −0.02 to +1.74 px off WPF's, and the width it
believes is available is never more than 0.02 px too generous. ScreenToGif, 5 windows: 5
clipped by both, 0 false alarms, 0 missed. This found eleven more bugs, listed in
`docs/M6-全面重讀.md`.

**False positives, against shipped software.** Hard-coded text in a released app presumably
fits. So the tool checks the *source* language too: **929 source-language measurements across
the corpus, 0 overflows.** While it was being built this number was not zero twice, and both
times the tool was wrong.

## What you have to tell it

Some things are not in the XAML. The tool reports them as blocked and names them; you declare
them once in `xamlfit.config.json`.

```jsonc
{
  // Horizontal padding (left + right, DIPs) of controls the tool does not know:
  // third-party controls, or standard ones restyled by an external theme.
  "chrome": { "Card": 16, "MetroWindow": 2, "GroupBox.Header": 9 },

  // A subclass that lays out exactly like its base. You are vouching for that.
  "treatAs": { "MyGrid": "Grid" },

  // A family in a FontFamily fallback list that target machines do not have.
  "absentFonts": ["Segoe UI Variable"],

  // The language the app is written in (see "The default font" below).
  "sourceCulture": "zh-Hant",

  // An external theme was detected, but it does not change control padding.
  "assumeDefaultTheme": false
}
```

Every run ends with the controls whose chrome it could not determine, ordered by how many
sites each one blocks — so you know which three lines of config are worth writing.

### External themes

If `App.xaml` merges a dictionary from another assembly (`MaterialDesignThemes.Wpf`, `Wpf.Ui`,
`HandyControl`, …), standard controls are restyled by XAML the tool cannot read: a Material
Design `Button` has 32 px of padding, not 4. The built-in table no longer applies, so sites
inside those controls become blocked until you declare their chrome.

### Templates in your own project

When *your* project replaces a `ControlTemplate`, nothing needs declaring: the tool reads the
template, substitutes `{TemplateBinding Padding}` with what the control actually has, and
works out the chrome. If the same style exists in several theme files (light and dark), it
computes each and accepts the answer only when they agree.

### The default font

WPF's default font is not Segoe UI. It is the *system message font*, and it follows the
Windows display language: Segoe UI on English or German Windows, **Microsoft JhengHei UI** on
Traditional Chinese, Microsoft YaHei UI on Simplified Chinese, Yu Gothic UI on Japanese,
Malgun Gothic on Korean. Latin letters in JhengHei UI are about 7% wider than in Segoe UI.

So text with no explicit `FontFamily` has a different width on different users' machines.
`check` measures each culture with that culture's font, and reports — exit 3 — a culture whose
font is missing from the metrics file rather than measuring it with the wrong one.

A `UserControl` inherits its font from whichever window hosts it, which is in another file.
The tool uses the consensus of the windows in the same application; if they disagree, the
inherited value is unknown until you pin `defaultFontFamily` / `defaultFontSize`.

## Fonts

No SkiaSharp, no WPF, no native binaries. XamlFit reads a handful of tables out of a font file
and sums advance widths: `width = Σ advance(codepoint) × fontSize ÷ unitsPerEm`.

```bash
xamlfit fonts export --family "Segoe UI" --family "Microsoft JhengHei UI"
git add xamlfit.fonts.json
```

`xamlfit.fonts.json` contains **advance widths only** — no glyph outlines, no font software.
You generate it from fonts you already have a licence for, into your own repository. CI needs
no fonts installed.

It will not substitute a font it has for one it lacks:

- the *first* family in a fallback list must be in the file (a later one is only a fallback);
- bold text is not measured with the regular face;
- a variable font only answers for its default weight;
- `TextFormattingMode="Display"` snaps glyphs to whole pixels and cannot be predicted from
  metrics at all (measured against WPF: −37% to +45%).

Each of these makes the site unmeasurable and names the font to export.

**Characters the font cannot draw** are drawn the way WPF draws them, which was measured, not
assumed: first from **another face of the same family** (Microsoft JhengHei UI Regular has no
Cyrillic; its Light face does, and WPF uses it even for bold text), then from WPF's own
fallback table (`GlobalUserInterface.CompositeFont`, MIT-licensed, bundled as
`data/font-fallback.json`). That table depends on the element's `Language` — `en-US` unless
`xml:lang` says otherwise — **not** on the Windows display language: on any Windows, an app
that does not set `Language` draws Chinese with Yu Gothic UI. A character none of WPF's
candidates in the metrics file can draw is unmeasurable, and the report names the candidates.
Invisible characters (variation selectors, zero-width space and joiner, BOM) have no width.

## `budget` — before translation

The unit is the **string**, not the UI slot:

> **a string's budget is the place where it runs out of room first.**

```csv
key,limit_px,limit_chars,status,font,size,binding_site,source,source_px,headroom_px,…
Save,64.0,10,measured,Segoe UI,12,MainToolbar.xaml:42,Save,31.4,32.6,…
Help,,,wraps,Segoe UI,12,About.xaml:9,…
Unbounded,,,no-declared-limit,,,,…
```

`limit_px` is authoritative; `limit_chars` is an estimate, because glyph widths differ. Four
statuses, never a blank cell:

| status | meaning |
|---|---|
| `measured` | `limit_px` applies |
| `wraps` | the text wraps and its height is free — only a single word wider than the line is cut |
| `no-declared-limit` | no width bound found in the XAML. **Not** a guarantee |
| `not-analysed` | a bound exists but could not be resolved |

`--sites sites.csv` writes one row per text site with the reason it is blocked.

Text fields that start with `=`, `+`, `-`, `@`, a tab or a carriage return are prefixed with a
single quote so a spreadsheet does not run them as formulas. Use `--csv-raw` for byte-exact
strings when feeding an API — and do not open that file in Excel.

## `check` — after translation

```bash
xamlfit check ./src --resx ./Resources --baseline xamlfit-baseline.json
```

Translations come from `.resx` (`{x:Static p:Strings.Save}`) and from XAML string dictionaries
(`<sys:String x:Key="Save">` with `{DynamicResource Save}`), which are found automatically.
The second form is by far the more common in real WPF applications.

| the text | result | severity |
|---|---|---|
| does not wrap, no trimming, too wide | `overflow` — spills out or is clipped | **fail** |
| does not wrap, ellipsis, too wide | `trimmed` — information lost | warn |
| wraps into more lines than a fixed height holds | `vertical-clip` | **fail** |
| wraps into more lines than the source language | `reflow` — layout grows | info |
| does not wrap, less than `--margin` slack | `tight` | warn |

Line counts come from real word wrap, not total width divided by line width.

Three things that are easy to get wrong and are checked against WPF:

- **Wrapping that cannot happen.** Inside a horizontal `StackPanel`, an `Auto` grid column or a
  `WrapPanel`, WPF measures the child with unlimited width: `TextWrapping="Wrap"` does nothing
  and the text is cut. That is reported as `overflow`, not as harmless `reflow`.
- **Rows of translated strings.** A row of buttons shares its width. What is left for one
  depends on how wide the others are *in that language* — so each culture is laid out again
  with its own strings.
- **Access keys.** In `_Save` on a `Button`, `Label`, `CheckBox`, `MenuItem`… the underscore is
  not drawn. On a `TextBlock` or `ListBoxItem` it is.

On two public projects with their real translations — Playnite (38 languages, 4,989
measurements) and ScreenToGif (29 languages, 1,231 measurements) — it reported 75 overflows at
7 sites and 9 at 6, with none in the source language of either. All 75 in Playnite were
checked by drawing the windows in WPF: 71 are really clipped, 4 are false alarms. 5 of the 9 in
ScreenToGif were checked and are really clipped; the other 4 are in drop-down lists and a
hidden panel that the harness cannot draw.

A row of controls shares its width **in document order**: the ones written first get what they
need, and a long translation pushes the ones after it out of the window. That is how WPF lays
out a horizontal `StackPanel` or `DockPanel`, and it is what `check` reports.

What is *not* judged is counted and printed: strings with run-time placeholders (`{0}`), text
the metrics do not cover (with the code points, so you know which font to export), and
scripts that reshape in context (Arabic, Hebrew, Indic, Thai). **A culture in which nothing
could be measured makes the run exit 3** — "not checked" must not look like "passed".

`--source-only` checks just the source language and needs no translations. `--pseudo`
simulates expansion from a length-bucketed table (`< 10` chars → 200%, `11–20` → 180%,
`21–30` → 160%, `31–50` → 130%, `50+` → 120%); its findings are downgraded one level and never
fail a build, because a guess should not block someone's pull request.

### Exit codes

| | |
|---|---|
| 0 | pass |
| 1 | findings at or above the gate (`--fail-on fail` by default) |
| 2 | usage, config or input error |
| 3 | **result not trustworthy** — resolution below `--min-coverage`, nothing measured, or a whole culture that could not be measured |

## Known limitations

| | handling |
|---|---|
| `ControlTemplate` / `DataTemplate` contents | width is decided where the template is used; counted separately as "in a template" |
| An element with `Width` inside something narrower *and unresolvable* | the explicit `Width` is taken at face value. When the outer width *is* resolvable, the smaller wins |
| A label in an `Auto` column | budgeted up to the point where it would be clipped; it will squeeze `*` columns before that |
| Fonts set by a control's own template or by code | not visible; the site's font is whatever the XAML says |
| Styles applied by triggers, visual states or code-behind | not followed |
| Text set through properties not scanned (`ToolTip`, `Title`, watermark/hint attached properties, `Hyperlink`/`Span` content) | not seen at all |
| Resource dictionaries merged in code at run time | every dictionary file is assumed merged; conflicting definitions make the value unknown |
| No kerning or shaping | ≤ 1.43% over-estimate of text width; never under |
| Vertical clipping | only when the text element itself declares `Height`/`MaxHeight` |
| Window frame, scroll bar width | taken at the display scaling where they are largest in DIPs |
| Default UI font per language | only Traditional Chinese was measured on a real system; the others are from Windows' font configuration. Override with `defaultFontByCulture` |

## Configuration

`xamlfit.config.json` next to the scanned root, or `--config <path>`. Besides the keys under
[What you have to tell it](#what-you-have-to-tell-it):

```jsonc
{
  "schemaVersion": 1,
  "defaultFontFamily": "Segoe UI",
  "defaultFontSize": 12,
  "marginPercent": 5,
  "minCoverage": 60,
  "metrics": "xamlfit.fonts.json",
  "exclude": ["/Generated/"],
  "resx": ["Resources"],
  "cultures": ["de", "fr"],

  // The element language WPF uses to pick fallback fonts. WPF's default is en-US whatever
  // the Windows language. "culture" means: your app sets Language to the UI culture.
  "elementLanguage": "en-US"
}
```

Comments and trailing commas are allowed. Relative paths are relative to the config file.
**Command-line flags always win over the file.** A misspelled property is an **error**, not a
silent skip: the run exits 2 and names it.

## CI output

```bash
xamlfit check ./src --resx ./Resources --sarif fit.sarif --msbuild
```

`--sarif` writes SARIF 2.1.0 for GitHub code scanning. `--msbuild` prints MSBuild diagnostics
on stdout, so a build that runs the tool via `Exec` surfaces them in the Visual Studio Error
List: `XF0001` for gated findings, `XF0002` for warnings, `XF0003` when the result is not
trustworthy.

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
