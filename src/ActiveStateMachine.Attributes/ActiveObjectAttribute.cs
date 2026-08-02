using System;

namespace ActiveStateMachine.Attributes
{
    /// <summary>
    /// Marks a partial class as an Active Object backed by a Stateless state machine and a
    /// Channel-based mailbox. The source generator emits the state machine, mailbox, worker
    /// loop and message plumbing for the class.
    /// </summary>
    /// <remarks>
    /// The decorated class MUST be declared <c>partial</c> and SHOULD declare a
    /// <c>partial void ConfigureStateMachine();</c> method that configures the underlying
    /// <c>Stateless.StateMachine</c>.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ActiveObjectAttribute : Attribute
    {
        /// <summary>
        /// Creates the attribute.
        /// </summary>
        /// <param name="stateType">The enum type used for states.</param>
        /// <param name="triggerType">The enum type used for triggers.</param>
        public ActiveObjectAttribute(Type stateType, Type triggerType)
        {
            StateType = stateType;
            TriggerType = triggerType;
        }

        /// <summary>The enum type used for states.</summary>
        public Type StateType { get; }

        /// <summary>The enum type used for triggers.</summary>
        public Type TriggerType { get; }
    }
}
