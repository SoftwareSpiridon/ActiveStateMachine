using ActiveStateMachine.Attributes;

namespace ActiveStateMachine.Example.Async;

// These map directly to the Stateless example.
public enum PhoneState { OffHook, Ringing, Connected, OnHold }

public enum PhoneTrigger { CallDialed, HungUp, CallConnected, PlacedOnHold, TakenOffHold, LeftMessage }

/// <summary>
/// A modern Active Object implementation. Method calls (like <see cref="DialAsync"/>) execute
/// asynchronously by enqueuing a message to a Channel. A dedicated worker Task processes those
/// messages sequentially, ensuring absolute thread-safety for the internal state machine without
/// any locks. All of that plumbing — the message queue, the worker loop, the message records, the
/// constructor and disposal — is emitted by the ActiveStateMachine source generator; the only
/// code below is the state configuration and the public trigger API.
/// </summary>
[ActiveObjectAsync(typeof(PhoneState), typeof(PhoneTrigger), Name = "Async-Phone")]
public partial class PhoneActiveObject : IAsyncDisposable
{
    /// <summary>Current state of the phone. Reads are only safe between awaited calls.</summary>
    public PhoneState State => _machine.State;

    // These proxy methods are the public API of the Active Object. They hide the existence of
    // the message queue entirely from the caller; the generator implements each body.

    // Parameterized trigger (1 argument) -> TriggerWithParameters<string>.
    [StateTrigger("PhoneTrigger.CallDialed")]
    public partial Task DialAsync(string number);

    [StateTrigger("PhoneTrigger.HungUp")]
    public partial Task HangUpAsync();

    [StateTrigger("PhoneTrigger.CallConnected")]
    public partial Task ConnectCallAsync();

    [StateTrigger("PhoneTrigger.PlacedOnHold")]
    public partial Task PutOnHoldAsync();

    [StateTrigger("PhoneTrigger.TakenOffHold")]
    public partial Task TakeOffHoldAsync();

    // The user configures the underlying Stateless machine here. The generator calls this
    // from the generated constructor.
    partial void ConfigureStateMachine()
    {
        _machine.Configure(PhoneState.OffHook)
            .Permit(PhoneTrigger.CallDialed, PhoneState.Ringing);

        _machine.Configure(PhoneState.Ringing)
            .OnEntryFrom(TriggerDialAsync, number => Console.WriteLine($"[Phone] Ringing... Dialing number: {number}"))
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook)
            .Permit(PhoneTrigger.CallConnected, PhoneState.Connected);

        _machine.Configure(PhoneState.Connected)
            // CurrentActiveObjectName flows with the worker's async context.
            .OnEntry(() => Console.WriteLine($"[Phone] Call Connected! (on active object '{CurrentActiveObjectName.Value}')"))
            .OnExit(() => Console.WriteLine("[Phone] Call Ended!"))
            .Permit(PhoneTrigger.LeftMessage, PhoneState.OffHook)
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook)
            .Permit(PhoneTrigger.PlacedOnHold, PhoneState.OnHold);

        _machine.Configure(PhoneState.OnHold)
            .SubstateOf(PhoneState.Connected) // Inherits behavior from Connected
            .OnEntry(() => Console.WriteLine("[Phone] Call Placed On Hold."))
            .OnExit(() => Console.WriteLine("[Phone] Call Resumed."))
            .Permit(PhoneTrigger.TakenOffHold, PhoneState.Connected)
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook);

        // Graceful handling of bad commands.
        _machine.OnUnhandledTrigger((state, trigger) =>
            Console.WriteLine($"[Warning] Invalid trigger '{trigger}' in state '{state}' ignored."));

        _machine.OnTransitioned(t =>
            Console.WriteLine($"[State Change] {t.Source} -> {t.Destination}"));
    }
}
