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
/// (継承分含む) を持つ partial な Widget 派生クラスへ <c>SetProp&lt;T&gt;</c> の override を生成。
/// Tailwind utility (名前ベース PropPart) が任意プロパティを状態付きで書ける。</item>
/// <item><b>デバッグ焼き込み</b>: 同クラスへ <c>DebugProps</c>/<c>SetDebugProp</c> の override を生成
/// (switch + <c>WidgetDebugCodec.Write&lt;T&gt;</c> / <c>WriteParsable&lt;T&gt;</c> / <c>Enum.TryParse</c>)。</item>
/// <item><b>ファクトリ生成</b>: <c>[UiComponent(Factory = "Kit")]</c> 付きクラスに対し、
/// 最長 public コンストラクタの引数 + [UiParam] フィールド (すべて <c>Bindable&lt;T&gt;</c>) +
/// <c>params IConfigPart[]</c> を受けるベアファクトリ関数を static partial class に生成。</item>
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

    private static readonly DiagnosticDescriptor NoCtor = new(
        "NGUI002", "component needs accessible ctor",
        "'{0}' は [UiComponent] ですが public/internal コンストラクタが無いためファクトリを生成できません",
        "Luxel.UI", DiagnosticSeverity.Warning, true);

    internal enum BindKind { Color, Int, Float, Double, Bool, String, Enum, Parsable, Other, Text /* BindableString */ }

    internal sealed class FieldModel : IEquatable<FieldModel>
    {
        public readonly string Name;
        public readonly BindKind Kind;
        public readonly bool IsReadOnly;
        public readonly string TypeFq;
        public readonly string EnumHint;
        public readonly bool Stateable;   // [UiParam(Stateable = true)] — When/{Class}Props に出す
        public readonly bool Own;         // 自身の型で宣言 (false = 基底 Widget の共通パラメータ)
        public readonly string Summary;   // /// summary (ControlApi 用)
        public FieldModel(string name, BindKind kind, bool isReadOnly, string typeFq, string enumHint, bool stateable,
                          bool own = false, string summary = "")
        { Name = name; Kind = kind; IsReadOnly = isReadOnly; TypeFq = typeFq; EnumHint = enumHint; Stateable = stateable;
          Own = own; Summary = summary; }
        public bool Equals(FieldModel? o) => o is not null && Name == o.Name && Kind == o.Kind
            && IsReadOnly == o.IsReadOnly && TypeFq == o.TypeFq && EnumHint == o.EnumHint && Stateable == o.Stateable
            && Own == o.Own && Summary == o.Summary;
        public override bool Equals(object? obj) => Equals(obj as FieldModel);
        public override int GetHashCode()
        { unchecked { return (((((Name.GetHashCode() * 397 ^ (int)Kind) * 397 ^ TypeFq.GetHashCode()) * 397 ^ EnumHint.GetHashCode()) * 397 ^ Summary.GetHashCode()) * 8 + (IsReadOnly ? 1 : 0)) + (Stateable ? 2 : 0) + (Own ? 4 : 0); } }
    }

    internal sealed class EventModel : IEquatable<EventModel>
    {
        public readonly string Name;
        public readonly string[] ArgTypesFq;   // 空 = 引数なし (UiEvent)
        public readonly bool Own;
        public readonly string Summary;
        public EventModel(string name, string[] argTypesFq, bool own = false, string summary = "")
        { Name = name; ArgTypesFq = argTypesFq; Own = own; Summary = summary; }
        public bool Equals(EventModel? o)
        {
            if (o is null || Name != o.Name || ArgTypesFq.Length != o.ArgTypesFq.Length
                || Own != o.Own || Summary != o.Summary) return false;
            for (int i = 0; i < ArgTypesFq.Length; i++) if (ArgTypesFq[i] != o.ArgTypesFq[i]) return false;
            return true;
        }
        public override bool Equals(object? obj) => Equals(obj as EventModel);
        public override int GetHashCode()
        { unchecked { int h = Name.GetHashCode() * 397 ^ Summary.GetHashCode(); foreach (string a in ArgTypesFq) h = h * 397 ^ a.GetHashCode(); return h * 2 + (Own ? 1 : 0); } }
    }

    internal sealed class CtorParam : IEquatable<CtorParam>
    {
        public readonly string TypeFq;
        public readonly string Name;
        public readonly string? DefaultLiteral;   // null = 既定値なし
        public readonly string Summary;           // /// <param> (ControlApi 用)
        public CtorParam(string typeFq, string name, string? defaultLiteral, string summary = "")
        { TypeFq = typeFq; Name = name; DefaultLiteral = defaultLiteral; Summary = summary; }
        public bool Equals(CtorParam? o) => o is not null && TypeFq == o.TypeFq && Name == o.Name
            && DefaultLiteral == o.DefaultLiteral && Summary == o.Summary;
        public override bool Equals(object? obj) => Equals(obj as CtorParam);
        public override int GetHashCode()
        { unchecked { return ((TypeFq.GetHashCode() * 397 ^ Name.GetHashCode()) * 397 ^ (DefaultLiteral?.GetHashCode() ?? 0)) * 397 ^ Summary.GetHashCode(); } }
    }

    internal sealed class WidgetModel : IEquatable<WidgetModel>
    {
        public readonly string TypeFq;
        public readonly string Namespace;
        public readonly string ClassName;
        public readonly bool IsPartial;
        public readonly bool IsInternal;
        public readonly bool DeclaresOwnParams;
        public readonly bool IsComponent;
        public readonly string? FactoryClass;
        public readonly string FactoryName;
        public readonly string DocSummary;
        public readonly FieldModel[] Fields;      // 自身 → 基底の順
        public readonly EventModel[] Events;      // [UiEvent] フィールド
        public readonly CtorParam[]? Ctor;        // null = public ctor なし
        public WidgetModel(string typeFq, string ns, string className, bool isPartial, bool isInternal,
            bool declaresOwn, bool isComponent, string? factoryClass, string factoryName,
            string docSummary, FieldModel[] fields, EventModel[] events, CtorParam[]? ctor)
        {
            TypeFq = typeFq; Namespace = ns; ClassName = className; IsPartial = isPartial; IsInternal = isInternal;
            DeclaresOwnParams = declaresOwn; IsComponent = isComponent;
            FactoryClass = factoryClass; FactoryName = factoryName; DocSummary = docSummary; Fields = fields;
            Events = events; Ctor = ctor;
        }
        public bool Equals(WidgetModel? o)
        {
            if (o is null || TypeFq != o.TypeFq || Namespace != o.Namespace || ClassName != o.ClassName
                || IsPartial != o.IsPartial || IsInternal != o.IsInternal
                || DeclaresOwnParams != o.DeclaresOwnParams
                || IsComponent != o.IsComponent || FactoryClass != o.FactoryClass || FactoryName != o.FactoryName
                || DocSummary != o.DocSummary || Fields.Length != o.Fields.Length
                || Events.Length != o.Events.Length) return false;
            for (int i = 0; i < Fields.Length; i++) if (!Fields[i].Equals(o.Fields[i])) return false;
            for (int i = 0; i < Events.Length; i++) if (!Events[i].Equals(o.Events[i])) return false;
            if ((Ctor is null) != (o.Ctor is null)) return false;
            if (Ctor is not null && o.Ctor is not null)
            {
                if (Ctor.Length != o.Ctor.Length) return false;
                for (int i = 0; i < Ctor.Length; i++) if (!Ctor[i].Equals(o.Ctor[i])) return false;
            }
            return true;
        }
        public override bool Equals(object? obj) => Equals(obj as WidgetModel);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = TypeFq.GetHashCode();
                foreach (FieldModel f in Fields) h = h * 397 ^ f.GetHashCode();
                foreach (EventModel e in Events) h = h * 397 ^ e.GetHashCode();
                if (Ctor is not null) foreach (CtorParam p in Ctor) h = h * 397 ^ p.GetHashCode();
                return h;
            }
        }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var widgets = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { BaseList: not null },
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

        context.RegisterSourceOutput(widgets.Combine(options),
            static (spc, pair) => Emit(spc, pair.Left, pair.Right.AssemblyName, pair.Right.FactoryDefault));
    }

    // ---- 収集 ----

    private static WidgetModel? Transform(GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.SemanticModel.GetDeclaredSymbol((ClassDeclarationSyntax)ctx.Node, ct) is not INamedTypeSymbol sym) return null;
        if (sym.IsAbstract || sym.IsStatic || sym.IsGenericType) return null;
        if (sym.ContainingType is not null) return null;   // nested 型は対象外
        if (sym.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)) return null;

        bool isWidget = false;
        for (INamedTypeSymbol? b = sym.BaseType; b is not null; b = b.BaseType)
            if (b.ToDisplayString() == WidgetTypeName) { isWidget = true; break; }
        if (!isWidget) return null;

        // [UiParam]/[UiEvent] フィールドを自身 → 基底の順で収集 (Widget 基底の共通プロパティも含む)
        var fields = new List<FieldModel>();
        var events = new List<EventModel>();
        var seenNames = new HashSet<string>();
        bool declaresOwn = false;
        for (INamedTypeSymbol? t = sym; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
        {
            foreach (ISymbol m in t.GetMembers())
            {
                if (m is not IFieldSymbol f) continue;
                if (f.IsStatic || f.IsConst || f.IsImplicitlyDeclared) continue;
                if (f.DeclaredAccessibility != Accessibility.Public) continue;
                bool own = SymbolEqualityComparer.Default.Equals(t, sym);
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
                    events.Add(new EventModel(f.Name, args, own, ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct))));
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
                        own, ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct))));
                    continue;
                }
                if (nt.Arity != 1 || nt.Name != "Bindable") continue;
                if (!seenNames.Add(f.Name)) continue;
                if (own) declaresOwn = true;

                ITypeSymbol arg = nt.TypeArguments[0];
                (BindKind kind, string enumHint) = Classify(arg);
                fields.Add(new FieldModel(f.Name, kind, f.IsReadOnly,
                    arg.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), enumHint, stateable,
                    own, ExtractSummary(f.GetDocumentationCommentXml(cancellationToken: ct))));
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

        // 最長 public/internal コンストラクタの引数をファクトリ先頭引数に写す
        // (ファクトリは同一アセンブリに生成されるため internal ctor でよい — 外部からの直接 new を防ぐ)
        CtorParam[]? ctor = null;
        IMethodSymbol? best = null;
        foreach (IMethodSymbol c in sym.InstanceConstructors)
        {
            if (c.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal)) continue;
            if (HasAttribute(c, "Luxel.UI.UiCtorAttribute")) { best = c; break; }   // 明示指定が最優先
            if (best is null || c.Parameters.Length > best.Parameters.Length) best = c;
        }
        if (best is not null)
        {
            string ctorXml = best.GetDocumentationCommentXml(cancellationToken: ct) ?? "";
            var ps = new CtorParam[best.Parameters.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                IParameterSymbol p = best.Parameters[i];
                ps[i] = new CtorParam(p.Type.ToDisplayString(TypeFmt), p.Name,
                    p.HasExplicitDefaultValue ? FormatDefault(p) : null,
                    ExtractParamDoc(ctorXml, p.Name));
            }
            ctor = ps;
        }

        return new WidgetModel(
            sym.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sym.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : "",
            sym.Name, isPartial, sym.DeclaredAccessibility == Accessibility.Internal,
            declaresOwn, isComponent, factoryClass, nameOverride ?? sym.Name,
            ExtractSummary(sym.GetDocumentationCommentXml(cancellationToken: ct)), fields.ToArray(), events.ToArray(), ctor);
    }

    private static string FormatDefault(IParameterSymbol p)
    {
        object? v = p.ExplicitDefaultValue;
        if (v is null) return p.Type.IsValueType && p.Type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
            ? "default" : "null";
        if (p.Type.TypeKind == TypeKind.Enum)
            return "(" + p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + Convert.ToInt64(v, CultureInfo.InvariantCulture);
        return v switch
        {
            bool b => b ? "true" : "false",
            string s => SymbolDisplay.FormatLiteral(s, true),
            char c => SymbolDisplay.FormatLiteral(c, true),
            float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
            double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
            decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
            uint u => u.ToString(CultureInfo.InvariantCulture) + "u",
            long l => l.ToString(CultureInfo.InvariantCulture) + "L",
            ulong ul => ul.ToString(CultureInfo.InvariantCulture) + "UL",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "default",
        };
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
        return (BindKind.Other, "");
    }

    private static string ExtractSummary(string? xml)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        Match m = Regex.Match(xml!, @"<summary>(.*?)</summary>", RegexOptions.Singleline);
        if (!m.Success) return "";
        return CleanDoc(m.Groups[1].Value);
    }

    /// <summary>ctor XML doc から &lt;param name="..."&gt; の本文を取り出す (ControlApi 用)。</summary>
    private static string ExtractParamDoc(string? xml, string paramName)
    {
        if (string.IsNullOrEmpty(xml)) return "";
        Match m = Regex.Match(xml!, "<param name=\"" + Regex.Escape(paramName) + "\">(.*?)</param>", RegexOptions.Singleline);
        return m.Success ? CleanDoc(m.Groups[1].Value) : "";
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
            if (w.Ctor is null)
            {
                spc.ReportDiagnostic(Diagnostic.Create(NoCtor, Location.None, w.TypeFq));
                continue;
            }
            (string, string) key = (w.Namespace, w.FactoryClass ?? factoryDefault);
            if (!groups.TryGetValue(key, out List<WidgetModel>? g)) groups[key] = g = new List<WidgetModel>();
            g.Add(w);
        }
        foreach (KeyValuePair<(string Ns, string Factory), List<WidgetModel>> g in
                 groups.OrderBy(static kv => kv.Key.Ns, StringComparer.Ordinal)
                       .ThenBy(static kv => kv.Key.Factory, StringComparer.Ordinal))
            EmitFactoryClass(sb, g.Key.Ns, g.Key.Factory, g.Value);

        // ---- (3) ControlApi 登録 (docs の ApiTable 用 — /// コメントごと焼き込み) ----
        EmitControlApi(sb, list, assemblyName);

        spc.AddSource("LuxelUiComponents.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    /// <summary>[UiComponent] 毎に ControlApiRegistry.Register を module initializer で焼き込む。
    /// メンバー順: ctor 引数 → イベント → 自身の [UiParam] → 基底 (共通) の [UiParam]。</summary>
    private static void EmitControlApi(StringBuilder sb, List<WidgetModel> list, string assemblyName)
    {
        var comps = list.Where(static w => w.IsComponent && w.Ctor is not null).ToList();
        if (comps.Count == 0) return;
        comps.Sort(static (a, b) => string.CompareOrdinal(a.FactoryName, b.FactoryName));

        sb.AppendLine();
        sb.AppendLine("namespace Luxel.UI.Generated");
        sb.AppendLine("{");
        sb.Append("    internal static class ControlApiRegistration_").AppendLine(StoryGenerator.Sanitize(assemblyName));
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        foreach (WidgetModel w in comps)
        {
            sb.Append("            global::Luxel.UI.ControlApiRegistry.Register(new global::Luxel.UI.ControlApi(")
              .Append(Lit(w.FactoryName)).Append(", ").Append(Lit(w.DocSummary))
              .AppendLine(", new global::Luxel.UI.ApiMember[] {");
            foreach (CtorParam p in w.Ctor!)
                sb.Append("                new(").Append(Lit(p.Name)).Append(", ").Append(Lit(ShortType(p.TypeFq)))
                  .Append(", \"ctor\", ").Append(Lit(p.Summary)).AppendLine("),");
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
                BindKind.Color    => ("color", Codec + ".FormatColor(" + access + ")"),
                BindKind.Int      => ("int", access + ".ToString()"),
                BindKind.Float    => ("float", access + ".ToString()"),
                BindKind.Double   => ("float", access + ".ToString()"),
                BindKind.Bool     => ("bool", access + " ? \"true\" : \"false\""),
                BindKind.String   => ("string", access + " ?? \"\""),
                BindKind.Text     => ("string", access),   // BindableString.Get() は非 null
                BindKind.Enum     => (f.EnumHint, access + ".ToString()"),
                // Length は専用ヒント (Gallery が数値+単位コンボの LengthField を出す)
                BindKind.Parsable => (f.TypeFq == LengthType ? "length" : "string", access + ".ToString() ?? \"\""),
                _                 => ("string", Codec + ".FormatBoxed(" + access + ")"),
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
        CtorParam[] ctor = w.Ctor!;
        var ctorNames = new HashSet<string>();
        foreach (CtorParam p in ctor) ctorNames.Add(p.Name);

        if (w.DocSummary.Length > 0)
            sb.Append(pad).Append("/// <summary>").Append(new System.Xml.Linq.XText(w.DocSummary).ToString()).AppendLine("</summary>");
        sb.Append(pad).Append(w.IsInternal ? "internal" : "public").Append(" static ").Append(w.TypeFq)
          .Append(' ').Append(w.FactoryName).AppendLine("(");

        // (1) ctor 引数 (そのまま写す)
        foreach (CtorParam p in ctor)
        {
            sb.Append(pad).Append("    ").Append(p.TypeFq).Append(' ').Append(SafeName(p.Name));
            if (p.DefaultLiteral is not null) sb.Append(" = ").Append(p.DefaultLiteral);
            sb.AppendLine(",");
        }
        // (1.5) [UiEvent] フィールド (Action? の省略可能引数 — ctor 直後に置き位置引数互換を保つ)
        foreach (EventModel e in w.Events)
        {
            if (ctorNames.Contains(ParamName(e.Name))) continue;
            sb.Append(pad).Append("    ").Append(ActionType(e)).Append("? ")
              .Append(SafeName(ParamName(e.Name))).AppendLine(" = null,");
        }
        // (2) [UiParam] フィールド (readonly / ctor 引数と名前衝突は除外)
        // Length は値型のまま受ける (int/float/string からの暗黙変換を 1 段に保つため
        // Bindable<Length> だと 2 段のユーザー定義変換が必要になり width: 380 が書けない)
        foreach (FieldModel f in w.Fields)
        {
            if (ctorNames.Contains(ParamName(f.Name))) continue;
            // Length は値型のまま (= default + .IsSet)、Bindable 系は nullable (= null)
            string paramType = f.Kind == BindKind.Text
                ? "global::Luxel.UI.BindableString?"
                : f.TypeFq == LengthType
                ? LengthType
                : "global::Luxel.UI.Bindable<" + f.TypeFq + ">?";
            sb.Append(pad).Append("    ").Append(paramType).Append(' ')
              .Append(SafeName(ParamName(f.Name)))
              .AppendLine(f.TypeFq == LengthType ? " = default," : " = null,");
        }
        sb.Append(pad).AppendLine("    params global::Luxel.UI.IConfigPart[] parts)");
        sb.Append(pad).AppendLine("{");

        sb.Append(pad).Append("    var w = new ").Append(w.TypeFq).Append('(');
        for (int i = 0; i < ctor.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(SafeName(ctor[i].Name));
        }
        sb.AppendLine(");");

        foreach (EventModel e in w.Events)
        {
            if (ctorNames.Contains(ParamName(e.Name))) continue;
            string pn = SafeName(ParamName(e.Name));
            sb.Append(pad).Append("    if (").Append(pn).Append(" is not null) w.")
              .Append(e.Name).Append(" = ").Append(pn).AppendLine(";");
        }
        foreach (FieldModel f in w.Fields)
        {
            if (ctorNames.Contains(ParamName(f.Name))) continue;
            string pn = SafeName(ParamName(f.Name));
            // フィールドは readonly — 差し替えず SetBase で中身を書く (状態レイヤ/override 維持)
            string guard = f.TypeFq == LengthType ? pn + ".IsSet" : pn + " is not null";
            sb.Append(pad).Append("    if (").Append(guard).Append(") w.")
              .Append(f.Name).Append(".SetBase(").Append(pn).AppendLine(");");
        }
        sb.Append(pad).AppendLine("    foreach (global::Luxel.UI.IConfigPart p in parts) p.Apply(w);");
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
