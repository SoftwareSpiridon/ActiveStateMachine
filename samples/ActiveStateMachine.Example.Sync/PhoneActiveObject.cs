using ActiveStateMachine.Attributes;

namespace ActiveStateMachine.Example.Sync;

// These map directly to the Stateless example.
public enum PhoneState { OffHook, Ringing, Connected, OnHold }

public enum PhoneTrigger { CallDialed, HungUp, CallConnected, PlacedOnHold, TakenOffHold, LeftMessage }

/// <summary>
/// A classic, "old fashioned" Active Object implementation. Method calls (like <see cref="Dial"/>)
/// enqueue a message onto a <see cref="System.Collections.Concurrent.BlockingCollection{T}"/> that
/// is drained by a single dedicated background <see cref="System.Threading.Thread"/>. Each trigger
/// method returns <c>void</c> and blocks the caller until the message has been processed on that
/// thread, guaranteeing absolute thread-safety for the internal state machine without any locks and
/// without <c>async</c>/<c>await</c>, <c>Task</c>-returning methods, or Channels.
///
/// All of that plumbing — the queue, the worker thread, the message classes, the constructor and
/// disposal — is emitted by the ActiveStateMachine source generator; the only code below is the
/// state configuration and the public trigger API.
/// </summary>
[ActiveObjectSync(typeof(PhoneState), typeof(PhoneTrigger), Name = "Sync-Phone")]
public partial class PhoneActiveObject : IDisposable
{
    /// <summary>Current state of the phone. Reads are only safe between blocking calls.</summary>
    public PhoneState State => _machine.State;

    // These proxy methods are the public API of the Active Object. Each blocks until the worker
    // thread has applied the trigger; the generator implements every body.

    // Parameterized trigger (1 argument) -> TriggerWithParameters<string>.
    [StateTrigger(PhoneTrigger.CallDialed)]
    public partial void Dial(string number);

    [StateTrigger(PhoneTrigger.HungUp)]
    public partial void HangUp();

    [StateTrigger(PhoneTrigger.CallConnected)]
    public partial void ConnectCall();

    [StateTrigger(PhoneTrigger.PlacedOnHold)]
    public partial void PutOnHold();

    [StateTrigger(PhoneTrigger.TakenOffHold)]
    public partial void TakeOffHold();

    // The user configures the underlying Stateless machine here. The generator calls this
    // from the generated constructor.
    partial void ConfigureStateMachine()
    {
        _machine.Configure(PhoneState.OffHook)
            .Permit(PhoneTrigger.CallDialed, PhoneState.Ringing);

        _machine.Configure(PhoneState.Ringing)
            .OnEntryFrom(TriggerDial, number => Console.WriteLine($"[Phone] Ringing... Dialing number: {number}"))
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook)
            .Permit(PhoneTrigger.CallConnected, PhoneState.Connected);

        _machine.Configure(PhoneState.Connected)
            // The worker Thread is named after the Active Object, so it shows up here (and in the debugger).
            .OnEntry(() => Console.WriteLine($"[Phone] Call Connected! (on thread '{Thread.CurrentThread.Name}')"))
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
