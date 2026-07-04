using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.UI.Generators;

/// <summary>
/// <c>[Story("Component/Name")]</c> 付き static メソッドを収集し、アセンブリごとに
/// <c>[ModuleInitializer]</c> で <c>Luxel.UI.StoryRegistry.Register</c> するコードを焼き込む
/// (reflection なしのストーリー登録)。署名: <c>static Widget M()</c> / <c>static Widget M(StoryContext)</c>。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StoryGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor BadSignature = new(
        "NGUI010", "invalid story signature",
        "'{0}' の [Story] メソッドは 'static Widget M()' か 'static Widget M(StoryContext)' である必要があります",
        "Luxel.UI", DiagnosticSeverity.Warning, true);

    internal sealed class StoryModel : IEquatable<StoryModel>
    {
        public readonly string Path;
        public readonly int Width, Height, Order;
        public readonly string? Theme;
        public readonly string MethodFq;    // global::Ns.Type.Method
        public readonly string Source;      // メソッドの C# ソース (storysource)
        public readonly bool HasCtx;
        public readonly bool Valid;
        public readonly bool RealWindowOnly;
        public StoryModel(string path, int w, int h, int order, string? theme, string methodFq, string source, bool hasCtx, bool valid, bool realWindowOnly)
        { Path = path; Width = w; Height = h; Order = order; Theme = theme; MethodFq = methodFq; Source = source; HasCtx = hasCtx; Valid = valid; RealWindowOnly = realWindowOnly; }
        public bool Equals(StoryModel? o) => o is not null && Path == o.Path && Width == o.Width && Height == o.Height
            && Order == o.Order && Theme == o.Theme && MethodFq == o.MethodFq && Source == o.Source
            && HasCtx == o.HasCtx && Valid == o.Valid && RealWindowOnly == o.RealWindowOnly;
        public override bool Equals(object? obj) => Equals(obj as StoryModel);
        public override int GetHashCode()
        { unchecked { return (((((Path.GetHashCode() * 397 ^ MethodFq.GetHashCode()) * 397 ^ Width * 31 + Height) * 397 ^ Order) * 397 ^ Source.GetHashCode()) * 4 + (HasCtx ? 2 : 0)) + (RealWindowOnly ? 1 : 0); } }
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
                        if (a.AttributeClass?.ToDisplayString() == "Luxel.UI.StoryAttribute") { attr = a; break; }
                    if (attr is null) return null;

                    string path = attr.ConstructorArguments.Length == 1 && attr.ConstructorArguments[0].Value is string p ? p : m.Name;
                    int w = 0, h = 0, order = 1000; string? theme = null; bool realWindowOnly = false;
                    foreach (KeyValuePair<string, TypedConstant> na in attr.NamedArguments)
                    {
                        if (na.Key == "Width" && na.Value.Value is int wi) w = wi;
                        if (na.Key == "Height" && na.Value.Value is int hi) h = hi;
                        if (na.Key == "Order" && na.Value.Value is int oi) order = oi;
                        if (na.Key == "Theme" && na.Value.Value is string th) theme = th;
                        if (na.Key == "RealWindowOnly" && na.Value.Value is bool rw) realWindowOnly = rw;
                    }
                    // Width/Height を両方省略 = fill (0,0 — ホストがプレビュー領域いっぱいに表示)。
                    // 片方だけの指定は従来既定 (480×320) で補完する。
                    if (w != 0 || h != 0)
                    {
                        if (w == 0) w = 480;
                        if (h == 0) h = 320;
                    }

                    bool returnsWidget = IsWidget(m.ReturnType);
                    bool hasCtx = m.Parameters.Length == 1
                        && m.Parameters[0].Type.ToDisplayString() == "Luxel.UI.StoryContext";
                    bool valid = m.IsStatic && returnsWidget && (m.Parameters.Length == 0 || hasCtx)
                        && m.ContainingType is not null;

                    string fq = m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + m.Name;
                    // storysource: メソッド宣言のソースをそのまま焼き込む (先頭の共通インデントは剥がす)
                    string source = Dedent(((MethodDeclarationSyntax)ctx.Node).ToString());
                    return new StoryModel(path, w, h, order, theme, fq, source, hasCtx, valid, realWindowOnly);
                })
            .Where(static s => s is not null)
            .Collect();

        var withAsm = stories.Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Assembly"));
        context.RegisterSourceOutput(withAsm, static (spc, pair) => Emit(spc, pair.Left, pair.Right));
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
        sb.AppendLine("// <auto-generated/> Luxel.UI.Generators.StoryGenerator");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Luxel.UI.Generated");
        sb.AppendLine("{");
        sb.Append("    internal static class StoryRegistration_").AppendLine(Sanitize(assemblyName));
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        foreach (StoryModel s in list)
        {
            sb.Append("            global::Luxel.UI.StoryRegistry.Register(new global::Luxel.UI.StoryInfo(")
              .Append(Literal(s.Path)).Append(", ").Append(s.Width).Append(", ").Append(s.Height).Append(", ")
              .Append(s.Theme is null ? "null" : Literal(s.Theme)).Append(", ")
              .Append(s.HasCtx ? "static ctx => " + s.MethodFq + "(ctx)" : "static _ => " + s.MethodFq + "()")
              .Append(", ").Append(s.Order)
              .Append(", ").Append(Literal(s.Source))
              .Append(s.RealWindowOnly ? ", true" : "")
              .AppendLine("));");
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
