namespace ActiveStateMachine.Generators
{
    /// <summary>A single parameter of a trigger method.</summary>
    internal readonly record struct ParameterInfo(string Type, string Name);

    /// <summary>A method marked with [StateTrigger].</summary>
    internal sealed record TriggerMethodInfo(
        string Name,
        string Accessibility,
        string Trigger,
        EquatableArray<ParameterInfo> Parameters)
    {
        public int ParameterCount => Parameters.Count;

        public bool IsParameterized => Parameters.Count > 0;
    }

    /// <summary>A class marked with an Active Object attribute, fully described for emission.</summary>
    internal sealed record ActiveObjectInfo(
        string? Namespace,
        string ClassName,
        string Accessibility,
        string StateType,
        string TriggerType,
        string? Name,
        EquatableArray<TriggerMethodInfo> Methods);
}
