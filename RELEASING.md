# Releasing

**推 tag 就是發版動作。** 其餘由 GitHub Actions 完成。

## 版本規則：tag 是唯一真相源

`release.yml` 從 tag 導出版本號再用 `-p:Version=` 覆寫。
`src/XamlFit.Cli/XamlFit.Cli.csproj` 的 `<Version>` 固定寫 `0.0.0-dev`。

⚠ **不要改成真的版本號**（會跟 tag 漂開，從原始碼 build 出來的執行檔自稱舊版，
連它蓋在 JSON 報告上的 `toolVersion` 都是錯的）。
⚠ **也不要整格刪掉**（退回 MSBuild 預設的 1.0.0，等於宣告介面已凍結）。

## 每次發布前要查的（CI 不會擋的那些）

- [ ] `CHANGELOG.md` 的版本段落寫完整
- [ ] `bash scripts/verify-demo.sh` 綠燈（計數斷言）
- [ ] `bash scripts/verify-budget.sh` 綠燈
- [ ] 動過 console 輸出的話，README 裡貼的範例要跟著改
- [ ] `bash scripts/verify-readme-facts.sh` 綠燈：README 的 Action 範例釘的版本 = CHANGELOG 最新版（CHANGELOG 加了新版本段落之後跑 `--update` 重貼；CI 也會擋）
- [ ] 相依升版了，`THIRD-PARTY-NOTICES.md` 要跟著改
- [ ] **字型 EULA 的法務功課**（見下）

## 1.0 之前一定要處理的法務項目

`THIRD-PARTY-NOTICES.md` 記著一條未完成的事：從商用字型萃取 advance width 並 commit
進使用者自己的 repo，法律立場尚未確認。

目前的設計已經是最安全的形態（使用者自行匯出、放自己 repo、我們不經手，
套件不附任何字型的度量；示範專案用的是 `prototype/DemoMetrics` 產生的合成字型），但**在 1.0 宣告介面凍結之前要把這條查清楚並寫進文件**。
問律師時，度量檔裡到底有什麼以 `THIRD-PARTY-NOTICES.md` 的「Font metrics」那一節為準 —— 不只是寬度：
字距保留了 GPOS 成對調整 lookup 的結構（涵蓋範圍、類別定義、類別矩陣），還有照字型的 GSUB／GPOS 規則算出來的寬度。
每次度量檔多記一種資料，那一節要跟著改。

## 發版步驟

```bash
git tag v0.1.0
git push origin v0.1.0
```

`release.yml` 接手：從 tag 導出版本 → build → test → `dotnet pack` →
用 **NuGet Trusted Publishing**（OIDC 換臨時金鑰，沒有長效 API key、沒有 repo secret）推上去。

## 首次發佈的一次性設定

1. repo 推上 GitHub（`Directory.Build.props` 的 `RepositoryUrl` 與 `release.yml` 要指到同一個路徑）
2. nuget.org → 帳號 → Trusted Publishing 建一條政策：
   - Package Owner：`Cornhsu`
   - Repository Owner：`HSU-YU-MING`
   - Workflow File：`release.yml`
   - Environment：留空
   - ⚠ 政策綁的是 repo + workflow，**新 repo 要建新政策**，不能沿用別支套件的
3. 確認 `release.yml` 裡 `NuGet/login` 的 `user:` 是 nuget.org 使用者名稱（`Cornhsu`）
4. GitHub repo 的 Actions 需要 `id-token: write`（workflow 已宣告）

## 本機乾跑

```bash
v=0.0.0-local.$(date +%s)
dotnet pack src/XamlFit.Cli/XamlFit.Cli.csproj -c Release -o ./artifacts -p:Version=$v
dotnet tool install --tool-path ./artifacts/tool --add-source ./artifacts Cornhsu.XamlFit --version $v
./artifacts/tool/xamlfit --version && ./artifacts/tool/xamlfit --help
```

三個細節都是踩過的坑，不要簡化掉：

- **一定要 `--version`。** 不指定時 NuGet 不選預覽版，`--add-source` 只是「多一個來源」，
  結果裝到的是 nuget.org 上的正式版 —— 乾跑驗的根本不是你剛 pack 的東西。
- **每次用唯一版本號。** 同一個版本號第二次 pack，install 會拿快取裡的舊檔。
- **用 `--tool-path` 不用 `--global`。** 全域已裝正式版時 `--global` 會失敗，
  而且乾跑不該動到它。

## 發完之後

nuget.org 的套件清單（flatcontainer）會先出現新版，但 `dotnet tool install`
走的 registration 索引比較晚。要驗安裝就等
`registration5-gz-semver2/cornhsu.xamlfit/index.json` 出現新版再裝，
別誤判成發佈失敗。

⚠ **第一次建立一個新的套件 ID 比較久**：0.1.0 實測 **5 分半**（每 30 秒查一次，
前十次都是 404，第 11 次才 200），新 ID 要先過套件驗證才進索引。
既有套件發新版才是約 2～3 分鐘。照舊的數字等 3 分鐘就放棄，會誤判成發佈失敗。

接著更新作品集網站的版本欄位（`portfolio-release-update` skill）。
