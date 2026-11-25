#!/usr/bin/env bash
# 印出 CHANGELOG 某一版的段落，GitHub Release 用。bash scripts/release-notes.sh 0.1.0
# 沒有這一版、段落是空的、日期還寫 Unreleased：exit 1。release.yml 在推套件**之前**跑它 ——
# 套件推上 nuget.org 之後才發現 CHANGELOG 沒寫，就救不回來了。
set -euo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."
ver=${1:?version}
head=$(tr -d '\r' < CHANGELOG.md | grep -F "## [$ver]" | head -n 1) || { echo "release-notes: no section for $ver in CHANGELOG.md" >&2; exit 1; }
[[ "$head" =~ ^##\ \[[0-9.]+\]\ -\ [0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || { echo "release-notes: the heading of $ver has no date: $head" >&2; exit 1; }
body=$(tr -d '\r' < CHANGELOG.md | awk -v h="$head" '$0 == h { on = 1; next } on && /^## \[/ { exit } on')
[[ "$body" =~ [^[:space:]] ]] || { echo "release-notes: the section of $ver is empty" >&2; exit 1; }
printf '%s\n' "$body"
