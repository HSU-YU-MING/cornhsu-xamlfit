# Roadmap

規劃書（`XamlFit專案規畫書.md`）是完整版；這份是對外的摘要。

## 0.1.0（首發）

- [x] M0 可量測比例調查 —— 結論在 `docs/M0-可量測比例調查.md`（⚠ 那份的數字是實作前的估計，
      引擎實際的數字以 `docs/M6-全面重讀.md` 為準）
- [x] M1 字型度量讀取器 + `fonts export`
- [x] M2 版面解析 + 控制項內距表 + 樣式解析
- [x] M3 `budget` 模式
- [x] M4 `check` 模式
- [x] M5 repo 骨架、CI、示範專案的計數斷言
- [x] M6 全面重讀：把「高估可用寬度」的洞補掉、拿真的 WPF 視窗逐案對照
- [x] GitHub repo 公開（2026-11-14），CI 在 Linux 與 Windows 上通過
- [x] Trusted Publishing 政策、推 tag 發 0.1.0（2026-11-17 上架）

~~**發版前必辦**：在 Linux 上實際執行一次~~ —— 2026-11-14 推上 GitHub 後 CI 第一次就在 Ubuntu 上全部通過（一般與 globalization-invariant 模式）。

### M6 一併做掉的（原本排在 0.x 與 1.x+）

這幾條以前列在「之後再說」，M6 做完了，首發就有：

- **佈景套件的內距表。** MahApps、MaterialDesign 2／3、HandyControl、WPF-UI 的控制項內距與捲軸寬度
  已經量好附在工具裡（`data/theme-chrome.json`，量法見 `prototype/ThemeChrome`，
  用 `LayoutOracle -- --theme` 拿真的視窗驗）。套件版本換了要重量。
  還沒量過的佈景底下，同一個範圍裡的標準控制項一律維持「不知道」。
- **收成 `Width="0"` 的面板。** 同檔 Storyboard 的 `DoubleAnimation` 取最小的非 0 字面值；
  `To` 不是字面值的維持不知道。
- **GPOS 字距調整。** 依 WPF 的文字段與元素語言套（不是把所有 lookup 取聯集）。
- **複雜文字系統。** 希伯來文、阿拉伯文、波斯文、泰文、印度系（天城體／孟加拉／古木基／古吉拉特／
  奧里亞／坦米爾／泰盧固／卡納達／馬拉雅拉姆／僧伽羅）、寮文、高棉文、藏文、緬甸文、蒙古文。
  每一種都拿 WPF 的 `FormattedText` 逐句對照過，窮舉驗收。
- **`TextFormattingMode="Display"`。** hinting 寬度（hdmx 加 GDI 補值），可變字型另外處理。

## 0.x（介面凍結前）

依「卡住的位置數」排序，數字來自公開語料的實跑：

1. ~~**清單控制項的自訂範本**（ListBox／ListView／ItemsControl）~~ ✅ 0.7.0：內距照範本裡的 ItemsPresenter 算，
   捲軸照清單自己的 ScrollViewer.* 設定（LayoutOracle 10 案，0 高估）。TreeView、DataGrid 的範本照舊不讀。

2. ~~**標題放在巢狀控制項裡的範本**（Expander 的標題在 ToggleButton 裡）~~ ✅ 0.8.0 查證：其實已經算得出來
   （LayoutOracle 加 3 案全部一致）。真正卡住的是 ExpandDirection 左右 —— 0.8.0 照 WPF 預設範本實量的規則算
   （那一欄 = max(標題寬, 19) + 2 + 左右 Padding）；專案範本與外部佈景的左右標題照舊量不到。

3. ~~**沒掃描的文字屬性**：`ToolTip`、`Title`、浮水印／提示的附加屬性~~ ✅ 0.7.0：ToolTip（系統字型、popupMaxWidth）、
   浮水印（MahApps／MaterialDesign／WPF-UI 在真的視窗裡量過的組合，其他列出來但量不到）、
   視窗標題（drawn-by-windows，不進可解析率的分母）。
   （`Hyperlink`／`Span` 裡的文字 M6 已經會掃了：那三層包裝不改字寬，當成裡面的 `Run` 本身。）

4. ~~**合併圖**~~ **不做**（2026-11-19 實查）：ScreenToGif、Playnite、QuillNest、Cornea、Kindling 沒有任何位置
   卡在「同名定義互相矛盾」。而 Playnite 在執行期從資料夾載入佈景字典 ——「沒被合併的檔案」不代表沒用到，
   照合併圖把它們丟掉，會把「不知道」變成「沒設定」。有語料證明卡住了再談。

5. **標題列的建模。** 視窗標題的可用寬度 = 視窗寬 − 圖示 − 標題列按鈕，系統的標題字型。
   要在各 Windows 版本、縮放、WindowStyle／ResizeMode 組合下實量（DpiProbe 的 DWMWA_CAPTION_BUTTON_BOUNDS）才開。
   2026-11-19 在一台 Windows 11、125% 的機器上量過（PrintWindow 前後兩張比對標題像素）：
   可見寬 = Width − 12.8；標題從可見左緣 29.6 DIP 開始（ToolWindow 8.8）；標題列按鈕 CanResize／CanMinimize 148 DIP、
   NoResize 與 ToolWindow 49.6；刪節號後的字停在按鈕前 1.6～4 DIP；系統標題字型這台是 Microsoft JhengHei UI 12。
   按鈕一顆 49.3 DIP、不是名義上的 46：DIP 值跟著縮放變，只量一種縮放的模型可能在別的縮放高估 —— 所以還沒開。

**不打算做的**：`SharedSizeGroup`（欄寬由整個 scope 的其他 Grid 共同決定）、
Auto 欄裡的跨欄子元素（WPF 的分攤規則複雜，算錯會連帶弄錯星號分配）、
`DataGrid` 的儲存格（寬度來自 DataGridColumn 自己的尺寸系統）。

其他：

- ~~`action.yml` 的實測~~ ✅ CI 的 `action`（Linux）與 `action-windows`（含 export-fonts）job 真的跑 Action
- 中日韓系統預設字型的實機確認（現在只有繁中量過）
- WPF 預設 TabControl 範本的選取標籤（用負邊距放大）沒建模，維持不知道

## 1.x+（已知的近似，有理由再動）

這些現在的行為都偏向「量不到」而不是「算錯」，所以不急：

- **Nastaliq（烏都文的排版字型）。** 匯出引擎不做草寫連接（GPOS `curs`），而且手邊沒有 Nastaliq 字型可以對照。
  現在的做法是對「草寫連接的錨點大多不在字的邊上」的字型整個不匯出阿拉伯文 —— 那些字就量不到。
  拿到字型要先用 `WpfOracle --scripts` 對照過才開。
- **匯出引擎不是完整的排版引擎。** 不處理特性遮罩、不做 reph 與前置母音的最終重排，
  改用「幾種排法都算、取寬的」。每加一種文字都要先對照，**偏窄超過 0.25% 就不開**。
- **可變字型在 Display 模式下的字距進位**找不到規則，現在往零取整（偏寬的那一邊）。
  沒有 hdmx 也叫不到 GDI 寬度的字級一律量不到。

## 明確不做

- **把 App 跑起來。** 那是所有競品在做的事，也是本工具存在的理由。
- **自動修版面。** 寬度要放到哪裡是設計決定，工具只報不改。
- **WinUI／Avalonia／MAUI。** 內距、預設字型、版面語意都不一樣；那是另一支工具。
- **npm 包裝。** 使用者是 .NET 開發者，人人都有 dotnet。

## 與家族其他兩支的關係

`XamlContrast`（對比度）、`XamlKeys`（資源鍵）、`XamlFit`（版面容納）
共用的是**慣例與知識**，不是程式碼：退出碼心智模型、baseline 形狀、`x:Key` 解析的眉角。
刻意不共用引擎 —— 沒有消費者的公開 API 只是提前凍結自己的重構自由。

⚠ 一條從 XamlContrast 抄來、在這裡被推翻的慣例：**「控制項名稱一律後綴比對」**。
拿來判斷「它是不是分頁標籤、是不是清單」沒問題；拿來**套內距的數字**是錯的 ——
`ui:Button` 的範本跟 WPF 的 `Button` 沒有關係。
