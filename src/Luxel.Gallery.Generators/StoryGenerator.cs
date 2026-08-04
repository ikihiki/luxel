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
        public readonly string? RuntimeBundleId;
        public readonly string? CapabilityNote;
        public readonly string MethodFq;    // global::Ns.Type.Method
        public readonly string Source;      // メソッドの C# ソース (storysource)
        /// <summary>引数の並び。各要素は "ctx" (= StoryContext) か、DI 解決するグローバル修飾型名。</summary>
        public readonly string[] Params;
        public readonly bool Valid;
        public readonly bool RealWindowOnly;
        public readonly bool ReturnsStoryResult;
        public readonly bool ReturnsSemanticDocument;
        public readonly string? SchemaMethod;
        public readonly string? ResultMethod;
        public StoryModel(string path, int w, int h, int order, string? theme, string methodFq, string source, string[] paramz, bool valid, bool realWindowOnly, string? sampleBundle, string? runtimeBundleId, string? capabilityNote, bool returnsStoryResult, bool returnsSemanticDocument, string? schemaMethod, string? resultMethod)
        { Path = path; Width = w; Height = h; Order = order; Theme = theme; MethodFq = methodFq; Source = source; Params = paramz; Valid = valid; RealWindowOnly = realWindowOnly; SampleBundle = sampleBundle; RuntimeBundleId = runtimeBundleId; CapabilityNote = capabilityNote; ReturnsStoryResult = returnsStoryResult; ReturnsSemanticDocument = returnsSemanticDocument; SchemaMethod = schemaMethod; ResultMethod = resultMethod; }
        public bool Equals(StoryModel? o) => o is not null && Path == o.Path && Width == o.Width && Height == o.Height
            && Order == o.Order && Theme == o.Theme && MethodFq == o.MethodFq && Source == o.Source
            && Params.Length == o.Params.Length && ParamsEqual(o) && Valid == o.Valid && RealWindowOnly == o.RealWindowOnly
            && SampleBundle == o.SampleBundle && RuntimeBundleId == o.RuntimeBundleId && CapabilityNote == o.CapabilityNote
            && ReturnsStoryResult == o.ReturnsStoryResult && ReturnsSemanticDocument == o.ReturnsSemanticDocument
            && SchemaMethod == o.SchemaMethod && ResultMethod == o.ResultMethod;
        private bool ParamsEqual(StoryModel o) { for (int i = 0; i < Params.Length; i++) if (Params[i] != o.Params[i]) return false; return true; }
        public override bool Equals(object? obj) => Equals(obj as StoryModel);
        public override int GetHashCode()
        { unchecked { return ((((((((Path.GetHashCode() * 397 ^ MethodFq.GetHashCode()) * 397 ^ Width * 31 + Height) * 397 ^ Order) * 397 ^ Source.GetHashCode()) * 397 ^ (SampleBundle?.GetHashCode() ?? 0)) * 397 ^ (RuntimeBundleId?.GetHashCode() ?? 0)) * 397 ^ (CapabilityNote?.GetHashCode() ?? 0)) * 4 + (Params.Length << 1)) + (RealWindowOnly ? 1 : 0); } }
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
                    int w = 0, h = 0, order = 1000; string? theme = null; bool realWindowOnly = false;
                    string? sampleBundle = null, runtimeBundleId = null, capabilityNote = null, schemaMethod = null, resultMethod = null;
                    foreach (KeyValuePair<string, TypedConstant> na in attr.NamedArguments)
                    {
                        if (na.Key == "Width" && na.Value.Value is int wi) w = wi;
                        if (na.Key == "Height" && na.Value.Value is int hi) h = hi;
                        if (na.Key == "Order" && na.Value.Value is int oi) order = oi;
                        if (na.Key == "Theme" && na.Value.Value is string th) theme = th;
                        if (na.Key == "RealWindowOnly" && na.Value.Value is bool rw) realWindowOnly = rw;
                        if (na.Key == "SampleBundle" && na.Value.Value is string sb) sampleBundle = sb;
                        if (na.Key == "RuntimeBundleId" && na.Value.Value is string rb) runtimeBundleId = rb;
                        if (na.Key == "CapabilityNote" && na.Value.Value is string cn) capabilityNote = cn;
                        if (na.Key == "Result" && na.Value.Value is string rm) resultMethod = rm;
                        if (na.Key == "Args" && na.Value.Value is string am) schemaMethod = am;
                    }
                    if (runtimeBundleId is null && ctx.SemanticModel.Compilation.AssemblyName == "Luxel.Gallery.Stories.CoreUi")
                        runtimeBundleId = "webgpu-browser-v1";
                    // Width/Height を両方省略 = fill (0,0 — ホストがプレビュー領域いっぱいに表示)。
                    // 片方だけの指定は従来既定 (480×320) で補完する。
                    if (w != 0 || h != 0)
                    {
                        if (w == 0) w = 480;
                        if (h == 0) h = 320;
                    }

                    bool returnsWidget = IsWidget(m.ReturnType);
                    bool returnsStoryResult = m.ReturnType.ToDisplayString() == "Luxel.Gallery.StoryResult";
                    bool returnsSemanticDocument = !RequiresStoryServices((MethodDeclarationSyntax)ctx.Node, ctx.SemanticModel, ct)
                        && IsSemanticDocumentMethod(m, ctx.SemanticModel.Compilation, ct,
                            new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default));
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
                    // storysource: メソッド宣言のソースをそのまま焼き込む (先頭の共通インデントは剥がす)
                    string source = Dedent(((MethodDeclarationSyntax)ctx.Node).ToString());
                    string? schemaFq = schemaMethod is null ? null : m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + schemaMethod;
                    string? resultFq = resultMethod is null ? null : m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + resultMethod;
                    return new StoryModel(path, w, h, order, theme, fq, source, paramz, valid, realWindowOnly, sampleBundle, runtimeBundleId, capabilityNote, returnsStoryResult, returnsSemanticDocument, schemaFq, resultFq);
                })
            .Where(static s => s is not null)
            .Collect();

        var withAsm = stories.Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Assembly"));
        context.RegisterSourceOutput(withAsm, static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static bool RequiresStoryServices(MethodDeclarationSyntax declaration, SemanticModel model,
        System.Threading.CancellationToken cancellationToken)
        => declaration.DescendantNodes().OfType<InvocationExpressionSyntax>().Any(invocation
            => model.GetSymbolInfo(invocation, cancellationToken).Symbol is IMethodSymbol called
               && called.Name == "Require"
               && called.ContainingType.ToDisplayString() == "Luxel.Gallery.StoryContext");

    private static bool IsSemanticDocumentMethod(IMethodSymbol method, Compilation compilation,
        System.Threading.CancellationToken cancellationToken, HashSet<IMethodSymbol> visited)
    {
        method = method.OriginalDefinition;
        if (!visited.Add(method)) return false;
        foreach (SyntaxReference reference in method.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration) continue;
            SemanticModel model = compilation.GetSemanticModel(declaration.SyntaxTree);
            foreach (InvocationExpressionSyntax invocation in declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                if (model.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol called) continue;
                string containingType = called.ContainingType.ToDisplayString();
                if ((called.Name == "DocNew" && containingType == "Luxel.Gallery.Stories.DocsKit")
                    || (called.Name is "Create" or "FromDoc" && containingType == "Luxel.Controls.MarkdownDoc"))
                    return true;
                if (called.DeclaringSyntaxReferences.Length > 0
                    && IsSemanticDocumentMethod(called, compilation, cancellationToken, visited))
                    return true;
            }
        }
        return false;
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
        list.Sort(static (a, b) => string.CompareOrdinal(a.Path, b.Path));

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
            if (s.RuntimeBundleId is not null) sb.Append(", RuntimeBundleId: ").Append(Literal(s.RuntimeBundleId));
            if (s.SchemaMethod is not null) sb.Append(", ArgDefinitions: ").Append(s.SchemaMethod).Append("()");
            if (s.CapabilityNote is not null) sb.Append(", CapabilityNote: ").Append(Literal(s.CapabilityNote));
            sb.AppendLine("));");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        spc.AddSource("GalleryStories.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string Literal(string s) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, true);

    /// <summary>2 行目以降の共通インデントを剥がす (メソッド宣言はクラス内の字下げが付いてくる)。</summary>
    private static string Dedent(string src)
    {
        string[] lines = src.Replace("\r", "").Split('\n');
        int min = int.MaxValue;
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i].Trim().Length == 0) continue;
            int n = 0;
            while (n < lines[i].Length && lines[i][n] == ' ') n++;
            if (n < min) min = n;
        }
        if (min == int.MaxValue || min == 0) return src;
        var sb = new StringBuilder(lines[0]);
        for (int i = 1; i < lines.Length; i++)
            sb.Append('\n').Append(lines[i].Length >= min ? lines[i].Substring(min) : lines[i].TrimStart());
        return sb.ToString();
    }

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
