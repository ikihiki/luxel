using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.Gallery.Generators;

/// <summary>
/// <c>[Story("Component/Name")]</c> 付き static メソッドを収集し、アセンブリごとに
/// <c>[ModuleInitializer]</c> で <c>Luxel.Gallery.StoryRegistry.Register</c> するコードを焼き込む
/// (reflection なしのストーリー登録)。署名: <c>static Widget M()</c> / <c>static Widget M(StoryContext)</c>。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StoryGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor BadSignature = new(
        "NGUI010", "invalid story signature",
        "'{0}' の [Story] メソッドは 'static Widget M()' か 'static Widget M(StoryContext)' である必要があります",
        "Luxel.Gallery", DiagnosticSeverity.Warning, true);

    internal sealed class StoryModel : IEquatable<StoryModel>
    {
        public readonly string Path;
        public readonly int Width, Height, Order;
        public readonly string? Theme;
        public readonly string? SampleBundle;
        public readonly string? CapabilityNote;
        public readonly string MethodFq;    // global::Ns.Type.Method
        public readonly string Source;      // メソッドの C# ソース (storysource)
        /// <summary>引数の並び。各要素は "ctx" (= StoryContext) か、DI 解決するグローバル修飾型名。</summary>
        public readonly string[] Params;
        public readonly bool Valid;
        public readonly bool RealWindowOnly;
        public readonly bool Toc;
        public readonly bool ReturnsStoryResult;
        public readonly bool ReturnsSemanticDocument;
        public readonly string? SchemaMethod;
        public readonly string? ResultMethod;
        public StoryModel(string path, int w, int h, int order, string? theme, string methodFq, string source, string[] paramz, bool valid, bool realWindowOnly, bool toc, string? sampleBundle, string? capabilityNote, bool returnsStoryResult, bool returnsSemanticDocument, string? schemaMethod, string? resultMethod)
        { Path = path; Width = w; Height = h; Order = order; Theme = theme; MethodFq = methodFq; Source = source; Params = paramz; Valid = valid; RealWindowOnly = realWindowOnly; Toc = toc; SampleBundle = sampleBundle; CapabilityNote = capabilityNote; ReturnsStoryResult = returnsStoryResult; ReturnsSemanticDocument = returnsSemanticDocument; SchemaMethod = schemaMethod; ResultMethod = resultMethod; }
        public bool Equals(StoryModel? o) => o is not null && Path == o.Path && Width == o.Width && Height == o.Height
            && Order == o.Order && Theme == o.Theme && MethodFq == o.MethodFq && Source == o.Source
            && Params.Length == o.Params.Length && ParamsEqual(o) && Valid == o.Valid && RealWindowOnly == o.RealWindowOnly
            && Toc == o.Toc && SampleBundle == o.SampleBundle && CapabilityNote == o.CapabilityNote
            && ReturnsStoryResult == o.ReturnsStoryResult && ReturnsSemanticDocument == o.ReturnsSemanticDocument
            && SchemaMethod == o.SchemaMethod && ResultMethod == o.ResultMethod;
        private bool ParamsEqual(StoryModel o) { for (int i = 0; i < Params.Length; i++) if (Params[i] != o.Params[i]) return false; return true; }
        public override bool Equals(object? obj) => Equals(obj as StoryModel);
        public override int GetHashCode()
        { unchecked { return ((((((((Path.GetHashCode() * 397 ^ MethodFq.GetHashCode()) * 397 ^ Width * 31 + Height) * 397 ^ Order) * 397 ^ Source.GetHashCode()) * 397 ^ (SampleBundle?.GetHashCode() ?? 0))) * 397 ^ (CapabilityNote?.GetHashCode() ?? 0)) * 4 + (Params.Length << 1)) + (RealWindowOnly ? 1 : 0); } }
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var stories = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is MethodDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, ct) =>
                {
                    if (ctx.SemanticModel.GetDeclaredSymbol((MethodDeclarationSyntax)ctx.Node, ct) is not IMethodSymbol m) return null;
                    AttributeData? attr = null;
                    foreach (AttributeData a in m.GetAttributes())
                        if (a.AttributeClass?.ToDisplayString() == "Luxel.Gallery.StoryAttribute") { attr = a; break; }
                    if (attr is null) return null;

                    string path = attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string p ? p : m.Name;
                    int w = 0, h = 0, order = 1000; string? theme = null; bool realWindowOnly = false, toc = false;
                    string? sampleBundle = null, capabilityNote = null, schemaMethod = null, resultMethod = null;
                    foreach (KeyValuePair<string, TypedConstant> na in attr.NamedArguments)
                    {
                        if (na.Key == "Width" && na.Value.Value is int wi) w = wi;
                        if (na.Key == "Height" && na.Value.Value is int hi) h = hi;
                        if (na.Key == "Order" && na.Value.Value is int oi) order = oi;
                        if (na.Key == "Theme" && na.Value.Value is string th) theme = th;
                        if (na.Key == "RealWindowOnly" && na.Value.Value is bool rw) realWindowOnly = rw;
                        if (na.Key == "Toc" && na.Value.Value is bool tc) toc = tc;
                        if (na.Key == "SampleBundle" && na.Value.Value is string sb) sampleBundle = sb;
                        if (na.Key == "CapabilityNote" && na.Value.Value is string cn) capabilityNote = cn;
                        if (na.Key == "Result" && na.Value.Value is string rm) resultMethod = rm;
                        if (na.Key == "Args" && na.Value.Value is string am) schemaMethod = am;
                    }
                    // Width/Height を両方省略 = fill (0,0 — ホストがプレビュー領域いっぱいに表示)。
                    // 片方だけの指定は従来既定 (480×320) で補完する。
                    if (w != 0 || h != 0)
                    {
                        if (w == 0) w = 480;
                        if (h == 0) h = 320;
                    }

                    bool returnsWidget = IsWidget(m.ReturnType);
                    bool returnsStoryResult = m.ReturnType.ToDisplayString() == "Luxel.Gallery.StoryResult";
                    bool returnsSemanticDocument = false;
                    // 引数: StoryContext は "ctx"、その他は DI 解決するグローバル修飾型名 (minimal API 風)
                    var paramz = new string[m.Parameters.Length];
                    for (int pi = 0; pi < m.Parameters.Length; pi++)
                    {
                        ITypeSymbol pt = m.Parameters[pi].Type;
                        paramz[pi] = pt.ToDisplayString() == "Luxel.Gallery.StoryContext"
                            ? "ctx"
                            : pt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    bool valid = m.IsStatic && (returnsWidget || returnsStoryResult) && m.ContainingType is not null;

                    string fq = m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + m.Name;
                    // Story source is the Roslyn method syntax exactly as captured; hosts display it unchanged.
                    var methodDeclaration = (MethodDeclarationSyntax)ctx.Node;
                    string source = methodDeclaration.ToString();
                    string? schemaFq = schemaMethod is null ? null : m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + schemaMethod;
                    string? resultFq = resultMethod is null ? null : m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + resultMethod;
                    return new StoryModel(path, w, h, order, theme, fq, source, paramz, valid, realWindowOnly, toc, sampleBundle, capabilityNote, returnsStoryResult, returnsSemanticDocument, schemaFq, resultFq);
                })
            .Where(static s => s is not null)
            .Collect();

        var withAsm = stories.Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Assembly"));
        context.RegisterSourceOutput(withAsm, static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static int ComponentRank(string path)
    {
        string component = path.Split('/')[0];
        return component switch
        {
            "Start" => 0,
            "Learn" => 10,
            "Examples" => 30,
            "Controls" => 40,
            "Apps" => 50,
            "Game" => 60,
            "Reference" => 70,
            "Internals" => 80,
            "RealWindow" => 90,
            "ADR" => 100,
            "Docs" => 110,
            _ => 1000,
        };
    }

    private static bool IsWidget(ITypeSymbol t)
    {
        for (ITypeSymbol? cur = t; cur is not null; cur = (cur as INamedTypeSymbol)?.BaseType)
            if (cur.ToDisplayString() == "Luxel.UI.Widget") return true;
        return false;
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<StoryModel?> models, string assemblyName)
    {
        var list = new List<StoryModel>();
        var seen = new HashSet<string>();
        foreach (StoryModel? m in models)
        {
            if (m is null) continue;
            if (!m.Valid) { spc.ReportDiagnostic(Diagnostic.Create(BadSignature, Location.None, m.MethodFq)); continue; }
            if (seen.Add(m.MethodFq)) list.Add(m);
        }
        if (list.Count == 0) return;
        list.Sort(static (a, b) =>
        {
            int component = ComponentRank(a.Path).CompareTo(ComponentRank(b.Path));
            if (component != 0) return component;
            int order = a.Order.CompareTo(b.Order);
            return order != 0 ? order : string.CompareOrdinal(a.Path, b.Path);
        });

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Luxel.Gallery.Generators.StoryGenerator");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Luxel.Gallery.Generated");
        sb.AppendLine("{");
        sb.Append("    public static class StoryRegistration_").AppendLine(Sanitize(assemblyName));
        sb.AppendLine("    {");
        // CoreUi is composed explicitly by native, Site and browser hosts. Avoid eager module
        // initialization in browser-WASM, where static arg schema creation must stay behind the catalog root.
        if (assemblyName != "Luxel.Gallery.Stories.CoreUi")
        {
            sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
            sb.AppendLine("        internal static void Init()");
            sb.AppendLine("        {");
            sb.AppendLine("            var builder = new global::Luxel.Gallery.StoryCatalogBuilder();");
            sb.AppendLine("            Register(builder);");
            sb.AppendLine("            foreach (global::Luxel.Gallery.StoryInfo story in builder.Build().All)");
            sb.AppendLine("                global::Luxel.Gallery.StoryRegistry.Register(story);");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
        sb.AppendLine("        public static void Register(global::Luxel.Gallery.StoryCatalogBuilder builder)");
        sb.AppendLine("        {");
        sb.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(builder);");
        foreach (StoryModel s in list)
        {
            var args = new string[s.Params.Length];
            for (int i = 0; i < s.Params.Length; i++)
                args[i] = s.Params[i] == "ctx" ? "ctx" : "ctx.Require<" + s.Params[i] + ">()";
            string invocation = s.MethodFq + "(" + string.Join(", ", args) + ")";
            string semanticBuilder = "static ctx => " + invocation;
            string widgetBuilder = s.ReturnsStoryResult
                ? "static ctx => { global::Luxel.Gallery.StoryResult result = " + invocation
                    + "; return result.Kind == global::Luxel.Gallery.StoryResultKind.Widget && result.Widget is not null"
                    + " ? result.Widget : throw new global::System.InvalidOperationException(\"Markdown story cannot be realized as a Widget. Use StoryInfo.BuildResult.\"); }"
                : semanticBuilder;

            sb.Append("            builder.Add(new global::Luxel.Gallery.StoryInfo(")
              .Append(Literal(s.Path)).Append(", ").Append(s.Width).Append(", ").Append(s.Height).Append(", ")
              .Append(s.Theme is null ? "null" : Literal(s.Theme)).Append(", ")
              .Append(widgetBuilder)
              .Append(", ").Append(s.Order)
              .Append(", ").Append(Literal(s.Source))
              .Append(", ").Append(s.RealWindowOnly ? "true" : "false")
              .Append(", ").Append(s.SampleBundle is null ? "null" : Literal(s.SampleBundle));
            if (s.ResultMethod is not null) sb.Append(", ResultBuild: static _ => ").Append(s.ResultMethod).Append("()");
            else if (s.ReturnsStoryResult || s.ReturnsSemanticDocument) sb.Append(", ResultBuild: ").Append(semanticBuilder);
            if (s.SchemaMethod is not null) sb.Append(", ArgDefinitions: ").Append(s.SchemaMethod).Append("()");
            if (s.CapabilityNote is not null) sb.Append(", CapabilityNote: ").Append(Literal(s.CapabilityNote));
            if (s.Toc) sb.Append(", Toc: true");
            sb.AppendLine("));");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        spc.AddSource("GalleryStories.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string Literal(string s) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, true);

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
