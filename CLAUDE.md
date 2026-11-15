# XamlFit — 給接手的人

一句話：**不開 App 就算出每個文字位置有多少像素** —— 翻譯之前給約束（`budget`），
翻譯之後擋溢出（`check`）。

完整設計在 `XamlFit專案規畫書.md`；閘門與實查數據在 `docs/`。
這份只記**踩過的坑**，而且每一條都有實查來源。

---

## 憲法

1. **靜態分析 + 全量掃描 + CI 退出碼。** 跟 XamlContrast、XamlKeys 同一條。
2. **「工具沒看到」與「看到了沒問題」必須分得開。** 量不到就要說量不到。
   最嚴重的失敗不是漏報，是**掃完說沒問題、上線被德文撐爆**。
3. **每條規則來自實查，而且實查的樣本必須包含非作者的專案。**
   （XamlContrast §8.3：四個自家專案覆蓋率 89~100%，八個公開專案合計 9.6%。）

---

## 不要「修正」的設計

### `NoDeclaredLimit` 不是「安全」，也不是「不可知」

它的意思是「分析範圍內查不到寬度上界」。執行期的視窗**有**寬度，所以它不能拿來宣稱安全；
但它也不是這次執行的失敗，所以**不進閘門的分母**。

M0 第一版把它當成覆蓋率的缺口，算出一個撐不住的「84% 覆蓋率」，後來撤回了
（`docs/M0-可量測比例調查.md` §4.5）。

### 閘門看 `Resolution` 不看 `Coverage`

`Coverage` 的分母是全部可在地化位置（含查不到上界的），那是靜態分析的天花板。
拿它當閘門會對每個專案開火，而**永遠開火的閘門會被關掉**。

`Resolution` = 量到的 ÷（量到的 + 卡住的）。只算「本來就該算得出數字卻卡住」的。

### 「不知道」不是「預設值」—— 這條是 M6 全面重讀的核心

凡是**有人設定了、但靜態讀不出來**的東西，一律把那個位置擋下來，不准當成沒設定：

- 綁定的 `Width`／`Margin`／`Padding`、專案外的樣式、被換掉而且拿不到的範本
- 不是 WPF 內建的控制項（`ui:Button` 的範本跟 `Button` 沒有關係）、表裡沒有的 WPF 元素
- 外部佈景底下的標準控制項、從宿主視窗繼承但各視窗不一致的字型
- 度量檔裡沒有的字型、`TextFormattingMode="Display"`

少扣內距 → 可用寬度偏大 → **漏報溢出**。把文字量窄也是同一個方向。
**不完整的數字比沒有數字危險，因為它錯在「看起來裝得下」那一邊。**

對應的出口是讓使用者**宣告**（設定檔的 `chrome`／`treatAs`／`absentFonts`／`defaultFontFamily`），
不是讓工具猜。報表結尾會點名哪些控制項的內距不明、各卡住幾個位置。

### 擋路者不立刻放棄

路上遇到算不下去的東西時繼續往上走：它上面如果根本沒有上界，答案仍然是 `NoDeclaredLimit`，不是 `Unmeasurable`。
否則可解析率的分母會被一堆「反正沒有上界」的位置灌水。細節在 `WidthResolver` 的類別說明。

### 每一條版面規則都要進 LayoutOracle

`prototype/LayoutOracle` 把同一段 XAML 交給真的 WPF 視窗與解析器，逐案比對。
**加新的版面規則之前先加案例。** M6 裡至少有三條規則是我照自己的理解寫完、測試也過了，
才被它指出理解本身是錯的（`ScrollBar Width="10"` 實際 16.8、Auto 欄不裁文字而是把後面的欄推出去、預設字型不是 Segoe UI）。

### `--pseudo` 的 finding 永遠降一級，永遠不擋 build

它報的是風險不是事實。拿猜測去擋別人的 PR，工具會被關掉。

### 膨脹率是分段表不是單一倍數

短字串膨脹最兇（`Save` → `Speichern` 是 +150%），而按鈕標籤正是最短的那一類。
而且這張表**只用來估算「大約幾個字」與 `--pseudo`，不參與 CI 判定**。

### baseline 的鍵不含行號、不含像素值

行號會隨無關的編輯漂移，像素值會隨 `fonts export` 重產而變。
任一進了鍵，一次格式化就讓整份 baseline 失效，然後使用者就把它關掉了。

---

## 實查踩過的坑

| # | 坑 | 後果 | 來源 |
|---|---|---|---|
| 1 | 文字位置**自己**是樣板化控制項時沒算 chrome（`<Button Content="Save"/>`） | `NeedsChrome` 從 19.1% 被低估成 1.8%，差十倍 | M0 |
| 2 | `Menu`／`TabControl`／`ToolBar` 被當成虛擬化清單 | 大量選單文字被丟進「不可知」 | M0 |
| 3 | 走到 `ItemsControl` 就中止，不往上找它自己的寬度 | 固定寬 ListBox 裡的項目整批進黑洞 | M0 |
| 4 | **快顯內容（`ContextMenu`／`ToolTip`／`Popup`）被當成受視窗寬度約束** | 給出完全錯誤的預算 | QuillNest `NoteEditorWindow.xaml:838` |
| 5 | 控制項名稱用精確比對，認不得 `MetroTabControl` | 第三方控制項庫整組漏掉。⚠ **M6 修正了當時的解法**：後綴比對只能用來判斷行為類別（它是分頁標籤、是清單）與「它是不是文字控制項」；拿來套內距的數字是錯的（見第 32 條之後） | MahApps；XamlContrast 也踩過同一個 |
| 6 | **`SizeToContent` 會讓視窗的 `Width` 失效** | 語料裡 46 個檔案同時寫了兩者 → 錯誤的預算 | 驗證者實跑 |
| 7 | **水平捲動的 `ScrollViewer` 讓上界失效**（文字可捲動不會被裁） | 語料 60 處 | 驗證者實跑 |
| 8 | **不解析 Style 的 Setter** | `SectionHint` 設 `FontSize=11`，用預設 12 去量，高估 9%，報出假的「已溢出 2.9px」 | QuillNest `WorkloadExportOptionsDialog.xaml:94` |
| 9 | 只認明寫的字型後備清單 | WPF 會做**系統層級自動後備**；不做的話真實專案的中文字串大量變成「量不到」。⚠ 當時的解法（度量檔裡其他字型依字母順序）是錯的，見第 49 條 | QuillNest |
| 10 | `{x:Static p:Strings.Save}` 的命名空間前綴讓 regex 比對失敗（`\w` 不含冒號） | **所有**資源字串被誤判成「綁資料」然後濾掉；示範專案第一次跑是 0 個位置 | 示範專案 |
| 11 | check 只量每個字串**最窄**的位置 | 同一字串在別處的 `vertical-clip` 漏掉 | 示範專案 |
| 12 | 純符號／數字（`→ ▲ : 00`）進了「最擠的位置」清單 | 最有用的欄位被雜訊佔滿 | QuillNest 實跑 |
| 13 | check 模式**不報解析錯誤** | 一個壞掉的 XAML 靜默消失 | 示範專案（XML 註解裡放了連續兩個減號） |
| 14 | 度量檔用 `JsonSerializer` 的 `WriteIndented` | Segoe UI 一支字型就 **2.1 MB**；改成手寫序列化後 494 KB。這個檔案要進使用者的 repo | M1 |
| 15 | 文化標籤只檢查「全是字母」 | `My.App.Strings.resx` 的 `Strings` 被當成文化 | 單元測試 |
| 17 | **未顯示的 `Window` 的版面不會傳遞** | GridOracle 第一版把案例包在 Window 裡，WPF 側全部回 0。直接量根元素 | Grid 驗收 |
| 18 | **程式碼 new 出來的控制項沒有視覺樹** | `VisualTreeHelper` 看到 0 個子元素，範本沒展開。要用 `XamlReader` 建樹 | ChromeGen |
| 19 | **深度優先找「內容宿主」會找錯** | TextBox 的樹是 border(300) → PART_ContentHost(298) → TextBoxView(294)；先撞到 298 算出 chrome=2，正確是 6。要分層找 | ChromeGen |
| 21 | **ComboBox 的 ContentPresenter 預設不拉伸** | 它的 ActualWidth 是「內容的寬」不是「可用區域的寬」。加 `HorizontalContentAlignment="Stretch"` 才量得到可用區域（300 → 272.2，chrome 27.8） | ChromeGen 第四種量法 |
| 23 | **ItemsControl 的探針把項目容器混進 chrome** | ListBox 量到 14.0（= 4.0 + ListBoxItem 的 10），解析器再扣一次 → 重複計算。要強制走 ItemsPresenter 那條路 | ChromeGen |
| 24 | **捲軸出不出現靜態不可知** | `VerticalScrollBarVisibility` 預設 Auto，取決於執行期項目數。選「假設它在」（誤報），不選「假設不在」（漏報）。寬度用 17.5（各種顯示縮放裡 DIP 值的上限），不是這台機器量到的 16.8 | ItemsControl 求解；M6 |
| 25 | **排除規則比對完整路徑** | 掃描根目錄位在 `.config` 或 `bin` 底下時（`C:\Users\x\.config\app`、`D:\bin\myproject`），**所有檔案都被排除**，然後吐 exit 3 不解釋為什麼。要比對**目錄名**並做目錄剪枝 | 跨平台稽核 |
| 26 | **`*.xaml` 在 Windows 不分大小寫、在 Linux 分** | 同一專案在 CI 上掃到的檔案比本機少，而且是靜默的。⚠ 語料 1,916 個檔全小寫，這個 bug **測不出來** —— 修它的理由是失敗模式（CI 比本機乾淨＝謊報健康），不是證據 | 跨平台稽核 |
| 27 | **`action.yml` 把輸入文字替換進 shell** | `${{ inputs.x }}` 是文字替換，帶引號或 `$()` 的輸入會變成可執行程式碼。要全部走 `env:` | action.yml 檢視 |
| 29 | **`--pseudo` 對資源引用的字串一筆都量不到** | 它只接受字面文字，但用 .resx 的專案正是主要使用者 —— 等於對目標受眾完全無效。要去中性語言的 .resx 取值 | CLI 煙霧測試 |
| 30 | **「量到 0 筆」回 exit 0** | 那等於在什麼都沒看的情況下說「沒問題」。原本的守衛刻意排除 --pseudo，而那正是最容易量到 0 的模式 | CLI 煙霧測試 |
| 31 | **設定檔在必填參數檢查之後才套用** | 設定檔裡寫了 `resx` 也永遠不生效，檢查會先把人擋掉 | CLI 煙霧測試 |
| 28 | ~~`set -e` 會因為 `[ -n ] &&` 條件為假而中止~~ | **這條是錯的，實測推翻。** `set -e` 不會因為 `&&` 清單的**條件部分**失敗而中止。留著免得下次又去修一個不存在的問題 | action.yml 檢視 |
| 22 | **ComboBoxItem 的 chrome 不該跟 ComboBox 的一起扣** | 項目內距只存在於展開的下拉清單；關起來的選取框沒有它。兩個都扣會多扣約 10 DIP（約 6%） | 語意檢視 |
| 20 | **M0 的「解 Grid 會推到 97.5%」是錯的** | 實測兩個專案完全沒改善（多數 Auto 欄裝綁資料的文字）；自家專案改善 25 個百分點，公開專案 0~5 —— 又一次取樣偏誤 | Grid 驗收 |
| 16 | ~~`{DynamicResource}` 式在地化沒支援~~ | **M6 已支援**（見第 41 條）。當時把它記成「v1 的範圍邊界，不是 bug」是低估了：這是真實 WPF 專案最主要的寫法 | 公開語料穩定性實跑 |

| 32 | **視窗邊框沒扣** | `Window.Width` 是外框寬；每個以視窗為上界的位置都高估 16 DIP。之前的對照程式只量根元素（沒 Show 過的 Window 沒有邊框），所以看不到 | LayoutOracle |
| 33 | **遇到第一個上界就停** | `Width="300"` 放在 100 寬的欄裡只看得到 100。每個上界都只是候選，取最小的 | LayoutOracle |
| 34 | **水平 StackPanel／DockPanel／UniformGrid 當成透明容器** | 每個子元素都以為自己有整個寬度 | LayoutOracle |
| 35 | **水平 StackPanel 與 Auto 欄裡 `Wrap` 不會換行** | WPF 用無限寬量子元素。報成 `reflow` 就是漏報 | LayoutOracle |
| 36 | **WPF 的預設字型跟著 Windows 語言走** | 繁中系統是 Microsoft JhengHei UI，拉丁字母寬 7%。對照程式也要用系統實際的字型，否則兄弟元素的寬度對不上 | LayoutOracle（這台機器） |
| 37 | **`ScrollBar Width="10"` 實際是 16.8** | 佈景的預設樣式設了 MinWidth，贏過比較窄的 Width | LayoutOracle |
| 38 | **樣式索引沒有作用域** | 別的視窗的 `Window.Resources` 被當成候選；QuillNest 每個 Border 的 Padding 都變成「讀不出來」 | QuillNest 實跑 |
| 39 | **同型別、互相以型別為基底的隱含樣式** | N 份互相把對方當基底，路徑數是 N 的階乘。只擋深度或只記錄走過的樣式都擋不住 | MaterialDesign 實跑（卡死） |
| 40 | **帶 `x:Class` 的根元素是子類別** | App.xaml 的 `<Style TargetType="Window">` 套不到它；`Themes/Generic.xaml` 的佈景樣式會 | ScreenToGif |
| 41 | **`{DynamicResource}` 字串被當成綁資料** | Playnite 1,038 處、ScreenToGif 1,540 處整批沒看到。取代舊的第 16 條 | 語料統計 |
| 42 | **`xml:space="preserve"` 會繼承** | 寫在字串字典的根元素上；只看元素自己會把 `&#x0d;` 換行吃掉，兩行當成一長行，連帶把旁邊的欄算窄 | 來源語言自我檢驗（ScreenToGif） |
| 43 | **UserControl 的字型來自宿主視窗** | 每個檔案各自從預設值算起，等於把整個 App 的字型設定丟掉 | 來源語言自我檢驗（MaterialDesign） |
| 44 | **字型的 name 表有好幾種語言** | msjh.ttc 的第一筆是「微軟正黑體」；要挑語言 0x0409 的 | 匯出後對不上 |
| 45 | **WinUI 與 WPF 的預設 xmlns 是同一個 URI** | 唯一可靠的記號是 `using:` 對 `clr-namespace:`；沒有記號的小檔案看鄰居 | WinUI-Gallery |
| 46 | **寫檔時自動建立目錄** | Git Bash 下 `/tmp/x/out.json` 被 .NET 當成目前磁碟根目錄底下的 `\tmp\x`，於是在 D 槽根目錄建出目錄樹。不替使用者建目錄 | CLI 煙霧測試 |
| 47 | **Bash 的 heredoc 會吃掉反斜線、太長會被截斷** | 寫程式碼檔一律用 Write 工具；要改既有檔案就寫一支 `.js` 用 `split/join` 取代，找不到錨點要報錯 | 這一輪反覆踩到 |
| 48 | **`XamlReader.Parse` 會刪掉單獨的 `&#x0d;`，編譯後的 BAML 會保留** | 執行期載入的對照程式看到一行、真的 App 是兩行。CorpusRender 要先把單獨的 CR 換成 LF 來模擬 BAML | CorpusRender |
| 49 | **WPF 怎麼補字型畫不出來的字** | 先找**同家族的其他字面**（正黑體 UI 的 Regular 沒有西里爾字母、Light 有），再照 `GlobalUserInterface.CompositeFont`，而且看**元素的 Language**（預設 en-US），不看 Windows 的語言。依字母順序挑字型讓日文假名寬了 40%；沒找同家族字面讓烏克蘭文少算 13.6 px（高估） | CorpusRender、LayoutOracle `--probe` |
| 50 | **Linux CI 常開 globalization-invariant 模式** | `CultureInfo.GetCultureInfo("zh-TW")` 會丟例外。執行路徑上不能靠文化資料庫做判斷；測試要切文化時先 try/catch。每次改到語言相關的程式都要用 `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` 再跑一次測試 | invariant 模式重跑 |
| 51 | **舊式底線語言碼（`sr_SP`、`pt_BR`）** | .NET 不認得 `sr-SP`，被當成中性語言，整個語言默默沒檢查。用 `ResxReader.LooksLikeCultureTag` | Playnite |
| 52 | **水平排列依文件順序分寬度** | 前面的先拿、後面的被擠出去。把一整排一起算是 111 筆誤報、43 筆漏報。check 用 `RowSharing.Preceding` | CorpusRender（Playnite） |
| 53 | **側邊頁籤要先套範本的觸發器** | 專案自己的 TabControl 範本靠 `TabStripPlacement` 觸發器把標籤列移到 Auto 欄；靜態讀範本只看得到上方的排法，58 個位置高估。現在先套觸發器（只套這個屬性、只接受「設範本裡具名元素的屬性」），標籤列 = 最寬的「標題 + TabItem 範本的標題 chrome」。WPF 預設範本（選取的標籤用負邊距放大）沒建模，維持不知道 | Playnite、LayoutOracle「side tabs」 |
| 54 | **專案自己的 Window 子類別也有邊框；`WindowChrome` 可以寫在樣式裡** | `ExWindow`／`WindowBase` 沒扣邊框是高估 16 px；樣式裡的 WindowChrome 沒看到是誤報 | ScreenToGif |
| 55 | **看不見的字寬度是 0** | U+FE0F、200B、200D、FEFF、2060 在 WPF 是 0 寬。⚠ U+FEFF 落在阿拉伯表現形式的範圍裡，要**先**判斷看不見的字 | LayoutOracle `--fallback` |
| 56 | **對照程式的字型假設要跟工具一樣** | 在繁中 Windows 上畫法文、希臘文會用正黑體 UI；工具假設那些語言的 Windows 用 Segoe UI。差距是字型不是版面 —— 比對前用 `defaultFontByCulture` 對齊 | CorpusRender（ScreenToGif） |
| 57 | **行內的 Run** | LineBreak 不算「同一行有別的內容」；元素間的換行縮排是一個空格、用 TextBlock 的字型；LineBreak 兩側的空白被修掉（`--inlines`）。第一版只要有兄弟就放棄，QuillNest 32 處量不到 | QuillNest |
| 58 | **DataGrid 欄標題的字型不是欄的** | 欄的 FontSize／FontWeight 只給儲存格；標題吃標題樣式或 DataGrid 的（`--header-font`） | LayoutOracle |
| 59 | **DataGridColumnHeader 的 Padding** | 全 0 時範本自己用 5,0,5,0（表裡的 10）；否則取代它；BorderThickness 不影響寬度。13 種樣式逐一實測，第一版「加上去」的模型錯了 | LayoutOracle |
| 60 | **報告輸出要 UTF-8** | .NET 被導向時用系統字碼頁；繁中 Windows 是 Big5，實跑 QuillNest 每句中文都是亂碼 | 自家 App 實際使用 |
| 61 | **CorpusRender 的下拉選單** | 可編輯的看 TextBox 的捲動範圍（字元位置與視埠直接相減固定差 2 px）；放畫面元素的選項，框裡是 VisualBrush，下拉清單沒打開過就沒排版、寬度 0 | CorpusRender（ScreenToGif） |
| 62 | **CorpusRender 量換行文字：中日韓每個字都是斷行點** | 用空格切「最長的字」會把一整句中文當成一個字，換行後明明全部顯示也報成被切掉 | 修 QuillNest 之後實測 |
| 63 | **ItemTemplate 畫的是範本，不是項目** | 項目不是容器（ComboBoxItem…）而清單有 ItemTemplate 時，畫面上的字是範本的（ScreenToGif 範本 FontSize=14、項目 12）。照項目量是 21 筆誤報 | CorpusRender（ScreenToGif 編輯器） |
| 64 | **兩個子指令共用一份旗標解析** | `check --sites` 照收、什麼都沒寫，對照腳本讀到一份 20:05 的舊檔案、整個編輯器的比對被默默略過。各自拒絕對方的旗標 | 對照數字不動才發現 |
| 65 | **`UseLayoutRounding` 讓 Auto 欄少一個裝置像素** | ScreenToGif 編輯器的 Expander 標題，旁邊空 200 px，WPF 畫出來仍比文字短 0.5～1.2 px。跟譯文長度無關，工具不模擬；對照報告裡單獨列出，不調容差 | CorpusRender |
| 66 | **收起面板的展開寬度在動畫裡** | Width="0" ＋ 同檔 Storyboard 的 DoubleAnimation To。取最小的非 0 字面值；To 不是字面就不知道。CorpusRender 也要照動畫打開它 —— ⚠ `(Grid.Width)` 的 PropertyPath.Path 是 `(0)`，屬性在 PathParameters | ScreenToGif 編輯器 |
| 67 | **第三方 Grid 子類別不內建** | GridEx 的語意照 Playnite 原始碼寫，但只認設定檔宣告的 —— 別的專案同名類別可能完全不同 | Playnite |
| 68 | **只換範本的 ScrollBar 寬度仍是系統寬** | WPF 預設樣式設了 Width 與 MinWidth；專案樣式只換 Template 時，範本根元素寫多寬都沒用（`--scrollbar`）。OverridesDefaultStyle="True" 才由範本決定 | LayoutOracle |
| 69 | **複製出來的範本要接回原本的資源範圍** | 算範本內距時把範本複製到新文件，複本裡只看得到全域樣式 —— Grid／視窗自己資源裡的捲軸樣式被漏掉，多算 6.5 px。用 LexicalContinuation 從控制項的位置往上找；快取鍵要含資源範圍 | LayoutOracle |
| 70 | **CorpusRender 拿掉 GridEx 的子元素會讓後面的全部錯格** | 自動排列依順序填格。換成空的佔位元素；換命名空間時要宣告前綴，不然預設命名空間被改掉、DockPanel.Dock 解析不到 | CorpusRender（Playnite） |
| 71 | **算範本內距時，內容路徑上的樣式也會設字型** | Playnite 的 TabItem 範本把標題放在 `TextBlock Style="{DynamicResource BaseTextBlockStyle}"` 裡。文字是用控制項的字型量的，所以樣式設的值要跟控制項的一樣（同一個資源也算），不一樣就不知道 | Playnite |
| 72 | **水平 StackPanel 裡太長的控制項保持原寬、被外層裁掉** | 它右邊的 Padding、邊框、Margin 在裁切線外，不會擋到文字（LayoutOracle「trailing:」：WPF 184、工具原本 133）。DockPanel／Grid／Border 會把內容縮窄，右邊照算。只在 check 原諒，而且只原諒讀得出來、確定在右邊的那幾段 | LayoutOracle、CorpusRender（Playnite 保加利亞文） |
| 73 | **跨到星號欄的子元素不會撐大 Auto 欄** | 就算 Grid 放在水平 StackPanel 或外層 Auto 欄裡也一樣；只跨 Auto／固定欄的才會（維持不知道） | LayoutOracle「spanning sibling」 |
| 74 | **側邊的 TabPanel 不算標籤的 Margin** | GetDesiredSizeWithoutMargin：負的不讓標籤列變窄、正的也不讓它變寬。預設範本：最寬的「標題 + 左右 Padding」+ 2，BorderThickness 與選取都不影響 | LayoutOracle「default tabs」 |
| 75 | **對照程式與測試要跟掃描器一樣保留空白** | XDocument.Parse 預設丟掉只有空白的文字節點，Run 之間的空格就不見了 —— 對照程式看起來高估 2.99 px，其實是對照程式自己讀錯 | LayoutOracle |
| 76 | **沒翻譯的字串要在那個語言的版面裡判** | WPF 顯示來源語言的字，但旁邊的譯文比較寬、預設字型也可能不同。ScreenToGif 義大利文：英文選項只剩 101.6 px（英文版面 122.4），先前只判來源語言一次 → 12 筆漏報 | CorpusRender |
| 77 | **UseLayoutRounding 的差距不是「裝不下」** | 每一層捨入到裝置像素，內容大小的文字少畫 0.5～1.2 px；英文在同一位置也少 0.4 px，跟字串長短無關。對照時另外算（drive.js 的 rounding），工具不報 | CorpusRender（ScreenToGif） |
| 78 | **CorpusRender 的「被裁」要看字形** | 字幅比字形寬：只裁到最後一個字右邊的空白時字其實完整（標成 2）。用 FormattedText.BuildGeometry 的右緣判斷 | CorpusRender |
| 79 | **綁定的 Visibility 當成看得見** | 鄰居的寬度一律是被扣掉的（Auto 欄、同一排、標籤列），取大的只會少給文字空間。Canvas 沒寫 Width 時寬度 0（不量子元素） | LayoutOracle |
| 80 | **DataGrid 的星號欄寬是延後算的** | 排進 Dispatcher：只呼叫 UpdateLayout 量到的是 MinWidth（每一案都是 70）。LayoutOracle／CorpusRender 要先讓 Dispatcher 跑完（ContextIdle）再量 | LayoutOracle「star column」 |
| 81 | **依儲存格內容長的欄不能拿 MaxWidth 當寬度** | 那是「資料夠長時」的最壞情況；拿來算星號欄，QuillNest 的來源語言冒出 1 筆只有資料夠長才會發生的溢出。維持不知道 | 語料重跑 |
| 82 | **WPF 套的是 GPOS 的 'kern'，不是舊的 kern 表** | 預設開著（`Typography.Kerning`）。Segoe UI 100 px 的「To」窄 9.8 px。不套時字距多的字串偏寬到 5.7% | WpfOracle |
| 83 | **右到左文字裡，中性字旁邊的字距 WPF 不套** | 空白、標點跟隔壁的字可能分在不同方向的段落：阿拉伯文句子裡「URL 」的「L 空白」(Arial −76) 沒套。這種位置只套會變寬的字距 | WpfOracle --scripts |
| 84 | **阿拉伯文的字距在形狀換過的字形上** | Segoe UI 的阿拉伯文字距掛在 GPOS（arab）、對象是字首／字中形，以碼位查的字距表看不到 → 偏窄 2.8%。匯出時要把 GSUB 跑完再跑 GPOS | WpfOracle --scripts |
| 85 | **形狀的寬度要用「整串總寬 − 延長線」算，不能照字形歸屬分** | Segoe UI 的 ۓ 在字尾時會換掉**前一個**字的字形，寬度落到別的字頭上 → 少 6 px | WpfOracle --scripts（逐字母） |
| 86 | **元素語言是阿拉伯文時，0–9 會換成阿拉伯—印度數字** | 連純數字的字串也換；前面最近的字母是拉丁字母的不換（「8x8」只換第一個）。Tahoma 的比較寬（1171 vs 1118）、Segoe UI 的比較窄 → 兩種都量取寬的 | WpfOracle --scripts |
| 87 | **阿拉伯文的附加符號寬度是 0** | Tahoma 的 U+0654 在 hmtx 有寬度，WPF 量出來是 0 | WpfOracle --scripts |
| 89 | **佈景樣式的 TargetType 常是基底型別** | MahApps.Styles.Button 是 ButtonBase：只比對 TargetType == 控制項會漏掉主要的樣式。量在第一個適用的具體控制項上，記下量在哪個，只套在同一種控制項 | ThemeChrome |
| 90 | **量佈景樣式設了什麼要看值的來源** | 沒設的屬性量到的是這台機器的系統字型（Microsoft JhengHei UI）；MaterialDesign 的 PasswordBox 甚至把 FontFamily 設成系統字型。只記 BaseValueSource 是 Style 的，而且等於系統字型的不記 | ThemeChrome |
| 91 | **捲軸要放進真的視窗才量得到寬度** | 單獨 Measure 一個 ScrollBar 得到 0。MahApps 的捲軸 14（固定）、MaterialDesign 的等於系統寬（跟著縮放 → 用 17.5 上限） | ThemeChrome |
| 92 | **同一個範圍裡有沒量過的佈景，量過的也不能用** | 不知道另一個佈景改了哪些控制項。NETworkManager 的 Dragablz 其實是 git submodule —— 抓下來之後它是專案自己的 XAML（只有 keyed 樣式與它自己的控制項），佈景就只剩 MahApps | 語料（NETworkManager） |
| 93 | **Display 模式的字寬是 hdmx 的 hinting 寬度，像素大小 0.5 進位** | 字距也四捨五入到整數像素（偶數進位）。有資料的大小 Segoe UI、Arial、Trebuchet MS 每一組都一樣；12.5 px 這種剛好一半的大小 Tahoma、JhengHei 偏寬 | WpfOracle --display |
| 94 | **hinting 旗標說「不改寬度」不能信** | Yu Gothic UI 的 head.flags 第 4 位是 0，照樣在 20、21、24、28 px 改寬度（偏窄 2%）。Consolas 剛好沒改。沒有 hdmx 的大小一律量不到；hinting 讓一個字多出好幾個像素也有（hdmx 實查），沒有上限可估 | WpfOracle --display |
| 95 | **TTC 的字面要照 nameID 1 挑** | YuGothR.ttc 裡的是 Yu Gothic 與 Yu Gothic UI Semilight；Yu Gothic UI Regular 在 YuGothM.ttc。比對程式挑錯字面就以為模型錯了 | WpfOracle --display |
| 96 | **馬拉地文的 ल、श 字形不同** | Nirmala UI 的 dev2 有 MAR 語言系統；用預設的語言系統量 ल 窄 4.5%。印度系文字也要依元素語言挑語言系統 | WpfOracle --scripts |
| 97 | **坦米爾文 virama 後面接鼻化符號是不合法的組合** | WPF 畫一個虛線圓圈佔位，寬到 +45%。這種序列量不到 | WpfOracle --scripts |
| 98 | **畫在前面的母音符號要先移到前面再套字距** | 坦米爾文的 ெ ே ை（與兩段式母音的前段）畫在子音前面，字型的字距是照畫出來的順序套的；照字元順序算，叢集偏窄到 3.7% | WpfOracle --scripts |
| 99 | **標點跟著印度系文字併進同一段** | 「ல்:」的冒號被字型撐開 0.08 px：叢集後面接標點時加匯出時量到的最大值 | WpfOracle --scripts |
| 100 | **叢集相加對印度系文字是安全的近似** | 連寫（半形、合字）只會讓整個字變窄：馬拉地文 1,153 個字，叢集相加沒有一個比整個字窄。子音 + virama 後面接子音的改變量取「所有接法裡最少的」 | WpfOracle --indic |
| 101 | **連寫的改變量要依「兩個子音」記，而且只套一串的第一個 virama** | 依第一個子音記「最少改變」會被不連寫的組合拉成 0（馬拉地文平均偏寬 2.1%）；依兩個子音記之後 0.6%。但三個子音（क्र्श）兩組都套時中間的 र 同時算成下加形與 reph，偏窄 30% —— 只套第一個；後面那個叢集自己又是「子音 + virama」也不套 | WpfOracle --scripts（隨機連寫字） |
| 102 | **帶 nukta 的子音不能用不帶 nukta 的連寫值** | 匯出只算「子音 + virama」兩個字；「子音 + nukta + virama」借用會偏窄 1% | WpfOracle --scripts |
| 103 | **單獨的「子音 + virama」要套 haln** | 卡納達文、僧伽羅文字尾的 virama 由 haln 組成；不套偏窄到 36%。僧伽羅文的 virama 一律看得見、跟子音成合字：照全部特性跑 | WpfOracle --scripts |
| 104 | **古吉拉特文、泰盧固文的叢集會互相影響** | 附加符號（anusvara、visarga）與兩段式母音讓整個字比叢集相加寬到 3%：叢集模型處理不了，沒開 | WpfOracle --scripts |
| 105 | **佈景的範本裡可能有 UseLayoutRounding** | 控制項本身沒設，範本裡的元素設了：內容寬度帶小數時控制項寬度被捨入到像素（HandyControl、WPF-UI 幾乎每個控制項）。用小數寬度的內容在真的視窗裡量才看得出來；當鄰居時多留 1 DIP | LayoutOracle --theme |
| 88 | **成對的表蓋不住跨三個字以上的上下文規則** | Segoe UI 的 ے 依前面兩個字換長度（另存三字改變量）；Segoe UI 的點避讓字距、Calibri 6.27 的大量 calt 跨更遠。匯出時用 2 萬個隨機字自我檢查，拼窄的字母移出驗證清單（Segoe UI 13 個少用字母），剩不到一半整支不寫（Calibri） | 匯出自我檢查 |

### 環境層級的坑

- **本機的 `python` 是 Windows 佔位捷徑**，會**卡在等 stdin** 而不是失敗。腳本一律用 `py -3`。
  （nuget skill 記過一次，這個專案又踩了一次。）
- **`node -e` 裡不要用 `require(絕對路徑)`** 讀 JSON，Windows 路徑會 `MODULE_NOT_FOUND`。
  用 `fs.readFileSync`。
- **XML 註解裡不能有連續兩個減號。** 寫 `dotnet run --project …` 在註解裡會讓整份 XAML 解析失敗。
- 本機沒有 `jq`（**不要為此下載執行檔**）；CI 有。本機要斷言就用 `node`。
- 專案裡有 `.claude/worktrees/` 的副本，掃描一定要排除點開頭的目錄，否則重複計算三倍。

---

## 驗收怎麼跑

```bash
dotnet test XamlFit.slnx -c Release          # 661 個單元測試（改到語言相關的程式時，再用 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 跑一次）
bash scripts/verify-demo.sh                  # 示範專案：預期 exit 1 + 五種判定各一個
bash scripts/verify-budget.sh                # budget 不擋門 + 每列都有具名 status
bash scripts/verify-cli.sh                   # 42 條 CLI 路徑的退出碼
dotnet run -c Release --project prototype/LayoutOracle   # 228 案 vs 真的 WPF 視窗（--check 用檢查模式的排法再跑一次）（Windows）；有高估就回 1
```

**來源語言自我檢驗**（沒有腳本，手動跑）：對一個已經出貨的 App 跑
`xamlfit check <root> --source-only`。寫死的文字理應都裝得下 —— 報出溢出就先懷疑工具。
M6 裡它兩次不是零，兩次都是工具錯。

`prototype/` 底下九個是**拋棄式**工具，不要從裡面複製程式碼：

- `LayoutOracle` —— **版面語意的真值**：同一段 XAML 給真的 WPF 視窗（顯示在螢幕外）與解析器，逐案比對。
  `-- --defaults` 印佈景預設的 Padding／Border，`-- --text` 印快速鍵、模擬粗體、Display 模式的實測，
  `-- --fallback` 印補字與看不見的字，`-- --probe "家族|字重|Italic" 字級 "文字"` 印整句與逐字的寬度、
  主字型缺哪些字、哪支已安裝的字型寬度完全一樣（找 WPF 實際拿來補字的字面）（Windows only）
- `CorpusRender` —— **真實專案的真值**：把專案的 XAML 連同它的 `App.xaml` 合併字典、來源與各語言字串載進真的 WPF，
  量每段文字需要多寬、看得見多寬，輸出 CSV。輸入是一份 JSON 規格（`root`、`app`、`source`、`cultures`、`window`、
  `lines`、`replace`、`output`）；跟工具的判斷逐筆比對的腳本不在 repo 裡（它要語料）。畫不出下拉選單的內容與資料範本（Windows only）
- `ThemeChrome` —— 量控制項函式庫的佈景：`themechrome mahapps|md2|md3|handycontrol|wpfui out.json`，再用 `node prototype/ThemeChrome/combine.js <輸出目錄> data/theme-chrome.json`
  合成（之後複製到 `src/XamlFit.Core/`）。套件版本寫在它的 csproj；換版本要重量。LayoutOracle `-- --theme mahapps|md2|md3|handycontrol|wpfui`（可加 `--check`）拿真的視窗驗（Windows only）
- `DpiProbe` —— 各縮放比例下的視窗邊框與捲軸寬度（`AdjustWindowRectExForDpi`、`GetSystemMetricsForDpi`）。
  內距表裡的 16／20／17.5 就是從這裡確認的（Windows only）

- `M0Survey` —— 可量測比例調查（已修過五個 bug，結論在 `docs/`）
- `WpfOracle` —— 拿 WPF 的 `FormattedText` 當真值驗量測（Windows only）。`-- --scripts <度量檔> <strings.json>` 對照希伯來文、阿拉伯文、波斯文、泰文（strings.json：`{"ar": [...], ...}`；`XF_SCRIPT_FONTS` 換字型、`XF_SCRIPT_DUMP="ar/Segoe UI"` 加 `XF_SCRIPT_ALL=1` 逐句印、`XF_SCRIPT_LTR=1` 用左到右排）。`-- --display <out.tsv>` 存 Display 模式的寬度（10 支字型 × 11 個字級 × 5 種縮放），`-- --display-check <度量檔> <tsv>` 拿工具的量法比；
`-- --indic <strings.json>` 比「整個字」與「叢集相加」，`-- --indic-gen <out.json>` 從 IndicClusters 的定義產生每種文字的測試字串（餵給 `--scripts`）。匯出時的阿拉伯文自我檢查：`XAMLFIT_ARABIC_SELFCHECK=1 xamlfit fonts export …` 印每個語言系統拼窄的字數與移掉的字母
- `ChromeGen` —— 量控制項內距表（Windows only）
- `DemoMetrics` —— 產生示範用的合成字型度量（跨平台）
- `GridOracle` —— 早期的 Grid 欄寬對照。⚠ 它量的是根元素而不是 Show 過的視窗，
  而且 ItemsControl 那兩案的預期值是照舊語意寫的；新的案例都加在 `LayoutOracle`

`ChromeGen` 與 `DemoMetrics` 的產出要 commit；另外兩個只產生文件。
