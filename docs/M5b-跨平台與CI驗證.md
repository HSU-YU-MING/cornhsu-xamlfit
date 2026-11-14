# 跨平台與 CI 驗證

> 日期：2026-11-14
> **結論：README 的「runs on Linux CI」仍然是未完全驗證的主張。**
> 建置層面成立，實際執行沒驗過，原因與補救寫在第 1 節。

---

## 1. 為什麼沒有真的在 Linux 上跑

這台機器**沒有 WSL 也沒有 Docker**，所以無法實際執行。
能做的只有建置層面的驗證與程式碼稽核。

**唯一能真正驗證的途徑是 GitHub Actions 的 `ci.yml`（跑在 `ubuntu-24.04`），
而它從沒執行過 —— repo 還沒公開。**

在那之前，README 的這句話要被當成「有根據的推測」而不是「已驗證的事實」。

## 2. 驗得過的部分

| 檢查 | 結果 |
|---|---|
| `dotnet publish -r linux-x64` | 成功，產出 `XamlFit.Core.dll` 與 `xamlfit.dll` |
| `deps.json` 裡的 Windows-only 組件 | **0**（`System.Windows`／`PresentationCore`／`WindowsBase`／`System.Drawing`／`Microsoft.Win32` 皆無） |
| 方案檔是否含 Windows-only 專案 | 否 —— `XamlFit.slnx` 只有 Core／Cli／Tests，三個 WPF 原型（ChromeGen、GridOracle、WpfOracle）**刻意不在方案裡**，所以 CI 在 Linux 上不會去 build 它們 |
| `scripts/*.sh` 的語法 | `bash -n` 通過 |
| workflow 與 `action.yml` 的 YAML | 無 tab、縮排一致 |

## 3. 稽核找到的兩個真跨平台 bug

兩個都是**靜默**的失敗 —— 不會報錯，只是結果變少。那正是「謊報健康」。

### 3.1 掃描根目錄位在點開頭的資料夾底下時，所有檔案都被排除

排除規則比對的是**完整路徑**：

```csharp
var p = f.Replace('\\', '/');
if (p.Contains("/.", StringComparison.Ordinal)) continue;   // ← 完整路徑
```

所以掃 `C:\Users\x\.config\app` 或 `/home/u/.local/src/app` 時，**每一個檔案都中規則**，
工具吐 exit 3 說「找不到任何文字位置」，而且不解釋為什麼。
同樣的問題也發生在路徑裡剛好有個叫 `bin` 的目錄（`D:\bin\myproject`）。

**修法**：比對**目錄名**而不是完整路徑，並改成目錄剪枝（被排除的目錄根本不進去——
順便解決「一個帶 `node_modules` 的 repo 要列舉十幾萬個檔案才找到幾十個 XAML」）。

### 3.2 副檔名比對在 Windows 不分大小寫、在 Linux 分

```csharp
Directory.EnumerateFiles(root, "*.xaml", SearchOption.AllDirectories)
```

Windows 會配到 `.XAML`／`.Xaml`，Linux 只配 `.xaml`。
結果是**同一個專案在 CI 上掃到的檔案比本機少**，CI 看起來比本機乾淨。

⚠ 語料裡 1,916 個檔案全是小寫，所以**這個 bug 在現有語料上測不出來** ——
修它的理由不是證據，是失敗模式：平台之間靜默的結果差異會直接摧毀對工具的信任。

**修法**：用 `EndsWith(extension, OrdinalIgnoreCase)` 自己過濾。
`.resx` 的列舉原本有同樣的兩個問題，一併修掉。

六個新的單元測試守住這兩條。

## 4. `action.yml` 的腳本注入

`action.yml` 從沒執行過。檢視時發現它把輸入**以文字替換**插進 shell 腳本：

```yaml
args=(check "${{ inputs.root }}" ...)
```

`${{ }}` 是 GitHub 在產生腳本時做的文字替換，所以帶引號或 `$(…)` 的輸入會變成
可執行的程式碼。這是 composite action 的典型腳本注入，GitHub 自己的文件就要求改用 `env:`。

**修法**：所有輸入走 `env:`（`XF_ROOT` 等），腳本只引用環境變數。

**實測證明**（`check "$XF_ROOT"`，輸入 `./src"; touch /tmp/XAMLFIT_PWNED; echo "`）：

- 注入**沒有發生**，那串字被當成單一的字串參數傳下去
- 帶空白的路徑（`./my src`）維持成單一參數

## 5. 一個我原本擔心、但實測證明不成立的

GitHub 的 composite action 用 `bash -eo pipefail` 執行，所以我懷疑
`[ -n "$X" ] && args+=(...)` 在條件為假時會讓整個步驟失敗。

**實測不會。** `set -e` 不會因為 `&&` 清單的**條件部分**失敗而中止
（只有「最後一個 `&&` 之後的那個命令」才算）。
用跑的比用想的可靠 —— 這一條留著，免得下次又去「修」一個不存在的問題。

## 6. 還沒驗證的（發版前要處理）

1. **實際在 Linux 上執行** —— 需要 GitHub Actions 或一台 Linux 機器
2. `release.yml` 從沒執行過（含 NuGet Trusted Publishing 那一段）
3. `action.yml` 從沒在真的 workflow 裡跑過；上面驗的只有參數組裝與注入防護
4. 字型 EULA 的法務立場（`RELEASING.md` 記著）
