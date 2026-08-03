using System.Collections.Concurrent;
using ActiveStateMachine.Example.Sync;
using Xunit;

namespace ActiveStateMachine.Tests;

public class SyncPhoneActiveObjectTests
{
    [Fact]
    public void Parameterized_trigger_transitions_state()
    {
        using var phone = new PhoneActiveObject(PhoneState.OffHook);
        Assert.Equal(PhoneState.OffHook, phone.State);

        phone.Dial("555-1234");

        Assert.Equal(PhoneState.Ringing, phone.State);
    }

    [Fact]
    public void Full_happy_path_walks_through_states()
    {
        using var phone = new PhoneActiveObject(PhoneState.OffHook);

        phone.Dial("555-0000");
        Assert.Equal(PhoneState.Ringing, phone.State);

        phone.ConnectCall();
        Assert.Equal(PhoneState.Connected, phone.State);

        phone.PutOnHold();
        Assert.Equal(PhoneState.OnHold, phone.State);

        phone.TakeOffHold();
        Assert.Equal(PhoneState.Connected, phone.State);

        phone.HangUp();
        Assert.Equal(PhoneState.OffHook, phone.State);
    }

    [Fact]
    public void Trigger_call_blocks_until_processed()
    {
        using var phone = new PhoneActiveObject(PhoneState.OffHook);

        // A synchronous call must return only after the transition has been applied, so the
        // state is already up to date on the very next line (no polling / waiting needed).
        phone.Dial("555-5555");
        Assert.Equal(PhoneState.Ringing, phone.State);
    }

    [Fact]
    public void Invalid_trigger_is_ignored_and_worker_survives()
    {
        using var phone = new PhoneActiveObject(PhoneState.OffHook);

        // ConnectCall is not permitted from OffHook; OnUnhandledTrigger swallows it.
        phone.ConnectCall();
        Assert.Equal(PhoneState.OffHook, phone.State);

        // The worker thread survives: a subsequent valid call still works.
        phone.Dial("555-2222");
        Assert.Equal(PhoneState.Ringing, phone.State);
    }

    [Fact]
    public void Concurrent_callers_are_serialized_by_the_worker_thread()
    {
        using var phone = new PhoneActiveObject(PhoneState.OffHook);
        phone.Dial("555-3333");
        phone.ConnectCall();

        // Hammer the object from many threads. Each call blocks until processed; the single worker
        // thread guarantees the state machine is never touched concurrently. An unsynchronized
        // Stateless machine would corrupt or throw under this load — here nothing throws.
        Parallel.For(0, 200, _ =>
        {
            phone.PutOnHold();
            phone.TakeOffHold();
        });

        // The interleaved burst leaves the phone in either Connected or OnHold; normalize with one
        // final call (TakenOffHold from Connected is simply ignored) so the assertion is deterministic.
        phone.TakeOffHold();
        Assert.Equal(PhoneState.Connected, phone.State);
    }

    [Fact]
    public void Exception_from_worker_propagates_to_the_caller()
    {
        // A configuration whose entry action throws lets us observe that the exception is
        // marshalled back to the blocking caller rather than swallowed on the worker thread.
        using var boom = new Boom(BoomState.A);

        var ex = Assert.Throws<InvalidOperationException>(() => boom.Go());
        Assert.Equal("boom", ex.Message);
    }

    [Fact]
    public void Calls_after_dispose_throw()
    {
        var phone = new PhoneActiveObject(PhoneState.OffHook);
        phone.Dispose();

        Assert.Throws<InvalidOperationException>(() => phone.Dial("555-4444"));
    }

    [Fact]
    public void Name_from_attribute_is_used_for_the_worker_thread()
    {
        using var ao = new SyncNamed(BoomState.A);

        Assert.Equal("sync-fixture", ao.Name);

        // The captured value is Thread.CurrentThread.Name observed inside the worker thread.
        ao.Go();
        Assert.Equal("sync-fixture", ao.CapturedThreadName);
    }

    [Fact]
    public void Name_from_constructor_overrides_the_attribute()
    {
        using var ao = new SyncNamed(BoomState.A, "custom-thread");

        Assert.Equal("custom-thread", ao.Name);

        ao.Go();
        Assert.Equal("custom-thread", ao.CapturedThreadName);
    }

    [Fact]
    public void OnDisposing_hook_runs_exactly_once_on_dispose()
    {
        var ao = new SyncDisposeHook(BoomState.A);
        Assert.Equal(0, ao.OnDisposingCalls);

        ao.Dispose();
        Assert.Equal(1, ao.OnDisposingCalls);
    }
}

public enum BoomState { A, B }

public enum BoomTrigger { Explode }

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class Boom : IDisposable
{
    [ActiveStateMachine.Attributes.StateTrigger("BoomTrigger.Explode")]
    public partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        _machine.Configure(BoomState.B)
            .OnEntry(() => throw new InvalidOperationException("boom"));
    }
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class SyncDisposeHook : IDisposable
{
    public int OnDisposingCalls;

    [ActiveStateMachine.Attributes.StateTrigger("BoomTrigger.Explode")]
    public partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);
    }

    partial void OnDisposing() => OnDisposingCalls++;
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger), Name = "sync-fixture")]
public partial class SyncNamed : IDisposable
{
    public string? CapturedThreadName;

    [ActiveStateMachine.Attributes.StateTrigger("BoomTrigger.Explode")]
    public partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        // Runs on the worker thread, so it observes that thread's name.
        _machine.Configure(BoomState.B)
            .OnEntry(() => CapturedThreadName = Thread.CurrentThread.Name);
    }
}
