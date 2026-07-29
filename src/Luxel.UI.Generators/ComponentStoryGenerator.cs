using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Luxel.UI.Generators;

/// <summary>Generates strongly typed component-story factories and registrations without reflection.</summary>
[Generator(LanguageNames.CSharp)]
public sealed class ComponentStoryGenerator : IIncrementalGenerator
{
    private const string StoryAttribute = "Luxel.Gallery.ComponentStoryAttribute";
    private const string ArgAttribute = "Luxel.Gallery.ComponentArgAttribute";
    private const string UiComponentAttribute = "Luxel.UI.UiComponentAttribute";
    private const string UiParamAttribute = "Luxel.UI.UiParamAttribute";

    private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private static readonly DiagnosticDescriptor UnknownMember = new(
        "NGUI020", "unknown component story member",
        "Component story arg '{0}' does not name a [UiParam] on '{1}' and has no Apply method",
        "Luxel.UI", DiagnosticSeverity.Warning, true);
    private static readonly DiagnosticDescriptor BadDefault = new(
        "NGUI021", "invalid component story default",
        "Default value for component story arg '{0}' is not compatible with '{1}'",
        "Luxel.UI", DiagnosticSeverity.Warning, true);
    private static readonly DiagnosticDescriptor BadApply = new(
        "NGUI022", "invalid component story apply method",
        "Apply method '{0}' must be an accessible static void method with parameters ({1}, {2})",
        "Luxel.UI", DiagnosticSeverity.Warning, true);
    private static readonly DiagnosticDescriptor BadTemplate = new(
        "NGUI023", "invalid component story template",
        "Template method '{0}' must be an accessible static method taking '{1}' and returning Widget",
        "Luxel.UI", DiagnosticSeverity.Warning, true);
    private static readonly DiagnosticDescriptor BadComponent = new(
        "NGUI024", "invalid component story component",
        "'{0}' is not a [UiComponent] with a resolvable generated factory method",
        "Luxel.UI", DiagnosticSeverity.Warning, true);

    private sealed class ArgModel
    {
        public string Member = "";
        public string Name = "";
        public string Type = "";
        public string Default = "";
        public string? Apply;
        public string? Description;
        public int Order;
        public double? Min, Max, Step;
        public string Parameter = "";
    }

    private sealed class StoryModel
    {
        public string Path = "";
        public int Width, Height, Order;
        public string? Theme, SampleBundle, RuntimeBundleId, Template;
        public bool RealWindowOnly;
        public string StoryType = "";
        public string ComponentType = "";
        public string FactoryType = "";
        public string FactoryMethod = "";
        public string Source = "";
        public readonly List<ArgModel> Args = new();
        public readonly List<(DiagnosticDescriptor Descriptor, Location Location, object[] Args)> Diagnostics = new();
        public bool Valid => Diagnostics.Count == 0;
    }

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var stories = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is ClassDeclarationSyntax { AttributeLists.Count: > 0 },
                static (ctx, ct) => Transform(ctx, ct))
            .Where(static model => model is not null)
            .Collect();
        var input = stories.Combine(context.CompilationProvider.Select(static (c, _) => c.AssemblyName ?? "Assembly"));
        context.RegisterSourceOutput(input, static (spc, pair) => Emit(spc, pair.Left, pair.Right));
    }

    private static StoryModel? Transform(GeneratorSyntaxContext context, System.Threading.CancellationToken ct)
    {
        var declaration = (ClassDeclarationSyntax)context.Node;
        if (context.SemanticModel.GetDeclaredSymbol(declaration, ct) is not INamedTypeSymbol storyType) return null;
        AttributeData? storyAttribute = storyType.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == StoryAttribute);
        if (storyAttribute is null) return null;

        var model = new StoryModel { StoryType = storyType.ToDisplayString(TypeFormat), Source = declaration.ToString() };
        Location location = declaration.Identifier.GetLocation();
        if (storyAttribute.ConstructorArguments.Length < 2
            || storyAttribute.ConstructorArguments[0].Value is not INamedTypeSymbol component
            || storyAttribute.ConstructorArguments[1].Value is not string path)
        {
            model.Diagnostics.Add((BadComponent, location, new object[] { storyType.Name }));
            return model;
        }

        model.ComponentType = component.ToDisplayString(TypeFormat);
        model.Path = path;
        model.Order = 1000;
        INamedTypeSymbol? explicitFactory = null;
        string? explicitFactoryMethod = null;
        foreach (KeyValuePair<string, TypedConstant> named in storyAttribute.NamedArguments)
        {
            switch (named.Key)
            {
                case "Factory": explicitFactory = named.Value.Value as INamedTypeSymbol; break;
                case "FactoryMethod": explicitFactoryMethod = named.Value.Value as string; break;
                case "Template": model.Template = named.Value.Value as string; break;
                case "Width" when named.Value.Value is int value: model.Width = value; break;
                case "Height" when named.Value.Value is int value: model.Height = value; break;
                case "Order" when named.Value.Value is int value: model.Order = value; break;
                case "Theme": model.Theme = named.Value.Value as string; break;
                case "RealWindowOnly" when named.Value.Value is bool value: model.RealWindowOnly = value; break;
                case "SampleBundle": model.SampleBundle = named.Value.Value as string; break;
                case "RuntimeBundleId": model.RuntimeBundleId = named.Value.Value as string; break;
            }
        }
        NormalizeSize(model);

        AttributeData? componentAttribute = component.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == UiComponentAttribute);
        if (componentAttribute is null)
        {
            model.Diagnostics.Add((BadComponent, location, new object[] { component.Name }));
            return model;
        }

        string factoryName = explicitFactoryMethod ?? NamedString(componentAttribute, "Name") ?? component.Name;
        INamedTypeSymbol? factoryType = explicitFactory ?? ResolveDefaultFactory(context.SemanticModel.Compilation, component, componentAttribute);
        IMethodSymbol? factoryMethod = factoryType?.GetMembers(factoryName).OfType<IMethodSymbol>()
            .FirstOrDefault(method => method.IsStatic && SymbolEqualityComparer.Default.Equals(method.ReturnType, component));
        if (factoryType is null || factoryMethod is null)
        {
            model.Diagnostics.Add((BadComponent, location, new object[] { component.Name }));
            return model;
        }
        model.FactoryType = factoryType.ToDisplayString(TypeFormat);
        model.FactoryMethod = factoryName;

        Dictionary<string, ITypeSymbol> members = CollectUiParams(context.SemanticModel.Compilation, component);
        foreach (AttributeData argAttribute in storyType.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == ArgAttribute))
        {
            Location argLocation = argAttribute.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation() ?? location;
            if (argAttribute.ConstructorArguments.Length < 2 || argAttribute.ConstructorArguments[0].Value is not string member) continue;
            TypedConstant defaultValue = argAttribute.ConstructorArguments[1];
            string? apply = NamedString(argAttribute, "Apply");
            members.TryGetValue(member, out ITypeSymbol? argType);
            if (argType is null && apply is null)
            {
                model.Diagnostics.Add((UnknownMember, argLocation, new object[] { member, component.Name }));
                continue;
            }
            argType ??= DefaultType(defaultValue);
            if (argType is null || !IsCompatible(defaultValue, argType))
            {
                model.Diagnostics.Add((BadDefault, argLocation, new object[] { member, argType?.ToDisplayString() ?? "unknown" }));
                continue;
            }

            string typeFq = argType.ToDisplayString(TypeFormat);
            if (apply is not null && !HasApply(storyType, apply, component, argType))
            {
                model.Diagnostics.Add((BadApply, argLocation, new object[] { apply, component.Name, argType.ToDisplayString() }));
                continue;
            }

            string publicName = NamedString(argAttribute, "Name") ?? char.ToLowerInvariant(member[0]) + member.Substring(1);
            var arg = new ArgModel
            {
                Member = member,
                Name = publicName,
                Type = typeFq,
                Default = Constant(defaultValue, argType),
                Apply = apply,
                Description = NamedString(argAttribute, "Description"),
                Order = NamedInt(argAttribute, "Order", 1000),
                Min = NamedDouble(argAttribute, "Min"),
                Max = NamedDouble(argAttribute, "Max"),
                Step = NamedDouble(argAttribute, "Step"),
                Parameter = factoryMethod.Parameters.FirstOrDefault(p => string.Equals(p.Name, char.ToLowerInvariant(member[0]) + member.Substring(1), StringComparison.OrdinalIgnoreCase))?.Name ?? ""
            };
            if (apply is null && arg.Parameter.Length == 0)
            {
                model.Diagnostics.Add((UnknownMember, argLocation, new object[] { member, component.Name }));
                continue;
            }
            model.Args.Add(arg);
        }

        if (model.Template is not null && !HasTemplate(storyType, model.Template, component))
            model.Diagnostics.Add((BadTemplate, location, new object[] { model.Template, component.Name }));
        return model;
    }

    private static void NormalizeSize(StoryModel model)
    {
        if (model.Width != 0 || model.Height != 0)
        {
            if (model.Width == 0) model.Width = 480;
            if (model.Height == 0) model.Height = 320;
        }
    }

    private static INamedTypeSymbol? ResolveDefaultFactory(Compilation compilation, INamedTypeSymbol component, AttributeData componentAttribute)
    {
        string? factory = NamedString(componentAttribute, "Factory");
        if (factory is null)
        {
            AttributeData? defaults = component.ContainingAssembly.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == "Luxel.UI.UiFactoryDefaultsAttribute");
            factory = defaults?.ConstructorArguments.FirstOrDefault().Value as string ?? "Factories";
        }
        string metadataName = component.ContainingNamespace.IsGlobalNamespace
            ? factory : component.ContainingNamespace.ToDisplayString() + "." + factory;
        return compilation.GetTypeByMetadataName(metadataName);
    }

    private static Dictionary<string, ITypeSymbol> CollectUiParams(Compilation compilation, INamedTypeSymbol component)
    {
        var result = new Dictionary<string, ITypeSymbol>(StringComparer.Ordinal);
        for (INamedTypeSymbol? type = component; type is not null; type = type.BaseType)
        {
            foreach (ISymbol member in type.GetMembers())
            {
                if (!member.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == UiParamAttribute)) continue;
                ITypeSymbol? declared = member switch { IFieldSymbol field => field.Type, IPropertySymbol property => property.Type, _ => null };
                if (declared is not INamedTypeSymbol named) continue;
                ITypeSymbol? valueType = named.OriginalDefinition.ToDisplayString() == "Luxel.UI.Bindable<T>"
                    ? named.TypeArguments[0]
                    : named.ToDisplayString() == "Luxel.UI.BindableString"
                        ? compilation.GetSpecialType(SpecialType.System_String)
                        : null;
                if (valueType is null) continue;
                string name = member.Name;
                if (member is IFieldSymbol && name.StartsWith("_", StringComparison.Ordinal) && name.Length > 1)
                    name = char.ToUpperInvariant(name[1]) + name.Substring(2);
                if (!result.ContainsKey(name)) result.Add(name, valueType);
            }
        }
        return result;
    }

    private static bool HasApply(INamedTypeSymbol story, string name, INamedTypeSymbol component, ITypeSymbol argType)
        => story.GetMembers(name).OfType<IMethodSymbol>().Any(method => method.IsStatic && method.DeclaredAccessibility != Accessibility.Private
            && method.ReturnsVoid && method.Parameters.Length == 2
            && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, component)
            && SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, argType));

    private static bool HasTemplate(INamedTypeSymbol story, string name, INamedTypeSymbol component)
        => story.GetMembers(name).OfType<IMethodSymbol>().Any(method => method.IsStatic && method.DeclaredAccessibility != Accessibility.Private
            && method.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, component)
            && IsWidget(method.ReturnType));

    private static bool IsWidget(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = (current as INamedTypeSymbol)?.BaseType)
            if (current.ToDisplayString() == "Luxel.UI.Widget") return true;
        return false;
    }

    private static ITypeSymbol? DefaultType(TypedConstant constant) => constant.Type is { SpecialType: not SpecialType.System_Object } type ? type : null;

    private static bool IsCompatible(TypedConstant value, ITypeSymbol target)
    {
        if (value.IsNull) return target.IsReferenceType || target.NullableAnnotation == NullableAnnotation.Annotated;
        if (target.TypeKind == TypeKind.Enum) return value.Kind == TypedConstantKind.Enum && SymbolEqualityComparer.Default.Equals(value.Type, target);
        return target.SpecialType switch
        {
            SpecialType.System_Boolean => value.Value is bool,
            SpecialType.System_String => value.Value is string,
            SpecialType.System_Int32 => value.Value is int,
            SpecialType.System_Single => value.Value is float,
            SpecialType.System_Double => value.Value is double or float or int,
            _ => SymbolEqualityComparer.Default.Equals(value.Type, target),
        };
    }

    private static string Constant(TypedConstant value, ITypeSymbol target)
    {
        if (value.IsNull) return "null!";
        if (target.TypeKind == TypeKind.Enum)
        {
            string enumType = target.ToDisplayString(TypeFormat);
            IFieldSymbol? field = ((INamedTypeSymbol)target).GetMembers().OfType<IFieldSymbol>()
                .FirstOrDefault(candidate => candidate.HasConstantValue && Equals(candidate.ConstantValue, value.Value));
            return field is null ? "(" + enumType + ")" + Convert.ToString(value.Value, CultureInfo.InvariantCulture) : enumType + "." + field.Name;
        }
        return value.Value switch
        {
            string text => SymbolDisplay.FormatLiteral(text, true),
            char character => SymbolDisplay.FormatLiteral(character, true),
            bool boolean => boolean ? "true" : "false",
            float single => single.ToString("R", CultureInfo.InvariantCulture) + "f",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "default",
        };
    }

    private static string? NamedString(AttributeData attribute, string name)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value as string;
    private static int NamedInt(AttributeData attribute, string name, int fallback)
        => attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value is int value ? value : fallback;
    private static double? NamedDouble(AttributeData attribute, string name)
    {
        object? value = attribute.NamedArguments.FirstOrDefault(pair => pair.Key == name).Value.Value;
        return value is double number && !double.IsNaN(number) ? number : null;
    }

    private static void Emit(SourceProductionContext context, ImmutableArray<StoryModel?> models, string assemblyName)
    {
        var valid = new List<StoryModel>();
        foreach (StoryModel? model in models)
        {
            if (model is null) continue;
            foreach (var diagnostic in model.Diagnostics)
                context.ReportDiagnostic(Diagnostic.Create(diagnostic.Descriptor, diagnostic.Location, diagnostic.Args));
            if (model.Valid) valid.Add(model);
        }
        if (valid.Count == 0) return;
        valid.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));

        var source = new StringBuilder();
        source.AppendLine("// <auto-generated/> Luxel.UI.Generators.ComponentStoryGenerator");
        source.AppendLine("#nullable enable");
        source.AppendLine("namespace Luxel.Gallery.Generated");
        source.AppendLine("{");
        source.Append("    public static class ComponentStoryRegistration_").AppendLine(Sanitize(assemblyName));
        source.AppendLine("    {");
        source.AppendLine("        [global::System.Runtime.CompilerServices.ModuleInitializer]");
        source.AppendLine("        internal static void Init()");
        source.AppendLine("        {");
        source.AppendLine("            var builder = new global::Luxel.UI.StoryCatalogBuilder();");
        source.AppendLine("            Register(builder);");
        source.AppendLine("            foreach (global::Luxel.UI.StoryInfo story in builder.Build().All)");
        source.AppendLine("                global::Luxel.UI.StoryRegistry.Register(story);");
        source.AppendLine("        }");
        source.AppendLine();
        source.AppendLine("        public static void Register(global::Luxel.UI.StoryCatalogBuilder builder)");
        source.AppendLine("        {");
        source.AppendLine("            global::System.ArgumentNullException.ThrowIfNull(builder);");
        for (int i = 0; i < valid.Count; i++)
        {
            StoryModel story = valid[i];
            source.Append("            builder.Add(new global::Luxel.UI.StoryInfo(")
                .Append(Literal(story.Path)).Append(", ").Append(story.Width).Append(", ").Append(story.Height).Append(", ")
                .Append(story.Theme is null ? "null" : Literal(story.Theme)).Append(", static ctx => Build_").Append(i).Append("(ctx), ")
                .Append(story.Order).Append(", ").Append(Literal(story.Source)).Append(", ").Append(story.RealWindowOnly ? "true" : "false")
                .Append(", ").Append(story.SampleBundle is null ? "null" : Literal(story.SampleBundle))
                .Append(story.RuntimeBundleId is null ? "" : ", RuntimeBundleId: " + Literal(story.RuntimeBundleId)).AppendLine("));");
        }
        source.AppendLine("        }");

        for (int i = 0; i < valid.Count; i++) EmitBuilder(source, valid[i], i);
        source.AppendLine("    }");
        source.AppendLine("}");
        context.AddSource("ComponentStories.g.cs", SourceText.From(source.ToString(), Encoding.UTF8));
    }

    private static void EmitBuilder(StringBuilder source, StoryModel story, int index)
    {
        source.AppendLine();
        source.Append("        private static global::Luxel.UI.Widget Build_").Append(index).AppendLine("(global::Luxel.UI.StoryContext ctx)");
        source.AppendLine("        {");
        for (int i = 0; i < story.Args.Count; i++)
        {
            ArgModel arg = story.Args[i];
            source.Append("            global::Luxel.UI.Signal<").Append(arg.Type).Append("> arg").Append(i)
                .Append(" = ctx.Arg<").Append(arg.Type).Append(">(").Append(Literal(arg.Name)).Append(", ").Append(arg.Default);
            if (arg.Description is not null || arg.Order != 1000 || arg.Min.HasValue || arg.Max.HasValue || arg.Step.HasValue)
            {
                source.Append(", new global::Luxel.UI.StoryArgOptions<").Append(arg.Type).Append("> { ");
                if (arg.Description is not null) source.Append("Description = ").Append(Literal(arg.Description)).Append(", ");
                if (arg.Order != 1000) source.Append("Order = ").Append(arg.Order).Append(", ");
                if (arg.Min.HasValue) source.Append("Min = ").Append(arg.Min.Value.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
                if (arg.Max.HasValue) source.Append("Max = ").Append(arg.Max.Value.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
                if (arg.Step.HasValue) source.Append("Step = ").Append(arg.Step.Value.ToString("R", CultureInfo.InvariantCulture)).Append(", ");
                source.Append("}");
            }
            source.AppendLine(");");
        }
        source.AppendLine("            return new global::Luxel.Gallery.ComponentStoryPreview(() =>");
        source.AppendLine("            {");
        source.Append("                ").Append(story.ComponentType).Append(" component = ").Append(story.FactoryType).Append('.').Append(story.FactoryMethod).Append('(');
        bool first = true;
        for (int i = 0; i < story.Args.Count; i++)
        {
            ArgModel arg = story.Args[i];
            if (arg.Apply is not null) continue;
            if (!first) source.Append(", ");
            first = false;
            source.Append(arg.Parameter).Append(": arg").Append(i).Append(".Value");
        }
        source.AppendLine(");");
        for (int i = 0; i < story.Args.Count; i++)
        {
            ArgModel arg = story.Args[i];
            if (arg.Apply is not null)
                source.Append("                ").Append(story.StoryType).Append('.').Append(arg.Apply).Append("(component, arg").Append(i).AppendLine(".Value);");
        }
        source.Append("                return ");
        if (story.Template is not null) source.Append(story.StoryType).Append('.').Append(story.Template).Append("(component)");
        else source.Append("component");
        source.AppendLine(";");
        source.AppendLine("            });");
        source.AppendLine("        }");
    }

    private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, true);
    private static string Sanitize(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (char character in value) result.Append(char.IsLetterOrDigit(character) ? character : '_');
        return result.ToString();
    }
}
