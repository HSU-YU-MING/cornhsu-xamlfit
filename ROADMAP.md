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
- [ ] Trusted Publishing 政策、推 tag 發 0.1.0

~~**發版前必辦**：在 Linux 上實際執行一次~~ —— 2026-11-14 推上 GitHub 後 CI 第一次就在 Ubuntu 上全部通過（一般與 globalization-invariant 模式）。

## 0.x（介面凍結前）

依「卡住的位置數」排序，數字來自公開語料的實跑：

1. **佈景套件的內距表。** 用了 MaterialDesign／wpfui／HandyControl／MahApps 的應用程式，
   現在得自己在設定檔宣告每個控制項的內距。`prototype/LayoutOracle` 的量法可以直接對那些套件跑一次，
   把結果隨工具附上（或做成 `xamlfit chrome measure`，讓使用者對自己的佈景量）。
   這是把「控制項庫 0%」變成有用數字的唯一一條路。

2. **清單控制項的自訂範本**（ListBox／ListView／ItemsControl）。現在一律放棄 ——
   範本裡的捲動行為不是一個「內距」說得完的。

3. **標題放在巢狀控制項裡的範本**（Expander 的標題在 ToggleButton 裡）。現在找不到標題的內容宿主。

4. **沒掃描的文字屬性**：`ToolTip`、`Title`、浮水印／提示的附加屬性、`Hyperlink`／`Span` 的內容。
   它們現在是「工具不知道它們存在」，比「量不到」更糟。

5. **收成 `Width="0"` 的面板**（ScreenToGif 318 個位置）。展開後的寬度寫在動畫或程式碼裡；
   讀得到 Storyboard 的 `To` 值的那些可以算。

6. **合併圖。** 現在假設每個資源字典檔都被合併進應用程式。照 `MergedDictionaries` 的 `Source` 真的走一遍，
   可以消掉一部分「同名定義互相矛盾」。

**不打算做的**：`SharedSizeGroup`（欄寬由整個 scope 的其他 Grid 共同決定）、
Auto 欄裡的跨欄子元素（WPF 的分攤規則複雜，算錯會連帶弄錯星號分配）、
`DataGrid` 的儲存格（寬度來自 DataGridColumn 自己的尺寸系統）。

其他：

- `action.yml` 的實測
- 中日韓系統預設字型的實機確認（現在只有繁中量過）

## 1.x+

- **GPOS 字距調整**。改判條件寫死了：真實專案的誤報裡若有可觀比例是
  「差 1~2% 的邊界案例」，就回頭做。在那之前不做 —— 誤差方向是安全的那一邊（只會誤報，不會漏報）。
  目前來源語言的自我檢驗是 929 筆 0 誤報，沒有理由做。
- 複雜文字系統（阿拉伯文／希伯來文／印度系／泰文）—— 需要真正的 shaping
- `TextFormattingMode="Display"` —— 需要 hinting，不只是度量

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
