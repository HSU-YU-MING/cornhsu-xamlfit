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
- 度量檔裡沒有的字型、`TextFormattingMode="Display"` 下沒有 hinting 寬度的像素大小

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
| 93 | **Display 模式的字寬是 hinting 寬度，像素大小 0.5 進位** | 字距也四捨五入到整數像素，**剛好一半往大的進**（Calibri 32 px 的 −1.5 → −1；原本偶數進位拼窄 1 px）。剛好落在半個像素（12.5、31.5 px）時 WPF 字寬用小的或大的、字距可能照另一個算：兩個像素大小都量，字距取三者（兩個整數與原本的大小）最寬 | WpfOracle --display-check |
| 106 | **GDI 的 hinting 寬度就是 WPF Display 模式的寬度** | `fonts export --hinted` 用 GetCharWidth32W 補 hdmx 沒列的 8～48 px（WpfOracle --display-gdi：不含字距時一樣）。GDI 找不到要的字面會**默默換一支**：一定要比 GetTextFaceW、tmWeight、tmItalic，不對就不寫。同名字型不能同時註冊（會選到別支檔案），所以這段不平行。GDI 回報的是系統語言的名稱（繁中 Windows：「微軟正黑體」、Light 是「微軟正黑體 Light」），要用別名比對；Segoe UI Variable 的預設實例 GDI 叫不到名字（選到新細明體），不寫 | WpfOracle --display-gdi |
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
| 107 | **一個罕用字可以讓整個文字系統都偏寬** | 泰盧固文每個叢集交界都多 185 單位（9%），因為「接任何叢集時最多變寬多少」的最大值全來自已廢用的 ఴ（它的 virama 形往左伸）。拿掉後平均偏寬 9.3% → 7.9%。卡納達文的 ೞ 同一類。偏寬查原因：先看最大值是哪一個字造成的 | 匯出時印 argmax |
| 108 | **這支匯出引擎不是真的排版引擎** | 不處理特性遮罩、不做 reph／前置母音的最終重排：高棉文「ត៉ា」窄 0.44 px → 有沒有聲調轉換符號都算、取寬的；reph 前後都算；前置母音在 virama 看得見時只移到後一個子音前面。每加一種文字都要先用 WpfOracle --scripts 對照，**偏窄超過 0.25% 就不開** | WpfOracle --scripts |
| 110 | **LookupFlag 要處理** | 不處理時 Nirmala UI 的奧里亞文「ଦ + 下加形 ଭ」被一個該跳過下加形（附加符號分類 8）的 lookup 連成寬合字，連寫全部沒變窄（平均偏寬 5%）；緬甸文 ဋို့ 窄 29%。處理之後要注意：巢狀 lookup 在最後一個比對位置插入字符時，下一個位置要照整體增減算，否則同一條規則重套到記憶體用光（OutOfMemory，匯出整支字型讀不了） | WpfOracle --scripts |
| 111 | **「最少變窄」會被一個例外拖到 0** | 奧里亞文 1,444 組連寫裡 1,380 組被「後一個子音接 virama」（量的時候本來就不套）或 nukta、ୁ 形式拖成 0。量的時候不套的形式匯出也不要算；比單獨子音變窄得少的母音符號另外記 | 匯出時印每一組的值 |
| 112 | **調規則用的字串不能拿來驗收** | 用種子 31 的字串調完全部通過，換種子 777 冒出古木基文 −8%、奧里亞文 −5.9%、卡納達文 −1.9%（而且已經發佈的版本就有）。WpfOracle --indic-gen 用 `XF_GEN_SEED`、gen-sea.js 第二個參數換一組 | WpfOracle --scripts |
| 113 | **印度系文字要窮舉才算驗收** | 兩組隨機字串都過了，窮舉「每個連寫 × 每個母音符號／nukta／鼻化符號、每個不帶符號的三子音連寫、再抽 2 萬個帶符號的」（十種文字約 240 萬句）還冒出**上一版就有**的少算：孟加拉文 হ্যৃ −17%、奧里亞文 ଧ୍ଯି −35%、卡納達文 ಜ್ಞು −19%、馬拉雅拉姆文 യ്യൢ −32%。`XF_GEN_EXHAUSTIVE=1 WpfOracle --indic-gen`（高棉文也有） | WpfOracle --scripts |
| 114 | **匯出引擎不做 WPF 的音節重排** | 下加、後加的母音符號 WPF 放在主子音旁邊（হ্যৃ 的 ৃ 跟 হ 成合字；ಜ್ಞು 不成 ಜ್ಞ 合字）；前置形（泰盧固文、馬拉雅拉姆文的 ra）畫在最前面，**而且是搬過去之後才套最後一階段的規則**（గ్రై 的 ra 換成寬的那個字形）；奧里亞文的 ଁ 掛到第一個子音上。匯出時這幾種排法都算、取寬的，只對真的會變成下加／後加／前置形的子音做（每個子音都做的話，窮舉的平均偏寬馬拉地文 10% → 27%）。`XAMLFIT_INDIC_TRACE="碼位,…;…"` 印引擎排出的字形，`WpfOracle --glyphs` 印 WPF 的 | WpfOracle --glyphs |
| 115 | **三個子音的連寫依後兩個子音記** | 只記一個總最大值時，卡納達文、泰盧固文三子音的字平均偏寬 40～60%（下加形都被當成全寬）。中間是 ra、ya（有 reph、後加形）時引擎在連寫中間也照樣套，算出來偏窄 16～34%：這些用同類裡的最大值。後兩個都是下加形時母音符號掛在第一個子音上跟它成合字，帶符號的要每個第一子音都試。最後一個子音帶 nukta 時 WPF 整串不連寫（奧里亞文 ନ୍ଫ୍ଯ଼），另外記 | WpfOracle --scripts（窮舉） |
| 116 | **高棉文的 robat 只能緊接主子音、coeng ro 一串只能一個** | robat 接在下標子音或母音後面、兩個 ្រ：WPF 畫虛線圓圈（寬到 3 倍）。窮舉時六萬多個「少算」全是這個 —— 先確認是不是虛線圓圈再查量法 | WpfOracle --glyphs |
| 117 | **蒙古文的寬度要分很細，合字要整對記** | 每個字各取最大值時平均偏寬 60%：擴充字母（托忒、錫伯、滿文）混進一般字母、陰陽性、FVS、MVS／窄不換行空白前後、在不在第一個音節、前一個是不是母音，都會換字形；子音 + 母音的合字 WPF 整個算給子音、母音分到 0，要整對一起查。FVS 要沿用前一個字母的形狀特性（跟 HarfBuzz 一樣），不然引擎沒把它換成零寬字形（2214 單位）。MVS 在字中間 WPF 畫 205 單位寬，不是看不見的字 | WpfOracle --scripts、--mong-attr |
| 118 | **WPF 不認得「Segoe UI Variable」** | 它把名字拆成「Segoe UI」+ 字面名稱「Variable」，畫的是 Segoe UI（「Segoe UI Foo」也一樣）。可變字型 WPF 認得的家族是「家族名 + 那組實例共同的字」：Segoe UI Variable Text／Small／Display，字重是各具名實例。寬度要讀 HVAR，字距要讀 GDEF 的變化量 —— PairPos 格式 1 的裝置表**從 PairSet 起算**，不是從子表；不讀時粗體偏窄 0.16% | WpfOracle（`XF_TEXT_FACES`） |
| 119 | **可變字型的 Display 寬度是線性寬度四捨五入，不是 GDI 的** | Segoe UI Variable Small 10 px 的 i：WPF 3 px、GDI 2 px。WPF 不對可變字型 hinting：15 個實例 × 34 個字 × 8～24 px 逐字量（`WpfOracle --char-display`），除了小數點後 .49x 的（WPF 的前進寬度有小數，度量檔是整數），全部等於四捨五入 → 0.485 就進位。字距 WPF 怎麼進位找不到規則（四捨五入在 2,025 句裡窄 75 句）：往零取整。⚠ 上一輪寫「也不是四捨五入」是錯的：那次拿 GDI 的寬度去比，GDI 自己把幾個字捨掉了 | WpfOracle --char-display、--display-check |
| 120 | **完整的度量檔 gzip 後只剩五分之一** | 37 個家族加緬甸、藏、蒙古文字型 38 MB 的 JSON 壓成約 6 MB：`fonts export` 預設就寫 `xamlfit.fonts.json.gz`，讀的時候看檔頭（1F 8B）自動解壓；同一個資料夾兩個名字都有時用比較新的 | — |
| 121 | **三子音連寫的多算幾乎都來自「不分母音符號」** | 卡納達、泰盧固、奧里亞文的多算 90% 以上在三子音的字（平均偏寬 35～46%），兩子音只有 1～7%。照 WPF 規則只排一種順序（找主子音、符號緊接主子音）平均多算一點都沒降、還冒出偏窄 —— 「幾種排法取寬的」不是原因。依最後一個子音帶的符號分開記之後：卡納達 4.5～5% → 2.7～2.9%、泰盧固 3.7% → 1.7～1.8%、奧里亞 3.7～4.3% → 1.6～2.0%。代價：Nirmala UI 的度量約 7 → 12 MB | WpfOracle --scripts |
| 122 | **附加符號的定位特性也會改寬度** | Nirmala UI 奧里亞文的 `blwm` 有一條上下文定位：兩個下加形疊在一起時前一個撐開 550 單位。匯出只套 kern、dist 時三子音連寫偏窄到 27%（原本被「不分符號的最大值」蓋住）。匯出引擎現在套 kern、dist、abvm、blwm、mark、mkmk（只做會改寬度的單一／成對／上下文定位） | WpfOracle --glyphs、fontTools |
| 123 | **前置形移到主子音前面，不一定是最前面** | 馬拉雅拉姆文 ട്ച്ര：第一個子音的 virama 看得見，音節在那裡斷開，ra 的前置形畫在 ച 前面（−2%） | WpfOracle --glyphs |
| 124 | **高棉文的母音符號改的是第一個子音的寬度** | Leelawadee UI 的 វ្ច្ឌី：ី 讓 វ 寬 150 單位。三子音帶符號時只抽兩個第一子音試會漏（−18%）→ 高棉文（主子音一定是第一個）每個第一子音都試，Leelawadee UI 匯出 30 秒 → 3 分鐘 | WpfOracle --scripts（窮舉） |
| 125 | **WPF 把不是家族的名字拆成「家族 + 字面名稱」** | `Segoe UI Variable` → Segoe UI（`FontWeight=Bold` 就是粗體）、`Times New Roman Italic` → 斜體、`Arial Foo Bar` → Arial。要先知道整個名字不是裝著的字型才能照做：度量檔記匯出機器上的家族名（`installedFamilies`），沒有就不拆 | PowerShell + PresentationCore |
| 126 | **緬甸文 ြ 的單一定位 +440，WPF 不套** | Myanmar Text 的 `dist` 裡有一條最上層的單一定位（type 1）把 ြ（103C）往右推 440 單位，照套的話每個帶 ြ 的字偏寬約 5%。WPF 量到的寬度裡沒有它。整個 `dist` 不套又會讓 ya 下加形偏窄 24%（疊在一起的 ra 那 +350 是上下文定位，WPF 有套）。所以只對 `NoDistOn` 列的字（緬甸文 103C）跳過最上層的單一定位，其他照套 | WpfOracle --scripts（my_cl / my_sea） |
| 127 | **蒙古文的鄰字鍵要有訓練字蓋得到** | 加了第 16–21 位（母音記前一個字母、子音記後一個字母）之後，訓練字裡沒有的組合退回粗鍵：只用陽性／陰性母音產生訓練字時，中性母音 ᠢ 開頭的字偏窄 7.36%、加到 15 萬字還有 −3.04%。訓練字的母音要含中性的 ᠢ，數量加到 40 萬 | node gen-mong.js + WpfOracle --scripts |
| 128 | **緬甸文的叢集邊界不能照下一個叢集的第一個字形記** | 試過依「下一個叢集畫在最前面的字」（ေ、ြ）分開記邊界寬度：產生的字偏窄 12%、真實的緬甸文 −1.99%（前置符號的字距跟後面的子音也有關，一個鍵蓋不住）。已整個拿掉，別再試同一招 | WpfOracle --scripts（my_sea / real） |
| 129 | **WPF 換數字有自己的一張表，不照 .NET 的 DigitSubstitution** | 緬甸文真實句子「…မအောင်မြင်ပါ: {0}」偏窄 0.66%：元素語言是 my 時 WPF 把 0 畫成 ၀（Myanmar Text 寬 17%）。.NET 列為原生數字的 97 個文化逐一給 WPF 量：一律換的只有 my、dz、ne-IN、ks、lrc、mzn、pa-Arab、ps、sd、ur-IN、uz-Arab；ar、fa 只在阿拉伯字母後面換；km、bn、nqo、fa-AF、ne-NP、ur-PK 不換。表在 `TextMeasure.NativeZero`，兩種數字都量取寬的（App 可以用 NumberSubstitution 關掉替換）。這個偏窄之前被緬甸文的多算蓋住 | WpfOracle --glyphs / --scripts（XF_SCRIPT_SET） |
| 130 | **夾在蒙古文裡的標點也套蒙古文的規則** | 蒙古文真實句子裡的「《」偏窄 0.44%：Mongolian Baiti 的 `rclt` 無條件把 60 個標點換成另一個字形（《 1026 → 1116 單位），WPF 只對跟蒙古文排在同一段的標點套（單獨的 《 不套）。匯出時每個標點夾在蒙古文裡排一次，比原本寬的記成 `1,碼位`（25 個）；字串裡有蒙古文就用那個寬度 | WpfOracle --scripts（mn_real2） |
| 131 | **reph 只給音節開頭的 ra** | 匯出引擎原本對每個「ra + virama」都套 rphf，連寫中間的 ra 也變成 reph、算出來比 WPF 窄，所以中間是 ra 的三子音連寫只能用總的最大值（卡納達文這種字偏寬 16～23 px）。改成只有前面沒有子音（或後面沒有子音：reph 移到後面的那種排法）的才套。只定義了 `Ra` 的文字系統這樣遮；沒定義的照舊全部套、中間那個子音也還當特殊的 | WpfOracle --scripts（kn_chain）、窮舉 |
| 132 | **ra 開頭的三子音連寫要另外記，帶符號的多寬量還是要拿 ra 取樣** | 卡納達文每一組（C2, C3）都被 ra 開頭的撐大。分開記之後只從自己這組取「帶符號時多寬多少」的樣本：窮舉抓到孟加拉文 ঘ্দ্বু 偏窄 21%（原本 ra 的多寬量剛好蓋過）、馬拉雅拉姆文帶前置母音的 −7%、奧里亞文 ra 開頭帶鼻化符號的 −0.17%。樣本照舊（全部 C1 裡最寬的兩個與 ra），馬拉雅拉姆文帶前置母音時每個 C1 都試，奧里亞文 ra 開頭帶符號的不比其他 C1 窄。ra 的鍵只記跟一般的不一樣的（奧里亞文 2.6 萬個裡 2.4 萬個一樣） | 窮舉（XF_GEN_EXHAUSTIVE） |
| 133 | **緬甸文交界依符號記，後一個叢集要依上方符號挑** | 交界撐開多少幾乎只看兩邊的符號（1,600 組對照）。改成依符號記時：只有子音的叢集全部共用一個鍵會讓真實文字偏寬 10%（沒有符號時看子音）；後一個叢集只挑「畫出來的第一個字」的最短代表時真實文字偏窄 0.21%（ပေါ် 接 ရိ 撐開 4.69 px，接 ရ 不撐開）。挑法改成「第一個字 + 上方符號」，候選從 40 個變 360 個，單執行緒一支字型要 9 分鐘，改成平行算；完整匯出 20 → 26 分鐘。另外試過再記一份依最後兩個字的、取小的：每一組結果都一樣，拿掉了 | WpfOracle --scripts（my_sea、real、grid） |
| 134 | **連寫的值跟交界的值不能重複算** | 泰盧固、古吉拉特文（叢集會互相影響）每個叢集交界都加「接任何叢集最多撐開多少」，連「హ్」接 ణ 也加（+1576 單位），連寫值（−1877）又算一次：హ్ణ 偏寬 71%。兩個子音的連寫中間不加交界，連寫後面改用依兩個子音記的「接任何一個字最多撐開多少」（`ConjunctTrailKey`；只用後一個叢集自己的交界時 హ్భ + క 偏窄 0.17 px）。三個子音以上照舊加：拿掉的話窮舉偏窄 2.6%（原本被重複算的量蓋住） | 窮舉 + 隨機字 |
| 135 | **三子音連寫扣掉 C1 + C2 的連寫值只能用在不帶符號的** | 第一個子音不同時整串差 5～15 px，扣掉 C1 + C2 的連寫值後 1～4 px。全部形式都這樣記：古木基文 ਝ੍ਯ੍ਝ −5.6%（特殊的中間子音在總最大值 ≤ 0 時沒寫進表）、馬拉地文 −6%、奧里亞文帶 nukta −4.6%（帶符號的多寬量是從幾個 C1 取樣的，扣掉連寫值後蓋不住沒取樣的）。只給最後一個子音不帶符號的另外記一個鍵（`ChainBareKey3`，每個 C1 都試過），再加連寫後面接一個字最多撐開多少（卡納達文 ತ್ಥ್ಯ + ಶ 寬 0.06 px） | 窮舉 + 隨機字 |
| 136 | **「母音符號搬到第一個子音後面」那種排法不能隨便拿掉** | 泰盧固文、奧里亞文照原本的順序排跟 WPF 一樣寬的組合佔九成以上（只差拆成兩段的母音），看起來這種排法是多餘的。只對拆成兩段的母音搬：泰盧固文平均一點沒變，奧里亞文帶 ya 的連寫偏窄到 40%（WPF 真的會搬）。撤掉 | 窮舉 |
| 137 | **緬甸文交界依兩邊記會偏窄** | 真實的緬甸文多算的 0.87 個百分點都在交界（不加交界時平均 0.03%、最窄 −1.99%）。依「前一個叢集的符號 + 後一個叢集的種類（第一個字 + 上方符號）」記：平均降到 0.04%，但每一種後一個叢集只拿最短的一個試，帶 ့、ွ、ှ 的撐得更開，產生的字偏窄 12%。要全部試是 4 萬 × 4 萬，撤掉 | WpfOracle --scripts |
| 138 | **匯出慢在總工作量，不在哪一段單執行緒** | 迴圈改平行沒有變快（Nirmala UI 36 個字面 × 文字系統的工作已經佔滿 20 核）。真正有用的是排版引擎：每個 lookup 先記「可能從哪些字開始」，其他位置直接跳過 —— 輸出逐位元組一樣，Nirmala UI 534 → 316 秒、Myanmar Text 488 → 108 秒。`XAMLFIT_DEBUG=1` 匯出時印每支字型、每段花的時間 | — |
| 139 | **字距要照 WPF 的段與語言套，不能把全部 lookup 取聯集** | 字距原本是所有文字系統、語言系統的 lookup 聯集，依碼位查。三個錯：(1) 跨文字系統：希臘文後面「 API」的空白在希臘文那段、A 在拉丁文那段，WPF 不套（Arial 偏窄 1.3%）；(2) 別的文字系統的 lookup：Calibri 阿拉伯文的 lookup 有「",」（拉丁文偏窄 3.6%）、Tahoma 跟 Ebrima 的拉丁文根本沒掛 kern；(3) 語言：Segoe UI 的 latn/TRK 少了「Ta」「Te」的 lookup（土耳其文偏窄 2.9%），Arial 的土耳其文用 locl 把 i 換成沒有字距的字形（−0.5%）。度量檔記每個 lookup 掛在哪些系統、每個語言換了哪些字；共用字跟著前一個有文字系統的字，只有共用字的一段當拉丁文 | WpfOracle --scripts（XF_SCRIPT_SET 七支字型 × 八種語言） |
| 140 | **WPF 每 100 個字一段排，字距不跨段** | 俄文長句「…процедуру.」偏窄 0.19%：「у.」剛好在第 99、100 個字（UTF-16），WPF 沒套字距；第 199／200 個也一樣。阿拉伯文的連寫、印度系的連寫會跨段（只有 GPOS 字距不跨） | WpfOracle --glyphs |
| 141 | **組合符號：WPF 畫寬度 0，有合成字就組起來** | 分解寫法的越南文偏窄到 6%、偏寬到 64%：Segoe UI 的 U+0323 在 hmtx 寫 682、WPF 畫 0；「T + e + ̂」WPF 畫成 T + ê（字距是 T–ê 的）。照原樣量（符號 0、後面接符號的字跟前一個的字距只套會變寬的）與 NFC 各量一次取寬的；字型沒有那個符號 WPF 畫缺字方框（Verdana 的 U+031B 1 em）→ 量不到。globalization-invariant 模式不能正規化（PlatformNotSupportedException），只照原樣量 | WpfOracle --glyphs / --scripts |
| 142 | **Nastaliq 的判斷是推的，不是量的** | 匯出引擎不做草寫連接（GPOS 'curs'）。這台機器沒有 Nastaliq 字型（Urdu Typesetting 是 Windows 的選用功能）。有 'curs' 的 Dubai 不理它量出來 0.03% 以內（Light 有一半以上錨點不在邊上也是）—— 所以只對「草寫連接至少 100 個字、大多數錨點不在邊上」的字型不匯出阿拉伯文。拿到 Nastaliq 字型要先對照 | WpfOracle --scripts（XF_SCRIPT_FONTS=Dubai） |
| 143 | **希伯來文的點也會換掉前面的字** | Arial 的「ע + ִ」WPF 畫成另一個 ע（1096，原本 1084 單位）：ccmp 換的。匯出時每個字母接每種符號跑一次 ccmp，取最寬的換過的字形（`markedWidths`，希伯來文的點與拉丁、希臘、西里爾的組合符號）；量的時候後面接符號的字用它。取各種符號裡最寬的，帶點的希伯來文偏寬約 0.5% | WpfOracle --scripts（he 帶點） |
| 144 | **阿拉伯字母跨字型連寫量不到** | 拿 Dubai 量隨機的阿拉伯字：度量檔裡有別的字型時，Dubai 沒有的字母（ؿ）由後備字型補，照連寫的形狀量偏窄 41%。WPF 每支字型各排一段，兩邊不連。跟前一個字母連寫、但是別支字型畫的 → 量不到（只有 Dubai 一支的度量檔時這些字母本來就缺字，看不出來） | WpfOracle --scripts（XF_SCRIPT_FONTS=Dubai，完整度量檔） |
| 109 | **產生測試字串時順序寫錯，WPF 會畫虛線圓圈** | 緬甸文「်」寫在「့」前面、寮文聲調寫在 ະ 後面：WPF 畫虛線圓圈（寬很多），看起來像工具偏窄 18%。先確認字串本身合法；工具這邊對不合法的順序報量不到（InvalidPairs） | WpfOracle --scripts |
| 104 | **古吉拉特文、泰盧固文的叢集會互相影響** | WPF 套字距時**跳過**附加符號：「દૃં」+「ડ」照「દૃ」+「ડ」撐開；這支匯出引擎不處理 LookupFlag，量不到。改成每個交界加「這個叢集或它任何前綴接任何叢集時最多變寬多少」；泰盧固文另外不用連寫值（ళ్కి 偏窄 2.8%）（偏寬 1.9%／7.9%，偏窄 ≤0.03%） | WpfOracle --scripts |
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
dotnet test XamlFit.slnx -c Release          # 726 個單元測試（改到語言相關的程式時，再用 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 跑一次）
bash scripts/verify-demo.sh                  # 示範專案：預期 exit 1 + 五種判定各一個
bash scripts/verify-budget.sh                # budget 不擋門 + 每列都有具名 status
bash scripts/verify-cli.sh                   # 47 條 CLI 路徑的退出碼
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
- `WpfOracle` —— 拿 WPF 的 `FormattedText` 當真值驗量測（Windows only）。`-- --scripts <度量檔> <strings.json>` 對照希伯來文、阿拉伯文、波斯文、泰文、印度系文字、寮文、高棉文、藏文、緬甸文（strings.json：鍵是語言，km／lo／bo／my 用 Leelawadee UI／Microsoft Himalaya／Myanmar Text；`{"ar": [...], ...}`；`XF_SCRIPT_FONTS` 換字型、`XF_SCRIPT_SET="dz=dz-BT/Microsoft Himalaya;ps=ps-AF/Segoe UI"` 加別的語言（文化／字型）、`XF_SCRIPT_DUMP="ar/Segoe UI"` 加 `XF_SCRIPT_ALL=1` 逐句印、`XF_SCRIPT_LTR=1` 用左到右排）。`-- --display <out.tsv>` 存 Display 模式的寬度（10 支字型 × 11 個字級 × 5 種縮放；`XF_DISPLAY_FAMILIES="Microsoft JhengHei,MingLiU"` 換一組字型），`-- --display-check <度量檔> <tsv>` 拿工具的量法比（度量檔要用 `fonts export --hinted` 匯出才每組都量得到），`-- --display-gdi <tsv>` 拿 GDI 的 hinting 寬度比（`XF_GDI_NARROW=1` 印偏窄的）；
`-- --glyphs <字型> <語言> <文字或碼位…>` 印 WPF 實際排出的字形編號與寬度（跟匯出時 `XAMLFIT_INDIC_TRACE` 印的比），`-- --mong-attr <度量檔> <strings.json>` 把 WPF 的字形照 ClusterMap 分給字、跟蒙古文表比、列出多算最多的鍵；`-- --char-display <家族> <字重> <字…>` 印 Display 模式下每個字在 8～24 px 的寬度；主程式設 `XF_TEXT_FACES="Segoe UI Variable Text:300,400;…"` 換一組字面，`--display` 設 `XF_DISPLAY_WEIGHTS="300,350,400,600,700"` 量可變字型的每個實例；`-- --indic <strings.json>` 比「整個字」與「叢集相加」，`-- --indic-gen <out.json>` 從 IndicClusters 的定義產生每種文字的測試字串（餵給 `--scripts`；`XF_GEN_SEED` 換一組，調完規則一定要換一組驗）。`--indic-gen` 加 `XF_GEN_EXHAUSTIVE=1` 窮舉（每個連寫 × 每種形式、每個不帶符號的三子音連寫、2 萬個帶符號的；含高棉文；一種文字 10～80 萬句，跑一次約 20 秒）。`node prototype/WpfOracle/gen-mong.js <out.json> [種子]` 產生蒙古文的字與句子（匯出時的自我檢查用另一種產生方式；匯出時設 `XAMLFIT_MONGOLIAN_WHY="碼位,形狀;…"` 印出把這些鍵撐到最大的上下文、`XAMLFIT_DEBUG=1` 印自我檢查拼窄的字數）；`node prototype/WpfOracle/gen-sea.js <out.json> [種子]` 依音節結構產生緬甸文、高棉文、藏文、寮文各 3,000 個字（固定亂數種子；語料裡沒有這幾種語言）。`fonts export` 時設 `XAMLFIT_DEBUG=1` 會印出讀不了的字型檔與例外（平常一個資料夾裡有壞字型是常態，靜靜跳過）。匯出時的阿拉伯文自我檢查：`XAMLFIT_ARABIC_SELFCHECK=1 xamlfit fonts export …` 印每個語言系統拼窄的字數與移掉的字母
- `ChromeGen` —— 量控制項內距表（Windows only）
- `DemoMetrics` —— 產生示範用的合成字型度量（跨平台）
- `GridOracle` —— 早期的 Grid 欄寬對照。⚠ 它量的是根元素而不是 Show 過的視窗，
  而且 ItemsControl 那兩案的預期值是照舊語意寫的；新的案例都加在 `LayoutOracle`

`ChromeGen` 與 `DemoMetrics` 的產出要 commit；另外兩個只產生文件。
