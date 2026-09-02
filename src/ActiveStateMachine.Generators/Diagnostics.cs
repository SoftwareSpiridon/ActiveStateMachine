using Microsoft.CodeAnalysis;

namespace ActiveStateMachine.Generators
{
    internal static class Diagnostics
    {
        private const string Category = "ActiveStateMachine";

        /// <summary>Class decorated with an Active Object attribute must be partial.</summary>
        public static readonly DiagnosticDescriptor NotPartial = new(
            id: "ASM001",
            title: "Active Object class must be partial",
            messageFormat: "Class '{0}' is marked as an Active Object but is not declared 'partial'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>[StateTrigger] method on an [ActiveObjectAsync] class must return Task.</summary>
        public static readonly DiagnosticDescriptor MustReturnTask = new(
            id: "ASM002",
            title: "Async state trigger method must return Task",
            messageFormat: "Method '{0}' is a trigger on an [ActiveObjectAsync] class but does not return 'System.Threading.Tasks.Task'",
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

        /// <summary>[StateTrigger] value could not be resolved to an enum member.</summary>
        public static readonly DiagnosticDescriptor MissingTrigger = new(
            id: "ASM005",
            title: "State trigger must be an enum value",
            messageFormat: "Method '{0}' is marked with [StateTrigger] but was not given an enum value",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>[StateTrigger] method on an [ActiveObjectSync] class must return void.</summary>
        public static readonly DiagnosticDescriptor MustReturnVoid = new(
            id: "ASM006",
            title: "Sync state trigger method must return void",
            messageFormat: "Method '{0}' is a trigger on an [ActiveObjectSync] class but does not return 'void'",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// An idle trigger takes no parameters: the worker fires it with no arguments, so it cannot be
        /// a <c>TriggerWithParameters</c>.
        /// </summary>
        public static readonly DiagnosticDescriptor IdleTriggerMustBeParameterless = new(
            id: "ASM007",
            title: "Idle state trigger method must have no parameters",
            messageFormat: "Method '{0}' sets IdleTimeoutMilliseconds and has {1} parameters; the worker fires an idle trigger with no arguments, so it must be parameterless",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>The worker has a single mailbox wait, so it can drive only one idle trigger.</summary>
        public static readonly DiagnosticDescriptor DuplicateIdleTrigger = new(
            id: "ASM008",
            title: "Only one idle state trigger is allowed per Active Object",
            messageFormat: "Class '{0}' declares more than one [StateTrigger] with IdleTimeoutMilliseconds ('{1}' and '{2}'); the worker has a single mailbox wait and can drive only one",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        /// <summary>
        /// The idle tick is implemented for the sync flavour only. Reported rather than ignored so it
        /// fails loudly instead of silently never ticking.
        /// </summary>
        public static readonly DiagnosticDescriptor IdleTriggerSyncOnly = new(
            id: "ASM009",
            title: "Idle state trigger is supported on [ActiveObjectSync] only",
            messageFormat: "Method '{0}' sets IdleTimeoutMilliseconds, which is only supported on an [ActiveObjectSync] class",
            category: Category,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);
    }
}
