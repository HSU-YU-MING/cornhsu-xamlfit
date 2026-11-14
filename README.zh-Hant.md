# XamlFit

**WPF XAML 文字容納度的靜態稽核。不開 App 就算出每個文字位置有多少像素 ——
翻譯之前把限制交給譯者，翻譯之後讓 CI 擋下真正的溢出。**

[English](README.md)

```bash
dotnet tool install -g Cornhsu.XamlFit

xamlfit fonts export                      # 在有字型的機器上跑一次
xamlfit budget ./src --csv budget.csv     # 翻譯之前：把真實的像素上限交給譯者
xamlfit check  ./src --resx ./Resources   # 翻譯之後：真的裝不下就擋 CI
```

> 需要 **.NET 10 runtime**。解析與量測都是純算術，不相依 WPF、沒有原生二進位。
>
> ⚠ **還沒有在 Linux 上實際跑過**：repo 尚未公開，CI 從來沒執行過。見 `docs/M5b-跨平台與CI驗證.md`。
>
> **只做 WPF。** WinUI／UWP、Avalonia、MAUI 的檔案會被辨識出來並回報「沒有分析」——
> 它們的控制項內距、預設字型、版面規則都不一樣，拿 WPF 的去算只會得到錯的數字。

## 它做了什麼別人沒做的事

所有偽本地化工具的做法都一樣：**把 App 跑起來、切到德文、一頁一頁看**。
微軟自己的在地化部落格十年前就描述過靜態版本，到今天沒有人做。

XamlFit 讀你的 XAML，算出每段文字實際有幾個像素，拿真實的譯文去量，裝不下就讓建置失敗。

有一類 bug **只有**這樣看得到：`TextWrapping="NoWrap"` 加 `TextTrimming="None"` 的
`TextBlock` 被裁切時**不會觸發 `IsTextTrimmedChanged`**。執行期偵測看不到，靜態量得出來。

## 唯一的一條規則

> **寧可說「量不到」，也不要算出一個偏大的數字。**

可用寬度高估，就是把「裝不下」說成「裝得下」—— 那是這支工具最嚴重的失敗。
所以凡是它讀不出來的東西都是**不知道**，不是**當成預設值**：綁定的 `Width`、定義在別的組件裡的樣式、
第三方控制項的範本、從宿主視窗繼承來的字型。每一種都會把那個位置擋下來，並且說明原因。

## 它看得到多少（老實說）

**可解析率** = 有寬度上界、本來就該算得出數字的文字位置裡，實際算出來的比例。
每次執行都會印出來，低於 `--min-coverage` 時回 exit 3。

不寫任何設定檔，直接跑十一個專案：

| 專案 | 可在地化位置 | 量得到 | 卡住 | 查不到上界 | 可解析率 |
|---|---:|---:|---:|---:|---:|
| Cornea（自家 App） | 68 | 59 | 0 | 9 | **100%** |
| QuillNest（自家 App） | 685 | 487 | 55 | 116 | **89.9%** |
| Kindling（自家 App） | 150 | 57 | 18 | 75 | **76.0%** |
| Playnite | 1,107 | 194 | 220 | 506 | **46.9%** |
| ScreenToGif | 1,277 | 99 | 375 | 790 | **20.9%** |
| HandyControl | 210 | 11 | 61 | 123 | 15.3% |
| MahApps.Metro | 1,613 | 53 | 749 | 718 | 6.6% |
| MaterialDesignInXamlToolkit | 2,339 | 0 | 867 | 1,432 | 0% |
| wpfui | 440 | 0 | 15 | 405 | 0% |
| WinUI-Gallery | — | — | — | — | 不是 WPF |

這張表要分兩群看。

**應用程式**從 21% 到 100%。卡住的原因都很具體，而且會印出來：工具沒見過的自訂面板
（Playnite 的 `GridEx`）、收成 `Width="0"` 再用動畫展開的側邊面板（ScreenToGif，318 個位置）、
同一排的鄰居是綁資料的、`SharedSizeGroup` 的欄。

**控制項庫**幾乎是零，而那是對的答案：在那些 repo 裡，控制項**就是**佈景。
用了它們的應用程式也會落在同樣的處境，除非告訴工具那個佈景做了什麼 —— 見下面「要你告訴它的事」。

這份 README 的前幾版對同樣的專案寫的是 77~100%。那些數字是用一個「把預設佈景的內距套到重畫過的控制項上、
沒扣視窗邊框、把 WinUI 的檔案當成 WPF」的引擎算出來的。
**用錯的假設堆出來的高數字，比誠實的低數字糟。** 完整經過在 `docs/M6-全面重讀.md`。

⚠ **「查不到寬度上界」不等於安全。** 它的意思是分析在 XAML 裡找不到上界 —— 執行期的視窗仍然有寬度。
這類位置一律報成 `no-declared-limit`，**永遠不是空白**，也不算量得到。

## 它憑什麼這樣說

三種互相獨立的驗證，每一種的對照都不是這支工具自己。

**文字寬度，對照 WPF 的 `FormattedText`。** 1,400 組比對：平均誤差 **0.039%**、最大低估 **0.068%**、
最大高估 1.43%（一個字距調整對）。XamlFit 不做字距調整，所以文字會被量得稍微偏寬 —— 錯的方向是多報。

**版面，對照真的 WPF 視窗。** `prototype/LayoutOracle` 把 138 段 XAML 同時交給一個真的（顯示在螢幕外的）
`Window` 和解析器，比對實際看得見的寬度：**113 案一致、25 案偏保守、0 案高估。**
偏保守的都是刻意的（假設垂直捲軸在場、視窗邊框取最寬的那個縮放比例）。
上一輪稽核修掉的問題，大部分是這支對照程式抓出來的。它需要 Windows，所以結論另外寫成任何平台都跑得動的單元測試。

**誤報，對照已經出貨的軟體。** 出貨的 App 上寫死的文字理應都裝得下，所以工具連**來源語言**也檢查：
整個語料 **958 筆來源語言量測，0 筆溢出。** 開發過程中這個數字有兩次不是零，兩次都是工具自己錯。

## 要你告訴它的事

有些事不在 XAML 裡。工具會把它們報成「卡住」並且點名；你在 `xamlfit.config.json` 裡宣告一次就好。

```jsonc
{
  // 工具不認識的控制項的水平內距（左右合計，DIP）：第三方控制項，或被外部佈景重畫的標準控制項
  "chrome": { "Card": 16, "MetroWindow": 2, "GroupBox.Header": 9 },

  // 排版方式跟基底型別完全一樣的子類別。這是你在擔保。
  "treatAs": { "MyGrid": "Grid" },

  // FontFamily 後備清單裡、目標機器上沒有的字型
  "absentFonts": ["Segoe UI Variable"],

  // App 是用哪個語言寫的（見下面「預設字型」）
  "sourceCulture": "zh-Hant",

  // 偵測到外部佈景，但它沒有改動控制項的內距
  "assumeDefaultTheme": false
}
```

每次執行的結尾會列出「內距不明」的控制項，依卡住的位置數排序 —— 你一眼就知道哪三行設定值得寫。

### 外部佈景

`App.xaml` 合併了別的組件裡的資源字典（MaterialDesign、wpfui、HandyControl…）時，
標準控制項是被工具看不到的 XAML 重畫的：Material Design 的 `Button` 內距是 32 px，不是 4。
內建的表不再適用，那些控制項裡的文字會被擋下來，直到你宣告它們的內距。

### 你自己專案裡的範本

**你的**專案換掉 `ControlTemplate` 時什麼都不用宣告：工具會讀那個範本，
把 `{TemplateBinding Padding}` 換成控制項實際生效的值，然後算出內距。
同一個樣式在好幾個佈景檔各有一份（深色、淺色）時，每一份都算，結果一致才採用。

### 預設字型

WPF 的預設字型不是 Segoe UI，是**系統訊息字型**，而且跟著 Windows 的顯示語言走：
英文或德文 Windows 是 Segoe UI，**繁體中文是 Microsoft JhengHei UI**，簡體中文是 Microsoft YaHei UI，
日文是 Yu Gothic UI，韓文是 Malgun Gothic。正黑體的拉丁字母比 Segoe UI 寬大約 7%。

所以沒寫 `FontFamily` 的文字，在不同使用者的電腦上寬度不一樣。`check` 會用每個語言自己的字型去量；
那個字型不在度量檔裡的話，會回報那個語言「沒有檢查」（exit 3），不會拿別的字型代替。

`UserControl` 的字型是從裝它的視窗繼承來的，而那個視窗在另一個檔案裡。
工具用同一個應用程式裡所有視窗的共識；它們不一致時，繼承到的值就是不知道，
除非你用 `defaultFontFamily`／`defaultFontSize` 指定。

## 字型

不用 SkiaSharp、不用 WPF、沒有原生二進位。XamlFit 自己讀字型檔裡的幾張表，把前進寬度加起來。

```bash
xamlfit fonts export --family "Segoe UI" --family "Microsoft JhengHei UI"
git add xamlfit.fonts.json
```

`xamlfit.fonts.json` 裡**只有前進寬度**，沒有字形外框、沒有字型程式碼。
你從自己已授權的字型匯出到自己的 repo，CI 不需要安裝任何字型。

它不會拿手上有的字型去代替手上沒有的：

- 後備清單裡的**第一支**一定要在檔案裡（後面的只是後備）；
- 粗體文字不會用一般字重的寬度去量；
- 可變字型只回答它預設的那個字重；
- `TextFormattingMode="Display"` 會把字對齊到整數像素，從度量算不出來（對照 WPF 實測：−37% 到 +45%）。

每一種都會讓那個位置變成量不到，並且指名該匯出哪支字型。

## `budget` —— 翻譯之前

單位是**字串**不是 UI 位置：

> **一個字串的預算 = 它最先放不下的那個位置。**

`limit_px` 是權威，`limit_chars` 只是估計 —— 每個字母寬度不同。狀態欄有四種，永遠不是空白：

| 狀態 | 意思 |
|---|---|
| `measured` | `limit_px` 適用 |
| `wraps` | 文字會換行而且高度不固定 —— 只有比行寬還長的單一個詞會被切掉 |
| `no-declared-limit` | XAML 裡查不到寬度上界。**不是**保證 |
| `not-analysed` | 有上界，但算不出來 |

`--sites sites.csv` 會寫出每個文字位置一列，附上卡住的原因。

以 `=`、`+`、`-`、`@`、Tab 或 CR 開頭的文字欄位前面會加一個單引號，免得試算表把它當公式執行。
直接餵進翻譯平台 API 時用 `--csv-raw` 取得逐位元組相同的字串 —— 那個檔案不要用 Excel 開。

## `check` —— 翻譯之後

譯文來自 `.resx`（`{x:Static p:Strings.Save}`），也來自 XAML 字串字典
（`<sys:String x:Key="Save">` 配 `{DynamicResource Save}`）—— 後者會自動找到，
而且在真實的 WPF 應用程式裡常見得多。

| 文字的情形 | 結果 | 嚴重度 |
|---|---|---|
| 不換行、不裁切、太寬 | `overflow` 疊出去或被切掉 | **fail** |
| 不換行、有省略號、太寬 | `trimmed` 資訊遺失 | warn |
| 換行後的行數超過固定高度放得下的 | `vertical-clip` | **fail** |
| 換行後比來源語言多行 | `reflow` 版面被撐高 | info |
| 不換行、餘裕低於 `--margin` | `tight` | warn |

行數是用真的逐詞換行算的，不是總寬除以行寬。

三件容易算錯、而且對照過 WPF 的事：

- **不會發生的換行。** 在水平 `StackPanel`、Grid 的 `Auto` 欄、`WrapPanel` 裡，WPF 用無限寬去量子元素：
  `TextWrapping="Wrap"` 沒有作用，文字直接被切掉。這會報成 `overflow`，不是無害的 `reflow`。
- **一整排都是譯文。** 一排按鈕共用寬度，留給其中一個的空間取決於其他幾個**在那個語言**有多寬 ——
  所以每個語言都用自己的字串重新排一次。
- **快速鍵。** `Button`、`Label`、`CheckBox`、`MenuItem`… 上的 `_Save`，底線不會畫出來；
  `TextBlock` 與 `ListBoxItem` 上的會。

拿兩個公開專案的真實譯文去跑 —— Playnite（37 個語言、5,953 筆量測、11 秒）與
ScreenToGif（29 個語言、1,229 筆量測、9 秒）—— 分別報出 20 個位置上的 148 筆溢出、
與 6 個位置上的 7 筆，兩者的來源語言都是 0 筆。Playnite 的 148 筆裡有 142 筆是共用寬度的一整排控制項：
英文放得下，其他 28 個語言放不下。這些**還沒有實際開 App 確認過**。

**沒有判定**的東西會計數並印出來：含執行期佔位符（`{0}`）的字串、度量檔沒涵蓋的字（附上碼位，
你才知道該匯出哪支字型）、字形會依上下文變形的文字系統（阿拉伯文、希伯來文、印度系、泰文）。
**一個語言整批量不到時，執行結果是 exit 3** ——「沒檢查」不能長得跟「通過」一樣。

`--source-only` 只檢查來源語言，不需要任何譯文。`--pseudo` 照分段膨脹表模擬
（`<10` 字元 → 200%、`11–20` → 180%、`21–30` → 160%、`31–50` → 130%、`50+` → 120%）；
它的 finding 一律降一級，永遠不擋 build —— 猜測不該擋別人的 PR。

### 退出碼

| | |
|---|---|
| 0 | 通過 |
| 1 | 有達到 gate 的 finding |
| 2 | 用法、設定或輸入檔錯誤 |
| 3 | **結果不可信** —— 可解析率低於 `--min-coverage`、什麼都沒量到，或某個語言整批量不到 |

## 已知限制

| | 處理方式 |
|---|---|
| `ControlTemplate`／`DataTemplate` 的內容 | 寬度由使用端決定；另外計數為「在範本裡」 |
| 明寫了 `Width` 的元素放在更窄、而且**算不出來**的容器裡 | 照明寫的 `Width` 算。外層算得出來時取小的那個 |
| 放在 `Auto` 欄裡的標籤 | 預算給到「它會被切掉」為止；在那之前它會先把 `*` 欄擠小 |
| 控制項範本或程式碼設定的字型 | 看不到；用 XAML 寫的那個 |
| 觸發器、視覺狀態、code-behind 套用的樣式 | 不追蹤 |
| 沒掃描的文字屬性（`ToolTip`、`Title`、浮水印／提示的附加屬性、`Hyperlink`／`Span` 的內容） | 完全沒看到 |
| 執行期用程式碼合併的資源字典 | 假設每個字典檔都有被合併；定義互相矛盾時那個值就是不知道 |
| 不做字距調整與 shaping | 文字寬度最多高估 1.43%，不會低估 |
| 垂直裁切 | 只有文字元素自己宣告了 `Height`／`MaxHeight` 才判 |
| 視窗邊框、捲軸寬度 | 取 DIP 值最大的那個顯示縮放比例 |
| 各語言的預設 UI 字型 | 只有繁體中文是在真實系統上量過的；其他來自 Windows 的字型設定。可用 `defaultFontByCulture` 蓋掉 |

## 設定檔

掃描根目錄旁的 `xamlfit.config.json`，或 `--config <路徑>`。除了上面「要你告訴它的事」那幾個鍵：

```jsonc
{
  "schemaVersion": 1,
  "defaultFontFamily": "Segoe UI",
  "defaultFontSize": 12,
  "marginPercent": 5,
  "minCoverage": 60,
  "metrics": "xamlfit.fonts.json",
  "exclude": ["/Generated/"],
  "resx": ["Resources"],
  "cultures": ["de", "fr"]
}
```

可以寫註解與尾逗號。相對路徑是相對於設定檔。**命令列旗標永遠優先於設定檔。**
拼錯的屬性名是**錯誤**不是默默略過：exit 2，並且指出是哪一個。

## CI 輸出

`--sarif` 寫出 SARIF 2.1.0（GitHub code scanning）。`--msbuild` 在 stdout 印 MSBuild 診斷，
Visual Studio 的錯誤清單就看得到：`XF0001`（擋下的）、`XF0002`（警告）、`XF0003`（結果不可信）。

壓掉單一位置要寫理由 —— 理由必填，而且被壓掉的會計數：

```xml
<!-- xamlfit-ignore: 裝飾用，不翻譯 -->
<TextBlock Width="40" Text="{x:Static p:Strings.Arrow}"/>
```

## 相關工具

[XamlContrast](https://www.nuget.org/packages/Cornhsu.XamlContrast) 稽核同一份 XAML 的
WCAG 對比度，[XamlKeys](https://www.nuget.org/packages/Cornhsu.XamlKeys) 稽核資源鍵的作用域。
同一套退出碼契約、同一種 baseline 形狀、同一個心智模型。

## 授權

MIT。隨附資料的聲明見 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
