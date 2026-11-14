# XamlFit

**XAML 文字容納度的靜態稽核。不開 App 就算出每個文字位置有多少像素 ——
翻譯之前把限制交給譯者，翻譯之後讓 CI 擋下真正的溢出。**

[English](README.md)

```bash
dotnet tool install -g Cornhsu.XamlFit

xamlfit fonts export                      # 在有字型的機器上跑一次
xamlfit budget ./src --csv budget.csv     # 翻譯之前：把真實的像素上限交給譯者
xamlfit check  ./src --resx ./Resources   # 翻譯之後：真的裝不下就擋 CI
```

> 需要 **.NET 10 runtime**。解析與量測都是純算術，不相依 WPF、沒有原生二進位，Linux CI 跑得動。

## 它做了什麼別人沒做的事

所有偽本地化工具的做法都一樣：**把 App 跑起來、切到德文、一頁一頁看**。
微軟自己的在地化部落格十年前就描述過靜態版本 ——
*「只要能對應出哪個字串會進到哪個標籤，就有可能做出一個檢查，報告哪些字串塞不下」* ——
到今天沒有人做。

有一類 bug **只有**這樣看得到：`TextWrapping="NoWrap"` 加 `TextTrimming="None"` 的
`TextBlock` 被裁切時**不會觸發 `IsTextTrimmedChanged`**。執行期偵測看不到，靜態量得出來。

## 誠實的覆蓋率

**一般專案裡，XamlFit 算得出數字的大約是四分之一到三分之一的文字；
而在「真的可能被裁」的位置中，它算得出 76~85%。**

實測 1,921 個 XAML 檔（三個自家專案 + ScreenToGif、Playnite、MaterialDesignInXamlToolkit、
wpfui、ILSpy、HandyControl、MahApps.Metro、WinUI-Gallery）：

| | 全部語料 | 只看 App 畫面 |
|---|---|---|
| 可在地化文字位置 | 9,149 | 7,074 |
| 有硬寬度上界（可能被裁） | 35.9% | 33.0% |
| **其中 XamlFit 算得出數字** | **75.9%** | **85.0%** |
| 查不到任何寬度上界 | 54.3% | 61.9% |

各專案從 **34.8%**（ScreenToGif，大量用 `Auto`/`*` 欄）到 **100%**（wpfui、ILSpy、WinUI-Gallery）。
解 `Auto`/`*` 欄是最大的剩餘缺口，也是 roadmap 第一順位 —— 做掉會推到約 97%。

每次執行都會印出自己的 `resolution`，低於 `--min-coverage` 時 `check` 回 **exit 3**。
**工具不會在只看到三分之一的情況下說「沒問題」。**

⚠ **「查不到寬度上界」不等於安全。** 它的意思是分析在 XAML 裡找不到上界 ——
執行期的視窗仍然有寬度。這類位置一律報成 `no-declared-limit`，**永遠不是空白**，
而且不計入覆蓋率。

## 量測怎麼做

不用 SkiaSharp、不用 WPF、沒有原生二進位。XamlFit 自己讀字型的五張表
（`head`、`hhea`、`hmtx`、`cmap`、`OS/2`）把前進寬度加起來。

拿 WPF 自己的 `FormattedText` 當真值比對 1,400 組（9 種字型組合 × 7 個字級 ×
28 個英/德/法/中/日/韓字串）：

| | |
|---|---|
| 平均絕對誤差 | **0.039%** |
| 最大**低估**（可能漏掉溢出） | **0.068%** |
| 最大高估（可能誤報） | 1.43% |

那 1.43% 是日文字型裡的**單一個字距調整對**。逐字符比對顯示每個字的前進寬度
與 `FormattedText` 吻合到 **0.001 px** —— 誤差全部來自字距調整，而 XamlFit 不做它。
不做字距調整會系統性地**高估**寬度，所以工具錯的方向是「多報」而不是「漏報」。
預設 5% 的安全邊界覆蓋它三倍有餘。

### 度量檔

字型不隨套件散布，CI 也不需要安裝字型：

```bash
xamlfit fonts export --family "Segoe UI" --family "Microsoft JhengHei UI"
git add xamlfit.fonts.json
```

`xamlfit.fonts.json` 裡**只有前進寬度**，沒有字形外框、沒有字型程式碼。
你從自己已授權的字型匯出到自己的 repo，CI 讀數字。

字型沒有度量時，用到它的位置變成 `unmeasurable` 並計入覆蓋率，**絕不默默當成裝得下**。

## `budget` —— 翻譯之前

單位是**字串**不是 UI 位置，而且：

> **一個字串的預算 = 它出現過的所有位置裡最窄的那一個。**

`Resources.Save` 可能在設定頁有 180 px，但同一個字串在工具列只有 64 px。
預算就是 64，而且報告會**點名是哪個位置卡住的**，你才知道該放寬哪裡。

`limit_px` 是權威，`limit_chars` 只是估計 —— 每個字母寬度不同。
CSV 的欄位順序把限制放在來源字串前面，那是 Smartling 式匯入的慣例。

`headroom_px` 單獨就有用，不需要任何翻譯檔：英文就只剩 2 px 的位置已經在倒數了。

## `check` —— 翻譯之後

每個可量測的位置**獨立判定** —— 同一個字串可能在 A 處水平溢出、在 B 處垂直被裁：

| 換行 | 裁切 | 高度 | 結果 | 嚴重度 |
|---|---|---|---|---|
| `NoWrap` | `None` | — | `overflow` 文字疊出去 | **fail** |
| `NoWrap` | 省略號 | — | `trimmed` 資訊遺失 | warn |
| `Wrap` | — | 固定 | `vertical-clip` 行數塞不下 | **fail** |
| `Wrap` | — | `Auto` | `reflow` 版面被撐高 | info |
| 任一 | 任一 | — | `tight` 餘裕低於 `--margin` | warn |

沒有翻譯時用 `--pseudo` 照**分段**膨脹表模擬（`<10` 字元 → 200%、`11–20` → 180%、
`21–30` → 160%、`31–50` → 130%、`50+` → 120%）。單一倍數是錯的：短字串膨脹最兇，
而按鈕標籤最短也最容易壞。**`--pseudo` 的 finding 一律降一級，永遠不擋 build** ——
猜測不該擋別人的 PR。

### 退出碼

| | |
|---|---|
| 0 | 通過 |
| 1 | 有達到 gate 的 finding |
| 2 | 用法、設定或輸入檔錯誤 |
| 3 | **結果不可信** —— resolution 低於 `--min-coverage`，或沒有任何可量測的位置 |

## 已知限制

詳見 [README.md](README.md#known-limitations)。最重要的三條：

- `Auto` / `*` Grid 欄目前解不了（佔有風險位置的 21.6%，roadmap 第一順位）
- `ComboBox` / `TextBox` 的 chrome 量不到 → 裡面的文字一律 `unmeasurable`，
  **絕不用「只算 padding+border」去估**（那會低估 chrome 而漏報溢出）
- 阿拉伯文／印度系／泰文的字形依上下文變形，加總前進寬度不正確 → `unmeasurable`，不猜

## 相關工具

[XamlContrast](https://www.nuget.org/packages/Cornhsu.XamlContrast) 稽核同一份 XAML 的
WCAG 對比度，[XamlKeys](https://www.nuget.org/packages/Cornhsu.XamlKeys) 稽核資源鍵的作用域。
同一套退出碼契約、同一種 baseline 形狀、同一個心智模型。

## 授權

MIT。隨附資料的聲明見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
