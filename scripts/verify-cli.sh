#!/usr/bin/env bash
# CLI 煙霧測試：每一條指令路徑至少被執行一次，並斷言退出碼。
#
# 為什麼需要這個：單元測試測得到 Core，測不到「旗標有沒有接線」。
# 實查到的兩個洞都只有真的跑才看得到：
#   - `--pseudo` 對資源引用的字串一筆都量不到（量了 0 筆還回 exit 0 說「沒問題」）
#   - 設定檔在必填參數檢查之後才套用，於是設定檔裡的 resx 永遠不生效
# 兩個都是「已出貨但沒人跑過」。

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

TMP="${TMPDIR:-/tmp}/xamlfit-cli-$$"
mkdir -p "$TMP"
trap 'rm -rf "$TMP"' EXIT

CLI=(dotnet run --project src/XamlFit.Cli -c Release --no-build --)
DEMO=samples/demo
RESX=samples/demo/Resources

fail=0

# run <預期退出碼> <說明> <參數...>
run() {
  local want="$1" what="$2"; shift 2
  "${CLI[@]}" "$@" >"$TMP/out" 2>&1
  local got=$?
  if [ "$got" != "$want" ]; then
    echo "FAIL: $what — expected exit $want, got $got"
    sed 's/^/       /' "$TMP/out" | tail -5
    fail=1
  else
    echo "ok: $what (exit $got)"
  fi
}

# ===== 基本 =====
run 0 "--version"                  --version
run 0 "--help"                     --help
run 2 "no arguments"
run 2 "unknown command"            frobnicate
run 2 "unknown flag"               budget "$DEMO" --nope

# ===== fonts =====
run 0 "fonts list"                 fonts list --metrics "$DEMO/xamlfit.fonts.json"
run 2 "fonts list, missing file"   fonts list --metrics "$TMP/nope.json"
run 2 "fonts, unknown subcommand"  fonts frobnicate

# ===== budget（匯出，不擋門）=====
run 0 "budget"                     budget "$DEMO"
run 0 "budget --csv"               budget "$DEMO" --csv "$TMP/b.csv"
run 0 "budget --json"              budget "$DEMO" --json "$TMP/b.json"
run 0 "budget --csv-raw"           budget "$DEMO" --csv-raw --csv "$TMP/raw.csv"
run 2 "budget, missing directory"  budget "$TMP/nope"
run 3 "budget, nothing to scan"    budget "$TMP"

# ===== check（閘門）=====
run 1 "check --resx"               check "$DEMO" --resx "$RESX"
run 1 "check --fail-on warn"       check "$DEMO" --resx "$RESX" --fail-on warn
run 1 "check --culture de"         check "$DEMO" --resx "$RESX" --culture de
run 3 "check, culture with no data" check "$DEMO" --resx "$RESX" --culture xx
run 1 "check --sarif"              check "$DEMO" --resx "$RESX" --sarif "$TMP/c.sarif"
run 1 "check --msbuild"            check "$DEMO" --resx "$RESX" --msbuild
run 2 "check, bad --fail-on"       check "$DEMO" --resx "$RESX" --fail-on sideways
run 2 "check, non-numeric --margin" check "$DEMO" --resx "$RESX" --margin abc

# --pseudo 永遠不擋 build（它報的是風險不是事實）
run 0 "check --pseudo"             check "$DEMO" --pseudo --resx "$RESX"

# ⚠ 量到 0 筆一定不能是 exit 0：那等於在什麼都沒看的情況下說「沒問題」
mkdir -p "$TMP/nosource"
cp "$DEMO/xamlfit.fonts.json" "$TMP/nosource/"
cat > "$TMP/nosource/Main.xaml" <<'XEOF'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:p="clr-namespace:Demo.Resources" Width="400">
  <TextBlock Width="90" Text="{x:Static p:Strings.Save}"/>
</Window>
XEOF
run 3 "check --pseudo with no source strings" check "$TMP/nosource" --pseudo

# ===== baseline 的來回 =====
run 0 "check --write-baseline"     check "$DEMO" --resx "$RESX" --write-baseline "$TMP/bl.json"
run 0 "check --baseline absorbs known debt" check "$DEMO" --resx "$RESX" --baseline "$TMP/bl.json"
run 2 "check, missing baseline"    check "$DEMO" --resx "$RESX" --baseline "$TMP/nope.json"

# ===== config =====
printf '{ "defaultFontSizes": 12 }' > "$TMP/bad.json"
run 2 "config, misspelled property" check "$DEMO" --resx "$RESX" --config "$TMP/bad.json"
printf '{ "marginPercent": 500 }' > "$TMP/range.json"
run 2 "config, out of range"       check "$DEMO" --resx "$RESX" --config "$TMP/range.json"
run 2 "config, missing file"       check "$DEMO" --resx "$RESX" --config "$TMP/nope.json"

# 設定檔提供 resx：⚠ 設定檔要在必填參數檢查之前套用，否則這一條會是 exit 2
run 1 "config supplies resx"       check "$DEMO"

echo
if [ "$fail" = 0 ]; then echo "CLI smoke test: all paths behaved as expected"; else echo "CLI smoke test: FAILED"; fi
exit "$fail"
