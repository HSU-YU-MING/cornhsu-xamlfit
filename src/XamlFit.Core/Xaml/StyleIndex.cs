using System.Xml.Linq;

namespace XamlFit.Core.Xaml;

/// <summary>屬性解析的三種結果。⚠ <see cref="Unreadable"/> 與 <see cref="NotSet"/> 一定要分開。</summary>
public enum ResolveKind
{
    /// <summary>沒有任何地方設定它 —— 用預設值是對的。</summary>
    NotSet,
    /// <summary>讀到了確定的值。</summary>
    Value,
    /// <summary>
    /// **有人設定了它，但值靜態讀不出來**（綁定、專案外的樣式、同名定義互相矛盾…）。
    /// ⚠ 這時用預設值是**錯的**：`Width="{Binding W}"` 當成沒寫 Width，會跳過它去用外層
    ///   更寬的上界；`FontFamily="{StaticResource UiFont}"` 當成沒寫，會拿預設字型去量。
    ///   兩者都是算出一個看起來很真的錯數字。
    /// </summary>
    Unreadable,
}

public readonly record struct Resolved(ResolveKind Kind, string? Value = null, string? Why = null)
{
    public static readonly Resolved NotSet = new(ResolveKind.NotSet);
    public static Resolved Of(string value) => new(ResolveKind.Value, value);
    public static Resolved Unknown(string why) => new(ResolveKind.Unreadable, null, why);
    public bool IsValue => Kind == ResolveKind.Value;
    public bool IsUnreadable => Kind == ResolveKind.Unreadable;
}

/// <summary>掛在 XDocument 上，讓元素查得到自己來自哪個檔。</summary>
public sealed record SourceFile(string Path);

public enum TemplateKind
{
    /// <summary>用的是佈景的預設範本 —— 內距表適用。</summary>
    Default,
    /// <summary>專案自己換了範本，而且我們拿得到那個範本。</summary>
    Custom,
    /// <summary>範本被換掉了、或可能被換掉了，但我們看不到換成什麼。</summary>
    Unknown,
}

/// <param name="Template">Kind 是 Custom 時的 ControlTemplate 元素。</param>
public readonly record struct TemplateInfo(TemplateKind Kind, XElement? Template, string Why)
{
    /// <summary>
    /// 同一個樣式在專案裡有好幾份定義（每個佈景一份）時的全部候選；null 元素代表「那一份沒換範本」。
    /// 呼叫端要對每一個都算內距，**結果一致才採用** —— 深色與淺色佈景的範本通常只差在顏色。
    /// 只有一個候選時是 null。
    /// </summary>
    public IReadOnlyList<XElement?>? Alternatives { get; init; }

    public static TemplateInfo Default => new(TemplateKind.Default, null, "");
    public static TemplateInfo Custom(XElement template, string from) => new(TemplateKind.Custom, template, from);
    public static TemplateInfo Unknown(string why) => new(TemplateKind.Unknown, null, why);
}

/// <summary>
/// 樣式與數值資源的解析，給版面與字型屬性用。
///
/// ── 作用域 ──
///
/// XAML 的資源查找是**有作用域**的：從元素自己往上，每一層的 <c>Resources</c> 都查，
/// 都沒有才到 Application 與它合併的資源字典。別的視窗的 <c>Window.Resources</c> 看不到。
///
/// 第一版把全專案的樣式攤成一個扁平的表，後來改成「同名時要一致才採用」——
/// 兩個都錯。實跑 QuillNest：每個對話框在自己的 <c>Window.Resources</c> 裡各定義一份
/// <c>&lt;Style TargetType="Border"&gt;</c>，扁平表把 16 份全部當成候選，然後因為它們不一致
/// 而把**每一個** Border 的 Padding 都判成讀不出來（89 個位置）。
///
/// 現在：
///   1. **語彙範圍**：元素自己與祖先的 <c>Resources</c>，直接從文件讀，最近的贏。
///   2. **全域**：根元素是 <c>Application</c> 或 <c>ResourceDictionary</c> 的檔案裡的頂層資源
///      （假設資源字典檔都被合併進應用程式 —— 這是近似，所以同名的多個定義仍然要一致才採用）。
///   3. 都找不到的鍵：定義在專案外，值是**不知道**，不是「沒設定」。
/// </summary>
public sealed class StyleIndex
{
    private sealed record StyleRecord(
        string File, string? Key, string? TargetType,
        string? BasedOnKey, string? BasedOnType, bool BasedOnUnreadable,
        Dictionary<string, string> Setters,
        bool SetsTemplate, XElement? Template, string? TemplateKey,
        XElement? Origin);

    private readonly Dictionary<string, List<StyleRecord>> _byKey = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<StyleRecord>> _implicit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string File, string Value)>> _values = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string File, XElement Template)>> _templates = new(StringComparer.Ordinal);

    private static readonly XName XKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

    private List<string> _projectDirs = [];

    /// <summary>垂直捲軸的寬度（DIP）。範本裡常見
    /// <c>MinWidth="{DynamicResource {x:Static SystemParameters.VerticalScrollBarWidthKey}}"</c>
    /// —— WPF 自己的 ComboBox 範本就是這樣留出下拉按鈕的位置。由掃描器從內距表設定。</summary>
    public double? SystemScrollBarWidth { get; set; }

    /// <summary>
    /// 繼承屬性一路查到檔案的根元素都沒人設定時，問「裝這個檔案的視窗」設了什麼。
    /// UserControl／Page 的字型是從宿主視窗繼承來的，而那個視窗在別的檔案裡。
    /// 由掃描器提供；沒設定就當成沒人設定。
    /// </summary>
    public Func<XElement, string, Resolved>? InheritedFromHost { get; set; }

    private static readonly XName XClass = XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml");

    /// <summary>
    /// 元素上某個屬性的原始寫法解成值（字面值、資源引用、認得的系統參數）。
    /// 給沒有樣式可言的東西用（<c>ColumnDefinition</c> 的 Width／MinWidth／MaxWidth）。
    /// </summary>
    public Resolved AttributeValue(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value is { } raw ? ValueOf(raw, element, FileOf(element)) : Resolved.NotSet;

    /// <summary>
    /// 告訴索引每個應用程式的根目錄（App.xaml 所在的目錄）。
    ///
    /// 一個 repo 裡常有好幾個應用程式，各有自己的 <c>Themes/ComboBox.xaml</c>
    /// （實查 ScreenToGif：主程式與翻譯工具各一份）。全域資源只在**同一個應用程式**裡找；
    /// 那裡找不到才退回全專案（執行期才用程式碼合併進來的佈景會落在這裡）。
    /// </summary>
    public void SetProjectDirectories(IEnumerable<string> appDirectories) =>
        _projectDirs = [.. appDirectories.Distinct(StringComparer.Ordinal).OrderByDescending(d => d.Length)];

    private string ProjectOf(string file)
    {
        foreach (var dir in _projectDirs)
            if (file.StartsWith(dir, StringComparison.Ordinal)) return dir;
        return "";
    }

    /// <summary>會從祖先繼承、而且可以用附加屬性寫法設在容器上的屬性。</summary>
    private static readonly HashSet<string> FontProperties = new(StringComparer.Ordinal)
    {
        "FontFamily", "FontSize", "FontWeight", "FontStyle", "FontStretch",
    };

    public int NamedCount => _byKey.Count;
    public int ImplicitCount => _implicit.Count;

    // ───────────────────────── 建立全域索引 ─────────────────────────

    /// <summary>
    /// 把一個檔案的**應用程式層級**資源加進全域索引。只有 Application 與資源字典檔有；
    /// 視窗與控制項自己的 Resources 是語彙範圍的，解析時直接從文件讀。
    /// </summary>
    public void Add(XDocument doc, string file = "")
    {
        if (doc.Root is not { } root) return;

        if (root.Name.LocalName == "ResourceDictionary")
        {
            IndexDictionary(root, file);
            return;
        }

        if (root.Name.LocalName.EndsWith("Application", StringComparison.Ordinal))
            foreach (var holder in root.Elements().Where(IsResourcesHolder))
                foreach (var dictionary in EntriesOf(holder))
                    IndexEntry(dictionary, file);
    }

    private void IndexDictionary(XElement dictionary, string file)
    {
        foreach (var entry in FlattenDictionary(dictionary)) IndexEntry(entry, file);
    }

    private void IndexEntry(XElement entry, string file)
    {
        var local = entry.Name.LocalName;

        if (local == "Style")
        {
            var record = ReadStyle(entry, file, origin: null);
            if (record.Key is not null) Append(_byKey, record.Key, record);
            else if (record.TargetType is not null) Append(_implicit, record.TargetType, record);
            return;
        }

        if (entry.Attribute(XKey)?.Value is not { Length: > 0 } key) return;

        if (local == "ControlTemplate")
        {
            if (!_templates.TryGetValue(key, out var list)) _templates[key] = list = [];
            list.Add((file, new XElement(entry)));
            return;
        }

        if (ValueOfResource(entry) is { } value)
        {
            if (!_values.TryGetValue(key, out var list)) _values[key] = list = [];
            list.Add((file, value));
        }
    }

    private static void Append(Dictionary<string, List<StyleRecord>> map, string key, StyleRecord r)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = [];
        list.Add(r);
    }

    private static bool IsResourcesHolder(XElement e) =>
        e.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal);

    /// <summary>Resources 屬性元素裡的項目（穿過包在外面的 ResourceDictionary 與行內的合併字典）。</summary>
    private static IEnumerable<XElement> EntriesOf(XElement holder)
    {
        foreach (var child in holder.Elements())
        {
            if (child.Name.LocalName == "ResourceDictionary")
                foreach (var e in FlattenDictionary(child)) yield return e;
            else
                yield return child;
        }
    }

    private static IEnumerable<XElement> FlattenDictionary(XElement dictionary)
    {
        foreach (var child in dictionary.Elements())
        {
            if (child.Name.LocalName.EndsWith(".MergedDictionaries", StringComparison.Ordinal))
            {
                // 行內寫內容的合併字典（少見）。帶 Source 的指向別的檔案 —— 那個檔案自己會被索引。
                foreach (var merged in child.Elements().Where(m => m.Name.LocalName == "ResourceDictionary"))
                    foreach (var e in FlattenDictionary(merged)) yield return e;
                continue;
            }
            if (child.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;
            yield return child;
        }
    }

    /// <summary>有直接文字值的資源（Double、Thickness、GridLength、FontFamily、String…）。物件型的回 null。</summary>
    private static string? ValueOfResource(XElement entry) =>
        !entry.HasElements && !string.IsNullOrWhiteSpace(entry.Value) ? entry.Value.Trim() : null;

    private static StyleRecord ReadStyle(XElement style, string file, XElement? origin)
    {
        var setters = new Dictionary<string, string>(StringComparer.Ordinal);
        var setsTemplate = false;
        XElement? template = null;
        string? templateKey = null;

        foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
        {
            var prop = setter.Attribute("Property")?.Value;
            if (prop is null) continue;

            // 「Control.FontSize」「ScrollViewer.VerticalScrollBarVisibility」取最後一段
            var name = prop[(prop.LastIndexOf('.') + 1)..];

            if (name == "Template")
            {
                setsTemplate = true;
                template = setter.Elements().FirstOrDefault(e => e.Name.LocalName == "Setter.Value")
                    ?.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate");
                // 全域索引活得比文件久：複製一份，不要把整份 XDocument 拖著
                if (template is not null && origin is null) template = new XElement(template);
                templateKey = ResourceKeyOf(setter.Attribute("Value")?.Value);
                continue;
            }

            // 值用屬性元素寫（<Setter.Value>…）的，內容是物件不是字串 —— 記成讀不出來
            setters[name] = setter.Attribute("Value")?.Value ?? "{complex}";
        }

        // BasedOn="{StaticResource K}" → K；BasedOn="{StaticResource {x:Type Button}}" → 這個型別的隱含樣式
        var basedOnRaw = style.Attribute("BasedOn")?.Value;
        var basedOnKey = ResourceKeyOf(basedOnRaw);
        var basedOnType = basedOnKey is null && basedOnRaw is not null ? TypeKeyOf(basedOnRaw) : null;

        return new StyleRecord(
            file, KeyOf(style), NormalizeType(style.Attribute("TargetType")?.Value),
            basedOnKey, basedOnType, basedOnRaw is not null && basedOnKey is null && basedOnType is null,
            setters, setsTemplate, template, templateKey, origin);
    }

    /// <summary>樣式的鍵。<c>x:Key="{x:Type Button}"</c> 等於沒有鍵（隱含樣式）。</summary>
    private static string? KeyOf(XElement entry)
    {
        var key = entry.Attribute(XKey)?.Value;
        return key is null || TypeKeyOf(key) is not null ? null : key;
    }

    /// <summary>「{x:Type Button}」或「{StaticResource {x:Type Button}}」裡的型別名。</summary>
    private static string? TypeKeyOf(string raw)
    {
        var marker = raw.IndexOf("x:Type", StringComparison.Ordinal);
        if (marker < 0) return null;
        var rest = raw[(marker + "x:Type".Length)..].Trim();
        var end = rest.IndexOf('}');
        if (end < 0) return null;
        var name = rest[..end].Trim();
        if (name.StartsWith("TypeName=", StringComparison.Ordinal)) name = name["TypeName=".Length..].Trim();
        var colon = name.LastIndexOf(':');
        name = colon >= 0 ? name[(colon + 1)..] : name;
        return name.Length > 0 ? name : null;
    }

    // ───────────────────────── 語彙範圍 ─────────────────────────

    private sealed class LexicalScope
    {
        public static readonly LexicalScope Empty = new();
        public Dictionary<string, XElement> ByKey { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, XElement> ImplicitStyles { get; } = new(StringComparer.Ordinal);
    }

    /// <summary>元素自己的 Resources。結果掛在元素上當快取 —— 每個位置都會往上查好幾次。</summary>
    private static LexicalScope ScopeOf(XElement owner)
    {
        if (owner.Annotation<LexicalScope>() is { } cached) return cached;

        var scope = LexicalScope.Empty;
        foreach (var holder in owner.Elements().Where(IsResourcesHolder))
        {
            foreach (var entry in EntriesOf(holder))
            {
                if (ReferenceEquals(scope, LexicalScope.Empty)) scope = new LexicalScope();

                if (entry.Name.LocalName == "Style" && KeyOf(entry) is null)
                {
                    if (NormalizeType(entry.Attribute("TargetType")?.Value) is { } type)
                        scope.ImplicitStyles[type] = entry;
                }
                else if (entry.Attribute(XKey)?.Value is { Length: > 0 } key)
                {
                    scope.ByKey[key] = entry;
                }
            }
        }

        owner.AddAnnotation(scope);
        return scope;
    }

    /// <summary>
    /// 範本被複製出來單獨算內距時，複本的根元素掛這個：資源查找走到複本頂端後，從原本的控制項繼續往上。
    /// ⚠ 沒有它的話，複本裡的元素只看得到全域的樣式 —— 視窗或 Grid 自己的資源裡定義的捲軸樣式被漏掉，
    ///   LayoutOracle 實測一個 OverridesDefaultStyle、寬 24 的捲軸被當成系統寬度，多算 6.5 px。
    /// </summary>
    public sealed record LexicalContinuation(XElement From);

    private static XElement? Up(XElement e) => e.Parent ?? e.Annotation<LexicalContinuation>()?.From;

    private static XElement? FindLexical(XElement from, string key)
    {
        for (XElement? cur = from; cur is not null; cur = Up(cur))
            if (ScopeOf(cur).ByKey.TryGetValue(key, out var found)) return found;
        return null;
    }

    /// <param name="skip">找「同型別的外層隱含樣式」時用：跳過這一個（它自己）。</param>
    private static XElement? FindLexicalImplicit(XElement from, string type, XElement? skip = null)
    {
        for (XElement? cur = from; cur is not null; cur = Up(cur))
            if (ScopeOf(cur).ImplicitStyles.TryGetValue(type, out var found) && !ReferenceEquals(found, skip))
                return found;
        return null;
    }

    /// <summary>同一個檔案裡的定義優先；再來是同一個應用程式裡的；都沒有才看全專案。</summary>
    private List<T> SameFileFirst<T>(List<T> all, Func<T, string> fileOf, string file)
    {
        if (all.Count <= 1) return all;

        var same = all.Where(s => fileOf(s) == file).ToList();
        if (same.Count > 0) return same;

        if (_projectDirs.Count > 0)
        {
            var project = ProjectOf(file);
            var sameProject = all.Where(s => ProjectOf(fileOf(s)) == project).ToList();
            if (sameProject.Count > 0) return sameProject;
        }

        return all;
    }

    /// <summary>從 <paramref name="from"/> 的位置看出去，鍵是 <paramref name="key"/> 的樣式有哪些候選。
    /// null = 這個鍵不在專案裡（或它不是樣式）。</summary>
    private List<StyleRecord>? StylesByKey(XElement? from, string key, string file)
    {
        if (from is not null && FindLexical(from, key) is { } lexical)
            return lexical.Name.LocalName == "Style" ? [Lexical(lexical, file)] : null;

        return _byKey.TryGetValue(key, out var all) ? SameFileFirst(all, s => s.File, file) : null;
    }

    /// <param name="self">找「同型別的外層隱含樣式」時（BasedOn="{StaticResource {x:Type Button}}"）
    /// 要跳過發問的那個樣式自己，否則它會以自己為基底繞圈。</param>
    private List<StyleRecord> ImplicitStyles(XElement? from, string type, string file, StyleRecord? self = null)
    {
        if (from is not null && FindLexicalImplicit(from, type, self?.Origin) is { } lexical)
            return [Lexical(lexical, file)];

        if (!_implicit.TryGetValue(type, out var all)) return [];

        var others = self is null ? all : all.Where(s => !ReferenceEquals(s, self)).ToList();

        // 應用程式層級的樣式問「我的基底是誰」：只有**自己不是**以型別為基底的那些才算
        if (self is not null && BasesItselfOnItsType(self))
            others = others.Where(s => !BasesItselfOnItsType(s)).ToList();

        return others.Count == 0 ? [] : SameFileFirst(others, s => s.File, file);
    }

    /// <summary>文件裡的樣式元素 → 紀錄。掛在元素上當快取：同一個樣式會被每個用到它的元素、每個屬性各問一次。</summary>
    private static StyleRecord Lexical(XElement style, string file)
    {
        if (style.Annotation<StyleRecord>() is { } cached) return cached;
        var record = ReadStyle(style, file, style);
        style.AddAnnotation(record);
        return record;
    }

    /// <summary>
    /// 會套到這個元素身上的隱含樣式。
    ///
    /// ⚠ 帶 <c>x:Class</c> 的根元素實際型別是它的**子類別**
    ///   （<c>&lt;Window x:Class="App.MainWindow"&gt;</c> 是 MainWindow 不是 Window），
    ///   而隱含樣式只套用到型別完全相同的元素 —— App.xaml 裡的 <c>&lt;Style TargetType="Window"&gt;</c>
    ///   套不到它身上（這是 WPF 的經典陷阱）。
    ///   例外是自訂控制項的**佈景樣式**（<c>Themes/Generic.xaml</c>）：那是透過 DefaultStyleKey 找的，
    ///   子類別會繼承。實查 ScreenToGif：每個視窗都是 <c>&lt;n:ExWindow x:Class=…&gt;</c>，
    ///   範本定義在 Themes/Generic.xaml。
    /// </summary>
    private List<StyleRecord> ImplicitFor(XElement element, string file)
    {
        var type = element.Name.LocalName;
        if (element.Attribute(XClass) is null) return ImplicitStyles(element, type, file);

        return _implicit.TryGetValue(type, out var all)
            ? SameFileFirst(all.Where(s => IsThemeStyleFile(s.File)).ToList(), s => s.File, file)
            : [];
    }

    private static bool IsThemeStyleFile(string file) =>
        file.EndsWith("Themes/Generic.xaml", StringComparison.OrdinalIgnoreCase)
        || file.Equals("Generic.xaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 一個**不在 XAML 裡**的元素（DataGrid 自己產生的欄標題）從 <paramref name="from"/> 的位置看得到的隱含樣式裡，
    /// 某個 setter 的值（含 BasedOn 鏈）。沒有隱含樣式、或樣式沒設這個屬性 → NotSet。
    /// </summary>
    public Resolved ImplicitSetter(XElement from, string targetType, string property)
    {
        var file = FileOf(from);
        var setterName = property[(property.LastIndexOf('.') + 1)..];
        var styles = ImplicitStyles(from, targetType, file);
        return styles.Count == 0 ? Resolved.NotSet : Agree(styles, s => FromRecord(s, setterName, file, 0));
    }

    /// <summary>
    /// 那個隱含樣式（含 BasedOn 鏈）有沒有可能換掉範本。範本寫在 <c>&lt;Setter.Value&gt;</c> 裡，
    /// 不在 <see cref="ImplicitSetter"/> 看得到的字串 setter 裡。基底讀不出來也算「可能」。
    /// </summary>
    public bool ImplicitStyleMaySetTemplate(XElement from, string targetType)
    {
        var file = FileOf(from);
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        bool Walk(StyleRecord r, int depth)
        {
            if (depth > 16 || !seen.Add(r.Origin ?? (object)r)) return false;
            if (r.SetsTemplate || r.BasedOnUnreadable) return true;
            if (r.BasedOnKey is not null)
                return StylesByKey(r.Origin, r.BasedOnKey, r.Origin is null ? r.File : file) is not { } bases
                       || bases.Any(b => Walk(b, depth + 1));
            if (r.BasedOnType is not null)
                return ImplicitStyles(r.Origin, r.BasedOnType, file, self: r).Any(b => Walk(b, depth + 1));
            return false;
        }
        return ImplicitStyles(from, targetType, file).Any(r => Walk(r, 0));
    }

    /// <summary>專案裡有沒有針對這個型別的隱含樣式（從這個元素的位置看得到的）。</summary>
    public bool HasImplicitStyle(XElement from, string targetType) =>
        FindLexicalImplicit(from, targetType) is not null || _implicit.ContainsKey(targetType);

    // ───────────────────────── 屬性解析 ─────────────────────────

    /// <summary>向下相容的簡化版：只要確定的值，其餘一律 null。**新程式碼請用 <see cref="ResolveEx"/>。**</summary>
    public string? Resolve(XElement element, string property, bool inheritable) =>
        ResolveEx(element, property, inheritable) is { Kind: ResolveKind.Value } r ? r.Value : null;

    /// <summary>
    /// 元素身上這個屬性的生效值，依 WPF 的優先序：
    /// 本地屬性 &gt; 明寫的樣式（含 BasedOn 鏈）&gt; 隱含樣式 &gt; 繼承自祖先。
    /// </summary>
    public Resolved ResolveEx(XElement element, string property, bool inheritable)
    {
        var file = FileOf(element);
        var setterName = property[(property.LastIndexOf('.') + 1)..];

        for (var cur = element; cur is not null; cur = inheritable ? cur.Parent : null)
        {
            // 屬性元素（<Grid.Children>）不是物件，沒有屬性也沒有樣式
            if (cur.Name.LocalName.Contains('.', StringComparison.Ordinal)) continue;

            // 1. 本地屬性
            if (LocalAttribute(cur, property) is { } raw)
                return ValueOf(raw, cur, file);

            // 屬性元素寫法（<Border.Padding>…）：有人設定了，但值是物件
            if (cur.Elements().Any(e => e.Name.LocalName == $"{cur.Name.LocalName}.{property}"))
                return Resolved.Unknown("set through a property element");

            // 2. 明寫的樣式。⚠ WPF 裡明寫的 Style 會**整個取代**隱含樣式（除非它自己 BasedOn），
            //    所以有明寫樣式時不再查隱含樣式。
            var (hasExplicit, fromExplicit) = FromExplicitStyle(cur, setterName, file);
            if (hasExplicit)
            {
                if (fromExplicit.Kind != ResolveKind.NotSet) return fromExplicit;
                continue;
            }

            // 3. 隱含樣式（TargetType 完全相同才套用）
            var r = Agree(ImplicitFor(cur, file), s => FromRecord(s, setterName, file, 0));
            if (r.Kind != ResolveKind.NotSet) return r;
        }

        return inheritable && InheritedFromHost is { } host ? host(element, property) : Resolved.NotSet;
    }

    private (bool HasExplicit, Resolved Result) FromExplicitStyle(XElement el, string setterName, string file)
    {
        var local = el.Name.LocalName;

        var raw = el.Attribute("Style")?.Value;
        if (raw is not null)
        {
            var key = ResourceKeyOf(raw);
            if (key is null) return (true, Resolved.Unknown($"<{local}> has a Style this tool cannot read"));
            if (StylesByKey(el, key, file) is not { } candidates)
                return (true, Resolved.Unknown($"<{local}> is styled by '{key}', which is not defined in the project"));
            return (true, Agree(candidates, s => FromRecord(s, setterName, file, 0)));
        }

        // 行內樣式：<Button.Style><Style …/></Button.Style>
        if (InlineStyle(el) is { } inline)
            return (true, FromRecord(Lexical(inline, file), setterName, file, 0));

        return (false, Resolved.NotSet);
    }

    private static XElement? InlineStyle(XElement el) =>
        el.Elements().FirstOrDefault(e => e.Name.LocalName == el.Name.LocalName + ".Style")
            ?.Elements().FirstOrDefault(e => e.Name.LocalName == "Style");

    /// <summary>
    /// 沿 BasedOn 往上找。
    ///
    /// ⚠ **要記錄走過的樣式，不能只靠深度上限。** 第一版只擋深度 16：
    ///   <c>BasedOn="{StaticResource {x:Type Button}}"</c> 的隱含樣式在專案裡有 N 份時，
    ///   每一份都把其他 N−1 份當基底，查找是 N 的 16 次方 —— 實跑 MaterialDesign 直接卡死。
    /// </summary>
    private Resolved FromRecord(StyleRecord r, string setterName, string file, int depth, HashSet<object>? seen = null)
    {
        if (depth > 16) return Resolved.NotSet;
        seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        if (!seen.Add(r.Origin ?? (object)r)) return Resolved.NotSet;

        if (r.Setters.TryGetValue(setterName, out var raw)) return ValueOf(raw, r.Origin, r.Origin is null ? r.File : file);

        if (r.BasedOnUnreadable) return Resolved.Unknown("a style in the chain has a BasedOn this tool cannot read");

        if (r.BasedOnKey is not null)
        {
            // 基底樣式在專案外 → 它設了什麼我們看不到
            if (StylesByKey(r.Origin, r.BasedOnKey, r.Origin is null ? r.File : file) is not { } bases)
                return Resolved.Unknown($"based on '{r.BasedOnKey}', which is not defined in the project");
            return Agree(bases, b => FromRecord(b, setterName, file, depth + 1, Fork(seen)));
        }

        if (r.BasedOnType is not null)
        {
            return Agree(ImplicitStyles(r.Origin, r.BasedOnType, file, self: r), b => FromRecord(b, setterName, file, depth + 1, Fork(seen)));
        }

        return Resolved.NotSet;
    }

    /// <summary>
    /// 應用程式層級的隱含樣式寫 <c>BasedOn="{StaticResource {x:Type Button}}"</c>：
    /// 以「在它之前合併進來的那個 Button 隱含樣式」為基底，沒有的話就是佈景的預設樣式。
    ///
    /// 靜態不知道合併的順序，所以把基底限定成**自己不是以型別為基底**的那些定義：
    ///   - 實查 Playnite：DefaultControls/Button.xaml 定義真正的樣式，另一個檔案的隱含樣式
    ///     以型別為基底、只加了觸發器 → 後者的基底是前者。
    ///   - N 個佈景檔各有一份、全部都以型別為基底：它們是同一個位置的不同版本，
    ///     彼此不是基底 → 基底是佈景的預設樣式。
    ///
    /// ⚠ 不加這個限制的話，N 份互相把對方當基底，路徑數是 N 的階乘 —— 測試用 12 份就跑不完，
    ///   「記錄走過的樣式」也擋不住（每條路徑各自都沒有重複）。實跑 MaterialDesign 直接卡死。
    /// </summary>
    private static bool BasesItselfOnItsType(StyleRecord r) =>
        r.Origin is null && r.Key is null && r.BasedOnType is not null && r.BasedOnType == r.TargetType;

    /// <summary>每個候選各走各的路，但都不能回到已經走過的樣式。</summary>
    private static HashSet<object> Fork(HashSet<object> seen) => new(seen, ReferenceEqualityComparer.Instance);

    /// <summary>把屬性的原始寫法變成值：字面值直接用，資源引用去查，其他標記延伸是讀不出來。</summary>
    /// <param name="from">資源引用從哪個元素的位置開始找（null = 只看全域）。</param>
    private Resolved ValueOf(string raw, XElement? from, string file)
    {
        var t = raw.Trim();
        if (t.Length == 0) return Resolved.NotSet;
        if (t.StartsWith("{}", StringComparison.Ordinal)) return Resolved.Of(t[2..]);
        if (!t.StartsWith('{')) return Resolved.Of(t);

        if (t.Equals("{x:Null}", StringComparison.Ordinal)) return Resolved.NotSet;

        if (SystemScrollBarWidth is { } scrollBar && t.Contains("SystemParameters.VerticalScrollBarWidth", StringComparison.Ordinal))
            return Resolved.Of(scrollBar.ToString("R", System.Globalization.CultureInfo.InvariantCulture));

        if (ResourceKeyOf(t) is { } key)
        {
            if (from is not null && FindLexical(from, key) is { } lexical)
                return ValueOfResource(lexical) is { } v
                    ? Resolved.Of(v)
                    : Resolved.Unknown($"resource '{key}' is not a plain value");

            if (!_values.TryGetValue(key, out var all))
                return Resolved.Unknown($"resource '{key}' has no readable value in the project");

            var candidates = SameFileFirst(all, v => v.File, file);
            var first = candidates[0].Value;
            return candidates.All(c => c.Value == first)
                ? Resolved.Of(first)
                : Resolved.Unknown($"resource '{key}' is defined with different values in {candidates.Count} places");
        }

        return Resolved.Unknown("set by a binding or markup extension");
    }

    /// <summary>多個候選時，**答案一致才採用**。不一致就是不知道 —— 不要隨便挑一個。</summary>
    private static Resolved Agree(List<StyleRecord> candidates, Func<StyleRecord, Resolved> pick)
    {
        if (candidates.Count == 0) return Resolved.NotSet;
        var first = pick(candidates[0]);
        for (var i = 1; i < candidates.Count; i++)
        {
            var other = pick(candidates[i]);
            if (other.Kind != first.Kind || other.Value != first.Value)
                return Resolved.Unknown($"defined differently in {candidates.Count} styles with the same key or target");
        }
        return first;
    }

    // ───────────────────────── 範本 ─────────────────────────

    /// <summary>
    /// 這個元素用的是哪個控制項範本。內距表量的是預設範本；換掉之後表裡的數字不適用 ——
    /// 拿得到新範本就從它算（<see cref="TemplateKind.Custom"/>），拿不到就是不知道。
    /// </summary>
    public TemplateInfo TemplateOf(XElement element)
    {
        var local = element.Name.LocalName;
        var file = FileOf(element);

        // 1. 本地
        if (element.Attribute("Template")?.Value is { } rawTemplate)
        {
            if (ResourceKeyOf(rawTemplate) is { } key && FindTemplate(element, key, file) is { } keyed)
                return TemplateInfo.Custom(keyed, $"Template '{key}'");
            return TemplateInfo.Unknown("has a Template this tool cannot read");
        }

        if (element.Elements().FirstOrDefault(e => e.Name.LocalName == local + ".Template") is { } holder)
            return holder.Elements().FirstOrDefault(e => e.Name.LocalName == "ControlTemplate") is { } own
                ? TemplateInfo.Custom(own, "its own ControlTemplate")
                : TemplateInfo.Unknown("has a Template this tool cannot read");

        // 2. 明寫的樣式
        var styleRaw = element.Attribute("Style")?.Value;
        if (styleRaw is not null)
        {
            var key = ResourceKeyOf(styleRaw);
            if (key is null) return TemplateInfo.Unknown("has a Style this tool cannot read");
            if (StylesByKey(element, key, file) is not { } candidates)
                return TemplateInfo.Unknown($"is styled by '{key}', which is not defined in the project");
            return TemplateFrom(candidates, file, $"style '{key}'");
        }

        if (InlineStyle(element) is { } inline)
            return TemplateFrom([Lexical(inline, file)], file, "its inline Style");

        // 3. 隱含樣式
        return TemplateFrom(ImplicitFor(element, file), file, $"the implicit Style for {local}");
    }

    private TemplateInfo TemplateFrom(List<StyleRecord> candidates, string file, string what, HashSet<object>? seen = null)
    {
        if (candidates.Count == 0) return TemplateInfo.Default;

        seen ??= new HashSet<object>(ReferenceEqualityComparer.Instance);
        var results = candidates.Select(c => TemplateInChain(c, file, what, 0, Fork(seen))).ToList();
        return Combine(results, what);
    }

    /// <summary>同名的多個定義（各檔一份）合成一個答案：有任何一份看不到就是不知道；
    /// 否則把全部候選交給呼叫端，由它確認算出來的內距一不一致。</summary>
    private static TemplateInfo Combine(List<TemplateInfo> results, string what)
    {
        if (results.Count == 1) return results[0];
        if (results.FirstOrDefault(r => r.Kind == TemplateKind.Unknown) is { Kind: TemplateKind.Unknown } unknown) return unknown;
        if (results.All(r => r.Kind == TemplateKind.Default)) return TemplateInfo.Default;

        // 展開巢狀的候選（基底樣式自己也有好幾份時）
        var alternatives = new List<XElement?>();
        foreach (var r in results)
        {
            if (r.Alternatives is { } nested) alternatives.AddRange(nested);
            else alternatives.Add(r.Template);
        }

        var distinct = alternatives.Distinct(ReferenceEqualityComparer.Instance).Cast<XElement?>().ToList();
        if (distinct.Count == 1) return results.First(r => r.Kind == TemplateKind.Custom);

        var first = results.First(r => r.Kind == TemplateKind.Custom);
        return new TemplateInfo(TemplateKind.Custom, first.Template, $"{what} (defined in {results.Count} places)")
        {
            Alternatives = distinct,
        };
    }

    private TemplateInfo TemplateInChain(StyleRecord r, string file, string what, int depth, HashSet<object> seen)
    {
        if (depth > 16) return TemplateInfo.Unknown($"{what} has a BasedOn chain that is too deep");
        if (!seen.Add(r.Origin ?? (object)r)) return TemplateInfo.Default;   // 繞回來了：這條路上沒有人換範本

        if (r.SetsTemplate)
        {
            if (r.Template is not null) return TemplateInfo.Custom(r.Template, what);
            if (r.TemplateKey is not null && FindTemplate(r.Origin, r.TemplateKey, r.Origin is null ? r.File : file) is { } keyed)
                return TemplateInfo.Custom(keyed, what);
            return TemplateInfo.Unknown($"{what} replaces the ControlTemplate with one this tool cannot read");
        }

        if (r.BasedOnUnreadable) return TemplateInfo.Unknown($"{what} has a BasedOn this tool cannot read");

        if (r.BasedOnKey is not null)
        {
            if (StylesByKey(r.Origin, r.BasedOnKey, r.Origin is null ? r.File : file) is not { } bases)
                return TemplateInfo.Unknown($"{what} is based on '{r.BasedOnKey}', which is not defined in the project");
            return Deeper(bases, file, what, depth, seen);
        }

        if (r.BasedOnType is not null)
        {
            return Deeper(ImplicitStyles(r.Origin, r.BasedOnType, file, self: r), file, what, depth, seen);
        }

        return TemplateInfo.Default;
    }

    private TemplateInfo Deeper(List<StyleRecord> bases, string file, string what, int depth, HashSet<object> seen)
    {
        if (bases.Count == 0) return TemplateInfo.Default;
        return Combine(bases.Select(b => TemplateInChain(b, file, what, depth + 1, Fork(seen))).ToList(), what);
    }

    private XElement? FindTemplate(XElement? from, string key, string file)
    {
        if (from is not null && FindLexical(from, key) is { } lexical)
            return lexical.Name.LocalName == "ControlTemplate" ? lexical : null;

        if (!_templates.TryGetValue(key, out var all)) return null;
        var candidates = SameFileFirst(all, t => t.File, file);
        return candidates.Count == 1 ? candidates[0].Template : null;
    }

    // ───────────────────────── 小工具 ─────────────────────────

    /// <summary>本地屬性。字型屬性另外認 TextElement.X／TextBlock.X／Control.X 這種附加寫法 ——
    /// `&lt;StackPanel TextElement.FontSize="14"&gt;` 是常見的設定繼承值的方式。</summary>
    private static string? LocalAttribute(XElement el, string property)
    {
        if (el.Attribute(property)?.Value is { } direct && !string.IsNullOrWhiteSpace(direct)) return direct;

        if (FontProperties.Contains(property))
        {
            foreach (var owner in (ReadOnlySpan<string>)["TextElement.", "TextBlock.", "Control."])
                if (el.Attribute(owner + property)?.Value is { } attached && !string.IsNullOrWhiteSpace(attached))
                    return attached;
        }

        return null;
    }

    private static string FileOf(XElement element) =>
        element.Document?.Annotation<SourceFile>()?.Path ?? "";

    /// <summary>{StaticResource K} / {DynamicResource K} / {ThemeResource K} 的 K。其他寫法回 null。</summary>
    internal static string? ResourceKeyOf(string? markup)
    {
        if (string.IsNullOrWhiteSpace(markup)) return null;
        var t = markup.Trim();
        if (!t.StartsWith('{') || !t.EndsWith('}')) return null;
        var inner = t[1..^1].Trim();
        foreach (var prefix in (ReadOnlySpan<string>)["StaticResource", "DynamicResource", "ThemeResource"])
        {
            if (!inner.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rest = inner[prefix.Length..].Trim();
            if (rest.StartsWith("ResourceKey=", StringComparison.Ordinal)) rest = rest["ResourceKey=".Length..].Trim();
            if (rest.Length == 0 || rest.StartsWith('{')) return null;   // {StaticResource {x:Type …}} 之類
            var end = rest.IndexOfAny([' ', ',']);
            return end < 0 ? rest : rest[..end];
        }
        return null;
    }

    /// <summary>TargetType="{x:Type Button}" 與 TargetType="Button" 是同一件事。</summary>
    internal static string? NormalizeType(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        var t = v.Trim();
        if (t.StartsWith('{'))
        {
            if (!t.EndsWith('}')) return null;
            var inner = t[1..^1].Trim();
            if (!inner.StartsWith("x:Type", StringComparison.Ordinal)) return null;
            t = inner["x:Type".Length..].Trim();
        }
        var colon = t.LastIndexOf(':');
        return colon >= 0 ? t[(colon + 1)..] : t;
    }
}
