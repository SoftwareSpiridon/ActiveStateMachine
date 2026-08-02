using Microsoft.CodeAnalysis;

namespace ActiveStateMachine.Generators
{
    internal static class Diagnostics
    {
        private const string Category = "ActiveStateMachine";

        /// <summary>Class decorated with [ActiveObject] must be partial.</summary>
        public static readonly DiagnosticDescriptor NotPartial = new(
            id: "ASM001",
            title: "Active Object class must be partial",
            messageFormat: "Class '{0}' is marked with [ActiveObject] but is not declared 'partial'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>[StateTrigger] method must return Task.</summary>
        public static readonly DiagnosticDescriptor MustReturnTask = new(
            id: "ASM002",
            title: "State trigger method must return Task",
            messageFormat: "Method '{0}' is marked with [StateTrigger] but does not return 'System.Threading.Tasks.Task'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>[StateTrigger] method has too many parameters.</summary>
        public static readonly DiagnosticDescriptor TooManyParameters = new(
            id: "ASM003",
            title: "State trigger method has too many parameters",
            messageFormat: "Method '{0}' is marked with [StateTrigger] and has {1} parameters; a maximum of 3 is supported",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>[StateTrigger] method must be partial.</summary>
        public static readonly DiagnosticDescriptor MustBePartial = new(
            id: "ASM004",
            title: "State trigger method must be partial",
            messageFormat: "Method '{0}' is marked with [StateTrigger] but is not declared 'partial'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>[StateTrigger] value could not be resolved.</summary>
        public static readonly DiagnosticDescriptor MissingTrigger = new(
            id: "ASM005",
            title: "State trigger value is missing",
            messageFormat: "Method '{0}' is marked with [StateTrigger] but no trigger value was supplied",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
