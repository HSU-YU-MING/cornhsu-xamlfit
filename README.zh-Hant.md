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
> CI 在 **Linux**（Ubuntu 24.04，一般模式與很多 Linux CI 映像檔用的 globalization-invariant 模式各跑一次）
> 與 **Windows** 上跑：單元測試、示範專案的精確計數、budget 模式、每一條 CLI 路徑。
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
下表是 `budget` 的數字。`check` 可以解得更多：它照文件順序排一整排（只需要知道某個位置**前面**的控制項多寬），
`budget` 則是整排有一個寬度讀不出來就不給數字 —— Kindling 在 `budget` 是 76.0%、在 `check` 是 86.7%。

不寫任何設定檔，直接跑十一個專案：

| 專案 | 可在地化位置 | 量得到 | 卡住 | 查不到上界 | 可解析率 |
|---|---:|---:|---:|---:|---:|
| Cornea（自家 App） | 68 | 59 | 0 | 9 | **100%** |
| QuillNest（自家 App） | 685 | 538 | 3 | 117 | **99.4%** |
| Kindling（自家 App） | 150 | 68 | 7 | 75 | **90.7%** |
| Playnite | 1,107 | 274 | 140 | 506 | **66.2%** |
| ScreenToGif | 1,277 | 167 | 203 | 790 | **45.1%** |
| HandyControl | 210 | 11 | 61 | 123 | 15.3% |
| MahApps.Metro | 1,613 | 56 | 746 | 718 | 7.0% |
| MaterialDesignInXamlToolkit | 2,339 | 0 | 867 | 1,432 | 0% |
| wpfui | 440 | 0 | 15 | 405 | 0% |
| WinUI-Gallery | — | — | — | — | 不是 WPF |

這張表要分兩群看。

**應用程式**從 45% 到 100%。卡住的原因都很具體，而且會印出來：工具沒見過的自訂面板
（Playnite 的 `GridEx`）、寬度要到執行期才知道的控制項（ScreenToGif 的數值框、顏色選擇器、程式碼填項目的下拉選單 ——
用 `controlWidths` 宣告）、同一排的鄰居是綁資料的、`SharedSizeGroup` 的欄。

**控制項庫**幾乎是零，而那是對的答案：在那些 repo 裡，控制項**就是**佈景。
用了它們的應用程式也會落在同樣的處境，除非告訴工具那個佈景做了什麼 —— 見下面「要你告訴它的事」。

這份 README 的前幾版對同樣的專案寫的是 77~100%。那些數字是用一個「把預設佈景的內距套到重畫過的控制項上、
沒扣視窗邊框、把 WinUI 的檔案當成 WPF」的引擎算出來的。
**用錯的假設堆出來的高數字，比誠實的低數字糟。** 完整經過在 `docs/M6-全面重讀.md`。

⚠ **「查不到寬度上界」不等於安全。** 它的意思是分析在 XAML 裡找不到上界 —— 執行期的視窗仍然有寬度。
這類位置一律報成 `no-declared-limit`，**永遠不是空白**，也不算量得到。

## 它憑什麼這樣說

三種互相獨立的驗證，每一種的對照都不是這支工具自己。

**文字寬度，對照 WPF 的 `FormattedText`。** 1,652 組比對（含 `To`、`AV` 這類字距調整多的配對）：
平均誤差 **0.006%**、最大低估 **0.025%**、最大高估 0.26%。字距調整從字型的 GPOS 表取，照 WPF 預設的方式套。

**右到左文字與泰文，對照 `FormattedText`。** 希伯來文、泰文跟拉丁字母一樣把寬度加起來就對（0.02% 以內）。
阿拉伯文、波斯文、烏爾都文的字母會依左右鄰居變形，所以 `fonts export` 用字型自己的替換與定位規則，
把每個字母的每種形狀、每兩個相鄰字母、ے 前面的三字組合都跑一遍，只存算出來的寬度。
對照 500 句真實譯文、1,000 個隨機字、每個字母單獨與夾在兩個字中間，Segoe UI、Arial、Tahoma、Courier New、
Microsoft Sans Serif、Times New Roman：最大低估 **0.05%**。寫入一支字型的阿拉伯文資料之前，匯出程式會拿 2 萬個隨機字
把這些寬度跟自己完整跑出來的結果比；會拼窄的字母拿掉（Segoe UI 拿掉 13 個少用字母），它們跟別的字母相鄰時報量不到；
要拿掉大半字母的字型整支不寫阿拉伯文資料（Calibri 6.27，上下文規則跨整個字）。

**版面，對照真的 WPF 視窗。** `prototype/LayoutOracle` 把 228 段 XAML 同時交給一個真的（顯示在螢幕外的）
`Window` 和解析器，比對實際看得見的寬度：**186 案一致、42 案偏保守、0 案高估**；用 `check` 的排法（同一排只算前面的兄弟）是 190／38／0。
偏保守的都是刻意的（假設垂直捲軸在場、視窗邊框取最寬的那個縮放比例）。
上一輪稽核修掉的問題，大部分是這支對照程式抓出來的。它需要 Windows，所以結論另外寫成任何平台都跑得動的單元測試。

**真實專案，對照真的 WPF 視窗。** `prototype/CorpusRender` 把真實專案的視窗連同它自己的 `App.xaml` 字典、
各語言的字串一起載進 WPF，量每段文字需要多寬、實際看得見多寬。Playnite 11 個視窗、所有語言（含阿拉伯文、波斯文、希伯來文）6,274 組比對
（宣告了 GridEx，對照程式裡也放了一個等效的 GridEx 讓 WPF 真的去排）：
**兩邊都說被裁 136、工具誤報 2、漏報 0**、兩邊都說裝得下 6,136。誤報都在 1.5 px 以內。
兩邊一致的地方，工具量的文字寬度跟 WPF 差 −0.02 ~ +0.03 px，以為可用的寬度最多多算 0.02 px。
ScreenToGif 6 個視窗（含編輯器的側邊面板與各種下拉選單）、兩邊用同一支字型（這台的）比 3,043 組：**兩邊都說被裁 122、誤報 28、漏報 0**、
兩邊都說裝得下 2,893。誤報全在同一個側邊面板：它的 `ScrollViewer` 內容超出才顯示捲軸，工具假設捲軸在（並且在結果裡講明），
對照用的視窗夠高、捲軸沒出現；在 `hiddenScrollBars` 宣告那個面板之後只剩 1 筆誤報（0.8 px），漏報仍是 0
（阿拉伯文、希伯來文也一起量之後是 3,361 組：兩邊都說被裁 124、誤報 1、漏報 0）。另外 76 組是 Expander 的標題在旁邊還有 200 px 空間時，WPF 畫出來比文字短 0.5～1.2 px ——
`UseLayoutRounding` 把每一層都捨入到裝置像素。英文在同一個位置也少 0.4 px，而且少多少跟字串長短無關：
那是像素格，不是譯文裝不下，所以工具不報，對照時另外算。這些對照找到的 bug 列在 `docs/M6-全面重讀.md`。

**誤報，對照已經出貨的軟體。** 出貨的 App 上寫死的文字理應都裝得下，所以工具連**來源語言**也檢查：
整個語料 **1,185 筆來源語言量測。** 開發過程中這個數字有四次不是零：兩次是工具自己錯；第三次——工具學會量 Run 與
重設樣式的欄標題之後，自家 App 冒出兩筆——是對的：用 WPF 畫出來，水平 `StackPanel` 裡的說明需要 935.7 px、只看得到 563.5；
欄標題需要的寬度跟工具算的一樣是 126.16 px，欄裡只有 114。第四次——工具看懂「跨過 Auto 欄的子元素」之後，
ScreenToGif 編輯器裡冒出四個下拉選項——也是對的：「Sum evenly with the remaining frames」需要 214.8 px，WPF 只畫出 122.4。

## 要你告訴它的事

有些事不在 XAML 裡。工具會把它們報成「卡住」並且點名；你在 `xamlfit.config.json` 裡宣告一次就好。

```jsonc
{
  // 工具不認識的控制項的水平內距（左右合計，DIP）：第三方控制項，或被外部佈景重畫的標準控制項
  "chrome": { "Card": 16, "MetroWindow": 2, "GroupBox.Header": 9 },

  // 排版方式跟基底型別完全一樣的子類別。這是你在擔保。
  "treatAs": { "MyGrid": "Grid" },

  // 用屬性產生欄的 Grid 子類別（Playnite 的 GridEx）。每個值是那個類別用的屬性名：
  // 欄數（全部 Auto）、星號欄的索引清單、「子元素依序每列放 N 個」（會蓋掉子元素自己的 Grid.Column）
  "gridPanels": { "GridEx": { "columnCount": "ColumnCount", "starColumns": "StarColumns",
                              "autoLayoutColumns": "AutoLayoutColumns" } },

  // 寬度要到執行期才知道的控制項（顯示數值的數值框、程式碼填項目的下拉選單）最寬會到多寬。
  // 只在它佔掉旁邊文字的空間時用；明寫的 Width 優先，MinWidth／MaxWidth 照樣夾住。
  // 寫實際會出現的最大值 —— 寫小了就是把文字的空間算大。工具自己從不猜這個數字。
  "controlWidths": { "IntegerUpDown": 100 },

  // 垂直捲軸實際上不會出現的 ScrollViewer／清單（寫 x:Name）：內容永遠不會比它高。
  // 沒寫的話，捲軸是 Auto 時一律假設它在、扣掉它的寬度
  "hiddenScrollBars": ["ActionScrollViewer"],

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

`xamlfit.fonts.json` 裡**只有數字** —— 前進寬度、字距調整量、照字型自己的規則算出來的阿拉伯字母各形狀寬度；
沒有字形外框、沒有字型程式碼。
你從自己已授權的字型匯出到自己的 repo，CI 不需要安裝任何字型。

它不會拿手上有的字型去代替手上沒有的：

- 後備清單裡的**第一支**一定要在檔案裡（後面的只是後備）；
- 粗體文字不會用一般字重的寬度去量；
- 可變字型只回答它預設的那個字重；
- `TextFormattingMode="Display"` 會把字對齊到整數像素，從度量算不出來（對照 WPF 實測：−37% 到 +45%）。

每一種都會讓那個位置變成量不到，並且指名該匯出哪支字型。

**行內文字。** 一個 `Run` 獨佔一行（前後是 `<LineBreak/>`）就是一行普通的文字。不換行的一行從左邊畫起、
右邊被裁，所以 `Run` 拿到的是它前面的內容用剩的寬度；元素之間的換行縮排是一個空格，用 TextBlock 的字型（實測：`--inlines`）。
包在 `Hyperlink`、`Span`、`Underline` 裡、而且是唯一內容的 `Run` 就是那個連結本身。
接在綁定後面的 `Run`、或是會換行而且有高度上限的段落，量不到。

**字型畫不出來的字**照 WPF 實際的做法補（實測過，不是假設）：先找**同一個家族的其他字面**
（微軟正黑體 UI 的 Regular 沒有西里爾字母、Light 有，WPF 就用 Light，粗體文字也一樣），
再照 WPF 自己的後備表（`GlobalUserInterface.CompositeFont`，MIT 授權，附在 `data/font-fallback.json`）。
那張表看的是**元素的 `Language`**（沒寫 `xml:lang` 就是 `en-US`），**不是** Windows 的顯示語言 ——
在任何語言的 Windows 上，沒設 `Language` 的 App 都用 Yu Gothic UI 畫中文。
WPF 的候選字型在度量檔裡都畫不出來的字算量不到，報告會列出候選字型。
看不見的字（變體選擇符、零寬空白、零寬連接符、BOM）寬度是 0。

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

拿兩個公開專案的真實譯文去跑 —— Playnite（38 個語言、9,400 筆量測）與
ScreenToGif（29 個語言、3,067 筆量測）—— 分別報出 21 個位置上的 133 筆溢出、
與 15 個位置上的 131 筆。Playnite 的 133 筆全部用 WPF 實際畫出來對照過：130 筆真的被裁、3 筆誤報。
ScreenToGif 英文的 4 筆是真的（見上面）。沒有譯文的字串也會在每個語言自己的版面裡判：WPF 在那裡顯示英文，旁邊的譯文可能比較寬。

一整排控制項是**照文件順序**分寬度的：寫在前面的先拿到它要的，一段很長的譯文會把後面的擠出視窗。
WPF 排水平的 `StackPanel`、`DockPanel` 就是這樣，`check` 報的也是這個。

**沒有判定**的東西會計數並印出來：含執行期佔位符（`{0}`）的字串、度量檔沒涵蓋的字（附上碼位，
你才知道該匯出哪支字型）、度量檔涵蓋不到的組字（印度系、緬甸文、高棉文、藏文，以及字型資料拿掉的阿拉伯字母）。
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
| 阿拉伯文以外需要組字的文字系統（印度系、緬甸文、高棉文、藏文、蒙古文） | 報量不到 |
| 上下文規則跨超過三個字的阿拉伯文字型 | 牽涉到的字母、或整支字型，報量不到（見上面） |
| 阿拉伯數字 | WPF 在阿拉伯文裡把 0–9 換成阿拉伯—印度數字；兩種都量、取寬的 |
| 阿拉伯文以外的連字（`fi`、`ffl`…） | 不套；已含在上面的文字寬度對照裡（最大低估 0.025%） |
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
  "cultures": ["de", "fr"],

  // WPF 挑後備字型時看的元素語言。WPF 預設是 en-US，跟 Windows 的語言無關。
  // "culture" 代表你的 App 把 Language 設成介面語言。
  "elementLanguage": "en-US"
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
