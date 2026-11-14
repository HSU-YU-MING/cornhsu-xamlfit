# XamlFit 專案規劃書 v0.1

> 日期：2026-11-14
> 狀態：**尚未動工，無原型。M0 是 go/no-go 閘門（見第 6 節）。**
> 一句話：**不開 App 就算出每個文字位置有多少像素 —— 翻譯之前給約束，翻譯之後擋溢出。**

---

## 0. 核心原則

**靜態分析 + 全量掃描 + CI 退出碼。** 跟 XamlContrast、XamlKeys 同一條憲法。

本專案自己的兩條：

**(1) 像素是權威，字元數是方便。** 現有的翻譯平台（Smartling、Transifex、SimpleLocalize）
都用**字元數**當長度限制，而且那個數字是人手打的。STAR Translation 的文件直接點破：
限制應該用像素表達，因為每個字母寬度不同，字元數過關的翻譯還是可能要重寫。
本工具算的是像素；字元數只是給人看的換算值，輸出時必須標明它是估計。

**(2) 量不到就要說，不准假設沒事。** 字型度量缺、複雜文字系統、可用寬度算不出來 ——
這些一律標 `unmeasurable` 並計入覆蓋率，低於下限就 exit 3。
**最嚴重的失敗模式是「掃完說沒問題，上線被德文撐爆」。**

憲法第三條（繼承）：**每條規則來自實查，而且實查的樣本必須包含非作者的專案。**

---

## 1. 這是什麼，跟現有工具的界線

| 工具 | 做什麼 | 缺什麼 |
|---|---|---|
| **pseudo-localization / pseudo-l10n / Unity 偽本地化** | 產生偽譯字串，把 App 跑起來看哪裡爆 | **執行期**。要跑、要人看、一頁一頁 |
| **Localization Overflow Guard** | 瀏覽器端的 CSS 溢出壓力測試 | 網頁，且是執行期 |
| **Smartling / Transifex / SimpleLocalize** | 支援對字串設長度限制並隨匯出給譯者 | **數字是人手打的**，而且是字元數不是像素。沒有任何東西從 UI 反推它 |
| **WPF 執行期偵測**（比較 `ActualWidth` 與 `FormattedText` 寬度、`IsTextTrimmedChanged`） | 抓得到被裁的文字 | 執行期；而且 `TextTrimming="None"` **不會觸發事件**，那一類它根本看不到 |
| **ReSharper / Rapid XAML** | XAML 靜態分析 | 規則裡沒有任何版面尺寸或文字量測 |

**2026-11-14 查證結果：**

- 所有偽本地化工具**都是把 App 渲染出來再看**，沒有任何一支做靜態量測。
- 微軟自己的在地化部落格（Jesper Hermansen）把這個構想寫得很具體：只要能對應出
  哪個字串會進到哪個標籤，就**有可能**做出一個檢查，報告哪些字串塞不下。
  那是十年前的文章，寫的是 *would be possible* —— **到今天還是沒有人做。**
- 沒有任何工具從 UI 版面反推像素預算並匯給譯者。
- NuGet：`cornhsu.xamlfit`、`xamlfit`、`cornhsu.xamlbudget`、`xamlbudget` 全部 404。
  GitHub 無同名 repo。

### 1.1 誰在什麼階段用

| 角色 | 階段 | 用哪個模式 |
|---|---|---|
| PM / 在地化負責人 | **送翻譯之前** | `budget` —— 匯出每個字串的像素上限給譯者 |
| 開發者 | 改完版面 | `budget` —— 看餘裕（headroom），英文就只剩 2px 的位置是地雷 |
| CI | 譯文回來之後的 PR | `check` —— 真實譯文量不下就擋 |
| 還沒有翻譯的專案 | 隨時 | `check --pseudo` —— 用膨脹率模擬，報風險 |

**`budget` 在翻譯之前跑，`check` 在翻譯之後跑。** 這是本工具跟所有競品最大的差別：
別人都只活在「之後」。

---

## 2. 名詞對照

| 詞 | 意思 |
|---|---|
| **文字位置（text site）** | 一個會顯示文字的 XAML 元素。工具的掃描單位 |
| **可用寬度（available width）** | 這個位置實際能畫文字的像素寬。扣掉 padding、margin、框線 |
| **預算（budget）** | 一個**字串資源**的像素上限 = 它出現過的所有位置裡最窄的那一個 |
| **卡點（binding site）** | 決定了預算的那個最窄位置 |
| **餘裕（headroom）** | 可用寬度減去目前字串的實際寬度 |
| **度量檔（metrics file）** | 從字型萃取出的純數字（advance width 等），不含字型本身 |
| **後端（backend）** | 量這一筆用的是哪種來源：字型檔、度量檔、或量不到 |
| **膨脹率（expansion）** | 翻譯後字串長度的成長倍率。**跟原字串長度強相關**，不是單一數字 |

---

## 3. 範圍邊界

### 做

- 解析 XAML，算出文字位置的可用寬度（固定寬度鏈 + 控制項預設內距表）
- 純 C# 讀 TrueType/OpenType 度量，量出字串像素寬
- `budget` 模式：匯出每個字串資源的像素與字元預算（CSV / JSON）
- `check` 模式：拿 `.resx` 的真實譯文去量，判定溢出／裁切／垂直裁切
- `--pseudo`：沒有翻譯時用分段膨脹率模擬
- CI 退出碼、baseline、ignore 註解

### 不做（v1 明確排除）

- **把 App 跑起來** —— 那是所有競品在做的事，也是本工具存在的理由
- **渲染或截圖** —— 只算數字，不畫圖
- **自動修版面** —— 寬度要放到哪裡是設計決定
- **翻譯品質** —— 只管裝不裝得下
- **複雜文字系統的量測**（阿拉伯文、印度系、泰文）—— 字形依上下文變形，加總 advance
  完全不對。**標 `unmeasurable`，不猜**
- **RTL 版面** —— `FlowDirection` 的鏡像不影響寬度，但相關的版面行為 v1 不處理
- **自訂 `ControlTemplate` 的內距** —— 偵測到使用者換過範本就標 `unmeasurable`
- **綁定特定翻譯平台的 API** —— 只出通用的 CSV / JSON

---

## 4. 系統架構

### 4.1 真正的難題：靜態可知的邊界

**這一節決定專案值不值得做**，所以它排在最前面，而且 M0 就是去量它。

**可以算出可用寬度的：**

| 情形 | 說明 |
|---|---|
| `Width="120"` 寫在文字元素上 | 最乾淨 |
| 固定像素的 `ColumnDefinition` / `RowDefinition` | `Width="120"` |
| 父層 `Border` / `StackPanel` 有固定 `Width` | 扣掉 `Padding`、`Margin`、`BorderThickness` |
| 有 `MaxWidth` | 給的是上界 —— 超過就一定裁或換行 |
| 固定寬視窗 + 一路固定寬的容器鏈 | 對話框常見 |

**算不出來的：**

| 情形 | 為什麼 | v1 處置 |
|---|---|---|
| `Auto` / `*` 欄 | 內容驅動或分配驅動 | `unmeasurable`（v2 解） |
| `ItemsControl` 項目 | 虛擬化 | `unmeasurable` |
| **`<Button Content="Save"/>`** | 內距來自控制項範本，不在 XAML 裡 | **見下，必須解** |

**按鈕是這個專案的成敗關鍵。** 德文膨脹的頭號受害者就是按鈕標籤（`Save` → `Speichern`
是 +150%），而它恰好落在「算不出來」那一格。一個只看固定寬度的工具會漏掉最該抓的一類。

**對策：內建控制項預設內距表。** 照 XamlContrast 處理 `WinUiTheme.json` 的同一招 ——
各控制項在各佈景（Aero2 / Fluent / WinUI）下的預設 `Padding`、`BorderThickness`、
最小尺寸，**從官方預設樣式產生，不是手抄**，產生方式寫成測試。
有了它，`Button`、`TabItem`、`MenuItem`、`CheckBox` 從「不可知」變成「可知減去已知常數」。

⚠ 使用者換過 `ControlTemplate` 時這張表就失效。偵測到自訂範本 → `unmeasurable`。

### 4.2 量測：度量檔優先，零原生相依

**量文字不需要光柵化器，只需要數字。**

advance width 躺在字型的五張表裡：

| 表 | 取什麼 |
|---|---|
| `head` | `unitsPerEm` |
| `hhea` | `numberOfHMetrics` |
| `hmtx` | 每個字符的 advance width |
| `cmap` | 碼位 → 字符（format 4 與 12） |
| `OS/2` | 行高度量（垂直裁切要用） |

讀這五張表是**約三百行純 C#**，零原生相依，.NET 跑得到的地方就跑得到。

```
寬度 = Σ advance(字符) × 字級 ÷ unitsPerEm
```

⚠ 這條線性公式對應的是 WPF 的 **`TextFormattingMode="Ideal"`**（WPF 的預設）。
`Display` 模式會把字符對齊像素格，寬度略有不同 —— 偵測到樹上設了 `Display` 就加註，
量測仍沿用 Ideal 並在報告標明。

**流程：**

```bash
xamlfit fonts export          # 在有字型的機器上跑 → xamlfit.fonts.json（只有數字）
git add xamlfit.fonts.json    # 進 repo
xamlfit budget ./src          # CI 讀數字，不需要安裝任何字型
```

這同時解掉兩件事：**CI 不必裝字型**，而且**我們從未散布任何字型** ——
使用者從自己已授權的字型匯出數字，放進自己的 repo。

> ⚠ **發版前必須做的法務功課**：從商用字型萃取 advance width 並 commit，
> 我的判斷是「度量是事實，不是字型軟體的創作表達」，但這需要真的讀一次字型 EULA 才能
> 寫進文件。即使最嚴格的解讀，「使用者自行匯出到自己 repo」這個形態也是最安全的。
> **不要在套件裡內建任何商用字型的度量**；只內建 OFL / Apache 授權字型（Noto、Roboto、
> Open Sans）作為方便值，並在 `THIRD-PARTY-NOTICES.md` 聲明。

**降級階梯（退化要用喊的）：**

1. 本機有字型檔 → 直接讀，最準
2. 有度量檔 → 純算術
3. 兩者皆無 → `unmeasurable`，計入覆蓋率，低於 `--min-coverage` 就 **exit 3**

**SkiaSharp / HarfBuzz 不是 v1 的相依**，降級成 v2 的選用高保真後端（真正的 shaping）。
理由：它解的是「怎麼畫」，不解「CI 上沒有 Segoe UI」；而且它會把 per-RID 原生二進位
拖進 dotnet tool —— 那正是 Parity 被迫拆成九個平台包的同一個坑。

### 4.3 沒有 shaping 會失去什麼

| 項目 | 影響 | 處置 |
|---|---|---|
| 字距調整（kerning / GPOS） | 拉丁文 UI 標籤誤差很小 | 安全邊界；報告標明後端 |
| 連字（ligature） | UI 字型少用 | 同上 |
| CJK | 幾乎固定前進寬度 | 無影響 |
| 阿拉伯文／印度系／泰文 | 字形依上下文變形，加總完全不對 | **`unmeasurable`，不猜** |
| 字型後備鏈 | `FontFamily="Segoe UI, Microsoft JhengHei"` 要逐碼位決定用哪一支 | 依序查度量檔；全部查不到 → `unmeasurable` |

**每一筆 finding 都要標明是哪個後端量的。** 使用者看到數字時要知道它的可信度。

### 4.4 預算的單位是字串，不是位置

**這是本工具最重要的一條規則，也是只有全專案靜態分析算得出來的東西：**

> **一個字串資源的預算 = 它出現過的所有位置裡，最窄的那一個。**

`Resources.Save` 在設定頁的大按鈕上有 180px，但同一個字串也出現在工具列的小按鈕上
只有 64px —— **預算就是 64**。

設計師不可能知道這件事，PM 手填長度限制時更不可能知道。
報告**必須點名卡點**（是哪個檔案的哪一行把預算壓到 64），否則使用者無從判斷該放寬哪裡。

### 4.5 判定矩陣

「塞不下」有四種結局，後果完全不同，不能混成一個 finding：

| `TextWrapping` | `TextTrimming` | 高度 | 結果 | kind | 預設分級 |
|---|---|---|---|---|---|
| `NoWrap` | `None` | — | 文字溢出，疊到隔壁或被父層 clip | `overflow` | **fail** |
| `NoWrap` | `CharacterEllipsis` / `WordEllipsis` | — | 顯示省略號，**資訊遺失** | `trimmed` | warn |
| `Wrap` | — | 固定 | 行數超出 → **垂直裁切** | `vertical-clip` | **fail** |
| `Wrap` | — | `Auto` | 版面被撐高，水平沒事 | `reflow` | info |
| 任一 | 任一 | — | 餘裕低於安全邊界（預設 5%） | `tight` | warn |

第一列最嚴重也最隱蔽：`TextTrimming="None"` 時**連執行期的 `IsTextTrimmedChanged`
事件都不會觸發** —— 這一類只有靜態量得出來，是整個工具最硬的賣點。

`vertical-clip` 要算行數，需要斷行演算法（WPF 的斷行規則）。v1 只在「固定高度 + 固定寬度」
兩者皆知時判定，其餘 `unmeasurable`。

### 4.6 膨脹率：分段表，而且只用在估計

查證到的分段表（W3C 式）：

| 原字串長度 | 可能膨脹到 |
|---|---|
| < 10 字元 | 200% |
| 11–20 | 180% |
| 21–30 | 160% |
| 31–50 | 130% |
| 50+ | 120% |

（Unity 偽本地化預設的倍率形狀一致：0–10 用 2×、10–20 用 1×、20–30 用 0.8×。
實例校驗：`Save` → `Speichern` +150%、`Delete` → `Löschen` +75%。）

**單一倍數是錯的。** 短字串膨脹最兇，而按鈕標籤正是最短的那一類。

⚠ **膨脹率只用來把像素預算換算成「大約幾個字」，不參與 CI 判定。**
拿猜測去擋別人的 PR，工具會被關掉。判定一律用真實譯文（`--pseudo` 模式除外，
而那個模式的 finding 預設只報不擋）。

### 4.7 兩個模式，一個引擎

```bash
xamlfit budget ./src --csv budget.csv        # 翻譯之前：匯出約束
xamlfit check  ./src --resx ./Resources      # 翻譯之後：真實譯文，CI 守門
xamlfit check  ./src --pseudo                # 還沒翻譯時的退路
xamlfit fonts  export                        # 產生度量檔
```

**子指令從第一天就設計進去。** 這是從 XamlKeys 那輪直接繼承的教訓：XamlContrast 1.0
凍結了「裸的位置參數就是 root」，現在再加子指令會跟一個叫 `Resources` 的資料夾撞名，
等於要發 2.0。XamlFit 一開始就是 `xamlfit <子指令> <root>`，這條路永遠開著。

### 4.8 輸出合約

**`budget` 的 CSV**（欄位順序照 Smartling 的慣例：限制放在來源字串**前面**一欄）：

```csv
key,limit_px,limit_chars_de,font,size,binding_site,source,source_px,headroom_px,backend
Resources.Save,64,10,Segoe UI,12,MainToolbar.xaml:42,Save,31,33,metrics
```

**`check` 的 JSON**：

```json
{
  "schemaVersion": 1,
  "toolVersion": "0.1.0",
  "summary": {
    "files": 128, "textSites": 642,
    "measured": 511, "unmeasurable": 131,
    "coverage": 79.6,
    "fontsResolved": 3, "fontsMissing": 1,
    "backend": { "fontFile": 0, "metrics": 511 },
    "suppressed": 0,
    "counts": { "overflow": 2, "trimmed": 7, "verticalClip": 1, "tight": 14, "reflow": 23 }
  },
  "findings": [ ... ]
}
```

`summary` 與 `findings` 分兩層的理由跟家族其他兩支一樣：CI 只消費 findings 的話，
「有一支字型沒有度量、131 個位置量不到」這件事等於不存在。

**退出碼契約：**

| 情況 | 碼 |
|---|---|
| 通過 | 0 |
| 找到違規（達到 gate 的 finding） | 1 |
| 用法、設定或輸入檔錯誤 | 2 |
| **結果不可信**：可量測比例低於 `--min-coverage`（預設 70%）、或 0 個可量測位置 | 3 |

**語言**：CLI 輸出與 JSON 欄位值一律英文，中文只留在 `README.zh-Hant.md`。

### 4.9 導入既有專案

照家族形狀，不發明新的：**baseline / ratchet**（鍵不含行號）、
**ignore 註解**（`<!-- xamlfit-ignore: 理由 -->`，管下一個元素，理由必填，計入 `suppressed`）。

---

## 5. Repo 結構

```
cornhsu-xamlfit/
├── src/
│   ├── XamlFit.Core/             版面解析、字型度量、判定
│   └── XamlFit.Cli/              CLI 外殼（子指令）
├── tests/
│   └── XamlFit.Tests/
├── samples/demo/                 刻意做壞的示範專案
├── baselines/
├── data/                         控制項預設內距表（產生，非手抄）
├── docs/
├── scripts/
├── .github/workflows/            ci.yml + release.yml
├── action.yml
├── README.md / README.zh-Hant.md
├── CHANGELOG.md / ROADMAP.md / RELEASING.md / CLAUDE.md
├── Directory.Build.props         ⚠ Version 固定 0.0.0-dev，tag 才是版本真相源
├── THIRD-PARTY-NOTICES.md        內建的 OFL 字型度量要聲明
└── LICENSE                       MIT
```

**不做 npm 包裝**（理由照家族慣例：使用者是 .NET 開發者，人人都有 dotnet）。

---

## 6. 里程碑

### M0 — 可量測比例調查（**go / no-go 閘門**）

**不寫任何正式程式碼之前，先回答一個數字：真實專案裡有多少文字位置的可用寬度是靜態可知的？**

用拋棄式腳本掃自家 WPF 專案 + 公開語料（ScreenToGif、Playnite、
MaterialDesignInXamlToolkit、wpfui、ILSpy、HandyControl、MahApps），
分成三類統計：固定寬度可知／靠控制項內距表可知／不可知。

**判準**：

- 加上內距表後 **≥ 40%** → 做，而且內距表是 v1 必要條件
- **15–40%** → 只做 `budget` 模式（預算表對少數位置也有價值），`check` 延後
- **< 15%** → **不做**。寫一份結論文件，把這個題目關掉

這一關的存在理由是 XamlContrast §8.3 的教訓：四個自家專案覆蓋率 89~100%，
八個公開專案合計 **9.6%** —— 同一個工具，兩個數量級。
**同源樣本量不出真實召回率。**

### M1 — 字型度量
純 C# 讀五張表、`fonts export` 指令、度量檔格式、後備鏈解析。
驗收：對同一組字串，度量檔算出的寬度與 WPF `FormattedText` 的差距 < 0.5%。

### M2 — 版面解析
固定寬度鏈、padding/margin/border 扣除、控制項預設內距表（從官方預設樣式產生）、
自訂範本偵測。驗收：M0 的「可知」分類全部能算出數字。

### M3 — `budget` 模式
跨位置取最小值、卡點點名、餘裕、CSV / JSON 匯出、字元估計（分段膨脹表）。

### M4 — `check` 模式
`.resx` 對應（XAML 的 `{x:Static p:Resources.K}` → 資源鍵）、判定矩陣、
`--pseudo`、退出碼、baseline、ignore。

### M5 — 發佈
repo 公開、CI（示範專案的退出碼與計數斷言）、`action.yml`、
nuget.org Trusted Publishing 政策、`v0.1.0`。

---

## 7. 已知盲區（誠實清單，寫進 README）

| 盲區 | 後果 | 處置 |
|---|---|---|
| `Auto` / `*` 欄 | 大量位置量不到 | v1 `unmeasurable`；v2 解 Grid |
| 自訂 `ControlTemplate` | 內距表失效 | 偵測到就標 `unmeasurable` |
| 執行期字級變化（高 DPI、協助工具放大） | 量的是設計值 | 文件揭露；可設全域縮放係數 |
| `TextFormattingMode="Display"` | 寬度略有差異 | 沿用 Ideal 計算並在報告標明 |
| 複雜文字系統 | 加總 advance 不正確 | `unmeasurable` |
| 無 kerning | 誤差數個 em 單位 | 安全邊界；v2 的 shaping 後端 |
| 多行斷行 | 行數算不準 | v1 只在寬高皆固定時判 `vertical-clip` |
| 字串來自 Binding / 資料 | 不是資源字串，量不到 | 只處理字面與 `.resx` 綁定 |

---

## 8. 設計決策紀錄

**為什麼不用 SkiaSharp（2026-11-14）。** SkiaSharp 解的是「怎麼畫」，不解
「CI 上沒有 Segoe UI」—— 字型不在，它一樣靜默掉到後備字型，每個寬度都錯。
而且它會把 per-RID 原生二進位拖進 dotnet tool（Parity 九個平台包的同一個坑）。
純 C# 讀五張表就能拿到需要的全部數字，跨平台、零原生相依。
SkiaSharp + HarfBuzz 留給 v2 當選用的高保真後端。

**為什麼有子指令。** 見 4.7。XamlContrast 的前車之鑑。

**為什麼不與 XamlContrast / XamlKeys 共用解析引擎。** 沿用 XamlKeys 的決策：
XamlContrast 的 ROADMAP 寫明「觸發條件才動工 —— 沒有消費者的公開 API
只是提前凍結自己的重構自由」。三支工具共用的是**慣例與知識**（baseline 形狀、
退出碼心智模型、`x:Key` 的解析眉角），不是程式碼。知識抄得走。

---

## 9. 風險表

| 風險 | 影響 | 對策 |
|---|---|---|
| **可量測比例太低** | 工具沒用 | **M0 的 go/no-go 閘門**，先量再做 |
| 量測誤差造成邊界假警報 | 工具被關掉 | 安全邊界可設；只報超過 N% 的；標明後端 |
| 字型授權 | 不能內建商用字型度量 | 使用者自行 `fonts export`；內建只放 OFL 字型 |
| 自訂範本讓內距表失效 | 誤判按鈕寬度 | 偵測到自訂範本就不猜 |
| 翻譯平台整合的維護成本 | 綁死某家廠商 | v1 只出通用 CSV / JSON |
| 市場 | — | 每個多語系 WPF/WinUI App 都是使用者；而且 `budget` 模式對**還沒開始翻譯**的專案就有價值 |
| 微軟補上這塊 | 被取代 | 他們的產品線是執行期工具；構想寫了十年沒做 |

---

## 10. 版本策略

**留在 0.x 直到介面凍結。** 0.x 期間 README 明寫「請 pin 確切版本」。
**起始版本 0.1.0。** 版本欄位固定 `0.0.0-dev`，**tag 才是版本真相源**。

1.0 的凍結面：

- [ ] 子指令與旗標
- [ ] 退出碼四碼的語意
- [ ] JSON 報告（`schemaVersion`）
- [ ] CSV 匯出的欄位與順序
- [ ] 度量檔格式
- [ ] `xamlfit.config.json` 的 schema
- [ ] baseline 檔格式
- [ ] `action.yml` 的 inputs
- [ ] ignore 註解語法

---

## 11. 命名（定案）

```
產品名 / repo    XamlFit
NuGet            Cornhsu.XamlFit
CLI 指令         xamlfit
GitHub           HSU-YU-MING/cornhsu-xamlfit
```

2026-11-14 查證：NuGet 與 GitHub 上精確名稱皆未被使用。

**選它的理由**：`fit` 同時蓋住兩個模式 ——「裝不裝得下」（`check`）與
「有多少位置」（`budget`）。並且延續家族公式：
`XamlContrast`（對比度）、`XamlKeys`（資源鍵）、`XamlFit`（版面容納）。

**落選**：`XamlBudget` 只講到一半；`TextFit` 沒有 XAML 這個搜尋詞。

---

## 下一步

1. **M0 可量測比例調查** —— 這是閘門，不是暖身。數字出來才決定要不要做
2. 若過關：M1 字型度量讀取器（純 C#，五張表）
3. 發版前的法務功課：字型 EULA 對「萃取並 commit advance width」的立場

---

# 附錄：實作後的修訂（2026-11-14）

規劃書寫在動工之前。以下是實作與實查推翻或修正的地方，依重要性排序。
**這一節存在的理由是：規劃書本身也會錯，而錯在哪裡比結論更值得留下來。**

## A. M0 的閘門判準，分母定義錯了

原訂「可量測比例 ≥40% 才做」。實測 27.3%，落在中間帶。

**分母錯了**：54.3% 的位置查不到寬度上界，那是靜態分析的天花板，不是這次執行的失敗。
改用「有裁切風險的位置」當分母後是 **75.9%**（App 畫面 85.0%）。

**而中間帶的處方也不成立**：原本寫「只做 budget 模式，check 延後」——
但兩個模式共用同一套寬度計算，拆開沒有工程意義。

→ **判定 GO**，理由改成「30%，而這個領域的現狀是 0%」，並且這 30% 恰好是
設計師刻意寫死寬度、也就是翻譯最可能打破那個決定的地方。

## B. 撤回過一個自己提出的數字

M0 文件第一版宣稱「budget 模式的實際覆蓋是 84%」。**多視角檢視時撤回。**

`NoDeclaredLimit` 的意思是「分析範圍內查不到上界」，**不是「不會被裁」**——
執行期的視窗有寬度。把沒驗證過的推論拿來撐結論，是這次最接近自我合理化的一步。

→ 改名為 `NoDeclaredLimit`，不計入覆蓋率，輸出裡必須是具名值而非空白。

## C. M1 的驗收條件改成「方向感知」

原訂「最大誤差 < 0.5%」。實測 CJK 標點的字距調整讓最大誤差到 1.43%。

但**低估與高估的後果完全不同**：低估 = 漏掉真的溢出 = 謊報健康；高估 = 噪音。
實測最大低估 **0.068%**，最大高估 1.43% —— 危險的方向小二十倍。

→ 條件改成四條（逐字符誤差 < 0.01px、最大低估 < 0.25%、平均 < 0.5%、
最大高估低於安全邊界）。**這不是把門檻往下挪**：逐字符那條比原本嚴格得多。

## D. 閘門指標從 `Coverage` 換成 `Resolution`

實跑 QuillNest 時 coverage 49.9%，用 70% 的門檻會直接 exit 3。
**永遠開火的閘門會被關掉。**

→ 新增 `Resolution` = 量到的 ÷（量到的 + 卡住的），分母排除「查不到上界」。
budget 預設不擋門（0），check 預設 60。

## E. 計畫外但必要的新增

| 新增 | 為什麼非做不可 |
|---|---|
| **具名／隱含樣式解析**（`StyleIndex`） | 不解析樣式，所有數字都不可信。實查 QuillNest 的 `SectionHint` 設 `FontSize=11`，用預設 12 量出假的「已溢出 2.9px」 |
| **自動字型後備** | WPF 會做系統層級的字型連結；只認明寫清單會讓真實專案的中文字串大量變成「量不到」 |
| **純符號過濾** | `→ ▲ : 00` 佔滿了「最擠的位置」前十名，把最有用的欄位變成雜訊 |
| **check 逐位置判定** | 同一字串在 A 處水平溢出、B 處垂直被裁；只量最窄的會漏掉另一種 |
| **check 回報解析錯誤** | 一個壞掉的 XAML 靜默消失 = 謊報健康 |
| **`SizeToContent` 與水平捲動** | 原本列在「盲區」，實查發現語料各命中 79／60 處，而且產生的是**錯誤數字**不是未知 → 升格為必要條件 |

## F. 刻意沒做的

- **GPOS 字距調整**。誤差方向是安全的（只會誤報），度量檔會變大，而那個檔案要進
  使用者的 repo。改判條件寫進 ROADMAP：真實專案的誤報裡若有可觀比例是邊界案例才做。
- **ComboBox／TextBox 的 chrome**。量不到就記 `null`，裡面的文字一律 `unmeasurable`。
  退回「只算 padding + border」會低估 chrome → 高估可用寬度 → **漏報溢出**。
- **Grid 的 `Auto`/`*` 求解**。v2 第一順位，佔有風險位置的 21.6%。

## G. 尚未完成（發版前必辦）

1. **字型 EULA 的法務確認**（§4.2 就記著，仍未做）
2. GitHub repo 公開、Trusted Publishing 政策、推 tag
3. `xamlfit.config.json` 的實際載入（目前只有 CLI 旗標）
4. SARIF 與 `--msbuild` 輸出（家族其他兩支都有）
5. `action.yml` 從未實跑過

## H. M6 全面重讀之後

整個專案從頭讀過一遍，並且拿真的 WPF 視窗逐案對照（`prototype/LayoutOracle`）。詳見 `docs/M6-全面重讀.md`。

**計畫書裡被推翻的假設：**

- §4.1「父層有固定 Width 就扣掉內距」——最近的那個 Width 不是答案，沿路每個上界都是候選、取最小的。
  而且「父層」要先確定不是會把寬度分給兄弟的面板（水平 StackPanel、DockPanel、UniformGrid）。
- §4.2 的預設字型——不是 Segoe UI，是跟著 Windows 語言走的系統訊息字型。
- 內距表的適用範圍——只適用 WPF 內建控制項的預設範本。第三方控制項、外部佈景、專案自己換掉的範本各有各的處理。
- 「資源字串 = `{x:Static}` 加 .resx」——真實專案絕大多數用 `{DynamicResource}` 加 XAML 字串字典。

**計畫外新增的：** 從專案自己的範本算內距、每個語言用自己的字串與字型重排、來源語言的檢查（同時是誤報率的自我檢驗）、
方言與外部佈景的偵測、`chrome`／`treatAs`／`absentFonts`／`sourceCulture` 這幾個讓使用者宣告的設定。

**數字：** 自家專案 76~100%，公開的應用程式 21~47%，控制項庫接近 0。
計畫書 §6 的 M0 閘門是用實作前的估計通過的；照現在的數字，結論仍然是「值得做」，
但理由變了——價值在應用程式（尤其是自己的範本看得到的那些），不在「拿任何 repo 來都有七八成」。

