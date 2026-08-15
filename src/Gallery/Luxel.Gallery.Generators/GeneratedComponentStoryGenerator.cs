using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.Gallery.Generators;

/// <summary>
/// UI コンポーネントの「組み立て側」と「デバッグ/スタイル書込側」を焼き込むジェネレーター。
/// <list type="bullet">
/// <item><b>SetProp 焼き込み</b>: <c>[UiParam]</c> 付き public <c>Bindable&lt;T&gt;</c> フィールド
/// (継承分含む) を持つ partial な Widget 派生クラスへ <c>SetProp&lt;T&gt;</c> の override を生成
/// (名前ベースで任意プロパティを状態付きで書ける — knobs/DevTools が使う)。</item>
/// <item><b>デバッグ焼き込み</b>: 同クラスへ <c>DebugProps</c>/<c>SetDebugProp</c> の override を生成
/// (switch + <c>WidgetDebugCodec.Write&lt;T&gt;</c> / <c>WriteParsable&lt;T&gt;</c> / <c>Enum.TryParse</c>)。</item>
/// <item><b>ファクトリ生成</b>: <c>[UiComponent(Factory = "Kit")]</c> 付きクラスに対し、
/// 最長 public コンストラクタの引数 + [UiParam] フィールド (すべて <c>Bindable&lt;T&gt;</c>) を
/// 名前付き引数で受けるベアファクトリ関数を static partial class に生成
/// (状態レイヤ等の追加宣言は fluent 拡張 — When/Transition/GridColumn)。</item>
/// </list>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class GeneratedComponentStoryGenerator : IIncrementalGenerator
{
    private const string WidgetTypeName = "Luxel.UI.Widget";
    private const string UiNamespace = "Luxel.UI";
    private const string Codec = "global::Luxel.UI.WidgetDebugCodec";

    private static readonly SymbolDisplayFormat TypeFmt =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor NotPartial = new(
        "NGUI001", "widget must be partial",
        "'{0}' に [UiParam]/[UiComponent] がありますが partial でないため生成コードを焼き込めません。'partial' を追加してください",
        "Luxel.UI", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor OwnCtor = new(
        "NGUI002", "component should not declare constructors",
        "'{0}' は [UiComponent] ですが手書きコンストラクタがあります。パラメータなし internal ctor はジェネレーターが自動定義し、すべてのパラメータは [UiParam] 経由で渡してください (初期化は partial void OnConstruct() へ)",
        "Luxel.UI", DiagnosticSeverity.Warning, true);

    internal enum BindKind { Color, Int, Float, Double, Bool, String, Enum, Parsable, Other, Text /* BindableString */ }

    internal sealed class FieldModel : IEquatable<FieldModel>
    {
        public readonly string Name;      // 公開名 (プロパティ名 — private フィールドは _x → X に写す)
        public readonly BindKind Kind;
        public readonly bool IsReadOnly;
        public readonly string TypeFq;
        public readonly string EnumHint;
        public readonly bool Stateable;   // [UiParam(Stateable = true)] — When/{Class}Props に出す
        public readonly bool Own;         // 自身の型で宣言 (false = 基底 Widget の共通パラメータ)
        public readonly string Summary;   // /// summary (ControlApi 用)
        public readonly string SourceField;   // 標準形: private フィールド名 ("" = 旧 public 宣言 — アクセサ生成不要)
        public readonly int Seq;          // 宣言順 (ファクトリ引数の順序 — [UiEvent] と混在で保存)
        public FieldModel(string name, BindKind kind, bool isReadOnly, string typeFq, string enumHint, bool stateable,
                          bool own = false, string summary = "", string sourceField = "", int seq = 0)
        {
            Name = name; Kind = kind; IsReadOnly = isReadOnly; TypeFq = typeFq; EnumHint = enumHint; Stateable = stateable;
            Own = own; Summary = summary; SourceField = sourceField; Seq = seq;
        }
        public bool Equals(FieldModel? o) => o is not null && Name == o.Name && Kind == o.Kind
            && IsReadOnly == o.IsReadOnly && TypeFq == o.TypeFq && EnumHint == o.EnumHint && Stateable == o.Stateable
            && Own == o.Own && Summary == o.Summary && SourceField == o.SourceField && Seq == o.Seq;
        public override bool Equals(object? obj) => Equals(obj as FieldModel);
        public override int GetHashCode()
        { unchecked { return (((((((Name.GetHashCode() * 397 ^ (int)Kind) * 397 ^ TypeFq.GetHashCode()) * 397 ^ EnumHint.GetHashCode()) * 397 ^ Summary.GetHashCode()) * 397 ^ SourceField.GetHashCode()) * 397 ^ Seq) * 8 + (IsReadOnly ? 1 : 0)) + (Stateable ? 2 : 0) + (Own ? 4 : 0); } }
    }

    internal sealed class EventModel : IEquatable<EventModel>
    {
        public readonly string Name;
        public readonly string[] ArgTypesFq;   // 空 = 引数なし (UiEvent)
        public readonly bool Own;
        public readonly string Summary;
        public readonly int Seq;               // 宣言順 (ファクトリ引数の順序 — [UiParam] と混在で保存)
        public EventModel(string name, string[] argTypesFq, bool own = false, string summary = "", int seq = 0)
        { Name = name; ArgTypesFq = argTypesFq; Own = own; Summary = summary; Seq = seq; }
        public bool Equals(EventModel? o)
        {
            if (o is null || Name != o.Name || ArgTypesFq.Length != o.ArgTypesFq.Length
                || Own != o.Own || Summary != o.Summary || Seq != o.Seq) return false;
            for (int i = 0; i < ArgTypesFq.Length; i++) if (ArgTypesFq[i] != o.ArgTypesFq[i]) return false;
            return true;
        }
        public override bool Equals(object? obj) => Equals(obj as EventModel);
        public override int GetHashCode()
        { unchecked { int h = (Name.GetHashCode() * 397 ^ Summary.GetHashCode()) * 397 ^ Seq; foreach (string a in ArgTypesFq) h = h * 397 ^ a.GetHashCode(); return h * 2 + (Own ? 1 : 0); } }
    }

    internal sealed class WidgetModel : IEquatable<WidgetModel>
    {
        public readonly string TypeFq;
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly bool IsPartial;
        public readonly bool IsInternal;
        public readonly bool IsAbstract;          // 抽象基底 (Widget 等) — アクセサプロパティのみ生成
        public readonly bool DeclaresOwnParams;
        public readonly bool IsComponent;
        public readonly string? FactoryClass;
        public readonly string FactoryName;
        public readonly string DocSummary;
        public readonly FieldModel[] Fields;      // 自身 → 基底の順
        public readonly EventModel[] Events;      // [UiEvent] フィールド
        public readonly bool HasOwnCtor;          // 手書き ctor が残っている (パラメータなし ctor を生成しない)
        public WidgetModel(string typeFq, string ns, string className, bool isPartial, bool isInternal,
            bool isAbstract, bool declaresOwn, bool isComponent, string? factoryClass, string factoryName,
            string docSummary, FieldModel[] fields, EventModel[] events, bool hasOwnCtor)
        {
            TypeFq = typeFq; Namespace = ns; ClassName = className; IsPartial = isPartial; IsInternal = isInternal;
            IsAbstract = isAbstract;
            DeclaresOwnParams = declaresOwn; IsComponent = isComponent;
            FactoryClass = factoryClass; FactoryName = factoryName; DocSummary = docSummary; Fields = fields;
            Events = events; HasOwnCtor = hasOwnCtor;
        }
        public bool Equals(WidgetModel? o)
        {
            if (o is null || TypeFq != o.TypeFq || Namespace != o.Namespace || ClassName != o.ClassName
                || IsPartial != o.IsPartial || IsInternal != o.IsInternal || IsAbstract != o.IsAbstract
                || DeclaresOwnParams != o.DeclaresOwnParams || HasOwnCtor != o.HasOwnCtor
                || IsComponent != o.IsComponent || FactoryClass != o.FactoryClass || FactoryName != o.FactoryName
                || DocSummary != o.DocSummary || Fields.Length != o.Fields.Length
                || Events.Length != o.Events.Length) return false;
            for (int i = 0; i < Fields.Length; i++) if (!Fields[i].Equals(o.Fields[i])) return false;
            for (int i = 0; i < Events.Length; i++) if (!Events[i].Equals(o.Events[i])) return false;
            return true;
        }
        public override bool Equals(object? obj) => Equals(obj as WidgetModel);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = TypeFq.GetHashCode() * 2 + (HasOwnCtor ? 1 : 0);
                foreach (FieldModel f in Fields) h = h * 397 ^ f.GetHashCode();
                foreach (EventModel e in Events) h = h * 397 ^ e.GetHashCode();
                return h;
            }
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var widgets = context.SyntaxProvider.CreateSyntaxProvider(
                // BaseList なしも対象 (Widget 基底自身の [UiParam] private フィールドにアクセサを生成する)
                static (node, _) => node is ClassDeclarationSyntax,
                static (ctx, ct) => Transform(ctx, ct))
            .Where(static m => m is not null)
            .Collect();

        context.RegisterSourceOutput(widgets.Combine(context.CompilationProvider),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    // ---- 収集 ----

    private static WidgetModel? Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, ct) is not INamedTypeSymbol sym) return null;
        if (sym.IsStatic || sym.IsGenericType) return null;
        if (sym.ContainingType is not null) return null;   // nested 型は対象外
        if (sym.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)) return null;

        // Widget 派生 + Widget/抽象基底自身 (基底の [UiParam] private フィールドにもアクセサを生成する)
        bool isWidget = sym.ToDisplayString() == WidgetTypeName;
        for (INamedTypeSymbol? b = sym.BaseType; !isWidget && b is not null; b = b.BaseType)
            if (b.ToDisplayString() == WidgetTypeName) isWidget = true;
        if (!isWidget) return null;

        // [UiParam] プロパティ/フィールドと [UiEvent] フィールドを自身 → 基底の順で収集
        // (Widget 基底の共通プロパティも含む)。標準形は
        // `[UiParam] public Bindable<T> X { get; internal init; }` — 生成コードはゲッター経由で
        // SetBase/SetState を呼ぶので、フィールドとプロパティで出力は同一。
        var fields = new List<FieldModel>();
        var events = new List<EventModel>();
        var seenNames = new HashSet<string>();
        bool declaresOwn = false;
        int seq = 0;   // 宣言順 (自身 → 基底) — ファクトリ引数は [UiParam]/[UiEvent] の混在宣言順
        for (INamedTypeSymbol? t = sym; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (ISymbol m in t.GetMembers())
            {
                bool own = SymbolEqualityComparer.Default.Equals(t, sym);

                // [UiParam] プロパティ (標準形): public get 必須 (init/set は任意 — 生成コードは触らない)
                if (m is IPropertySymbol prop)
                {
                    if (prop.IsStatic || prop.IsIndexer || prop.IsImplicitlyDeclared) continue;
                    if (prop.DeclaredAccessibility != Accessibility.Public) continue;
                    if (prop.GetMethod is not { DeclaredAccessibility: Accessibility.Public }) continue;
                    if (!TryGetUiParam(prop, out bool pStateable)) continue;
                    if (prop.Type is not INamedTypeSymbol pt || pt.ContainingNamespace?.ToDisplayString() != UiNamespace) continue;
                    if (pt.Arity == 0 && pt.Name == "BindableString")
                    {
                        if (!seenNames.Add(prop.Name)) continue;
                        if (own) declaresOwn = true;
                        fields.Add(new FieldModel(prop.Name, BindKind.Text, true, "string", "", pStateable,
                            own, ExtractSummary(prop.GetDocumentationCommentXml(cancellationToken: ct)), "", seq++));
                        continue;
                    }
                    if (pt.Arity != 1 || pt.Name != "Bindable") continue;
                    if (!seenNames.Add(prop.Name)) continue;
                    if (own) declaresOwn = true;
                    (BindKind pKind, string pHint) = Classify(pt.TypeArguments[0]);
                    fields.Add(new FieldModel(prop.Name, pKind, true,
                        pt.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), pHint, pStateable,
                        own, ExtractSummary(prop.GetDocumentationCommentXml(cancellationToken: ct)), "", seq++));
                    continue;
                }

                if (m is not IFieldSymbol f) continue;
                if (f.IsStatic || f.IsConst || f.IsImplicitlyDeclared) continue;

                // [UiParam] private フィールド (標準形): `_x` → 公開プロパティ `X` を生成する
                // (public get / internal init — 宣言はフィールドのまま、公開面はジェネレーターが作る)
                if (f.DeclaredAccessibility == Accessibility.Private && TryGetUiParam(f, out bool pvStateable))
                {
                    if (f.Type is not INamedTypeSymbol pvt || pvt.ContainingNamespace?.ToDisplayString() != UiNamespace) continue;
                    string propName = PropNameOf(f.Name);
                    if (propName.Length == 0 || !seenNames.Add(propName)) continue;
                    if (own) declaresOwn = true;
                    string summary = ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct));
                    if (pvt.Arity == 0 && pvt.Name == "BindableString")
                    {
                        fields.Add(new FieldModel(propName, BindKind.Text, true, "string", "", pvStateable,
                            own, summary, f.Name, seq++));
                        continue;
                    }
                    if (pvt.Arity != 1 || pvt.Name != "Bindable") { seenNames.Remove(propName); continue; }
                    (BindKind pvKind, string pvHint) = Classify(pvt.TypeArguments[0]);
                    fields.Add(new FieldModel(propName, pvKind, true,
                        pvt.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), pvHint, pvStateable,
                        own, summary, f.Name, seq++));
                    continue;
                }

                if (f.DeclaredAccessibility != Accessibility.Public) continue;
                if (HasAttribute(f, "Luxel.UI.UiEventAttribute"))
                {
                    // [UiEvent]: UiEvent / UiEvent<T> / UiEvent<T1,T2> フィールド → ファクトリの Action? 引数
                    if (f.Type is not INamedTypeSymbol et || et.Name != "UiEvent"
                        || et.ContainingNamespace?.ToDisplayString() != UiNamespace) continue;
                    if (!seenNames.Add(f.Name)) continue;
                    if (own) declaresOwn = true;
                    var args = new string[et.Arity];
                    for (int i = 0; i < et.Arity; i++)
                        args[i] = et.TypeArguments[i].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    events.Add(new EventModel(f.Name, args, own,
                        ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct)), seq++));
                    continue;
                }
                if (!TryGetUiParam(f, out bool stateable)) continue;
                if (f.Type is not INamedTypeSymbol nt || nt.ContainingNamespace?.ToDisplayString() != UiNamespace) continue;

                if (nt.Arity == 0 && nt.Name == "BindableString")
                {
                    // BindableString も [UiParam] 対応 (文字列専用の Bindable)
                    if (!seenNames.Add(f.Name)) continue;
                    if (own) declaresOwn = true;
                    fields.Add(new FieldModel(f.Name, BindKind.Text, f.IsReadOnly, "string", "", stateable,
                        own, ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct)), "", seq++));
                    continue;
                }
                if (nt.Arity != 1 || nt.Name != "Bindable") continue;
                if (!seenNames.Add(f.Name)) continue;
                if (own) declaresOwn = true;

                ITypeSymbol arg = nt.TypeArguments[0];
                (BindKind kind, string enumHint) = Classify(arg);
                fields.Add(new FieldModel(f.Name, kind, f.IsReadOnly,
                    arg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), enumHint, stateable,
                    own, ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct)), "", seq++));
            }
        }

        bool isPartial = sym.DeclaringSyntaxReferences.Any(static r =>
            r.GetSyntax() is ClassDeclarationSyntax c && c.Modifiers.Any(SyntaxKind.PartialKeyword));

        bool isComponent = false;
        string? factoryClass = null, nameOverride = null;
        foreach (AttributeData a in sym.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != "Luxel.UI.UiComponentAttribute") continue;
            isComponent = true;
            foreach (KeyValuePair<string, TypedConstant> na in a.NamedArguments)
            {
                if (na.Key == "Factory" && na.Value.Value is string fc) factoryClass = fc;
                if (na.Key == "Name" && na.Value.Value is string nm) nameOverride = nm;
            }
        }

        // コンストラクタは収集しない — パラメータなし internal ctor をジェネレーターが自動定義し、
        // すべてのパラメータは [UiParam] 経由で渡る (手書き ctor が残っていれば生成をスキップする)
        bool hasOwnCtor = false;
        foreach (IMethodSymbol c in sym.InstanceConstructors)
            if (!c.IsImplicitlyDeclared) { hasOwnCtor = true; break; }

        return new WidgetModel(
            sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sym.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            sym.Name, isPartial, sym.DeclaredAccessibility == Accessibility.Internal, sym.IsAbstract,
            declaresOwn, isComponent, factoryClass, nameOverride ?? sym.Name,
            ExtractSummary(sym.GetDocumentationCommentXml(cancellationToken: ct)), fields.ToArray(), events.ToArray(),
            hasOwnCtor);
    }

    /// <summary>private フィールド名 → 公開プロパティ名 ("_fontSize" → "FontSize")。</summary>
    private static string PropNameOf(string field)
    {
        string n = field.TrimStart('_');
        return n.Length == 0 ? "" : char.ToUpperInvariant(n[0]) + n.Substring(1);
    }

    private static bool HasAttribute(ISymbol s, string fqName)
    {
        foreach (AttributeData a in s.GetAttributes())
            if (a.AttributeClass?.ToDisplayString() == fqName) return true;
        return false;
    }

    /// <summary>[UiParam] の有無と Stateable 名前付き引数を読む。</summary>
    private static bool TryGetUiParam(ISymbol s, out bool stateable)
    {
        stateable = false;
        foreach (AttributeData a in s.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() != "Luxel.UI.UiParamAttribute") continue;
            foreach (KeyValuePair<string, TypedConstant> na in a.NamedArguments)
                if (na.Key == "Stateable" && na.Value.Value is bool b) stateable = b;
            return true;
        }
        return false;
    }

    private static (BindKind, string) Classify(ITypeSymbol t)
    {
        switch (t.SpecialType)
        {
            case SpecialType.System_UInt32: return (BindKind.Color, "");
            case SpecialType.System_Int32: return (BindKind.Int, "");
            case SpecialType.System_Single: return (BindKind.Float, "");
            case SpecialType.System_Double: return (BindKind.Double, "");
            case SpecialType.System_Boolean: return (BindKind.Bool, "");
            case SpecialType.System_String: return (BindKind.String, "");
        }
        if (t.TypeKind == TypeKind.Enum)
        {
            var members = new List<string>();
            foreach (ISymbol m in t.GetMembers())
                if (m is IFieldSymbol ef && ef.HasConstantValue) members.Add(ef.Name);
            return (BindKind.Enum, "enum:" + string.Join("|", members));
        }
        foreach (INamedTypeSymbol i in t.AllInterfaces)
            if (i.OriginalDefinition.ToDisplayString() == "System.IParsable<TSelf>"
                && SymbolEqualityComparer.Default.Equals(i.TypeArguments[0], t))
                return (BindKind.Parsable, "");
        // Other の EnumHint は値型マーク ("vt") に流用 — ファクトリ引数を生の型にするか
        // (参照型 = 生の T?) Bindable にするか (値型) の判定に使う
        return (BindKind.Other, t.IsValueType ? "vt" : "");
    }

    private static string ExtractSummary(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        Match m = Regex.Match(xml!, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (!m.Success) return "";
        return CleanDoc(m.Groups[1].Value);
    }

    /// <summary>doc コメント本文の整形: see/c 等のタグを中身だけに落とし、空白を畳む。</summary>
    private static string CleanDoc(string body)
    {
        string s = Regex.Replace(body, @"<see\s+cref=""[A-Z]:([^""]*)""\s*/>", static m =>
        {
            string full = m.Groups[1].Value;
            int i = full.LastIndexOf('.');
            return i >= 0 ? full.Substring(i + 1) : full;
        });
        s = Regex.Replace(s, @"<[^>]+>", "");   // 残りのタグは剥がす (c, paramref, list...)
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>fully-qualified 型名を表示用の短い名前へ ("global::Luxel.UI.Align" → "Align")。</summary>
    private static string ShortType(string typeFq)
        => Regex.Replace(typeFq, @"(global::)?(?:[A-Za-z_][A-Za-z0-9_]*\.)+", "");

    // ---- 生成 ----

    private static void Emit(SourceProductionContext spc, ImmutableArray<WidgetModel?> models, Compilation compilation)
    {
        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/> Luxel.Gallery.Generators.GeneratedComponentStoryGenerator");
        source.AppendLine("#nullable enable");

        var seen = new HashSet<string>();
        var current = new List<WidgetModel>();
        foreach (WidgetModel? model in models)
            if (model is not null && seen.Add(model.TypeFq)) current.Add(model);
        if (current.Count > 0)
        {
            string factoryDefault = FactoryDefault(compilation.Assembly);
            current.Sort(static (a, b) => string.CompareOrdinal(a.TypeFq, b.TypeFq));
            EmitComponentStories(source, current, compilation.AssemblyName ?? "Assembly", factoryDefault);
        }

        foreach ((string assemblyName, List<WidgetModel> referenced) in ReadReferencedMetadata(compilation))
            EmitComponentStories(source, referenced, assemblyName, "Factories");

        if (source.Length > 100)
            spc.AddSource("LuxelGeneratedComponentStories.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static string FactoryDefault(IAssemblySymbol assembly)
    {
        foreach (AttributeData attribute in assembly.GetAttributes())
            if (attribute.AttributeClass?.ToDisplayString() == "Luxel.UI.UiFactoryDefaultsAttribute"
                && attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string value)
                return value;
        return "Factories";
    }

    private static IEnumerable<(string AssemblyName, List<WidgetModel> Models)> ReadReferencedMetadata(Compilation compilation)
    {
        const string componentAttribute = "Luxel.UI.GeneratedComponentMetadataAttribute";
        const string parameterAttribute = "Luxel.UI.GeneratedComponentParameterMetadataAttribute";
        const string eventAttribute = "Luxel.UI.GeneratedComponentEventMetadataAttribute";

        foreach (IAssemblySymbol assembly in compilation.SourceModule.ReferencedAssemblySymbols.OrderBy(static value => value.Name, StringComparer.Ordinal))
        {
            var components = new Dictionary<string, (INamedTypeSymbol Type, string Category, string Namespace, string FactoryClass, string FactoryMethod, string Summary)>();
            var fields = new Dictionary<string, List<FieldModel>>();
            var events = new Dictionary<string, List<EventModel>>();
            foreach (AttributeData attribute in assembly.GetAttributes())
            {
                string? name = attribute.AttributeClass?.ToDisplayString();
                ImmutableArray<TypedConstant> args = attribute.ConstructorArguments;
                if (name == componentAttribute && args.Length == 6 && args[0].Value is INamedTypeSymbol component)
                {
                    string key = component.ToDisplayString(TypeFmt);
                    components[key] = (component, (string)args[1].Value!, (string)args[2].Value!,
                        (string)args[3].Value!, (string)args[4].Value!, (string)args[5].Value!);
                }
                else if (name == parameterAttribute && args.Length == 8 && args[0].Value is INamedTypeSymbol owner
                    && args[2].Value is ITypeSymbol valueType && Enum.TryParse((string?)args[3].Value, out BindKind kind))
                {
                    string key = owner.ToDisplayString(TypeFmt);
                    if (!fields.TryGetValue(key, out List<FieldModel>? list)) fields[key] = list = new List<FieldModel>();
                    list.Add(new FieldModel((string)args[1].Value!, kind, true, valueType.ToDisplayString(TypeFmt),
                        (string)args[4].Value!, (bool)args[5].Value!, (bool)args[5].Value!,
                        (string)args[6].Value!, "", (int)args[7].Value!));
                }
                else if (name == eventAttribute && args.Length == 6 && args[0].Value is INamedTypeSymbol eventOwner)
                {
                    string key = eventOwner.ToDisplayString(TypeFmt);
                    if (!events.TryGetValue(key, out List<EventModel>? list)) events[key] = list = new List<EventModel>();
                    string[] argumentTypes = args[2].Values.Select(static value => ((ITypeSymbol)value.Value!).ToDisplayString(TypeFmt)).ToArray();
                    list.Add(new EventModel((string)args[1].Value!, argumentTypes, (bool)args[3].Value!,
                        (string)args[4].Value!, (int)args[5].Value!));
                }
            }
            if (components.Count == 0) continue;

            var models = new List<WidgetModel>();
            foreach (KeyValuePair<string, (INamedTypeSymbol Type, string Category, string Namespace, string FactoryClass, string FactoryMethod, string Summary)> pair
                     in components.OrderBy(static value => value.Key, StringComparer.Ordinal))
            {
                string key = pair.Key;
                var metadata = pair.Value;
                FieldModel[] componentFields = fields.TryGetValue(key, out List<FieldModel>? fieldList)
                    ? fieldList.OrderBy(static value => value.Seq).ToArray() : Array.Empty<FieldModel>();
                EventModel[] componentEvents = events.TryGetValue(key, out List<EventModel>? eventList)
                    ? eventList.OrderBy(static value => value.Seq).ToArray() : Array.Empty<EventModel>();
                models.Add(new WidgetModel(key, metadata.Namespace, metadata.Type.Name, true,
                    metadata.Type.DeclaredAccessibility == Accessibility.Internal, false, true, true,
                    metadata.FactoryClass, metadata.FactoryMethod, metadata.Summary, componentFields, componentEvents, false));
            }
            yield return (assembly.Name, models);
        }
    }

    private static void EmitComponentStories(StringBuilder sb, List<WidgetModel> list, string assemblyName, string factoryDefault)
    {
        List<WidgetModel> components = list.Where(static widget => widget.IsComponent).OrderBy(static widget => widget.FactoryName, StringComparer.Ordinal).ToList();
        if (components.Count == 0) return;
        string registration = "ComponentStoryRegistration_" + GeneratedIdentifier.Sanitize(assemblyName);
        sb.AppendLine();
        sb.AppendLine("namespace Luxel.Gallery.Generated");
        sb.AppendLine("{");
        sb.Append("    public static class ").AppendLine(registration);
        sb.AppendLine("    {");
        sb.AppendLine("        public const int ComponentCount = " + components.Count + ";");
        sb.AppendLine("        public static global::System.Collections.Generic.IReadOnlyList<global::Luxel.Gallery.GeneratedComponentStoryDescriptor> Descriptors { get; } =");
        sb.AppendLine("        [");
        foreach (WidgetModel widget in components)
        {
            string category = widget.FactoryName;
            sb.Append("            new global::Luxel.Gallery.GeneratedComponentStoryDescriptor(")
                .Append(Lit(widget.TypeFq)).Append(", ").Append(Lit(category)).Append(", ")
                .Append(Lit("Controls/" + category + "/Overview")).Append(", ")
                .Append(Lit("Controls/" + category + "/Basic")).AppendLine("),");
        }
        sb.AppendLine("        ];");
        sb.AppendLine();
        sb.AppendLine("        public static void Register(global::Luxel.Gallery.StoryCatalogBuilder builder)");
        sb.AppendLine("        {");
        sb.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(builder);");
        for (int index = 0; index < components.Count; index++)
        {
            WidgetModel widget = components[index];
            string category = widget.FactoryName;
            sb.Append("            builder.Add(new global::Luxel.Gallery.StoryInfo(").Append(Lit("Controls/" + category + "/Overview"))
                .Append(", static _ => throw new global::System.InvalidOperationException(\"Overview is Markdown. Use BuildResult.\"), Source: ")
                .Append(Lit("Generated overview for " + widget.TypeFq)).Append(", ResultBuild: static _ => Overview_").Append(index).Append("()")
                .Append(", RegistrationKind: global::Luxel.Gallery.StoryRegistrationKind.GeneratedComponentFallback, ProductionComponent: Descriptors[").Append(index).AppendLine("]));");
            sb.Append("            builder.Add(new global::Luxel.Gallery.StoryInfo(").Append(Lit("Controls/" + category + "/Basic"))
                .Append(", static ctx => Basic_").Append(index).Append("(ctx), Source: ")
                .Append(Lit("Generated direct typed factory for " + widget.TypeFq)).Append(", ArgDefinitions: Args_")
                .Append(index).Append(", CapabilityNote: ").Append(Lit(CapabilityNote(widget)))
                .Append(", RegistrationKind: global::Luxel.Gallery.StoryRegistrationKind.GeneratedComponentFallback, ProductionComponent: Descriptors[").Append(index).AppendLine("]));");
        }
        sb.AppendLine("        }");
        for (int index = 0; index < components.Count; index++)
        {
            WidgetModel widget = components[index];
            List<FieldModel> args = RequiresFallback(widget) ? new List<FieldModel>() : StoryArgs(widget).ToList();
            sb.AppendLine();
            sb.Append("        private static readonly global::Luxel.Gallery.StoryArgDefinition[] Args_").Append(index).AppendLine(" =");
            sb.AppendLine("        [");
            foreach (FieldModel field in args)
                EmitStaticArgDefinition(sb, field);
            sb.AppendLine("        ];");
            sb.AppendLine();
            EmitGeneratedOverview(sb, widget, args, index);
            sb.AppendLine();
            EmitGeneratedBasic(sb, widget, args, index, factoryDefault);
        }
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static bool RequiresFallback(WidgetModel widget)
        => widget.Fields.Any(field => field.Own && field.Kind == BindKind.Other && StoryFixture(field) is null);

    private static string? StoryFixture(FieldModel field)
    {
        if (field.Kind != BindKind.Other) return null;
        const string signalPrefix = "global::Luxel.UI.Signal<";
        if (field.TypeFq.StartsWith(signalPrefix, StringComparison.Ordinal) && field.TypeFq.EndsWith(">", StringComparison.Ordinal))
        {
            string valueType = field.TypeFq.Substring(signalPrefix.Length, field.TypeFq.Length - signalPrefix.Length - 1);
            string? value = valueType switch
            {
                "bool" or "global::System.Boolean" => "false",
                "int" or "global::System.Int32" => "0",
                "float" or "global::System.Single" => "0f",
                "double" or "global::System.Double" => "0d",
                "uint" or "global::System.UInt32" => "0xffdbeafeu",
                "string" or "global::System.String" => Lit("Sample"),
                LengthType => "default(global::Luxel.UI.Length)",
                _ => null,
            };
            return value is null ? null : "new " + field.TypeFq + "(" + value + ")";
        }
        if (field.TypeFq == "global::Luxel.UI.Widget")
            return "new global::Luxel.Gallery.StoryCapabilityFallback(\"Child fixture\", \"Generated in-memory child widget.\")";
        if (field.TypeFq is "string[]" or "global::System.String[]")
            return "new string[] { \"One\", \"Two\", \"Three\" }";
        return null;
    }

    private static IEnumerable<FieldModel> StoryArgs(WidgetModel widget)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (FieldModel field in widget.Fields.OrderBy(static field => field.Own ? 0 : 1).ThenBy(static field => field.Seq))
        {
            if (!seen.Add(field.Name)) continue;
            if (field.TypeFq == LengthType || field.Kind is BindKind.Color or BindKind.Int or BindKind.Float or BindKind.Double or BindKind.Bool or BindKind.String or BindKind.Text or BindKind.Enum)
                yield return field;
        }
    }

    private static void EmitStaticArgDefinition(StringBuilder sb, FieldModel field)
    {
        string defaultValue = StoryDefault(field);
        sb.Append("            global::Luxel.Gallery.StoryArgDefinition.Create<").Append(field.TypeFq).Append(">(")
            .Append(Lit(ParamName(field.Name))).Append(", ").Append(Lit(StoryTypeHint(field))).Append(", ").Append(defaultValue)
            .Append(", description: ").Append(Lit(field.Summary.Length > 0 ? field.Summary : field.Own ? field.Name + " component parameter." : "Inherited layout parameter."));
        if (field.Kind == BindKind.Enum)
        {
            string[] names = field.EnumHint.StartsWith("enum:", StringComparison.Ordinal) ? field.EnumHint.Substring(5).Split('|') : Array.Empty<string>();
            sb.Append(", options: new string[] { ").Append(string.Join(", ", names.Select(Lit))).Append(" }");
        }
        sb.AppendLine("),");
    }

    private static void EmitGeneratedOverview(StringBuilder sb, WidgetModel widget, List<FieldModel> args, int index)
    {
        string category = widget.FactoryName;
        string summary = widget.DocSummary.Length > 0 ? widget.DocSummary : category + " is a production Luxel UI component.";
        string own = string.Join(", ", widget.Fields.Where(static field => field.Own).Select(static field => "`" + field.Name + "`").DefaultIfEmpty("No component-specific parameters"));
        string events = string.Join(", ", widget.Events.Where(static value => value.Own).Select(static value => "`" + value.Name + "`").DefaultIfEmpty("No declared events"));
        string exampleArgs = string.Join(", ", args.Where(static field => field.Own).Take(4).Select(field => ParamName(field.Name) + ": " + StoryDefault(field)));
        string example = widget.FactoryName + "(" + exampleArgs + ")";
        string markdown = "# " + category + "\n\n" + summary + "\n\n```luxel-story\n0\n```\n\n## Implementation\n\n```csharp\n" + example + "\n```\n\n## Patterns and variants\n\n- Basic typed factory usage\n- Representative editable scalar args\n- Inherited layout args: width, height, alignment and transforms when supported\n- Deterministic browser fixture/fallback for capability inputs\n\n## Events, parameters and API\n\n**Events:** " + events + "\n\n**Component parameters:** " + own + "\n\nSee `ControlApiRegistry` / the generated API table for the complete inherited API.\n";
        sb.Append("        private static global::Luxel.Gallery.StoryResult Overview_").Append(index).Append("() => global::Luxel.Gallery.StoryResult.FromMarkdown(")
            .Append(Lit(markdown)).Append(", global::Luxel.Gallery.StoryReference.To(").Append(Lit("Controls/" + category + "/Basic")).AppendLine("));");
    }

    private static void EmitGeneratedBasic(StringBuilder sb, WidgetModel widget, List<FieldModel> args, int index, string factoryDefault)
    {
        sb.Append("        private static global::Luxel.UI.Widget Basic_").Append(index).AppendLine("(global::Luxel.Gallery.StoryContext ctx)");
        sb.AppendLine("        {");
        if (RequiresFallback(widget))
        {
            string unsupported = string.Join(", ", widget.Fields.Where(field => field.Own && field.Kind == BindKind.Other && StoryFixture(field) is null).Select(field => field.Name));
            sb.Append("            return new global::Luxel.Gallery.StoryCapabilityFallback(").Append(Lit(widget.FactoryName)).Append(", ")
                .Append(Lit("Unsupported capability/constructor inputs use a deterministic fallback: " + unsupported + ".")).AppendLine(");");
            sb.AppendLine("        }");
            return;
        }
        for (int i = 0; i < args.Count; i++)
        {
            FieldModel field = args[i];
            sb.Append("            global::Luxel.UI.Signal<").Append(field.TypeFq).Append("> arg").Append(i).Append(" = ctx.Arg<")
                .Append(field.TypeFq).Append(">(").Append(Lit(ParamName(field.Name))).Append(", ").Append(StoryDefault(field))
                .Append(", new global::Luxel.Gallery.StoryArgOptions<").Append(field.TypeFq).Append("> { Description = ")
                .Append(Lit(field.Summary.Length > 0 ? field.Summary : field.Own ? field.Name + " component parameter." : "Inherited layout parameter."));
            if (field.Kind == BindKind.Parsable && field.TypeFq != LengthType)
                sb.Append(", Parser = static value => ").Append(field.TypeFq).Append(".Parse(global::Luxel.UI.WidgetDebugCodec.CoerceString(value), global::System.Globalization.CultureInfo.InvariantCulture)");
            sb.AppendLine(" });");
        }
        sb.AppendLine("            return new global::Luxel.Gallery.GeneratedComponentStoryPreview(() =>");
        sb.AppendLine("            {");
        string factory = "global::" + widget.Namespace + "." + (widget.FactoryClass ?? factoryDefault);
        sb.Append("                ").Append(widget.TypeFq).Append(" component = ").Append(factory).Append('.').Append(widget.FactoryName).Append('(');
        var values = new List<string>();
        for (int i = 0; i < args.Count; i++) values.Add(SafeName(ParamName(args[i].Name)) + ": arg" + i + ".Value");
        foreach (FieldModel field in widget.Fields.Where(static field => field.Own && field.Kind == BindKind.Other))
            if (StoryFixture(field) is { } fixture) values.Add(SafeName(ParamName(field.Name)) + ": " + fixture);
        foreach (EventModel evt in widget.Events.Where(static value => value.Own))
            values.Add(SafeName(ParamName(evt.Name)) + ": " + StoryEvent(evt, widget.FactoryName));
        sb.Append(string.Join(", ", values)).AppendLine(");");
        sb.AppendLine("                return component;");
        sb.AppendLine("            });");
        sb.AppendLine("        }");
    }

    private static string StoryEvent(EventModel evt, string category)
    {
        if (evt.ArgTypesFq.Length == 0) return "() => ctx.Log(" + Lit(category + "." + evt.Name) + ")";
        string parameters = string.Join(", ", Enumerable.Range(0, evt.ArgTypesFq.Length).Select(index => "_" + index));
        return "(" + parameters + ") => ctx.Log(" + Lit(category + "." + evt.Name) + ")";
    }

    private static string StoryDefault(FieldModel field)
    {
        if (field.TypeFq == LengthType) return field.Name switch { "Width" => "(global::Luxel.UI.Length)240", "Height" => "(global::Luxel.UI.Length)96", _ => "default(global::Luxel.UI.Length)" };
        if (field.Kind == BindKind.Color) return field.Name.Contains("Foreground", StringComparison.OrdinalIgnoreCase) ? "0xff202020u" : "0xffdbeafeu";
        if (field.Kind == BindKind.Int) return field.Name.Contains("Selected", StringComparison.OrdinalIgnoreCase) ? "0" : "1";
        if (field.Kind is BindKind.Float or BindKind.Double)
        {
            string suffix = field.Kind == BindKind.Float ? "f" : "d";
            if (field.Name.Contains("Opacity", StringComparison.OrdinalIgnoreCase) || field.Name.Contains("Scale", StringComparison.OrdinalIgnoreCase)) return "1" + suffix;
            if (field.Name.Contains("Font", StringComparison.OrdinalIgnoreCase)) return "16" + suffix;
            return "0" + suffix;
        }
        if (field.Kind == BindKind.Bool) return field.Name.Contains("Vertical", StringComparison.OrdinalIgnoreCase) ? "true" : "false";
        if (field.Kind is BindKind.String or BindKind.Text) return Lit(field.Name is "Text" or "Label" ? "Example" : field.Name.Contains("Placeholder", StringComparison.OrdinalIgnoreCase) ? "Type here" : "Sample");
        if (field.Kind == BindKind.Enum)
        {
            string name = field.EnumHint.StartsWith("enum:", StringComparison.Ordinal) ? field.EnumHint.Substring(5).Split('|').FirstOrDefault() ?? "0" : "0";
            return name == "0" ? "default(" + field.TypeFq + ")" : field.TypeFq + "." + name;
        }
        return "default(" + field.TypeFq + ")";
    }

    private static string StoryTypeHint(FieldModel field) => field.TypeFq == LengthType ? "length" : field.Kind switch
    {
        BindKind.Color => "color", BindKind.Int => "int", BindKind.Float or BindKind.Double => "float",
        BindKind.Bool => "bool", BindKind.Enum => field.EnumHint, _ => "string",
    };

    private static string CapabilityNote(WidgetModel widget)
    {
        string[] unsupported = widget.Fields.Where(static field => field.Own && field.Kind == BindKind.Other).Select(static field => field.Name).ToArray();
        if (RequiresFallback(widget))
            return "Deterministic explanatory fallback for unsupported capability inputs: " + string.Join(", ", unsupported.Where(name => widget.Fields.Any(field => field.Name == name && StoryFixture(field) is null))) + ".";
        return unsupported.Length == 0
            ? "Direct generated factory with browser-safe scalar args and generated event logging."
            : "Direct generated factory with deterministic in-memory fixtures for complex inputs: " + string.Join(", ", unsupported) + ".";
    }

    /// <summary>[UiComponent] 毎に ControlApiRegistry.Register を module initializer で焼き込む。
    /// メンバー順: ctor 引数 → イベント → 自身の [UiParam] → 基底 (共通) の [UiParam]。</summary>
    private static void EmitControlApi(StringBuilder sb, List<WidgetModel> list, string assemblyName)
    {
        var comps = list.Where(static w => w.IsComponent).ToList();
        if (comps.Count == 0) return;
        comps.Sort(static (a, b) => string.CompareOrdinal(a.FactoryName, b.FactoryName));

        sb.AppendLine();
        sb.AppendLine("namespace Luxel.Gallery.Generated");
        sb.AppendLine("{");
        sb.Append("    internal static class ControlApiRegistration_").AppendLine(GeneratedIdentifier.Sanitize(assemblyName));
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        foreach (WidgetModel w in comps)
        {
            sb.Append("            global::Luxel.UI.ControlApiRegistry.Register(new global::Luxel.UI.ControlApi(")
              .Append(Lit(w.Namespace)).Append(", ").Append(Lit(w.FactoryName)).Append(", ").Append(Lit(w.DocSummary))
              .AppendLine(", new global::Luxel.UI.ApiMember[] {");
            foreach (EventModel e in w.Events)
            {
                string type = e.ArgTypesFq.Length == 0
                    ? "UiEvent"
                    : "UiEvent<" + string.Join(", ", e.ArgTypesFq.Select(ShortType)) + ">";
                sb.Append("                new(").Append(Lit(e.Name)).Append(", ").Append(Lit(type))
                  .Append(", \"event\", ").Append(Lit(e.Summary))
                  .Append(", false, ").Append(e.Own ? "false" : "true").AppendLine("),");
            }
            foreach (FieldModel f in w.Fields.OrderBy(static f => f.Own ? 0 : 1))
                sb.Append("                new(").Append(Lit(f.Name)).Append(", ").Append(Lit(ShortType(f.TypeFq)))
                  .Append(", \"param\", ").Append(Lit(f.Summary)).Append(", ")
                  .Append(f.Stateable ? "true" : "false").Append(", ").Append(f.Own ? "false" : "true").AppendLine("),");
            sb.AppendLine("            }));");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
    }

    private static string Lit(string s) => SymbolDisplay.FormatLiteral(s, true);

    private static void EmitWidgetPartial(StringBuilder sb, WidgetModel w)
    {
        sb.AppendLine();
        OpenNamespace(sb, w.Namespace, out string pad);
        sb.Append(pad).Append("partial class ").AppendLine(w.ClassName);
        sb.Append(pad).AppendLine("{");

        // [UiParam] private フィールドの公開面 (標準形): 宣言はフィールドのまま、
        // 公開プロパティはここで生成 — 外部アセンブリは get only、構築 (同一アセンブリの
        // ファクトリ/テスト) は internal init。init は readonly フィールドにも書ける。
        // 生成プロパティにも [UiParam] を付ける — **参照アセンブリからは private フィールドが
        // 見えない (metadata は public のみ)** ため、継承先 (別アセンブリ) の収集は
        // このプロパティ経路で行われる (同一コンパイル内は自分の生成出力が見えないので二重収集しない)
        foreach (FieldModel f in w.Fields)
        {
            if (!f.Own || f.SourceField.Length == 0) continue;
            string propType = f.Kind == BindKind.Text
                ? "global::Luxel.UI.BindableString"
                : "global::Luxel.UI.Bindable<" + f.TypeFq + ">";
            if (f.Summary.Length > 0)
                sb.Append(pad).Append("    /// <summary>").Append(new System.Xml.Linq.XText(f.Summary).ToString()).AppendLine("</summary>");
            sb.Append(pad).Append("    [global::Luxel.UI.UiParam").Append(f.Stateable ? "(Stateable = true)" : "").AppendLine("]");
            sb.Append(pad).Append("    public ").Append(propType).Append(' ').Append(f.Name)
              .Append(" { get => ").Append(f.SourceField)
              .Append("; internal init => ").Append(f.SourceField).AppendLine(" = value; }");
        }

        // 抽象基底 (Widget 等) はアクセサのみ — SetProp/DebugProps 等の override は具象側が
        // 全フィールド (基底分含む) をまとめて生成する
        if (w.IsAbstract)
        {
            sb.Append(pad).AppendLine("}");
            CloseNamespace(sb, w.Namespace);
            return;
        }

        // パラメータなし internal ctor (自動定義) — すべてのパラメータは [UiParam] 経由で渡る。
        // 旧 ctor の初期化ロジックは partial void OnConstruct() へ (パラメータ非依存のみ —
        // パラメータ依存の初期化は初回 PerformLayout/Realize で遅延させる)
        if (w.IsComponent && !w.HasOwnCtor)
        {
            sb.Append(pad).AppendLine("    /// <summary>ファクトリ用 (生成) — すべてのパラメータは [UiParam] 経由。</summary>");
            sb.Append(pad).Append("    internal ").Append(w.ClassName).AppendLine("() { OnConstruct(); }");
            sb.AppendLine();
            sb.Append(pad).AppendLine("    /// <summary>生成 ctor から呼ばれる初期化フック (旧 ctor 本体の移設先)。</summary>");
            sb.Append(pad).AppendLine("    partial void OnConstruct();");
            sb.AppendLine();
        }

        // SetProp<T>: 名前ベースのプロパティ書込 (Tailwind utility / 状態レイヤ)。
        // フィールドは readonly Bindable — 差し替えず SetBase/SetState で中身を書く
        sb.Append(pad).AppendLine("    public override bool SetProp<T>(string name, global::Luxel.UI.WidgetState state, global::Luxel.UI.Bindable<T> value)");
        sb.Append(pad).AppendLine("    {");
        sb.Append(pad).AppendLine("        switch (name)");
        sb.Append(pad).AppendLine("        {");
        foreach (FieldModel f in w.Fields)
        {
            sb.Append(pad).Append("            case \"").Append(f.Name).Append("\": return global::Luxel.UI.PropWriter.Set(")
              .Append(f.Name).AppendLine(", state, value, this);");
        }
        sb.Append(pad).AppendLine("        }");
        sb.Append(pad).AppendLine("        return false;");
        sb.Append(pad).AppendLine("    }");
        sb.AppendLine();

        // DebugProps
        sb.Append(pad).AppendLine("    public override global::System.Collections.Generic.IEnumerable<global::Luxel.UI.DebugProp> DebugProps()");
        sb.Append(pad).AppendLine("        => new global::Luxel.UI.DebugProp[]");
        sb.Append(pad).AppendLine("        {");
        foreach (FieldModel f in w.Fields)
        {
            string access = f.Name + ".Get()";
            (string hint, string expr) = f.Kind switch
            {
                BindKind.Color => ("color", Codec + ".FormatColor(" + access + ")"),
                BindKind.Int => ("int", access + ".ToString()"),
                BindKind.Float => ("float", access + ".ToString()"),
                BindKind.Double => ("float", access + ".ToString()"),
                BindKind.Bool => ("bool", access + " ? \"true\" : \"false\""),
                BindKind.String => ("string", access + " ?? \"\""),
                BindKind.Text => ("string", access),   // BindableString.Get() は非 null
                BindKind.Enum => (f.EnumHint, access + ".ToString()"),
                // Length は専用ヒント (Gallery が数値+単位コンボの LengthField を出す)
                BindKind.Parsable => (f.TypeFq == LengthType ? "length" : "string", access + ".ToString() ?? \"\""),
                _ => ("string", Codec + ".FormatBoxed(" + access + ")"),
            };
            sb.Append(pad).Append("            new(\"").Append(f.Name).Append("\", \"").Append(hint)
              .Append("\", ").Append(expr).AppendLine("),");
        }
        sb.Append(pad).AppendLine("        };");
        sb.AppendLine();

        // When: 状態レイヤの型付き宣言 ([UiParam(Stateable = true)] のみ、引数名はファクトリと同一)。
        // トランジションは意図的に含めない — 対象プロパティ群の指定は fluent Transition 系で行う。
        var stateable = new List<FieldModel>();
        foreach (FieldModel f in w.Fields)
            if (f.Stateable) stateable.Add(f);
        if (stateable.Count > 0)
        {
            sb.Append(pad).AppendLine("    /// <summary>状態レイヤを積む (Tailwind の hover: 相当の型付き版)。");
            sb.Append(pad).AppendLine("    /// 引数はファクトリと同名 — 表示系 ([UiParam(Stateable = true)]) のみ。</summary>");
            sb.Append(pad).Append("    public ").Append(w.TypeFq).AppendLine(" When(global::Luxel.UI.WidgetState state,");
            for (int i = 0; i < stateable.Count; i++)
            {
                FieldModel f = stateable[i];
                string paramType = f.Kind == BindKind.Text
                    ? "global::Luxel.UI.BindableString"
                    : "global::Luxel.UI.Bindable<" + f.TypeFq + ">";
                sb.Append(pad).Append("        ").Append(paramType).Append("? ")
                  .Append(SafeName(ParamName(f.Name))).Append(" = null")
                  .AppendLine(i == stateable.Count - 1 ? ")" : ",");
            }
            sb.Append(pad).AppendLine("    {");
            foreach (FieldModel f in stateable)
            {
                string pn = SafeName(ParamName(f.Name));
                sb.Append(pad).Append("        if (").Append(pn).Append(" is not null) { if (state == global::Luxel.UI.WidgetState.Default) ")
                  .Append(f.Name).Append(".SetBase(").Append(pn).Append("); else ")
                  .Append(f.Name).Append(".SetState(state, ").Append(pn).AppendLine(", this); }");
            }
            sb.Append(pad).AppendLine("        return this;");
            sb.Append(pad).AppendLine("    }");
            sb.AppendLine();
        }

        // InvokeEvent: sender のみの [UiEvent] の名前発火 (テスト/リモート駆動用)
        var voidEvents = new List<EventModel>();
        foreach (EventModel e in w.Events)
            if (e.ArgTypesFq.Length == 1 && e.ArgTypesFq[0] == w.TypeFq) voidEvents.Add(e);
        if (voidEvents.Count > 0)
        {
            sb.Append(pad).AppendLine("    public override bool InvokeEvent(string name)");
            sb.Append(pad).AppendLine("    {");
            sb.Append(pad).AppendLine("        switch (name)");
            sb.Append(pad).AppendLine("        {");
            foreach (EventModel e in voidEvents)
                sb.Append(pad).Append("            case \"").Append(e.Name).Append("\": ")
                  .Append(e.Name).AppendLine(".Invoke(this); return true;");
            sb.Append(pad).AppendLine("        }");
            sb.Append(pad).AppendLine("        return false;");
            sb.Append(pad).AppendLine("    }");
            sb.AppendLine();
        }

        // SetDebugProp
        sb.Append(pad).AppendLine("    public override void SetDebugProp(string name, string type, global::System.Text.Json.JsonElement value)");
        sb.Append(pad).AppendLine("    {");
        sb.Append(pad).AppendLine("        switch (name)");
        sb.Append(pad).AppendLine("        {");
        foreach (FieldModel f in w.Fields)
        {
            if (f.Kind == BindKind.Other) continue;
            sb.Append(pad).Append("            case \"").Append(f.Name).Append("\": ");
            switch (f.Kind)
            {
                case BindKind.Enum:
                    sb.Append("if (global::System.Enum.TryParse<").Append(f.TypeFq).Append(">(")
                      .Append(Codec).Append(".CoerceString(value), true, out ").Append(f.TypeFq)
                      .Append(" __").Append(f.Name).Append(")) ").Append(f.Name)
                      .Append(".SetOverride(__").Append(f.Name).Append(");");
                    break;
                case BindKind.Parsable:
                    sb.Append(Codec).Append(".WriteParsable(").Append(f.Name).Append(", value);");
                    break;
                default:
                    sb.Append(Codec).Append(".Write(").Append(f.Name).Append(", value);");
                    break;
            }
            sb.AppendLine(" return;");
        }
        sb.Append(pad).AppendLine("        }");
        sb.Append(pad).AppendLine("    }");
        sb.Append(pad).AppendLine("}");

        // {Class}Props: stateable プロパティ名の定数 (fluent Transition の対象指定用 —
        // ファクトリ関数と型名の衝突 (CS0119) を避けるため partial 内でなく sibling クラスに出す)
        if (stateable.Count > 0)
        {
            sb.AppendLine();
            sb.Append(pad).Append("/// <summary>").Append(w.ClassName).AppendLine(" の状態可変プロパティ名 (Transition の対象指定用)。</summary>");
            sb.Append(pad).Append(w.IsInternal ? "internal" : "public").Append(" static class ")
              .Append(w.ClassName).AppendLine("Props");
            sb.Append(pad).AppendLine("{");
            foreach (FieldModel f in stateable)
                sb.Append(pad).Append("    public const string ").Append(f.Name)
                  .Append(" = \"").Append(f.Name).AppendLine("\";");
            sb.Append(pad).AppendLine("}");
        }
        CloseNamespace(sb, w.Namespace);
    }

    private static void EmitFactoryClass(StringBuilder sb, string ns, string factory, List<WidgetModel> widgets)
    {
        widgets.Sort(static (a, b) => string.CompareOrdinal(a.FactoryName, b.FactoryName));
        sb.AppendLine();
        OpenNamespace(sb, ns, out string pad);
        sb.Append(pad).Append("public static partial class ").AppendLine(factory);
        sb.Append(pad).AppendLine("{");
        bool first = true;
        foreach (WidgetModel w in widgets)
        {
            if (!first) sb.AppendLine();
            first = false;
            EmitFactoryMethod(sb, pad + "    ", w);
        }
        sb.Append(pad).AppendLine("}");
        CloseNamespace(sb, ns);
    }

    private static void EmitFactoryMethod(StringBuilder sb, string pad, WidgetModel w)
    {
        if (w.DocSummary.Length > 0)
            sb.Append(pad).Append("/// <summary>").Append(new System.Xml.Linq.XText(w.DocSummary).ToString()).AppendLine("</summary>");
        sb.Append(pad).Append(w.IsInternal ? "internal" : "public").Append(" static ").Append(w.TypeFq)
          .Append(' ').Append(w.FactoryName).Append('(');

        // 引数 = [UiParam]/[UiEvent] の**宣言順** (自身 → 基底)。位置引数の互換は宣言順が決める —
        // 旧 ctor 引数だったものはクラス先頭に宣言しておく。設定はすべて省略可能な名前付き引数
        var merged = new List<(int Seq, FieldModel? F, EventModel? E)>();
        foreach (FieldModel f in w.Fields) merged.Add((f.Seq, f, null));
        foreach (EventModel e in w.Events) merged.Add((e.Seq, null, e));
        merged.Sort(static (a, b) => a.Seq.CompareTo(b.Seq));

        var paramDecls = new List<string>();
        foreach ((int _, FieldModel? f, EventModel? e) in merged)
        {
            if (e is not null)
            {
                paramDecls.Add(ActionType(e) + "? " + SafeName(ParamName(e.Name)) + " = null");
                continue;
            }
            // Length は値型のまま受ける (int/float/string からの暗黙変換を 1 段に保つ)。
            // Other の参照型 (配列/リスト/Signal/デリゲート等) も生の型で受ける —
            // コレクション式 `[...]` やラムダの自然な型付けを保つ (Bindable へは SetBase の暗黙変換)
            string paramType = f!.Kind == BindKind.Text
                ? "global::Luxel.UI.BindableString?"
                : f.TypeFq == LengthType
                ? LengthType
                : f.Kind == BindKind.Other && f.EnumHint != "vt"
                ? f.TypeFq + "?"
                : "global::Luxel.UI.Bindable<" + f.TypeFq + ">?";
            paramDecls.Add(paramType + " " + SafeName(ParamName(f.Name))
                + (f.TypeFq == LengthType ? " = default" : " = null"));
        }
        for (int i = 0; i < paramDecls.Count; i++)
        {
            sb.AppendLine();
            sb.Append(pad).Append("    ").Append(paramDecls[i]);
            if (i < paramDecls.Count - 1) sb.Append(',');
        }
        sb.AppendLine(")");
        sb.Append(pad).AppendLine("{");

        // パラメータなし internal ctor (生成) で作り、すべて [UiParam]/[UiEvent] 経由で流し込む
        sb.Append(pad).Append("    var w = new ").Append(w.TypeFq).AppendLine("();");

        foreach ((int _, FieldModel? f, EventModel? e) in merged)
        {
            if (e is not null)
            {
                string en = SafeName(ParamName(e.Name));
                sb.Append(pad).Append("    if (").Append(en).Append(" is not null) w.")
                  .Append(e.Name).Append(" = ").Append(en).AppendLine(";");
                continue;
            }
            string pn = SafeName(ParamName(f!.Name));
            // Bindable は差し替えず SetBase で中身を書く (状態レイヤ/override 維持)。
            // Other の生受けは new Bindable<T>(値) で明示的に包む — T がインターフェイスだと
            // ユーザー定義暗黙変換 (T → Bindable<T>) が適用されないため
            string guard = f.TypeFq == LengthType ? pn + ".IsSet" : pn + " is not null";
            string arg = f.Kind == BindKind.Other && f.EnumHint != "vt" && f.TypeFq != LengthType
                ? "new global::Luxel.UI.Bindable<" + f.TypeFq + ">(" + pn + ")"
                : pn;
            sb.Append(pad).Append("    if (").Append(guard).Append(") w.")
              .Append(f.Name).Append(".SetBase(").Append(arg).AppendLine(");");
        }
        sb.Append(pad).AppendLine("    return w;");
        sb.Append(pad).AppendLine("}");
    }

    private const string LengthType = "global::Luxel.UI.Length";

    /// <summary>[UiEvent] のファクトリ引数型 (Action / Action&lt;T&gt; / Action&lt;T1,T2&gt;)。</summary>
    private static string ActionType(EventModel e)
        => e.ArgTypesFq.Length == 0
            ? "global::System.Action"
            : "global::System.Action<" + string.Join(", ", e.ArgTypesFq) + ">";

    private static string ParamName(string field) => char.ToLowerInvariant(field[0]) + field.Substring(1);

    private static string SafeName(string n)
        => SyntaxFacts.GetKeywordKind(n) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(n) != SyntaxKind.None
           ? "@" + n : n;

    private static void OpenNamespace(StringBuilder sb, string ns, out string pad)
    {
        if (ns.Length == 0) { pad = ""; return; }
        sb.Append("namespace ").AppendLine(ns);
        sb.AppendLine("{");
        pad = "    ";
    }

    private static void CloseNamespace(StringBuilder sb, string ns)
    {
        if (ns.Length > 0) sb.AppendLine("}");
    }
}
