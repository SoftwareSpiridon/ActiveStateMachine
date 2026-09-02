using System.Diagnostics;
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

    [Fact]
    public void Generic_active_object_class_is_generated_and_works()
    {
        using var ao = new SyncGeneric<string>(BoomState.A);
        ao.Go("hello");
        Assert.Equal(BoomState.B, ao.State);
        Assert.Equal("hello", ao.LastPayload);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var ao = new SyncDisposeHook(BoomState.A);

        ao.Dispose();
        ao.Dispose(); // second dispose must be a no-op, not throw

        Assert.Equal(1, ao.OnDisposingCalls);
    }

    [Fact]
    public void StateTrigger_can_be_an_override_of_an_abstract_base_command()
    {
        // The trigger method IS the public override of the base's abstract command — no wrapper.
        SyncCommandBase ao = new SyncOverrideTrigger(BoomState.A);

        ao.Go();

        Assert.Equal(BoomState.B, ((SyncOverrideTrigger)ao).State);
        Assert.True(((SyncOverrideTrigger)ao).Fired);
        ao.Dispose();
    }

    [Fact]
    public void Dispose_through_abstract_base_reference_runs_the_generated_override()
    {
        // The generated Dispose overrides the base's abstract Dispose, so disposing through a
        // base-class reference dispatches to it (and runs OnDisposing).
        SyncDisposeBaseFixture ao = new SyncOverrideDispose(BoomState.A);
        Assert.Equal(0, ((SyncOverrideDispose)ao).OnDisposingCalls);

        ao.Dispose();
        Assert.Equal(1, ((SyncOverrideDispose)ao).OnDisposingCalls);
    }

    [Fact]
    public void Wait_false_trigger_does_not_block_or_throw_and_worker_survives()
    {
        using var ao = new SyncNoWaitBoom(BoomState.A);

        // The entry action throws, but a Wait=false call is fire-and-forget: it must not surface the
        // exception to the caller (contrast with the Wait=true Boom fixture, which does throw).
        ao.Go();

        // The worker still processed the message (and survived the thrown exception).
        Assert.True(ao.Entered.Wait(TimeSpan.FromSeconds(5)));
    }

    // --- Idle tick ---

    [Fact]
    public void An_idle_active_object_ticks_by_itself()
    {
        using var ao = new Ticker(TickerState.Idling);

        Assert.True(SpinUntil(() => ao.Ticks >= 3, TimeSpan.FromSeconds(5)),
            $"Expected at least 3 idle ticks, saw {ao.Ticks}");
    }

    [Fact]
    public void A_busy_active_object_does_not_tick()
    {
        // The tick is idle-driven, not a fixed cadence: while messages keep arriving faster than the
        // timeout the mailbox never goes quiet, and traffic is itself proof the object is alive.
        using var ao = new Ticker(TickerState.Idling);

        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(Ticker.IdleTimeoutMs * 8);
        while (DateTime.UtcNow < deadline)
        {
            ao.DoWork();
            Thread.Sleep(Ticker.IdleTimeoutMs / 5);
        }

        Assert.True(ao.Work > 0, "the fixture never processed any work");
        Assert.Equal(0, ao.Ticks);
    }

    [Fact]
    public void A_throwing_idle_tick_reaches_the_hook_and_the_worker_survives()
    {
        using var ao = new Ticker(TickerState.Idling) { TicksThrow = true };

        Assert.True(SpinUntil(() => ao.IdleTickFailures >= 2, TimeSpan.FromSeconds(5)),
            $"Expected the failure hook to run, saw {ao.IdleTickFailures} failures");

        // The worker is still draining its mailbox, which is the point: a tick that throws has no
        // caller to fault, and letting it end the thread would silently stop the object for good.
        ao.TicksThrow = false;
        ao.DoWork();
        Assert.True(SpinUntil(() => ao.Work == 1, TimeSpan.FromSeconds(5)), "the worker stopped processing");
    }

    [Fact]
    public void A_throwing_idle_tick_is_survivable_without_the_hook()
    {
        // OnIdleTickFailed is an unimplemented partial here, so it is elided - the generated catch
        // must still swallow the exception rather than end the worker.
        using var ao = new TickerWithoutHook(TickerState.Idling);

        Assert.True(SpinUntil(() => ao.Ticks >= 3, TimeSpan.FromSeconds(5)),
            $"The worker stopped after a throwing tick; saw {ao.Ticks} ticks");
    }

    [Fact]
    public void An_idle_trigger_is_still_callable_directly()
    {
        // This is what lets a consumer's tests force a probe instead of sleeping for one.
        using var ao = new Ticker(TickerState.Idling);

        ao.Tick();
        ao.Tick();

        Assert.True(ao.Ticks >= 2);
    }

    [Fact]
    public void Dispose_stops_the_idle_ticking_promptly()
    {
        var ao = new Ticker(TickerState.Idling);
        Assert.True(SpinUntil(() => ao.Ticks >= 1, TimeSpan.FromSeconds(5)), "never ticked");

        var stopwatch = Stopwatch.StartNew();
        ao.Dispose();
        stopwatch.Stop();

        // Dispose completes the queue and joins the worker; the bounded wait means it waits at most
        // one idle timeout rather than blocking indefinitely.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"Dispose took {stopwatch.Elapsed}");

        int after = ao.Ticks;
        Thread.Sleep(Ticker.IdleTimeoutMs * 4);
        Assert.Equal(after, ao.Ticks);
    }

    private static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(5);
        }

        return condition();
    }
}

public enum BoomState { A, B }

public enum BoomTrigger { Explode }

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class Boom : IDisposable
{
    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode)]
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

    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode)]
    public partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);
    }

    partial void OnDisposing() => OnDisposingCalls++;
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class SyncGeneric<TPayload> : IDisposable
{
    public TPayload? LastPayload;

    public BoomState State => _machine.State;

    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode)]
    public partial void Go(TPayload payload);

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        _machine.Configure(BoomState.B)
            .OnEntryFrom(TriggerGo, payload => LastPayload = payload);
    }
}

public abstract class SyncDisposeBaseFixture : IDisposable
{
    public abstract void Dispose();
}

public abstract class SyncCommandBase : IDisposable
{
    public abstract void Go();

    public abstract void Dispose();
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class SyncOverrideTrigger : SyncCommandBase
{
    public bool Fired;

    public BoomState State => _machine.State;

    // The [StateTrigger] method is itself the override of the base's abstract command.
    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode)]
    public override partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        _machine.Configure(BoomState.B)
            .OnEntry(() => Fired = true);
    }
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class SyncOverrideDispose : SyncDisposeBaseFixture
{
    public int OnDisposingCalls;

    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode)]
    public partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);
    }

    partial void OnDisposing() => OnDisposingCalls++;
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger))]
public partial class SyncNoWaitBoom : IDisposable
{
    public readonly ManualResetEventSlim Entered = new();

    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode, Wait = false)]
    public partial void Go();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        // Signals that the worker processed the message, then throws — the fault must be swallowed
        // for a Wait=false trigger (the caller never observes it) and the worker must survive.
        _machine.Configure(BoomState.B)
            .OnEntry(() => { Entered.Set(); throw new InvalidOperationException("boom"); });
    }
}

[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(BoomState), typeof(BoomTrigger), Name = "sync-fixture")]
public partial class SyncNamed : IDisposable
{
    public string? CapturedThreadName;

    [ActiveStateMachine.Attributes.StateTrigger(BoomTrigger.Explode)]
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

public enum TickerState { Idling }

public enum TickerTrigger { Tick, Work }

/// <summary>
/// The idle tick under test. The mailbox wait is the only clock: no timer, task or thread beyond the
/// worker the generator already emits.
/// </summary>
[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(TickerState), typeof(TickerTrigger))]
public partial class Ticker : IDisposable
{
    private int _ticks;
    private int _work;
    private int _idleTickFailures;

    /// <summary>Set to make the next tick throw, so the worker's survival can be observed.</summary>
    public bool TicksThrow { get; set; }

    public int Ticks => Volatile.Read(ref _ticks);

    public int Work => Volatile.Read(ref _work);

    public int IdleTickFailures => Volatile.Read(ref _idleTickFailures);

    public const int IdleTimeoutMs = 50;

    [ActiveStateMachine.Attributes.StateTrigger(TickerTrigger.Tick, IdleTimeoutMilliseconds = IdleTimeoutMs)]
    public partial void Tick();

    [ActiveStateMachine.Attributes.StateTrigger(TickerTrigger.Work, Wait = false)]
    public partial void DoWork();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(TickerState.Idling)
            .InternalTransition(TickerTrigger.Tick, () =>
            {
                Interlocked.Increment(ref _ticks);
                if (TicksThrow)
                {
                    throw new InvalidOperationException("tick failed");
                }
            })
            .InternalTransition(TickerTrigger.Work, () => Interlocked.Increment(ref _work));
    }

    partial void OnIdleTickFailed(Exception exception) => Interlocked.Increment(ref _idleTickFailures);
}

/// <summary>An idle trigger with no OnIdleTickFailed implementation - the hook must stay optional.</summary>
[ActiveStateMachine.Attributes.ActiveObjectSync(typeof(TickerState), typeof(TickerTrigger))]
public partial class TickerWithoutHook : IDisposable
{
    private int _ticks;

    public int Ticks => Volatile.Read(ref _ticks);

    [ActiveStateMachine.Attributes.StateTrigger(TickerTrigger.Tick, IdleTimeoutMilliseconds = 20)]
    public partial void Tick();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(TickerState.Idling)
            .InternalTransition(TickerTrigger.Tick, () =>
            {
                Interlocked.Increment(ref _ticks);
                throw new InvalidOperationException("nobody is listening");
            });
    }
}
