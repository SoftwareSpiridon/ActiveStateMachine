using ActiveStateMachine.Attributes;

namespace ActiveStateMachine.Example;

public enum PhoneState
{
    OffHook,
    Ringing,
    Connected,
    OnHold,
    PhoneDestroyed,
}

public enum PhoneTrigger
{
    CallDialed,
    CallConnected,
    LeftMessage,
    PlacedOnHold,
    TakenOffHold,
    HungUp,
    PhoneHurledAgainstWall,
}


[ActiveObject(typeof(PhoneState), typeof(PhoneTrigger))]
public partial class PhoneActiveObject : IAsyncDisposable
{
    // The user configures the underlying Stateless machine here. The generator calls this
    // from the generated constructor.
    partial void ConfigureStateMachine()
    {
        _machine.Configure(PhoneState.OffHook)
            .Permit(PhoneTrigger.CallDialed, PhoneState.Ringing);

        _machine.Configure(PhoneState.Ringing)
            .OnEntryFrom(_trigger_DialAsync, number => Console.WriteLine($"    [machine] Dialing {number}..."))
            .Permit(PhoneTrigger.CallConnected, PhoneState.Connected)
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook);

        _machine.Configure(PhoneState.Connected)
            .OnEntry(() => Console.WriteLine("    [machine] Call connected."))
            .Permit(PhoneTrigger.PlacedOnHold, PhoneState.OnHold)
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook);

        _machine.Configure(PhoneState.OnHold)
            .OnEntry(() => Console.WriteLine("    [machine] On hold."))
            .Permit(PhoneTrigger.TakenOffHold, PhoneState.Connected)
            .Permit(PhoneTrigger.HungUp, PhoneState.OffHook)
            .Permit(PhoneTrigger.PhoneHurledAgainstWall, PhoneState.PhoneDestroyed);

        _machine.Configure(PhoneState.PhoneDestroyed)
            .OnEntry(() => Console.WriteLine("    [machine] The phone is now a pile of plastic."));
    }

    /// <summary>Current state of the phone. Reads are only safe between awaited calls.</summary>
    public PhoneState State => _machine.State;

    // Parameterized trigger (1 argument) -> TriggerWithParameters<string>.
    [StateTrigger("PhoneTrigger.CallDialed")]
    public partial Task DialAsync(string number);

    [StateTrigger("PhoneTrigger.CallConnected")]
    public partial Task ConnectCallAsync();

    [StateTrigger("PhoneTrigger.PlacedOnHold")]
    public partial Task PlaceOnHoldAsync();

    [StateTrigger("PhoneTrigger.TakenOffHold")]
    public partial Task TakeOffHoldAsync();

    [StateTrigger("PhoneTrigger.PhoneHurledAgainstWall")]
    public partial Task HurlAgainstWallAsync();

    [StateTrigger("PhoneTrigger.HungUp")]
    public partial Task HangUpAsync();
}
