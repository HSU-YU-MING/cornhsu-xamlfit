# Changelog

All notable changes to this project are documented here.
This project follows [Semantic Versioning](https://semver.org/).

## [0.3.0] - 2026-11-18

Follow-ups from the ZeroCel trial (report 2 §3-2, §3-3). Only additions: a user control now gets a width from the file that places it (no config), and two new config keys that are off unless declared. Findings can appear in user-control sites that previously had no declared limit.

### Added

- **A `UserControl` now takes its width from where it is placed.** 351 sites in ZeroCel had "no
  width bound found up to the root": the root was a user control, and the walk stopped there
  although the main window that places it is 1280 wide. The resolver now continues from the
  `<views:Name/>` element in the placing file (the same `x:Class` match the font inheritance uses),
  through nested user controls up to a window. Placed in several files, the narrowest bound
  wins; a placement that cannot be resolved blocks the site. The report names the placing file
  and line. Nothing is declared: this is read from the XAML.
- **`assumeWindowMinWidth`** (config) / `--assume-window-min-width`: a resizable window with a
  literal `MinWidth` is bounded by it instead of `Width`, so the check answers "does it fit when
  the window is as small as it gets". Off by default; the bound source says `MinWidth (assumed…)`.
  Windows with `ResizeMode="NoResize"`/`CanMinimize`, without `MinWidth`, or with `SizeToContent`
  are unchanged. Verified against WPF with the window shrunk to its MinWidth (`LayoutOracle`
  "minwidth:" cases).
- **`popupMaxWidth`** (config) / `--popup-max-width <n>`: the widest a popup may grow. Popups
  (`ContextMenu`, `ToolTip`, `Popup`, the submenu a `MenuItem` opens) size to their content and
  never clip, so they are not checked unless you declare this; then text in them is held to it
  minus the popup chrome measured in a real window (`LayoutOracle --popups`: ContextMenu 13,
  ToolTip 14, submenu 6, Popup 0; the entries are in `control-chrome.json`). Menu-bar items are
  unaffected. Findings say the limit is declared.

## [0.2.0] - 2026-11-18

Both changes come from a first external trial (ZeroCel, a WPF app with 35 XAML files; reports 1 and 2, 2026-11-18).

**Upgrading**: measurement semantics changed, so findings can move. A `TextBlock` with its own
`Width`/`MaxWidth` inside a horizontal `StackPanel` or an `Auto` column is no longer reported as
overflowing when it wraps; a text element whose own `Width` is wider than the column or row that
holds it is now reported as overflow instead of reflow; and sites in user controls that were
blocked as "windows in this project set different FontFamily values" are now measured when the
XAML places the control in one window. If a baseline hides findings in those sites, refresh it
with `--write-baseline` after reviewing the new report. Nothing else (CLI, config, report
schema, baseline keys) changed.

### Fixed

- **A text element with its own `Width` or `MaxWidth` inside a horizontal `StackPanel`, an `Auto`
  column or a `WrapPanel` was judged as if `TextWrapping` had no effect** (report 1: a
  `TextBlock Width="120" TextWrapping="Wrap"` was the project's only `fail overflow`, 72 px
  over). WPF clamps the available width to the element's own `Width`/`MaxWidth` before
  measuring, so the text wraps there even when the panel offers unlimited width. The tool now
  keeps the wrapping width separately from the visible width: wrapping happens at the first
  definite bound above the text (its own `Width`, a container's `Width`, a fixed or solved
  column), and only what the outer layout clips is reported — a `Width="300"` TextBlock in a
  100 px column wraps at 300 and loses 200 of every line, which was previously reported as a
  harmless reflow. `LayoutOracle --wrap` compares line counts with real WPF for these cases.

### Changed

- **A `UserControl` now takes its inherited font from the window that actually places it**
  (report 2 §2: the main window set no font, two dialogs set `Segoe UI`, and 100 sites in three
  user controls placed only in the main window were blocked as "windows disagree"). The tool
  matches the root's `x:Class` with `<views:Name/>` elements, follows nested user controls
  up to a window and reads the value at the placing element. Placed in windows that disagree,
  in a resource dictionary, or circularly, the value stays unknown and the reason names the
  files. A control no XAML file places falls back to the windows' consensus as before.
- **`fonts export --family` says which fallback families it left out** (report 2 §1: six
  families were exported, `✦` and `✕` were drawn by Segoe UI Symbol and 16 measurements were
  not possible). It names Segoe UI Symbol / Segoe UI Emoji when they are installed but not
  listed, and counts the other families WPF falls back to.

## [0.1.0] - 2026-11-17

First release. The entries below describe the implementation as it stands
after a from-scratch audit (`docs/M6-全面重讀.md`), which replaced several earlier behaviours
that over-estimated the available width.

### Added

- **GitHub Action inputs `version` and `sarif`**: pin the tool version the action installs (it
  installed the latest, so a new release could change an untouched build) and write SARIF for
  code scanning. The path is now passed after `--`, and `check`/`budget` accept `--` to end the
  options, so a folder named like a flag is scanned rather than parsed.

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
- Tab strips placed beside the content (`TabStripPlacement="Left"`/`"Right"`) in a project's own
  `TabControl` template: the template's `TabStripPlacement` triggers are applied, and the strip is
  measured as its widest tab (header text in each language + the `TabItem` template's header chrome).
  Templates that rearrange in ways that cannot be read statically, and WPF's default template, stay
  unknown at first (see below). Playnite 34.3% → 48.3% without configuration, 55.8% with `gridPanels`.
- WPF's default `TabControl` template with the tab strip beside the content: the strip is the widest
  tab's header plus its left and right `Padding` (6 + 6 by default), plus 2. A tab's `Margin` never
  widens a side strip (WPF's `TabPanel` measures side tabs without their margins), in any template.
- In `check`, a control that overflows a horizontal `StackPanel` keeps its width and is clipped by an
  outer element, so its own right padding, border and margin are cut off rather than taken from the
  text (Playnite's Bulgarian button rows: 5 false alarms gone). Only the right-hand amounts the tool
  can read are forgiven; budgets still keep the whole control on screen.
- Neighbours that used to block a site: a `Canvas` without `Width` (0), a neighbour whose
  `Visibility` is bound (counted as visible — the larger case), a `TextBlock` of `Run`s on one line
  (each in its own font, a space between elements), a control whose content is one element, and a
  child spanning an `Auto` and a star column (WPF never widens the `Auto` column for it).
  Text spanning an `Auto` column can grow until the grid is full.
- `controlWidths` in `xamlfit.config.json`: the widest a control sized at run time ever gets.
- `hiddenScrollBars` in `xamlfit.config.json`: ScrollViewers or lists (by `x:Name`) whose vertical bar never
  shows, so it is not taken off the width (ScreenToGif's editor panel: 28 false alarms → 1).
- `DataGrid` star columns: the grid's width minus its border, an assumed vertical scroll bar unless it is turned
  off, and its other columns, shared by weight and clamped by `MinWidth`. Other columns must be fixed or star —
  a column sized by its cells only has a width at run time.
- A `TextBlock` whose text comes from its own inline style (a setter and triggers) is measured at its widest value.
- A string with no translation in a language is judged in that language's layout (translated
  neighbours, that language's default font): WPF shows the source text there.
- Findings under a project `ScrollViewer` template whose bar is `Auto` say that they assume the bar.
- Playnite 48.3% → 66.2% without configuration (77.5% with `gridPanels`), ScreenToGif 40.0% → 45.1%,
  Kindling 76.0% → 90.7%, QuillNest 98.3% → 99.4%.
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
- **Kerning.** `fonts export` reads pair kerning from GPOS (`kern` feature, lookup types 2 and 9)
  and it is applied between neighbouring characters drawn by the same face, as WPF does by
  default. `Typography.Kerning="False"` (or bound) is reported as not measurable. Kerning-heavy
  strings used to measure up to 5.7% wide.
- **Hebrew and Thai** are measured by adding advances (they were reported as complex scripts).
- **Arabic, Persian and Urdu.** `fonts export` runs the font's GSUB and GPOS rules (joining forms,
  required ligatures, contextual alternates, kerning on the shaped glyphs) and stores the width of
  every letter form, the change for every pair of adjacent letters per language system
  (default, ARA, FAR, URD), and the change that depends on the two letters before `ے`/`ۓ`.
  Arabic marks take no width; 0–9 in Arabic text are measured both as written and as
  Arabic-Indic digits, the wider winning. Before writing a font's data the exporter checks it
  against its own full shaping on 20,000 random words, leaves out letters that would come out
  narrow, and leaves out the whole font when that would be most of the alphabet (Calibri 6.27).
  Next to an RTL character, kerning against a space or punctuation is only applied when it widens
  (WPF puts them in different runs).
- **Measured control-library themes**: MahApps.Metro 2.4 and MaterialDesignThemes 5.3
  (`MaterialDesign2.Defaults` / `MaterialDesign3.Defaults`), in `data/theme-chrome.json`, measured by
  `prototype/ThemeChrome`. Chrome, explicit-inset behaviour, the font size/weight/family the style
  sets, string casing (MahApps buttons are uppercase, "Square" ones lowercase; `ContentCharacterCasing`
  on the element wins), minimum widths, scroll bar width and fonts forced by the template, for implicit
  and keyed styles; project styles based on theme keys resolve. Applied only when the project's
  package reference (csproj, `Directory.Packages.props`, `packages.config`) has the same major.minor
  and no unmeasured theme shares the scope; otherwise the report says why. NETworkManager: 916 blocked
  sites -> 49.
- **Devanagari and Tamil.** `fonts export` shapes every grapheme cluster with the font's GSUB/GPOS per
  language system (default, Marathi, Hindi, Nepali, Tamil…), with pre-base vowel signs reordered and
  Tamil two-part vowels split, and stores its change from the plain advances; per consonant, the
  smallest change any following consonant produces when they join; and the largest kerning before
  punctuation. Text is measured as the sum of clusters (errs wide). A modifier after a virama, or any
  sequence that is not a cluster, is not measurable. Playnite's Marathi and ScreenToGif's Tamil are now
  judged (not-judged strings 304 -> 4 and 117 -> 1).
- **More Indic scripts**: Bengali, Gurmukhi, Odia, Kannada, Malayalam and Sinhala join Devanagari
  and Tamil, and Gujarati and Telugu with a boundary model: each cluster boundary adds the largest
  widening that cluster or any prefix of it produces before any cluster (WPF kerns across trailing
  marks); Telugu also leaves out joined-consonant narrowing and the obsolete ఴ.
- **Khmer, Myanmar and Tibetan** with the same cluster model (Khmer coeng as virama, register
  shifters before the vowel; Myanmar medials and vowel sequences, boundaries keyed by how a cluster
  ends; Tibetan stacks of up to two subjoined letters, keyed by stack with the widest vowel sign,
  plus the most any mark widens a stack). **Lao** adds advances like Thai. Punctuation of the
  script itself (Tibetan tsheg, danda) now counts as trailing punctuation.
- The export engine applies shaping features in stages and honours lookup flags (glyph classes,
  mark attachment classes, mark filtering sets). Joined consonants keep a separate value for vowel
  signs and nukta forms that narrow less than the bare consonant, are never narrower than the second
  cluster alone, and try a reph on either side; chains of three consonants add the largest chain
  widening seen. Odia measures 3.2% wide on average instead of 5.1%, Kannada 7.1% instead of 8.4%.
  The vocalic-L signs of Odia and Kannada, and a nukta on a letter that already has one, are not
  measurable. Joined consonants are now adjusted per pair
  (the least narrowing over every form of the second consonant), only for the first virama of a chain
  and only for plain consonants; a lone consonant + virama is shaped with halant forms; Sinhala's
  always-visible virama uses every feature. Marathi text measures 1.0% wide on average (was 2.1%).
- **HandyControl 3.5 and WPF-UI 4.3** are measured themes. Controls whose templates round widths to
  device pixels (found by measuring fractional content) get one extra pixel as neighbours.
- **`TextFormattingMode="Display"`** is measured from the font's `hdmx` hinted widths at each pixel size,
  with kerning rounded to whole pixels, at every scaling in the new `displayScales` setting (default
  1, 1.25, 1.5, 1.75, 2), keeping the widest. A pixel size the font does not list is not measurable;
  the report names it. The metrics file stores the hinted widths as differences from the rounded
  linear width. `fonts export --hinted` (Windows) adds GDI's hinted widths for every pixel size from
  8 to 48 px the font's `hdmx` leaves out, after checking GDI picked the same face (name, weight,
  italic, accepting the face's localized names: Traditional Chinese Windows reports 微軟正黑體).
  Faces GDI cannot select are listed. Kerning rounds half a pixel up; a size exactly on half a pixel
  is measured at both pixel sizes, with the widest kerning of either.
- `fonts export --family` skips other faces after reading their names, and exports files in
  parallel: ten families with `--hinted` in 6 minutes instead of 16. Each script and language
  system of a font is shaped in parallel too (Nirmala UI stays around 3 minutes with the extra work below).
- **Indic joins checked exhaustively**, and fixed where that found the previous version narrow:
  every two-consonant join with every vowel sign, nukta and mark found rare joins measured up to 35%
  narrow (Odia ଧ୍ଯି, Malayalam യ്യൢ, Bengali হ্যৃ, Kannada ಜ್ಞು). The export engine does not reorder a
  syllable the way WPF does, so each join is also shaped with a below-base or post-base vowel sign next
  to the base, a pre-base form (Telugu, Malayalam ra) moved to the front before the final stage, and
  Odia's candrabindu on the first consonant, keeping the widest — only for consonants that take
  below-base, post-base or pre-base forms. Telugu now uses joined-consonant narrowing. A chain of
  exactly three consonants adds a value kept per last two consonants (the widest over every first
  one; a separate value when the last carries a nukta; chains through ra or ya keep the widest of their
  kind) instead of one value for every chain: Kannada measures 4.5–5.0% wide on average instead of
  6.3–7.1%, Telugu 3.6–3.7% instead of 8.2%, Marathi 0.4% instead of 0.65%. Telugu's lone length mark
  ౖ and vocalic-L signs, a Khmer robat after anything but the base consonant and a second coeng ro in
  one cluster are not measurable (WPF draws them differently, or with a dotted circle).
- **Mongolian** is measured. `fonts export` runs the font's rules over every letter, pair, three
  basic letters in both genders and 400,000 random words (masculine, feminine and neutral-vowel), and
  keeps the widest width each letter — or each pair the font joins into one ligature — took,
  separately by joining position, vowel harmony, a following variation selector, an adjacent vowel
  separator or narrow no-break space, first syllable or not, whether the letter before is a vowel, and
  the neighbouring letter (a vowel's preceding letter, a consonant's following one); a coarser table
  backs it up. Variation selectors take the joining form of the letter before them, as in HarfBuzz. A
  self-check on 20,000 other words removes letters that come out narrow. Punctuation inside Mongolian
  text is shaped with it (Mongolian Baiti's `rclt` widens 25 marks such as 《 and 「), and the file
  records the wider width.
- **Digits WPF replaces.** Besides Arabic text, WPF always draws 0–9 as native digits when the
  element's language is Burmese, Dzongkha, Nepali (India), Pashto, Sindhi, Kashmiri, Urdu (India),
  Punjabi or Uzbek in Arabic script, Mazanderani or Northern Luri — and not for Khmer, Bengali or
  N'Ko, though .NET lists native digits for them (checked culture by culture against WPF). Both forms
  are measured and the wider one is used; Myanmar Text's ၀ is 17% wider than 0. In Display mode the
  replaced digits are reported as not measurable.
- **Variable fonts** are exported as one face per named instance, under the family names WPF uses
  (`Segoe UI Variable Text`, `Small`, `Display`; the bare `Segoe UI Variable` is not a family to WPF,
  which draws Segoe UI for it), with advance widths from `HVAR` and kerning from the variation deltas
  in `GDEF`. Legacy names such as `Segoe UI Variable Text Semibold` resolve. `--family` accepts either
  the font's family or an instance family. `--hinted` leaves variable fonts out: WPF's Display-mode
  widths for them are not GDI's.
- **Gzipped metrics files**: `fonts export` writes `xamlfit.fonts.json.gz` by default (a 38 MB file
  becomes about 6 MB; `--out xamlfit.fonts.json` writes it uncompressed); every command reads either,
  and when a folder has both, the newer one. Schema version 2 stores an Indic language system's table as its
  differences from the script's default table (about 1 MB less); version 1 files still read.
- **Three-consonant chains keep a value per sign on the last consonant**, and the export engine
  applies the mark-positioning features (`abvm`, `blwm`, `mark`, `mkmk`), where Nirmala UI spaces
  two stacked below-base forms. Average over-estimate on random words: Kannada 2.7–2.9% (was
  4.5–5.0%), Telugu 1.7–1.8% (3.6–3.7%), Odia 1.6–2.0% (3.7–4.3%), Malayalam 1.0% (1.7–1.9%), Khmer
  1.4% (2.5%). A Malayalam pre-base ra after a visible virama is drawn before the second consonant;
  a Khmer vowel sign over a chain is tried with every first consonant (it changes the base's width).
- **Reph only at the start of a syllable**, as WPF applies it: the export engine had turned a ra in
  the middle of a chain into a reph too, so chains with ra in the middle fell back to one maximum. A
  chain that starts with ra (its reph moved to the end) keeps its own value (stored only where it
  differs). Average over-estimate on random words: Kannada 1.8–1.9% (was 2.7–2.9%), Odia 1.5–1.8%
  (1.6–2.0%), Bengali 0.6% (0.8%), Marathi 0.3% (0.4%), Gujarati 1.3% (1.4%); Malayalam 1.1% (1.0%:
  its signed chains are now tried with every first consonant). Exhaustively still ≤ 0.03% narrow.
- **Kerning follows WPF's runs and languages.** The metrics file records which script and language
  system uses each kerning lookup (`kernSystems`, `kernLookupSystems`) and the letters each language
  draws as another glyph (`languageGlyphs`, from `locl`). Kerning applies only within a run of one
  script and only with that script's lookups (Calibri kerned `",` with its Arabic lookups in Latin
  text, 3.6% narrow), uses the element language's system (Segoe UI's Turkish has no "Ta"/"Te"
  kerning), only widens next to a letter the language swaps (Arial's Turkish *i*), and never narrows
  across WPF's 100-character chunks. Older metrics files keep applying every lookup.
- **Combining marks** are drawn with no width; text is also measured composed (NFC) and the wider
  is used; a letter followed by a mark kerns only to widen; a mark the font cannot draw makes the
  text not measurable (WPF draws a missing-glyph box). Decomposed Vietnamese was up to 6% narrow.
- **Checked against WPF**: Russian, Ukrainian, Bulgarian, Serbian, Greek, Turkish and Vietnamese
  (300 real strings each, seven fonts): worst under-estimate 0.03%; Amharic (Ebrima, locale strings
  and 2,000 generated words and sentences): 0.02%, 1.4% wide on average.
- **Hebrew points and cross-font Arabic joins**: Hebrew vowel points take no width and the letter
  before one takes the widest glyph the font composes for it (`markedWidths`; Arial's ע + ִ was 0.55%
  narrow); an Arabic letter joined to one drawn by another font is not measurable (Dubai lacks ؿ;
  measured as joined, 41% narrow).
- **Not measurable instead of guessed**: scripts that need shaping and are not modelled (Javanese,
  Balinese, Adlam, conjoining Hangul jamo…); Nastaliq fonts (cursive attachment on at least 100
  glyphs, mostly anchored away from the edges — inferred, no such font to check; Dubai is not
  affected); Display mode with combining marks or language-swapped letters.
- **Bare three-consonant chains are kept net of the first join**: when the last consonant has no
  sign, the chain value excludes the first two consonants' own join (added back when measuring), plus
  the most a following letter widens the chain; the first consonant had swung chains by 5–15 px.
- **Telugu and Gujarati joins are no longer counted twice**: a two-consonant join added both its join
  value and the boundary widening of its first half (హ్ణ measured 71% wide); after a join, the
  boundary is now the most that join widens before any letter. Average over-estimate on random words:
  Telugu 1.4% (was 1.7–1.8%), Gujarati 1.2% (1.3%), Marathi 0.2% (0.3%), Kannada 1.5–1.7%
  (1.8–1.9%). Exhaustively still ≤ 0.04% narrow.
- **Faster export engine**: each lookup records which glyphs can start it, and positions that cannot
  are skipped; output is byte-identical (Nirmala UI 534 s → 316 s, Myanmar Text 488 s → 108 s alone).
  `XAMLFIT_DEBUG=1 fonts export` prints the time per font file and per script section. With `--hinted`, GDI
  hinting now runs for each font file as soon as its shapes are done (still one file at a time)
  instead of after all of them.
- **Myanmar**: a stacked consonant (ဎ္ဘ) narrows by a value kept per medial or vowel sign on the
  stacked consonant, and a syllable boundary is keyed by the marks of the first cluster instead of its
  last two characters, with every next cluster tried by its first drawn character and its upper marks.
  Generated words 4.2% wide on average (was 7.7%), real Burmese 0.90% (was 0.79%, the price of
  trying more next clusters). The boundary search runs in parallel.
- **Variable fonts in Display mode** are measured: WPF rounds each glyph's linear width instead of
  hinting it (rounded up from 0.485), and the tool rounds kerning toward zero.
- **A family name WPF reads as family plus face name** (`Segoe UI Variable` → Segoe UI,
  `Times New Roman Italic` → italic) resolves the same way, when the metrics file lists the families
  installed where it was exported (`installedFamilies`, written by every new export).

### Verified

- **Text width** against WPF `FormattedText`, 1,652 comparisons including kerning-heavy pairs:
  mean error 0.006%, worst under-estimate 0.025%, worst over-estimate 0.26% (before kerning:
  0.039%, 0.068% and 1.43%).
- **Hebrew, Thai, Arabic, Persian** against `FormattedText` (`prototype/WpfOracle --scripts`):
  820 real translated strings from the test corpus (Hebrew 300, Arabic 300, Persian 200, Thai 20),
  1,000 random Arabic-script words and every letter
  alone and between two others, in Segoe UI, Arial, Tahoma, Courier New, Microsoft Sans Serif and
  Times New Roman (Leelawadee UI and Tahoma for Thai): worst under-estimate 0.05%, in either
  flow direction. Calibri is reported as not measurable.
- **Indic scripts** against `FormattedText` (Nirmala UI; 290 Marathi and 300 Tamil strings, and per
  script every cluster alone, random cluster sequences and conjunct-heavy random words, 4,000–15,000
  per script from `prototype/WpfOracle --indic-gen`): worst under-estimate 0.03%, Tamil 0.22%.
  Every Indic check repeated with an independent second set of generated strings, and exhaustively:
  every two-consonant join with every form of the second consonant, every bare three-consonant chain
  and 20,000 chains with signs, about 2.4 million strings (`XF_GEN_EXHAUSTIVE=1`): worst 0.03%.
  Khmer, Myanmar, Tibetan and Lao against two sets of 3,000 generated words each (Leelawadee UI,
  Myanmar Text, Microsoft Himalaya): 0.02%, 0.02%, 0.04% and 0.02%; Khmer exhaustively (about
  800,000 strings) 0.04%. Real translations: Khmer 1,176 strings (Survey Solutions, SystemTrayMenu),
  Tibetan 879 (SOSpect), Burmese 173 (Discord CustomRP, dotnet-ftps-server): worst under-estimate
  0.02%, 0.04% and 0.01%, on average 0.35%, 0.17% and 0.90% wide. Myanmar's generated words average
  4.2% wide (12% before Myanmar Text's single-adjustment `dist` lookup on ြ, which WPF does not apply,
  was skipped; 7.7% before stacked consonants and boundaries were keyed finer).
- **Mongolian** against `FormattedText` (Mongolian Baiti; `prototype/WpfOracle/gen-mong.js`, three sets
  of 4,000 words and sentences): worst under-estimate 0.04%, about 2.7% wide on average; the 32 strings
  of Windows' mn-Mong locale data: 0.02% and 2.4%; 312 real translations (MonkeyPaste): 0.02% and
  1.45%.
- **Variable fonts** against `FormattedText` (Segoe UI Variable, 15 named instances, 2,415 comparisons):
  mean error 0.016%, worst under-estimate 0.029%, worst over-estimate 0.09%. Display mode: 9,900
  widths over 15 instances, eleven sizes and five scalings, none narrower.
- **Display mode** against `FormattedText` (`prototype/WpfOracle --display` / `--display-check`; 13,200
  widths over ten fonts, eleven sizes, five scalings): with `--hinted`, all 13,200 measurable, 13,036
  exact, none narrower (with `hdmx` alone: 2,781 measurable, the rest reported as not measurable).
- **Themes** against real WPF windows with the theme merged (`prototype/LayoutOracle --theme
  mahapps|md2|md3|handycontrol|wpfui`, 43 cases each, both row semantics): MahApps 42 exact /
  1 conservative, MaterialDesign 2 and 3 41 / 2, HandyControl and WPF-UI 26 / 17, 0 over-estimates,
  0 wrong.
- **Layout** against real off-screen WPF windows, 228 cases: 186 exact, 42 conservative,
  0 over-estimates; with `check`'s row semantics 190 / 38 / 0 (`prototype/LayoutOracle --check`).
- **Real project windows** drawn by WPF with their own themes and translations
  (`prototype/CorpusRender`): Playnite, 6,444 comparisons in every language including Arabic,
  Persian, Hebrew and Marathi — 138 clipped by both, 2 false alarms, **0 missed**, text width within
  ±0.03 px of WPF's where both agree (Devanagari and Tamil err wide); ScreenToGif with the side panel
  declared and Arabic, Hebrew and Tamil measured, 3,540 comparisons — 133 / 2 / **0**. Before Arabic was measured: Playnite
  5,765 comparisons, 130 / 3 / 0; ScreenToGif, 3,043 comparisons including the editor's side panel and
  drop-downs, same font on both sides — 122 clipped by both, 28 false alarms (an assumed scroll
  bar), **0 missed**, and 76 headers that WPF draws 0.5–1.2 px short because of `UseLayoutRounding`
  (English loses up to 0.4 px at the same places; not a translation length issue, not reported). Eleven bugs found this way are fixed (see `docs/M6-全面重讀.md` §2.6), among
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
- 726 unit tests.

### Not verified

- nuget.org installation and Trusted Publishing: 0.1.0 is the first release, so that path runs
  for the first time with this tag. The release steps and the Action's script were rehearsed
  locally. (CI on Linux and Windows runs and passes.)
- The default UI fonts for Japanese, Korean and Simplified Chinese Windows: taken from
  Windows' font configuration, not measured on those systems.
- Text in drop-down lists, data templates and hidden panels: the render harness cannot draw
  it, so those judgements have not been compared against WPF.
