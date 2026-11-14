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
