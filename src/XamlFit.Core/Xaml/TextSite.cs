using XamlFit.Core.Fonts;
using XamlFit.Core.Layout;

namespace XamlFit.Core.Xaml;

/// <summary>文字從哪裡來。只有前兩種是翻譯對象。</summary>
public enum TextSource
{
    /// <summary>XAML 裡寫死的字串。</summary>
    Literal,
    /// <summary>指向資源字串（{x:Static p:Resources.K}、本地化標記延伸…）。</summary>
    Resource,
    /// <summary>綁到資料。不是翻譯對象，但仍可能溢出。</summary>
    DataBound,
}

/// <summary>換行與裁切的組合決定「塞不下」會長什麼樣（規畫書 §4.5 的判定矩陣）。</summary>
public enum WrapMode { NoWrap, Wrap, WrapWithOverflow }

public enum TrimMode { None, CharacterEllipsis, WordEllipsis }

/// <summary>一個會顯示文字的位置。</summary>
public sealed class TextSite
{
    public required string File { get; init; }
    public required int Line { get; init; }
    public required string Element { get; init; }

    public required TextSource Source { get; init; }

    /// <summary>字面文字，或資源鍵的原始寫法。</summary>
    public required string Text { get; init; }

    /// <summary>Source 是 Resource 時，抽出來的資源鍵（抽不出來是 null）。</summary>
    public string? ResourceKey { get; init; }

    /// <summary>來源語言的實際文字：字面字串就是它自己；資源字串是掃描時就查得到的值
    /// （XAML 字串字典）。查不到是 null —— .resx 的值要等 check 模式才讀。</summary>
    public string? SourceText { get; init; }

    public string? FontFamily { get; init; }
    public double FontSize { get; init; }
    public int FontWeight { get; init; } = 400;
    public FontStyleKind FontStyle { get; init; } = FontStyleKind.Normal;

    /// <summary>元素的 Language（xml:lang）。決定 WPF 拿哪支字型補中日韓的字。</summary>
    public string Language { get; init; } = FontFallback.DefaultLanguage;

    public WrapMode Wrap { get; init; } = WrapMode.NoWrap;
    public TrimMode Trim { get; init; } = TrimMode.None;

    /// <summary>宣告的高度（垂直裁切判定要用）。</summary>
    public double? Height { get; init; }

    public required AvailableWidth Width { get; init; }

    /// <summary>被 ignore 註解壓掉的理由。
    /// ⚠ **理由必填**：沒寫理由的 ignore 是無效的（<see cref="InvalidIgnore"/> 會是 true，
    /// 而這一欄維持 null）。沒有理由的壓抑，等於新開一條靜默退化的通道。</summary>
    public string? SuppressedReason { get; set; }

    /// <summary>有 ignore 註解但沒寫理由 —— 不生效，而且要警告。</summary>
    public bool InvalidIgnore { get; init; }

    public string Location => $"{File}:{Line}";

    public override string ToString() => $"<{Element}> \"{Text}\" @ {Location}";
}
