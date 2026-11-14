# Contributing

This project is maintained by one person. The most useful thing you can send is not a pull
request — it is **a report from a codebase I have never seen**.

Every layout rule here was checked against real WPF, by drawing the same XAML in a window and
measuring it. Several rules I wrote from my own understanding passed their tests and were still
wrong — a 10-pixel `ScrollBar` that is really 16.8, an `Auto` column that pushes its neighbours out
instead of clipping, a default font that isn't Segoe UI. Tests check that the code does what I
think WPF does. Only a real window checks that WPF does it.

Your project, your fonts and your translations are a sample I don't have.

## The most valuable reports

**A false alarm** — XamlFit says text overflows or is clipped, and in the running app it fits.
This is the highest-value report there is: a tool that cries overflow on text that fits is a tool
people turn off.

**A miss** — text that is really cut off, and XamlFit said nothing.

**A project it can't measure** — exit 3, or a `resolution` figure so low the result says nothing.
Sometimes that is one config line or one `fonts export` away from fixed; sometimes it is a real
boundary of static analysis. Either way I want to know which.

Use the [issue forms](https://github.com/HSU-YU-MING/cornhsu-xamlfit/issues/new/choose) — they ask
for exactly what I need to reproduce. **Write in English or 中文, whichever is easier.**

**One exception: security.** A crafted file that makes the tool hang, crash, or read or write
outside its paths should be reported privately — see [SECURITY.md](SECURITY.md). A wrong verdict is
not that, however badly it behaves; those belong in the open.

### Before you open one

Check [Known limitations](README.md#known-limitations). Text set from code, triggers or visual
states, fonts chosen inside a control's own template, and properties that aren't scanned (`ToolTip`,
`Title`, watermarks) are documented gaps. A report about one is still welcome — it tells me the gap
hurts in practice — just say so, so we skip the diagnosis.

### What makes a report reproducible

- **The finding, exactly as printed** — all three lines: the verdict with the culture and the
  site, the text, and `needs … px, has … px`.
- **The first lines of the run**, down to `resolution`. They name the metrics file and the default
  font; a surprising number of problems are really font or width-resolution problems.
- **A screenshot of the running app** in that language at 100% display scaling, if you can. For a
  layout tool, that is the ground truth.
- **The smallest XAML that reproduces it**: the element and its containers up to the first one with
  a known width. Rename keys and strings freely.
- **The tool version** (`xamlfit --version`).

## If you want to send code

Rules come from evidence. **A pull request that adds or changes a layout rule needs the real-world
case that proves it** — a project, a window, a screenshot — and a case in `prototype/LayoutOracle`
showing WPF's own measurement agrees. "WPF can also do X, so we should handle X" is not enough on
its own.

Every change needs a test: a new rule gets a test for the shape it resolves, a bug fix gets a test
that fails before the change. Run these before opening a pull request:

```bash
dotnet test XamlFit.slnx
bash scripts/verify-demo.sh
bash scripts/verify-budget.sh
bash scripts/verify-cli.sh
```
