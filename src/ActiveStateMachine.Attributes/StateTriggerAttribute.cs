using System;

namespace ActiveStateMachine.Attributes
{
    /// <summary>
    /// Marks a partial method as the public entry point for a state machine trigger.
    /// The source generator implements the method body so that calling it enqueues a
    /// message onto the Active Object's mailbox and fires the associated trigger on the
    /// worker thread.
    /// </summary>
    /// <remarks>
    /// The decorated method MUST be <c>partial</c> and return <c>Task</c>. A method with
    /// zero parameters maps to a plain trigger; a method with 1-3 parameters maps to a
    /// <c>TriggerWithParameters</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class StateTriggerAttribute : Attribute
    {
        /// <summary>
        /// Creates the attribute.
        /// </summary>
        /// <param name="trigger">
        /// The trigger enum value, written as it appears in source, e.g.
        /// <c>"PhoneTrigger.CallDialed"</c>.
        /// </param>
        public StateTriggerAttribute(string trigger)
        {
            Trigger = trigger;
        }

        /// <summary>The trigger enum value expressed as source text.</summary>
        public string Trigger { get; }
    }
}
