using XamlFit.Cli;

// XamlFit CLI —— XAML 文字容納度的靜態稽核。
//
// 子指令從第一天就存在（規畫書 §4.7）：
//   xamlfit fonts export     產生度量檔
//   xamlfit fonts list       列出度量檔裡有哪些字型
//   xamlfit budget <root>    匯出每個字串的像素預算（翻譯之前）
//   xamlfit check  <root>    拿真實譯文量，CI 守門（翻譯之後）
//
// 退出碼契約（1.0 凍結）：
//   0 通過 ／ 1 找到違規 ／ 2 用法、設定或輸入檔錯誤 ／ 3 結果不可信
//
// ⚠ exit 3 是本工具的核心防線：可量測比例低於下限時，「沒找到問題」這句話不可信。
//   把它當成 0 放行，就是謊報健康。

// ⚠ 輸出一律 UTF-8（不帶 BOM）。報告裡印的是使用者自己的介面文字 —— 中日韓、希臘文、表情符號。
//   .NET 在 Windows 上被導向（管線、CI、檔案）時用系統字碼頁：繁中 Windows 是 Big5，
//   實跑 QuillNest 時每一句中文與破折號都變成亂碼。GitHub Actions 的 log 也是 UTF-8。
try { Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false); }
catch (IOException) { }   // 沒有主控台（例如服務）時設不了：照原樣輸出

if (args.Length == 0)
{
    Usage.Print(Console.Out);
    return 2;
}

switch (args[0])
{
    case "-h" or "--help" or "help":
        Usage.Print(Console.Out);
        return 0;

    case "--version":
        Console.WriteLine(CommonOptions.ToolVersion());
        return 0;

    case "fonts":
        return FontsCommand.Run(args[1..]);

    case "budget":
        return BudgetCommand.Run(args[1..]);

    case "check":
        return CheckCommand.Run(args[1..]);

    default:
        Console.Error.WriteLine($"unknown command: {args[0]}");
        Console.Error.WriteLine("run 'xamlfit --help' for usage");
        return 2;
}
