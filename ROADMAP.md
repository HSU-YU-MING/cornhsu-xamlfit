# Roadmap

規劃書（`XamlFit專案規畫書.md`）是完整版；這份是對外的摘要。

## 0.1.0（首發）

- [x] M0 可量測比例調查（go/no-go 閘門）—— 通過，結論在 `docs/M0-可量測比例調查.md`
- [x] M1 字型度量讀取器 + `fonts export` —— 驗收通過，見 `docs/M1-字型度量驗收.md`
- [x] M2 版面解析 + 控制項內距表 + 具名／隱含樣式解析
- [x] M3 `budget` 模式（CSV / JSON，跨位置取最小值，卡點點名）
- [x] M4 `check` 模式（`.resx`、判定矩陣、`--pseudo`、baseline、ignore）
- [x] M5 repo 骨架、CI、示範專案的計數斷言
- [x] v2 第一項：解 Grid 的 Auto/星號欄（14/14 與 WPF 吻合）＋ TextBox/PasswordBox 的 chrome
- [ ] GitHub repo 公開、Trusted Publishing 政策、推 tag 發 0.1.0

## 0.x（介面凍結前）

**發版前必辦**：在 Linux 上實際執行一次（目前只驗到建置層面，見 `docs/M5b-跨平台與CI驗證.md`）。

**第一順位：Auto 欄裡綁資料或自訂控制項的子元素**（ScreenToGif 14 筆、MahApps 2 筆）。
現在是最大的「有機會做」的一項。

**第二順位：TreeView 的縮排**（MahApps 12 筆）。XAML 裡看得到巢狀結構的那些算得出來，
綁資料的算不出來 —— 要能分辨這兩種。

**不打算做的**：`SharedSizeGroup`（26 筆，欄寬由整個 scope 的其他 Grid 共同決定，
靜態看不到那些）、Auto 欄裡的跨欄子元素（11 筆，WPF 的分攤規則複雜，算錯會連帶弄錯星號分配）、
`DataGrid` 的儲存格（2 筆，寬度來自 DataGridColumn 自己的尺寸系統）。

~~解 Grid 的 Auto/星號欄~~、~~ComboBox/TextBox 的 chrome~~、~~ListBox/ListView/ItemsControl 的項目~~
—— **都已完成**（16/16 與 WPF 真實版面吻合）。
⚠ M0 預測「解掉 Grid 會推到 97.5%」是錯的，而且錯在歸因：真正的瓶頸是 ComboBox。
前後對照見 `docs/M2b-Grid求解驗收.md`。

其他：

- `xamlfit.config.json` 實際載入（目前只有 CLI 旗標）
- SARIF 輸出與 `--msbuild` 診斷格式（跟家族其他兩支對齊）
- `action.yml` 的實測
- ~~TextBox / PasswordBox / ComboBox 的 chrome~~ —— **已完成**（6.0 / 6.0 / 27.8 DIP）

## 1.x+

- **GPOS 字距調整**。改判條件寫死了：真實專案的誤報裡若有可觀比例是
  「差 1~2% 的邊界案例」，就回頭做。在那之前不做 —— 度量檔會變大，
  而誤差方向是安全的那一邊（只會誤報，不會漏報）。
- 自訂 `ControlTemplate` 的內距推算
- 其他資源檔格式。目前只有 `.resx` —— ⚠ 實測 ScreenToGif 用 `{DynamicResource}` 把 UI 字串
  放在 ResourceDictionary 裡（WPF 常見做法），那些字串現在會被當成「綁資料」跳過：
  M0 調查算 1,052 個可在地化位置，引擎只認得 192 個。這是 v1 的範圍邊界，不是 bug，
  但它讓這類專案的實際可用性遠低於 M0 的數字
- 複雜文字系統（阿拉伯文／印度系／泰文）—— 需要真正的 shaping

## 明確不做

- **把 App 跑起來。** 那是所有競品在做的事，也是本工具存在的理由。
- **自動修版面。** 寬度要放到哪裡是設計決定，工具只報不改。
- **npm 包裝。** 使用者是 .NET 開發者，人人都有 dotnet；受眾論證不成立就不付
  發佈管線與平台二進位的維護成本（同 XamlContrast 的決定）。

## 與家族其他兩支的關係

`XamlContrast`（對比度）、`XamlKeys`（資源鍵）、`XamlFit`（版面容納）
共用的是**慣例與知識**，不是程式碼：退出碼心智模型、baseline 形狀、
`x:Key` 解析的眉角、控制項名稱一律後綴比對。
刻意不共用引擎 —— 沒有消費者的公開 API 只是提前凍結自己的重構自由。
