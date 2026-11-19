#!/usr/bin/env bash
# CI 守門：示範專案是刻意做壞的，所以預期 exit 1，而且每一種判定各一個（五種寬度判定 + 三種翻譯完整度）。
#
# 計數斷言同時驗證兩件事：工具真的會擋，而且擋的數量沒有莫名其妙變動。
# 改了判定規則導致誤判變多，CI 立刻紅。
#
# ⚠ 本機沒有 jq 時改用 node 讀 report.json（不要為此下載執行檔）。

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

REPORT="${TMPDIR:-/tmp}/xamlfit-demo-check.json"

dotnet run --project src/XamlFit.Cli -c Release --no-build -- \
  check samples/demo --resx samples/demo/Resources --json "$REPORT"
CODE=$?

if [ "$CODE" -ne 1 ]; then
  echo "FAIL: expected exit 1 from the deliberately broken demo, got $CODE"
  exit 1
fi

NODE_COUNT='const r=JSON.parse(require("fs").readFileSync(process.argv[1],"utf8"));
process.stdout.write(String((r.summary.counts||{})[process.argv[2]]||0))'

NODE_CAPTION='const r=JSON.parse(require("fs").readFileSync(process.argv[1],"utf8"));
process.stdout.write(String(r.findings.filter(f=>f.key==="Caption").length))'

read_count() {
  if command -v jq >/dev/null 2>&1; then
    jq -r --arg k "$1" '.summary.counts[$k] // 0' "$REPORT"
  else
    # ⚠ 用 fs.readFileSync 不要用 require()：require 會把路徑當模組解析，
    #   Windows 的絕對路徑直接 MODULE_NOT_FOUND。
    node -e "$NODE_COUNT" "$REPORT" "$1"
  fi
}

fail=0
expect() {
  local kind="$1" want="$2" got
  got="$(read_count "$kind")"
  if [ "$got" != "$want" ]; then
    echo "FAIL: expected $want '$kind' finding(s), got $got"
    fail=1
  else
    echo "ok: $kind = $got"
  fi
}

expect overflow       1
expect vertical-clip  1
expect trimmed        1
expect tight          1
expect reflow         1
# 翻譯完整度（0.5.0）：Strings.resx 的 Farewell 沒有德文、SavedCount 的德文用了來源沒有的 {2}、AboutPanel.xaml 寫死一段字，
# 0.7.0 起加上 MainWindow.xaml 寫死的視窗標題（Title="XamlFit demo"，Windows 畫在標題列上）
expect missing-translation  1
expect placeholder-mismatch 1
expect hard-coded-text      2

# 樣式解析的斷言。
# Caption 的字級由 <Style x:Key="Caption"> 設成 11 → 84.8 px，塞得進 92 px。
# 工具若沒解析樣式而用預設的 12 去量，會得到 92.5 px 並誤報 overflow。
# 所以「Caption 一筆 finding 都沒有」就是「樣式解析有生效」。
if command -v jq >/dev/null 2>&1; then
  CAPTION="$(jq -r '[.findings[] | select(.key == "Caption")] | length' "$REPORT")"
else
  CAPTION="$(node -e "$NODE_CAPTION" "$REPORT")"
fi

if [ "$CAPTION" != "0" ]; then
  echo "FAIL: 'Caption' produced $CAPTION finding(s); the named style's FontSize=11 was not applied"
  fail=1
else
  echo "ok: style-supplied FontSize is being resolved"
fi

exit "$fail"
