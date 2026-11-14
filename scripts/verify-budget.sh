#!/usr/bin/env bash
# budget 模式是匯出不是閘門 —— 預期 exit 0，而且 CSV 的每一列都要有具名的 status
# （空白儲存格會被在地化負責人當成「沒限制」）。

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

CSV="${TMPDIR:-/tmp}/xamlfit-demo-budget.csv"

dotnet run --project src/XamlFit.Cli -c Release --no-build -- \
  budget samples/demo --csv "$CSV"
CODE=$?

if [ "$CODE" -ne 0 ]; then
  echo "FAIL: budget should not gate; expected exit 0, got $CODE"
  exit 1
fi

# 第 4 欄是 status。跳過註解列與標題列。
BLANK=$(awk -F, 'NR>2 && $4 == "" { n++ } END { print n+0 }' "$CSV")
if [ "$BLANK" -ne 0 ]; then
  echo "FAIL: $BLANK row(s) have a blank status column"
  exit 1
fi
echo "ok: every budget row has a named status"

# 至少要有一列 no-declared-limit（示範專案裡那個水平可捲動的 TextBlock）
if ! grep -q "no-declared-limit" "$CSV"; then
  echo "FAIL: expected at least one 'no-declared-limit' row"
  exit 1
fi
echo "ok: 'no-declared-limit' is reported as a named value"
