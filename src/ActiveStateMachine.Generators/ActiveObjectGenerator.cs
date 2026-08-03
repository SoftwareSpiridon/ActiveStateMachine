using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ActiveStateMachine.Generators
{
    [Generator(LanguageNames.CSharp)]
    public sealed class ActiveObjectGenerator : IIncrementalGenerator
    {
        private const string ActiveObjectAsyncAttributeName = "ActiveStateMachine.Attributes.ActiveObjectAsyncAttribute";
        private const string ActiveObjectSyncAttributeName = "ActiveStateMachine.Attributes.ActiveObjectSyncAttribute";
        private const string StateTriggerAttributeName = "ActiveStateMachine.Attributes.StateTriggerAttribute";
        private const string TaskTypeName = "System.Threading.Tasks.Task";
        private const int MaxParameters = 3;

        /// <summary>Which implementation flavour a class was tagged with.</summary>
        internal enum ActiveObjectKind
        {
            Async,
            Sync,
        }

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            // Inject the marker attributes into the consuming compilation so no separate attributes
            // assembly is needed. This runs before the pipelines below, so ForAttributeWithMetadataName
            // discovers usages against these emitted types.
            context.RegisterPostInitializationOutput(static ctx =>
                ctx.AddSource(EmbeddedAttributes.HintName, EmbeddedAttributes.Source));

            RegisterPipeline(context, ActiveObjectAsyncAttributeName, ActiveObjectKind.Async);
            RegisterPipeline(context, ActiveObjectSyncAttributeName, ActiveObjectKind.Sync);
        }

        private static void RegisterPipeline(
            IncrementalGeneratorInitializationContext context, string attributeName, ActiveObjectKind kind)
        {
            IncrementalValuesProvider<GenerationResult> results = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    attributeName,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: (ctx, _) => Transform(ctx, kind))
                .Where(static r => r is not null)
                .Select(static (r, _) => r!);

            context.RegisterSourceOutput(results, static (spc, result) =>
            {
                foreach (DiagnosticInfo diagnostic in result.Diagnostics)
                {
                    spc.ReportDiagnostic(diagnostic.ToDiagnostic());
                }

                if (result.Info is { } info)
                {
                    string source = result.Kind == ActiveObjectKind.Async
                        ? AsyncEmitter.Emit(info)
                        : SyncEmitter.Emit(info);
                    spc.AddSource($"{info.ClassName}.g.cs", source);
                }
            });
        }

        private static GenerationResult? Transform(GeneratorAttributeSyntaxContext context, ActiveObjectKind kind)
        {
            if (context.TargetSymbol is not INamedTypeSymbol classSymbol ||
                context.TargetNode is not ClassDeclarationSyntax classSyntax)
            {
                return null;
            }

            var diagnostics = new List<DiagnosticInfo>();

            // Rule: the class must be partial.
            bool isPartial = classSyntax.Modifiers.Any(SyntaxKind.PartialKeyword);
            if (!isPartial)
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NotPartial, classSyntax, classSymbol.Name));
            }

            // Read the [ActiveObject*(stateType, triggerType)] arguments.
            AttributeData attribute = context.Attributes[0];
            if (attribute.ConstructorArguments.Length < 2 ||
                attribute.ConstructorArguments[0].Value is not ITypeSymbol stateType ||
                attribute.ConstructorArguments[1].Value is not ITypeSymbol triggerType)
            {
                return new GenerationResult(null, kind, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
            }

            string stateTypeName = stateType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            string triggerTypeName = triggerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            // Optional Name = "..." named argument on the attribute.
            string? name = null;
            foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
            {
                if (named.Key == "Name" && named.Value.Value is string s && !string.IsNullOrWhiteSpace(s))
                {
                    name = s;
                }
            }

            var methods = new List<TriggerMethodInfo>();
            foreach (IMethodSymbol method in classSymbol.GetMembers().OfType<IMethodSymbol>())
            {
                AttributeData? triggerAttribute = method.GetAttributes().FirstOrDefault(
                    a => a.AttributeClass?.ToDisplayString() == StateTriggerAttributeName);
                if (triggerAttribute is null)
                {
                    continue;
                }

                SyntaxNode? methodSyntax = method.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();

                // Rule: the return type must match the flavour (Task for async, void for sync).
                if (kind == ActiveObjectKind.Async)
                {
                    if (method.ReturnType.ToDisplayString() != TaskTypeName)
                    {
                        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustReturnTask, methodSyntax, method.Name));
                        continue;
                    }
                }
                else if (method.ReturnType.SpecialType != SpecialType.System_Void)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustReturnVoid, methodSyntax, method.Name));
                    continue;
                }

                // Rule: the method must be partial.
                if (methodSyntax is MethodDeclarationSyntax mds && !mds.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustBePartial, methodSyntax, method.Name));
                    continue;
                }

                // Rule: at most 3 parameters.
                if (method.Parameters.Length > MaxParameters)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.TooManyParameters, methodSyntax, method.Name, method.Parameters.Length.ToString()));
                    continue;
                }

                // Resolve the trigger value text, e.g. "PhoneTrigger.CallDialed".
                string? triggerText = triggerAttribute.ConstructorArguments.Length > 0
                    ? triggerAttribute.ConstructorArguments[0].Value as string
                    : null;
                if (string.IsNullOrWhiteSpace(triggerText))
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MissingTrigger, methodSyntax, method.Name));
                    continue;
                }

                // Normalize to a fully-qualified enum member: global::Ns.PhoneTrigger.CallDialed
                string member = triggerText!.Substring(triggerText.LastIndexOf('.') + 1).Trim();
                string qualifiedTrigger = $"{triggerTypeName}.{member}";

                // Optional Wait = false named argument: when false the generated trigger method
                // enqueues the message and returns immediately (async) / does not block (sync),
                // instead of waiting for the worker to process it. Defaults to true.
                bool wait = true;
                foreach (KeyValuePair<string, TypedConstant> named in triggerAttribute.NamedArguments)
                {
                    if (named.Key == "Wait" && named.Value.Value is bool b)
                    {
                        wait = b;
                    }
                }

                var parameters = method.Parameters
                    .Select(p => new ParameterInfo(
                        p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        p.Name))
                    .ToArray();

                methods.Add(new TriggerMethodInfo(
                    method.Name,
                    AccessibilityText(method.DeclaredAccessibility),
                    qualifiedTrigger,
                    wait,
                    new EquatableArray<ParameterInfo>(parameters)));
            }

            string? ns = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            var info = new ActiveObjectInfo(
                ns,
                classSymbol.Name,
                AccessibilityText(classSymbol.DeclaredAccessibility),
                stateTypeName,
                triggerTypeName,
                name,
                new EquatableArray<TriggerMethodInfo>(methods.ToArray()));

            return new GenerationResult(info, kind, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        private static string AccessibilityText(Accessibility accessibility) => accessibility switch
        {
            Accessibility.Public => "public",
            Accessibility.Internal => "internal",
            Accessibility.Protected => "protected",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            Accessibility.Private => "private",
            _ => "internal",
        };

        internal sealed record GenerationResult(
            ActiveObjectInfo? Info, ActiveObjectKind Kind, EquatableArray<DiagnosticInfo> Diagnostics);
    }
}
