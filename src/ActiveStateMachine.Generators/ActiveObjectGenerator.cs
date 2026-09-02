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
                var mds = methodSyntax as MethodDeclarationSyntax;
                if (mds != null && !mds.Modifiers.Any(SyntaxKind.PartialKeyword))
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MustBePartial, methodSyntax, method.Name));
                    continue;
                }

                // Reproduce the user's declaration modifiers (accessibility plus any override / virtual
                // / sealed / new) so the generated implementing partial matches the defining one — this
                // lets a trigger method itself be the public/override entry point (no wrapper needed).
                // 'partial' is excluded because the emitter appends it.
                string modifiers = mds == null
                    ? AccessibilityText(method.DeclaredAccessibility)
                    : string.Join(" ", mds.Modifiers
                        .Where(m => !m.IsKind(SyntaxKind.PartialKeyword))
                        .Select(m => m.Text));

                // Rule: at most 3 parameters.
                if (method.Parameters.Length > MaxParameters)
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.TooManyParameters, methodSyntax, method.Name, method.Parameters.Length.ToString()));
                    continue;
                }

                // Resolve the trigger enum value ([StateTrigger(PhoneTrigger.CallDialed)]) to a
                // fully-qualified enum member, e.g. global::Ns.PhoneTrigger.CallDialed.
                TypedConstant triggerArg = triggerAttribute.ConstructorArguments.Length > 0
                    ? triggerAttribute.ConstructorArguments[0]
                    : default;

                string? qualifiedTrigger = ResolveTrigger(triggerArg);
                if (qualifiedTrigger is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MissingTrigger, methodSyntax, method.Name));
                    continue;
                }

                // Optional Wait = false named argument: when false the generated trigger method
                // enqueues the message and returns immediately (async) / does not block (sync),
                // instead of waiting for the worker to process it. Defaults to true.
                bool wait = true;

                // Optional IdleTimeoutMilliseconds named argument: when positive the worker also fires
                // this trigger itself after that long with an empty mailbox. Zero means no idle tick.
                int idleTimeout = 0;
                foreach (KeyValuePair<string, TypedConstant> named in triggerAttribute.NamedArguments)
                {
                    if (named.Key == "Wait" && named.Value.Value is bool b)
                    {
                        wait = b;
                    }
                    else if (named.Key == "IdleTimeoutMilliseconds" && named.Value.Value is int ms)
                    {
                        idleTimeout = ms;
                    }
                }

                if (idleTimeout > 0)
                {
                    // Only the sync worker has a bounded mailbox wait to hang the tick on; the async
                    // flavour would need a linked cancellation per iteration. Fail loudly rather than
                    // emit a class that silently never ticks.
                    if (kind == ActiveObjectKind.Async)
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.IdleTriggerSyncOnly, methodSyntax, method.Name));
                        continue;
                    }

                    // The worker fires it as _machine.Fire(trigger), with nothing to pass.
                    if (method.Parameters.Length > 0)
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.IdleTriggerMustBeParameterless, methodSyntax, method.Name,
                            method.Parameters.Length.ToString()));
                        continue;
                    }

                    TriggerMethodInfo? existingIdle = methods.FirstOrDefault(m => m.IsIdleTrigger);
                    if (existingIdle is not null)
                    {
                        diagnostics.Add(DiagnosticInfo.Create(
                            Diagnostics.DuplicateIdleTrigger, methodSyntax, classSymbol.Name,
                            existingIdle.Name, method.Name));
                        continue;
                    }
                }

                var parameters = method.Parameters
                    .Select(p => new ParameterInfo(
                        p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                        p.Name))
                    .ToArray();

                methods.Add(new TriggerMethodInfo(
                    method.Name,
                    modifiers,
                    qualifiedTrigger,
                    wait,
                    idleTimeout,
                    new EquatableArray<ParameterInfo>(parameters)));
            }

            string? ns = classSymbol.ContainingNamespace.IsGlobalNamespace
                ? null
                : classSymbol.ContainingNamespace.ToDisplayString();

            var info = new ActiveObjectInfo(
                ns,
                classSymbol.Name,
                TypeParametersText(classSymbol),
                TypeParameterConstraintsText(classSymbol),
                AccessibilityText(classSymbol.DeclaredAccessibility),
                stateTypeName,
                triggerTypeName,
                name,
                BaseHasOverridableMethod(classSymbol, "Dispose", isVoid: true),
                BaseHasOverridableMethod(classSymbol, "DisposeAsync", isVoid: false),
                new EquatableArray<TriggerMethodInfo>(methods.ToArray()));

            return new GenerationResult(info, kind, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));
        }

        /// <summary>
        /// Resolves a <c>[StateTrigger(EnumValue)]</c> argument to a fully-qualified enum member
        /// expression (e.g. <c>global::Ns.PhoneTrigger.CallDialed</c>), or null if it is not a usable
        /// enum value. Maps the constant's underlying value back to its member name on its enum type.
        /// </summary>
        private static string? ResolveTrigger(TypedConstant triggerArg)
        {
            if (triggerArg.Kind != TypedConstantKind.Enum ||
                triggerArg.Type is not INamedTypeSymbol enumType ||
                triggerArg.Value is null)
            {
                return null;
            }

            string? member = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, triggerArg.Value))
                ?.Name;
            if (member is null)
            {
                return null;
            }

            string enumTypeName = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return $"{enumTypeName}.{member}";
        }

        /// <summary>
        /// True if a base type (excluding the class itself and <c>object</c>) declares an accessible,
        /// overridable (virtual or abstract, non-sealed) parameterless method with the given name — so
        /// the generated disposal method must be emitted as <c>override</c> rather than <c>virtual</c>.
        /// </summary>
        private static bool BaseHasOverridableMethod(INamedTypeSymbol classSymbol, string methodName, bool isVoid)
        {
            for (INamedTypeSymbol? type = classSymbol.BaseType;
                 type is not null && type.SpecialType != SpecialType.System_Object;
                 type = type.BaseType)
            {
                foreach (IMethodSymbol method in type.GetMembers(methodName).OfType<IMethodSymbol>())
                {
                    if (method.Parameters.Length != 0 || method.IsStatic ||
                        method.DeclaredAccessibility == Accessibility.Private)
                    {
                        continue;
                    }

                    if (!method.IsVirtual && !method.IsAbstract && !method.IsOverride)
                    {
                        continue;
                    }

                    if (method.IsSealed)
                    {
                        continue;
                    }

                    bool returnsVoid = method.ReturnsVoid;
                    if (isVoid == returnsVoid)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>The class's type-parameter list, e.g. <c>&lt;T1, T2&gt;</c>, or empty if non-generic.</summary>
        private static string TypeParametersText(INamedTypeSymbol classSymbol)
        {
            if (classSymbol.TypeParameters.Length == 0)
            {
                return string.Empty;
            }

            return "<" + string.Join(", ", classSymbol.TypeParameters.Select(tp => tp.Name)) + ">";
        }

        /// <summary>
        /// The <c>where</c> constraint clauses for the class's type parameters (each prefixed with a
        /// space), reproduced so the generated partial declaration matches the user's declaration.
        /// </summary>
        private static string TypeParameterConstraintsText(INamedTypeSymbol classSymbol)
        {
            var sb = new System.Text.StringBuilder();
            foreach (ITypeParameterSymbol tp in classSymbol.TypeParameters)
            {
                var parts = new List<string>();

                if (tp.HasReferenceTypeConstraint)
                {
                    parts.Add("class");
                }

                if (tp.HasValueTypeConstraint)
                {
                    parts.Add(tp.HasUnmanagedTypeConstraint ? "unmanaged" : "struct");
                }

                if (tp.HasNotNullConstraint)
                {
                    parts.Add("notnull");
                }

                foreach (ITypeSymbol constraintType in tp.ConstraintTypes)
                {
                    parts.Add(constraintType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }

                if (tp.HasConstructorConstraint)
                {
                    parts.Add("new()");
                }

                if (parts.Count > 0)
                {
                    sb.Append($" where {tp.Name} : {string.Join(", ", parts)}");
                }
            }

            return sb.ToString();
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
