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
/// <c>[StoryMeta("Component")]</c> のクラスにある <c>[Story]</c> 付き static メソッドを収集し、アセンブリごとに
/// <c>[ModuleInitializer]</c> で <c>Luxel.Gallery.StoryRegistry.Register</c> するコードを焼き込む
/// (reflection なしのストーリー登録)。署名: <c>static StoryResult M()</c> / <c>static StoryResult M(StoryContext)</c>。
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class StoryGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor BadSignature = new(
        "NGUI010", "invalid story signature",
        "'{0}' の [Story] メソッドは 'static StoryResult M()' か 'static StoryResult M(StoryContext)' である必要があります",
        "Luxel.Gallery", DiagnosticSeverity.Warning, true);

    private static readonly DiagnosticDescriptor MissingMeta = new(
        "NGUI011", "story class is missing StoryMeta",
        "'{0}' の [Story] を含むクラスには [StoryMeta(\"title\")] が必要です",
        "Luxel.Gallery", DiagnosticSeverity.Warning, true);

    internal sealed class StoryModel : IEquatable<StoryModel>
    {
        public readonly string Path;
        public readonly string Title;
        public readonly string? CapabilityNote;
        public readonly string MethodFq;    // global::Ns.Type.Method
        public readonly string Source;      // メソッドの C# ソース (storysource)
        public readonly string SourceFile;
        public readonly int SourcePosition;
        /// <summary>引数の並び。各要素は "ctx" (= StoryContext) か、DI 解決するグローバル修飾型名。</summary>
        public readonly string[] Params;
        public readonly bool Valid;
        public readonly bool HasMeta;
        public readonly bool RealWindowOnly;
        public readonly bool ArgsEnabled;
        public readonly string? SchemaMethod;
        public StoryModel(string path, string title, string methodFq, string source, string sourceFile, int sourcePosition,
            string[] paramz, bool valid, bool hasMeta, bool realWindowOnly, bool argsEnabled, string? capabilityNote, string? schemaMethod)
        { Path = path; Title = title; MethodFq = methodFq; Source = source; SourceFile = sourceFile; SourcePosition = sourcePosition; Params = paramz; Valid = valid; HasMeta = hasMeta; RealWindowOnly = realWindowOnly; ArgsEnabled = argsEnabled; CapabilityNote = capabilityNote; SchemaMethod = schemaMethod; }
        public bool Equals(StoryModel? o) => o is not null && Path == o.Path && Title == o.Title && MethodFq == o.MethodFq && Source == o.Source
            && SourceFile == o.SourceFile && SourcePosition == o.SourcePosition
            && Params.Length == o.Params.Length && ParamsEqual(o) && Valid == o.Valid && RealWindowOnly == o.RealWindowOnly
            && ArgsEnabled == o.ArgsEnabled && HasMeta == o.HasMeta && CapabilityNote == o.CapabilityNote
            && SchemaMethod == o.SchemaMethod;
        private bool ParamsEqual(StoryModel o) { for (int i = 0; i < Params.Length; i++) if (Params[i] != o.Params[i]) return false; return true; }
        public override bool Equals(object? obj) => Equals(obj as StoryModel);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Path.GetHashCode();
                hash = hash * 397 ^ Title.GetHashCode();
                hash = hash * 397 ^ MethodFq.GetHashCode();
                hash = hash * 397 ^ Source.GetHashCode();
                hash = hash * 397 ^ SourceFile.GetHashCode();
                hash = hash * 397 ^ SourcePosition;
                hash = hash * 397 ^ (CapabilityNote?.GetHashCode() ?? 0);
                return hash * 16 + (Params.Length << 3) + (HasMeta ? 4 : 0) + (RealWindowOnly ? 2 : 0) + (ArgsEnabled ? 1 : 0);
            }
        }
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
                    {
                        string? attributeName = a.AttributeClass?.ToDisplayString();
                        if (attributeName is "Luxel.Gallery.Story" or "Luxel.Gallery.StoryAttribute") { attr = a; break; }
                    }
                    if (attr is null) return null;

                    string? title = null;
                    foreach (AttributeData typeAttribute in m.ContainingType.GetAttributes())
                        if (typeAttribute.AttributeClass?.ToDisplayString() == "Luxel.Gallery.StoryMeta"
                            && typeAttribute.ConstructorArguments.Length == 1
                            && typeAttribute.ConstructorArguments[0].Value is string value)
                        {
                            title = value.Trim().Trim('/');
                            break;
                        }
                    bool hasMeta = !string.IsNullOrWhiteSpace(title);
                    string path = hasMeta ? title + "/" + m.Name : m.Name;
                    bool realWindowOnly = false;
                    bool argsEnabled = true;
                    string? capabilityNote = null, schemaMethod = null, routeOverride = null;
                    foreach (KeyValuePair<string, TypedConstant> na in attr.NamedArguments)
                    {
                        if (na.Key == "RealWindowOnly" && na.Value.Value is bool rw) realWindowOnly = rw;
                        if (na.Key == "ArgsEnabled" && na.Value.Value is bool enabled) argsEnabled = enabled;
                        if (na.Key == "Path" && na.Value.Value is string route) routeOverride = route.Trim().Trim('/');
                        if (na.Key == "CapabilityNote" && na.Value.Value is string cn) capabilityNote = cn;
                        if (na.Key == "Args" && na.Value.Value is string am) schemaMethod = am;
                    }
                    if (!string.IsNullOrWhiteSpace(routeOverride)) path = routeOverride!;
                    bool returnsStoryResult = m.ReturnType.ToDisplayString() == "Luxel.Gallery.StoryResult";
                    // 引数: StoryContext は "ctx"、その他は DI 解決するグローバル修飾型名 (minimal API 風)
                    var paramz = new string[m.Parameters.Length];
                    for (int pi = 0; pi < m.Parameters.Length; pi++)
                    {
                        ITypeSymbol pt = m.Parameters[pi].Type;
                        paramz[pi] = pt.ToDisplayString() == "Luxel.Gallery.StoryContext"
                            ? "ctx"
                            : pt.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    }
                    bool valid = m.IsStatic && returnsStoryResult && m.ContainingType is not null;

                    string fq = m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + m.Name;
                    // Story source is the Roslyn method syntax exactly as captured; hosts display it unchanged.
                    var methodDeclaration = (MethodDeclarationSyntax)ctx.Node;
                    string source = methodDeclaration.ToString();
                    string? schemaFq = schemaMethod is null ? null : m.ContainingType!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + schemaMethod;
                    return new StoryModel(path, title ?? "", fq, source, methodDeclaration.SyntaxTree.FilePath,
                        methodDeclaration.SpanStart, paramz, valid, hasMeta, realWindowOnly, argsEnabled, capabilityNote, schemaFq);
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
            "Tutorials" => 10,
            "Learn" => 20,
            "Controls" => 30,
            "Examples" => 40,
            "Reference" => 50,
            "Internals" => 60,
            _ => 1000,
        };
    }

    private static int CompareSourceFiles(string left, string right)
    {
        const string samplesSuffix = ".Samples.cs";
        bool leftSamples = left.EndsWith(samplesSuffix, StringComparison.Ordinal);
        bool rightSamples = right.EndsWith(samplesSuffix, StringComparison.Ordinal);
        string leftGroup = leftSamples ? left.Substring(0, left.Length - samplesSuffix.Length) + ".cs" : left;
        string rightGroup = rightSamples ? right.Substring(0, right.Length - samplesSuffix.Length) + ".cs" : right;
        int group = string.CompareOrdinal(leftGroup, rightGroup);
        if (group != 0) return group;
        if (leftSamples != rightSamples) return leftSamples ? 1 : -1;
        return string.CompareOrdinal(left, right);
    }

    private static void Emit(SourceProductionContext spc, ImmutableArray<StoryModel?> models, string assemblyName)
    {
        var list = new List<StoryModel>();
        var seen = new HashSet<string>();
        foreach (StoryModel? m in models)
        {
            if (m is null) continue;
            if (!m.HasMeta) { spc.ReportDiagnostic(Diagnostic.Create(MissingMeta, Location.None, m.MethodFq)); continue; }
            if (!m.Valid) { spc.ReportDiagnostic(Diagnostic.Create(BadSignature, Location.None, m.MethodFq)); continue; }
            if (seen.Add(m.MethodFq)) list.Add(m);
        }
        if (list.Count == 0) return;
        list.Sort(static (a, b) =>
        {
            int component = ComponentRank(a.Path).CompareTo(ComponentRank(b.Path));
            if (component != 0) return component;
            // StoryMetaの階層はまとまりを保ち、その中ではC#の宣言順をサイドバーへ反映する。
            int title = string.CompareOrdinal(a.Title, b.Title);
            if (title != 0) return title;
            int file = CompareSourceFiles(a.SourceFile, b.SourceFile);
            if (file != 0) return file;
            int position = a.SourcePosition.CompareTo(b.SourcePosition);
            if (position != 0) return position;
            return string.CompareOrdinal(a.MethodFq, b.MethodFq);
        });

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/> Luxel.Gallery.Generators.StoryGenerator");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("namespace Luxel.Gallery.Generated");
        sb.AppendLine("{");
        sb.Append("    public static class StoryRegistration_").AppendLine(Sanitize(assemblyName));
        sb.AppendLine("    {");
        sb.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        sb.AppendLine("        internal static void Init()");
        sb.AppendLine("        {");
        sb.AppendLine("            var builder = new global::Luxel.Gallery.StoryCatalogBuilder();");
        sb.AppendLine("            Register(builder);");
        sb.AppendLine("            foreach (global::Luxel.Gallery.StoryInfo story in builder.Build().All)");
        sb.AppendLine("                global::Luxel.Gallery.StoryRegistry.Register(story);");
        sb.AppendLine("        }");
        sb.AppendLine();
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

            sb.Append("            builder.Add(new global::Luxel.Gallery.StoryInfo(")
              .Append(Literal(s.Path)).Append(", ")
              .Append(semanticBuilder)
              .Append(", Source: ").Append(Literal(s.Source))
              .Append(", RealWindowOnly: ").Append(s.RealWindowOnly ? "true" : "false");
            if (!s.ArgsEnabled)
                sb.Append(", ArgDefinitions: global::System.Array.Empty<global::Luxel.Gallery.StoryArgDefinition>()");
            else if (s.SchemaMethod is not null)
                sb.Append(", ArgDefinitions: ").Append(s.SchemaMethod).Append("()");
            if (s.CapabilityNote is not null) sb.Append(", CapabilityNote: ").Append(Literal(s.CapabilityNote));
            if (!IsPageNavigationCandidate(s.SourceFile)) sb.Append(", IncludeInPageNavigation: false");
            sb.AppendLine("));");
        }
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        spc.AddSource("GalleryStories.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string Literal(string s) => Microsoft.CodeAnalysis.CSharp.SymbolDisplay.FormatLiteral(s, true);

    internal static bool IsPageNavigationCandidate(string sourceFile)
        => !sourceFile.EndsWith(".Samples.cs", StringComparison.Ordinal);

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
        return sb.ToString();
    }
}
