# Security

## What this tool does, and what that means

XamlFit **reads** `.xaml` and `.resx` files as text, together with `xamlfit.config.json` and the
metrics file `xamlfit.fonts.json`, and writes a report. It does not load assemblies, instantiate
XAML, execute anything from the project it scans, or read credentials. There is no server and no
daemon.

**It makes no network requests at all** — not for statistics, not for updates, not for anything.
The only files it writes are the ones you name with `--json`, `--sarif`, `--csv`, `--sites`,
`--write-baseline` and, for `fonts export`, `--out`. It does not create directories for them.

That narrows the realistic threat surface to one thing: **it parses input it does not trust.**
People run it in CI, on pull requests, on branches pushed by strangers. A crafted `.xaml` or
`.resx` file — or a crafted translation — is attacker-controlled input reaching an XML parser and
the CI log, with the permissions of your CI job.

### XML external entities and entity expansion

**Blocked.** Every XML file is read through `XmlReader.Create` with
`DtdProcessing = Prohibit` and `XmlResolver = null`. A file that declares a DTD is refused and
counted as a parse error, so entity-expansion attacks ("billion laughs") cannot start.
`ScannerTests.A_document_with_a_doctype_is_a_parse_error_not_an_entity_expansion` covers this.

### Pathologically nested files

**Refused, not followed.** Nesting is measured while streaming, before the file is loaded. A file
nested more than 512 elements deep is counted as a parse error. Without the limit, a 1.7 MB file
nested 100,000 levels deep kept both `budget` and `check` busy for more than 170 seconds — on CI,
one pull request could hold the pipeline until it timed out. No file in a ten-project corpus of real
XAML reaches the limit. `ScannerTests.A_pathologically_nested_document_is_a_parse_error_not_a_hang`
covers this.

### Symlinks and directory junctions

**Skipped.** Recursive enumeration does not follow reparse points. A link that points at one of its
own ancestors would otherwise make a scan run forever. The cost — source reached only through a
link is not scanned. `SymlinkTests` covers this (on Windows it needs permission to create links,
and says so when it cannot).

### Text a scanned file could inject into a CI log

**Escaped.** Translations and XAML attribute values can contain real line breaks, so a string
could otherwise start its own line in the console or `--msbuild` output and forge a diagnostic.
Everything scanned that reaches plain-text output is printed with control characters and the
Unicode line and paragraph separators escaped. Azure Pipelines (`##vso[`) and the old GitHub
Actions format (`##[`) act on a command **anywhere** in a line — `##vso[task.setvariable ...]` can
change a later step's variables — so both are defused. JSON and SARIF keep the original value;
their serialisers escape it. `DiagnosticsTests` covers this.

### Text that ends up in a spreadsheet

`budget --csv` and `--sites` are meant to be opened by translators. A source string that starts
with `=`, `+`, `-` or `@` would be run as a formula, so it is prefixed with `'` by default.
`--csv-raw` turns this off for tools that need the exact bytes. `CsvInjectionTests` covers this.

### Files inside the scanned tree that change the result

`xamlfit.config.json` and `xamlfit.fonts.json` are picked up from the folder being scanned.
**On CI, both are part of the pull request.** The config can lower `minCoverage`, raise
`marginPercent`, exclude folders or declare fonts absent; the metrics file decides how wide every
character is. Either can turn the gate off from inside the change being reviewed.

This is not forbidden, because it is also how people configure the tool. Treat changes to these
two files like changes to the CI workflow itself, and **in CI pass `--config` and `--metrics`
explicitly**, pointing at files outside the pull request's reach, if you want the gate to be beyond
the author's control.

### Font files

`xamlfit fonts export` parses binary font files. It is meant to run on a machine whose fonts you
already trust, not in CI against fonts supplied by a pull request; CI only reads the JSON it
produced.

### The GitHub Action

`action.yml` passes every input to the shell through `env`, never as `${{ inputs.x }}` inside a
script, so an input wired to something a pull request author controls stays a string.
`DataFileTests.The_action_never_interpolates_an_input_into_a_shell_script` keeps it that way.

### Paths in the output

JSON, SARIF and CSV reports contain paths relative to the scan root. `--msbuild` output uses full
paths, because that is what Visual Studio needs to open the file.

## Reporting a vulnerability

Open a [security advisory](https://github.com/HSU-YU-MING/cornhsu-xamlfit/security/advisories/new)
rather than a public issue. A crafted input that makes the tool hang, crash, read a file outside
the scan root, write outside the paths given on the command line, or inject a command into a CI log
is in scope.

Findings that amount to "the tool measured something wrong" are bugs, not vulnerabilities — please
open a normal issue. The one rule this tool is built on is that it would rather say *unmeasurable*
than claim text fits when it does not, so a missed overflow is taken more seriously here than a
false alarm.
