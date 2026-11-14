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
| 9 | 只認明寫的字型後備清單 | WPF 會做**系統層級自動後備**；不做的話真實專案的中文字串大量變成「量不到」 | QuillNest |
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
dotnet test XamlFit.slnx -c Release          # 453 個單元測試
bash scripts/verify-demo.sh                  # 示範專案：預期 exit 1 + 五種判定各一個
bash scripts/verify-budget.sh                # budget 不擋門 + 每列都有具名 status
bash scripts/verify-cli.sh                   # 42 條 CLI 路徑的退出碼
dotnet run -c Release --project prototype/LayoutOracle   # 138 案 vs 真的 WPF 視窗（Windows）；有高估就回 1
```

**來源語言自我檢驗**（沒有腳本，手動跑）：對一個已經出貨的 App 跑
`xamlfit check <root> --source-only`。寫死的文字理應都裝得下 —— 報出溢出就先懷疑工具。
M6 裡它兩次不是零，兩次都是工具錯。

`prototype/` 底下六個是**拋棄式**工具，不要從裡面複製程式碼：

- `LayoutOracle` —— **版面語意的真值**：同一段 XAML 給真的 WPF 視窗（顯示在螢幕外）與解析器，逐案比對。
  `-- --defaults` 印佈景預設的 Padding／Border，`-- --text` 印快速鍵、模擬粗體、Display 模式的實測（Windows only）

- `M0Survey` —— 可量測比例調查（已修過五個 bug，結論在 `docs/`）
- `WpfOracle` —— 拿 WPF 的 `FormattedText` 當真值驗量測（Windows only）
- `ChromeGen` —— 量控制項內距表（Windows only）
- `DemoMetrics` —— 產生示範用的合成字型度量（跨平台）
- `GridOracle` —— 早期的 Grid 欄寬對照。⚠ 它量的是根元素而不是 Show 過的視窗，
  而且 ItemsControl 那兩案的預期值是照舊語意寫的；新的案例都加在 `LayoutOracle`

`ChromeGen` 與 `DemoMetrics` 的產出要 commit；另外兩個只產生文件。
