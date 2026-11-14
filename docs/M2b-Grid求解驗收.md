# Grid 求解驗收（v2 第一項）

> 日期：2026-11-14
> 真值來源：WPF 真實版面（`XamlReader` 建樹 + `Measure`/`Arrange`）
> 驗證工具：`prototype/GridOracle`（拋棄式，Windows only）
> **結果：14 ／ 14 完全吻合（差 0.000）**

---

## 1. 做了什麼

M0 的結論說 `Auto`／星號欄佔「有裁切風險」位置的 **21.6%**，是單一最大缺口。
這一輪實作了 WPF 的欄寬演算法：

1. 固定像素欄取自己的值
2. `Auto` 欄取該欄子元素**期望寬度**的最大值（含子元素 Margin）
3. 剩餘空間按星號權重分配
4. 每欄再被自己的 `MinWidth`／`MaxWidth` 夾住

Grid 自己的可用寬度用遞迴解（Grid 裡的 Grid 也解得開，深度上限 6）。

「期望寬度」由 `ElementMeasurer` 提供，只有三種算得出來：明寫 `Width`、
量得到的字面文字、只包一個子元素的容器（遞迴）。其他一律回 `null` ——
**Auto 欄寬猜錯，星號分配跟著錯，而且錯的方向不可控。**

## 2. 驗證

單元測試只能證明「程式符合我對 Grid 演算法的理解」。所以照 M1 的做法，
把同一段 XAML 交給 WPF 自己量，比對欄寬：

| 案例 | 吻合 |
|---|---|
| 單一星號欄 | ✓ |
| 固定 + 星號 | ✓ |
| 星號權重（`*` vs `2*`） | ✓ |
| Auto 由固定寬子元素決定 | ✓ |
| Auto 子元素帶 Margin | ✓ |
| Auto 取最寬的子元素 | ✓ |
| 空的 Auto 欄 = 0 | ✓ |
| `MaxWidth` 夾住星號欄 | ✓ |
| `MinWidth` 撐起 Auto 欄 | ✓ |
| `ColumnSpan` 相加 | ✓ |
| 巢狀 Grid | ✓ |
| 欄內 Border 的 Padding | ✓ |
| 欄內元素的 Margin | ✓ |
| Auto 欄由水平 StackPanel 餵 | ✓ |

**14 項全部差 0.000。**

⚠ 第一版的 oracle 把案例包在未顯示的 `Window` 裡，WPF 側全部回 0 ——
未顯示的 Window 的版面由 HWND 驅動，對它呼叫 `Measure`／`Arrange` 不會傳遞下去。
直接量根元素才對。

## 3. 實測的覆蓋率提升 —— 遠低於 M0 的預測

M0 預測「解掉 Grid 會把有風險覆蓋率從 75.9% 推到約 97.5%」。**那個預測錯了。**

| 專案 | 解 Grid 前 | 解 Grid 後 | 再修 TextBox chrome 後 |
|---|---|---|---|
| 自家-QuillNest | 62.3% | **87.2%** | 87.2% |
| HandyControl | 95.3% | 96.6% | **97.3%** |
| MahApps.Metro | 71.5% | 76.3% | **77.0%** |
| ScreenToGif | 32.7% | 37.4% | **39.0%** |
| Playnite | 51.8% | 51.8% | 51.8% |
| WinUI-Gallery | 67.4% | 67.4% | 67.4% |

**兩個專案完全沒有改善。** 預測錯在它假設「所有 Grid 情形都會變成可解」——
實際上多數 Auto 欄裡裝的是綁資料的文字或自訂控制項，解不開。

而 QuillNest 提升 25 個百分點，是因為作者寫的是固定寬對話框加簡單的 Auto／星號欄、
文字又是字面字串。**這又是一次取樣偏誤**：自家專案的改善幅度是公開專案的五倍。

## 4. 真正的第一順位變成 ComboBox

為了弄清楚「為什麼沒改善」，這一輪加了「卡在哪」的統計。結果很乾淨：

| 專案 | 最大卡點 | 筆數 |
|---|---|---|
| WinUI-Gallery | `control chrome is unknown: ComboBox` | **101** |
| ScreenToGif | 同上 | **72** |
| Playnite | 同上 | **27** |
| ScreenToGif | `Auto column: a child's width is unknown` | 12 |
| WinUI-Gallery | 同上 | 2 |

**Grid 已經幾乎解完（每個專案只剩 2~12 筆），ComboBox 才是現在的瓶頸。**

### 為什麼 ComboBox 量不到

試了三種量法都失敗：

1. **兩點內容探針**（給 100 與 200 寬的內容看斜率）—— ComboBox 不把內容寬度往外傳，斜率 0。
2. **視覺樹找內容宿主** —— 程式碼 `new` 出來的控制項，`VisualTreeHelper` 看到 0 個子元素
   （範本沒展開）。改用 `XamlReader` 建樹之後樹有了，但 `ContentPresenter` 在內容為空時
   排成 0 寬。
3. **塞一個超寬（10000）的選取內容**，期待 presenter 被裁到可用寬度 —— 仍然量不到。

### 為什麼不退回「只算 padding + border」

因為 ComboBox 的下拉按鈕（Aero2 實測 `splitBorder` 寬 16.8）不在 padding 裡。
少算 17px 會讓可用寬度偏大 → **漏報溢出**，那是最糟的失敗方向。

**維持 `unmeasurable`。** 那些位置的文字不會被誤判成「裝得下」，只是沒有答案。

### 一個尚未釐清的語意問題

`ComboBoxItem` 的文字在執行期出現在**兩個地方**：關起來的選取框（會被裁）
與展開的下拉清單（依內容決定大小，不會被裁）。
所以它既不是「純內容決定大小」（像 Menu），也不是單純受框寬約束。
正確的建模是「以關起來那個框為上界」，而那正好需要 ComboBox 的 chrome。

## 5. 對 ROADMAP 的修訂

| 原本 | 改成 |
|---|---|
| 第一順位：解 Grid（佔 21.6%） | 第一順位：**ComboBox 的 chrome**（實測 200 筆，跨三個專案） |
| — | 第二順位：Auto 欄裡綁資料／自訂控制項的子元素（實測 14 筆） |
| README：「解 Grid 會推到約 97%」 | 刪掉。改寫成實測的前後對照與各專案區間 |

---

## 6. 後記：ComboBox 的 chrome 量到了（同日）

第 4 節寫「三種量法都失敗、維持 `unmeasurable`」。**第四種成功了。**

### 做法

`ComboBox` 的 `ContentPresenter` 預設**靠左且不拉伸**，所以它的 `ActualWidth` 是
「內容本身的寬」而不是「可用區域的寬」——難怪空內容時是 0、放文字時是文字寬。

加上 `HorizontalContentAlignment="Stretch"` 之後它撐滿可用區域：
**300 寬的 ComboBox 量到 272.2 → chrome = 27.8。**

這個做法成立的理由是：對齊方式只改變內容在區域裡的擺放，不改變區域大小。

**而且有第二條獨立路徑互證**：視覺樹顯示 `contentPresenter` 的 x=6.0、右 margin 5.0，
下拉按鈕 `splitBorder` 在 x=282.2、寬 16.8 →
`6.0 + 5.0 + (300 − 282.2) = 27.8`。兩條路徑同一個數字。

### 一個語意修正

`ComboBoxItem` 的 10 DIP 內距**只存在於展開的下拉清單**；關起來的選取框是把項目的
內容直接畫在 ComboBox 的 `ContentPresenter` 上，那個容器不在場。
而「會不會被裁」問的正是關起來那個框，所以兩個都扣會多扣約 10 DIP（約 6%）。
`WidthResolver` 現在對「ComboBox 直屬的 ComboBoxItem」不計它自己的 chrome。

### 覆蓋率

| 專案 | M0 之後 | 解 Grid 後 | + TextBox | **+ ComboBox** |
|---|---|---|---|---|
| Playnite | 51.8% | 51.8% | 51.8% | **100.0%** |
| WinUI-Gallery | 67.4% | 67.4% | 67.4% | **99.2%** |
| HandyControl | 95.3% | 96.6% | 97.3% | **97.3%** |
| 自家-QuillNest | 62.3% | 87.2% | 87.2% | **96.4%** |
| MahApps.Metro | 71.5% | 76.3% | 77.0% | **81.9%** |
| ScreenToGif | 32.7% | 37.4% | 39.0% | **77.5%** |

**中位數從約 65% 變成約 97%。** 這一輪真正的槓桿是 ComboBox，不是 Grid ——
而那是「卡在哪」的統計告訴我們的，不是猜的。

### 剩下的卡點（都是靜態分析的真實極限）

| 卡點 | 筆數 | 打算怎麼辦 |
|---|---|---|
| `ItemsControl` 裡的項目 | MahApps 116、HandyControl 8 | 第一順位，固定寬 ListBox 理論上算得出來 |
| `SharedSizeGroup` 欄 | MahApps 26 | **不做** —— 欄寬由整個 scope 的其他 Grid 共同決定 |
| Auto 欄裡的跨欄子元素 | MahApps 11 | **不做** —— WPF 的分攤規則複雜，算錯會連帶弄錯星號分配 |
| Auto 欄裡綁資料／自訂控制項 | ScreenToGif 14、MahApps 2 | 第二順位 |

### 教訓

我在第 4 節寫下「三種量法都失敗」並決定停手，那個決定本身是對的（不猜比猜好），
但**結論下得太早**。真正的差別是：前三種都在問「內容有多寬」，第四種改問
「可用區域有多寬」—— 而那才是要量的東西。
