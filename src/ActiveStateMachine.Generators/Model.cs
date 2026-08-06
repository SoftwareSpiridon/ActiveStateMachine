namespace ActiveStateMachine.Generators
{
    /// <summary>A single parameter of a trigger method.</summary>
    internal readonly record struct ParameterInfo(string Type, string Name);

    /// <summary>A method marked with [StateTrigger].</summary>
    internal sealed record TriggerMethodInfo(
        string Name,
        string Modifiers,
        string Trigger,
        bool Wait,
        EquatableArray<ParameterInfo> Parameters)
    {
        public int ParameterCount => Parameters.Count;

        public bool IsParameterized => Parameters.Count > 0;
    }

    /// <summary>A class marked with an Active Object attribute, fully described for emission.</summary>
    internal sealed record ActiveObjectInfo(
        string? Namespace,
        string ClassName,
        string TypeParameters,
        string TypeParameterConstraints,
        string Accessibility,
        string StateType,
        string TriggerType,
        string? Name,
        bool BaseHasOverridableDispose,
        bool BaseHasOverridableDisposeAsync,
        EquatableArray<TriggerMethodInfo> Methods)
    {
        /// <summary>The class name with its type-parameter list, e.g. <c>Foo&lt;T&gt;</c>.</summary>
        public string ClassNameWithTypeParameters => ClassName + TypeParameters;
    }
}
