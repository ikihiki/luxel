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

namespace Luxel.UI.Generators;

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
public sealed class WidgetDebugGenerator : IIncrementalGenerator
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

    private static readonly DiagnosticDescriptor ReactiveLayoutUtility = new(
        "NGUI003", "reactive layout utility is not supported",
        "Layout utility '{0}' cannot use a reactive value until layout invalidation is supported",
        "Luxel.UI", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor StatefulLayoutUtility = new(
        "NGUI004", "layout utility cannot target visual state",
        "Layout utility '{0}' cannot be applied inside '{1}' until state-driven layout invalidation is supported",
        "Luxel.UI", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor UtilityTargetMismatch = new(
        "NGUI005", "utility target does not match component",
        "Utility '{0}' targets '{1}' and cannot be applied to component '{2}'",
        "Luxel.UI", DiagnosticSeverity.Error, true);

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

        var options = context.CompilationProvider.Select(static (c, _) =>
        {
            string? factoryDefault = null;
            foreach (AttributeData a in c.Assembly.GetAttributes())
                if (a.AttributeClass?.ToDisplayString() == "Luxel.UI.UiFactoryDefaultsAttribute"
                    && a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string s)
                    factoryDefault = s;
            return (AssemblyName: c.AssemblyName ?? "Assembly", FactoryDefault: factoryDefault ?? "Factories");
        });

        var reactiveLayoutUtilities = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (ctx, ct) => FindReactiveLayoutUtility(ctx, ct))
            .Where(static diagnostic => diagnostic is not null);
        context.RegisterSourceOutput(reactiveLayoutUtilities,
            static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic!));

        var statefulLayoutUtilities = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (ctx, ct) => FindStatefulLayoutUtility(ctx, ct))
            .Where(static diagnostic => diagnostic is not null);
        context.RegisterSourceOutput(statefulLayoutUtilities,
            static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic!));

        var utilityTargetMismatches = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (ctx, ct) => FindUtilityTargetMismatch(ctx, ct))
            .Where(static diagnostic => diagnostic is not null);
        context.RegisterSourceOutput(utilityTargetMismatches,
            static (spc, diagnostic) => spc.ReportDiagnostic(diagnostic!));

        context.RegisterSourceOutput(widgets.Combine(options),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right.AssemblyName, pair.Right.FactoryDefault));
    }

    private static Diagnostic? FindReactiveLayoutUtility(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (invocation.ArgumentList.Arguments.Count == 0) return null;
        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol method)
            return null;
        if (method.ReturnType.ToDisplayString() != "Luxel.UI.U") return null;
        if (!IsLayoutUtility(method.Name)) return null;

        ExpressionSyntax value = invocation.ArgumentList.Arguments[0].Expression;
        ITypeSymbol? sourceType = context.SemanticModel.GetTypeInfo(value, cancellationToken).Type;
        bool signal = sourceType is INamedTypeSymbol named
            && named.Name == "Signal"
            && named.Arity == 1
            && named.ContainingNamespace.ToDisplayString() == "Luxel.UI";
        bool reactiveBind = value is InvocationExpressionSyntax bindInvocation
            && context.SemanticModel.GetSymbolInfo(bindInvocation, cancellationToken).Symbol is IMethodSymbol bindMethod
            && bindMethod.Name == "From"
            && bindMethod.ContainingType.ToDisplayString() == "Luxel.UI.Bind";
        if (!signal && !reactiveBind) return null;

        return Diagnostic.Create(ReactiveLayoutUtility, value.GetLocation(), method.Name);
    }

    private static Diagnostic? FindUtilityTargetMismatch(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol componentFactory)
            return null;
        if (!IsWidgetType(componentFactory.ReturnType)) return null;

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            if (argument.NameColon?.Name.Identifier.ValueText != "utilities"
                || argument.Expression is not CollectionExpressionSyntax collection)
                continue;
            foreach (CollectionElementSyntax element in collection.Elements)
            {
                if (element is not ExpressionElementSyntax expressionElement
                    || expressionElement.Expression is not InvocationExpressionSyntax utilityInvocation
                    || context.SemanticModel.GetSymbolInfo(utilityInvocation, cancellationToken).Symbol is not IMethodSymbol utilityMethod)
                    continue;
                AttributeData? targetAttribute = utilityMethod.GetAttributes().FirstOrDefault(static attribute =>
                    attribute.AttributeClass?.ToDisplayString() == "Luxel.UI.UtilityTargetAttribute");
                if (targetAttribute is null || targetAttribute.ConstructorArguments.Length != 1
                    || targetAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol targetType) continue;
                Conversion conversion = context.SemanticModel.Compilation.ClassifyConversion(componentFactory.ReturnType, targetType);
                if (conversion.IsImplicit) continue;
                return Diagnostic.Create(UtilityTargetMismatch, utilityInvocation.GetLocation(),
                    utilityMethod.Name, targetType.Name, componentFactory.ReturnType.Name);
            }
        }
        return null;
    }

    private static bool IsWidgetType(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is INamedTypeSymbol named; current = named.BaseType)
            if (current.ToDisplayString() == WidgetTypeName) return true;
        return false;
    }

    private static Diagnostic? FindStatefulLayoutUtility(GeneratorSyntaxContext context, CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol stateMethod)
            return null;
        if (stateMethod.ReturnType.ToDisplayString() != "Luxel.UI.U") return null;
        if (stateMethod.Name is not ("When" or "Hover" or "Pressed" or "Focused" or "Disabled")) return null;

        int utilitiesIndex = stateMethod.Name == "When" ? 1 : 0;
        if (invocation.ArgumentList.Arguments.Count <= utilitiesIndex) return null;
        ExpressionSyntax utilities = invocation.ArgumentList.Arguments[utilitiesIndex].Expression;
        if (utilities is not CollectionExpressionSyntax collection) return null;

        foreach (CollectionElementSyntax element in collection.Elements)
        {
            if (element is not ExpressionElementSyntax expressionElement
                || expressionElement.Expression is not InvocationExpressionSyntax nested
                || context.SemanticModel.GetSymbolInfo(nested, cancellationToken).Symbol is not IMethodSymbol utilityMethod
                || utilityMethod.ReturnType.ToDisplayString() != "Luxel.UI.U"
                || !IsLayoutUtility(utilityMethod.Name))
                continue;
            return Diagnostic.Create(StatefulLayoutUtility, nested.GetLocation(), utilityMethod.Name, stateMethod.Name);
        }
        return null;
    }

    private static bool IsLayoutUtility(string name)
        => name is "Width" or "Height" or "Margin" or "Padding";

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
                        pt.TypeArguments[0].ToDisplayString(TypeFmt), pHint, pStateable,
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
                        pvt.TypeArguments[0].ToDisplayString(TypeFmt), pvHint, pvStateable,
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
                        args[i] = et.TypeArguments[i].ToDisplayString(TypeFmt);
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
                    arg.ToDisplayString(TypeFmt), enumHint, stateable,
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

    private static void Emit(SourceProductionContext spc, ImmutableArray<WidgetModel?> models,
        string assemblyName, string factoryDefault)
    {
        var seen = new HashSet<string>();
        var list = new List<WidgetModel>();
        foreach (WidgetModel? m in models)
            if (m is not null && seen.Add(m.TypeFq)) list.Add(m);
        if (list.Count == 0) return;
        list.Sort(static (a, b) => string.CompareOrdinal(a.TypeFq, b.TypeFq));

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Luxel.UI.Generators.WidgetDebugGenerator");
        sb.AppendLine("#nullable enable");
        // 派生が基底と同名のパラメータ (_height 等) を再宣言した場合、生成プロパティが基底を隠す — 意図的
        sb.AppendLine("#pragma warning disable CS0108, CS0114");

        // Gallery-neutral component metadata is emitted into production assemblies. Gallery-side
        // generators consume these assembly attributes from referenced production assemblies.
        EmitComponentMetadata(sb, list, assemblyName, factoryDefault);

        // ---- (1) SetProp + デバッグ焼き込み (partial override) ----
        foreach (WidgetModel w in list)
        {
            if (w.Fields.Length == 0 && w.Events.Length == 0) continue;
            if (!w.IsPartial)
            {
                if (w.DeclaresOwnParams || w.IsComponent)
                    spc.ReportDiagnostic(Diagnostic.Create(NotPartial, Location.None, w.TypeFq));
                continue;
            }
            EmitWidgetPartial(sb, w);
        }

        // ---- (2) ファクトリ ----
        var groups = new Dictionary<(string Ns, string Factory), List<WidgetModel>>();
        foreach (WidgetModel w in list)
        {
            if (!w.IsComponent) continue;
            if (w.HasOwnCtor)
                spc.ReportDiagnostic(Diagnostic.Create(OwnCtor, Location.None, w.TypeFq));
            (string, string) key = (w.Namespace, w.FactoryClass ?? factoryDefault);
            if (!groups.TryGetValue(key, out List<WidgetModel>? g)) groups[key] = g = new List<WidgetModel>();
            g.Add(w);
        }
        foreach (KeyValuePair<(string Ns, string Factory), List<WidgetModel>> g in
                 groups.OrderBy(static kv => kv.Key.Ns, StringComparer.Ordinal)
                       .ThenBy(static kv => kv.Key.Factory, StringComparer.Ordinal))
            EmitFactoryClass(sb, g.Key.Ns, g.Key.Factory, g.Value);

        // Raw XML summaries remain available outside Gallery. Gallery-side generated registration
        // replaces these entries with localized values through RegisterLocalized.
        EmitControlApi(sb, list, assemblyName);

        spc.AddSource("LuxelUiComponents.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static void EmitComponentMetadata(StringBuilder sb, List<WidgetModel> list, string assemblyName, string factoryDefault)
    {
        foreach (WidgetModel w in list.Where(static value => value.IsComponent).OrderBy(static value => value.FactoryName, StringComparer.Ordinal))
        {
            string factoryClass = w.FactoryClass ?? factoryDefault;
            sb.Append("[assembly: global::Luxel.UI.GeneratedComponentMetadata(typeof(").Append(w.TypeFq).Append("), ")
              .Append(Lit(assemblyName)).Append(", ").Append(Lit(w.ClassName)).Append(", ").Append(Lit(w.Namespace)).Append(", ")
              .Append(Lit(factoryClass)).Append(", ").Append(Lit(w.FactoryName)).Append(", ")
              .Append(Lit(w.DocSummary)).AppendLine(")] ");
            foreach (FieldModel f in w.Fields)
            {
                sb.Append("[assembly: global::Luxel.UI.GeneratedComponentParameterMetadata(typeof(").Append(w.TypeFq)
                  .Append("), ").Append(Lit(f.Name)).Append(", typeof(").Append(RuntimeType(f.TypeFq)).Append("), ")
                  .Append(Lit(f.Kind.ToString())).Append(", ").Append(Lit(f.EnumHint)).Append(", ")
                  .Append(f.Own ? "true" : "false").Append(", ").Append(Lit(f.Summary)).Append(", ")
                  .Append(f.Seq).AppendLine(")] ");
            }
            foreach (EventModel e in w.Events)
            {
                sb.Append("[assembly: global::Luxel.UI.GeneratedComponentEventMetadata(typeof(").Append(w.TypeFq)
                  .Append("), ").Append(Lit(e.Name)).Append(", new global::System.Type[] { ")
                  .Append(string.Join(", ", e.ArgTypesFq.Select(static type => "typeof(" + RuntimeType(type) + ")")))
                  .Append(" }, ").Append(e.Own ? "true" : "false").Append(", ").Append(Lit(e.Summary))
                  .Append(", ").Append(e.Seq).AppendLine(")] ");
            }
        }
        sb.AppendLine();
    }

    private static void EmitControlApi(StringBuilder sb, List<WidgetModel> list, string assemblyName)
    {
        var comps = list.Where(static w => w.IsComponent).ToList();
        if (comps.Count == 0) return;
        comps.Sort(static (a, b) => string.CompareOrdinal(a.FactoryName, b.FactoryName));

        sb.AppendLine();
        sb.AppendLine("namespace Luxel.UI.Generated");
        sb.AppendLine("{");
        sb.Append("    internal static class ControlApiRegistration_").AppendLine(GeneratedIdentifier.Sanitize(assemblyName));
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        foreach (WidgetModel w in comps)
        {
            sb.Append("            global::Luxel.UI.ControlApiRegistry.Register(new global::Luxel.UI.ControlApi(")
              .Append(Lit(w.Namespace)).Append(", ").Append(Lit(w.ClassName == "StackPanel" ? "Stack" : w.ClassName)).Append(", ").Append(Lit(w.DocSummary))
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
                // Length は専用ヒント (メタデータ利用側が数値+単位コンボの LengthField を出す)
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
                ? OptionalReferenceType(f.TypeFq)
                : "global::Luxel.UI.Bindable<" + f.TypeFq + ">?";
            paramDecls.Add(paramType + " " + SafeName(ParamName(f.Name))
                + (f.TypeFq == LengthType ? " = default" : " = null"));
        }
        // Utility collection は全 factory 共通の末尾 optional parameter。
        // 先に適用し、その後 named [UiParam] を流すことで named parameter を常に優先する。
        paramDecls.Add("global::Luxel.UI.Utilities utilities = default");
        for (int i = 0; i < paramDecls.Count; i++)
        {
            sb.AppendLine();
            sb.Append(pad).Append("    ").Append(paramDecls[i]);
            if (i < paramDecls.Count - 1) sb.Append(',');
        }
        sb.AppendLine(")");
        sb.Append(pad).AppendLine("{");

        // パラメータなし internal ctor (生成) で作り、Utility → named [UiParam]/[UiEvent] の順に流し込む。
        sb.Append(pad).Append("    var w = new ").Append(w.TypeFq).AppendLine("();");
        sb.Append(pad).AppendLine("    utilities.ApplyTo(w);");

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

    private static string RuntimeType(string type)
        => type.EndsWith("?", StringComparison.Ordinal) ? type.Substring(0, type.Length - 1) : type;

    private static string OptionalReferenceType(string type)
        => type.EndsWith("?", StringComparison.Ordinal) ? type : type + "?";

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
