using ActiveStateMachine.Example.Async;
using Xunit;

namespace ActiveStateMachine.Tests;

public class AsyncPhoneActiveObjectTests
{
    [Fact]
    public async Task Parameterized_trigger_transitions_state()
    {
        await using var phone = new PhoneActiveObject(PhoneState.OffHook);
        Assert.Equal(PhoneState.OffHook, phone.State);

        await phone.DialAsync("555-1234");

        Assert.Equal(PhoneState.Ringing, phone.State);
    }

    [Fact]
    public async Task Full_happy_path_walks_through_states()
    {
        await using var phone = new PhoneActiveObject(PhoneState.OffHook);

        await phone.DialAsync("555-0000");
        Assert.Equal(PhoneState.Ringing, phone.State);

        await phone.ConnectCallAsync();
        Assert.Equal(PhoneState.Connected, phone.State);

        await phone.PutOnHoldAsync();
        Assert.Equal(PhoneState.OnHold, phone.State);

        await phone.TakeOffHoldAsync();
        Assert.Equal(PhoneState.Connected, phone.State);

        await phone.HangUpAsync();
        Assert.Equal(PhoneState.OffHook, phone.State);
    }

    [Fact]
    public async Task Invalid_trigger_is_ignored_and_worker_survives()
    {
        await using var phone = new PhoneActiveObject(PhoneState.OffHook);

        // ConnectCall is not permitted from OffHook. Because the machine configures
        // OnUnhandledTrigger, the call completes successfully (ignored) and the state is
        // unchanged rather than throwing.
        await phone.ConnectCallAsync();
        Assert.Equal(PhoneState.OffHook, phone.State);

        // The worker survives: a subsequent valid call still works.
        await phone.DialAsync("555-2222");
        Assert.Equal(PhoneState.Ringing, phone.State);
    }

    [Fact]
    public async Task Messages_are_processed_serially_in_submission_order()
    {
        await using var phone = new PhoneActiveObject(PhoneState.OffHook);

        await phone.DialAsync("555-3333");

        // Fire a burst without awaiting individually; they must apply in order:
        // Connected -> OnHold -> back to Connected.
        var t1 = phone.ConnectCallAsync();
        var t2 = phone.PutOnHoldAsync();
        var t3 = phone.TakeOffHoldAsync();
        await Task.WhenAll(t1, t2, t3);

        Assert.Equal(PhoneState.Connected, phone.State);
    }

    [Fact]
    public async Task Calls_after_dispose_fault_gracefully()
    {
        var phone = new PhoneActiveObject(PhoneState.OffHook);
        await phone.DisposeAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => phone.DialAsync("555-4444"));
    }

    [Fact]
    public async Task High_volume_of_serial_transitions_stays_consistent()
    {
        await using var phone = new PhoneActiveObject(PhoneState.OffHook);

        for (int i = 0; i < 500; i++)
        {
            await phone.DialAsync($"num-{i}");
            await phone.HangUpAsync();
        }

        Assert.Equal(PhoneState.OffHook, phone.State);
    }

    [Fact]
    public async Task Name_from_attribute_flows_as_CurrentActiveObjectName()
    {
        await using var ao = new AsyncNamed(BoomState.A);

        Assert.Equal("async-fixture", ao.Name);

        // The captured value is AsyncNamed.CurrentActiveObjectName.Value observed on the worker.
        await ao.GoAsync();
        Assert.Equal("async-fixture", ao.CapturedName);
    }

    [Fact]
    public async Task Name_from_constructor_overrides_the_attribute()
    {
        await using var ao = new AsyncNamed(BoomState.A, "custom-async");

        Assert.Equal("custom-async", ao.Name);

        await ao.GoAsync();
        Assert.Equal("custom-async", ao.CapturedName);
    }

    [Fact]
    public async Task Distinct_instances_keep_distinct_CurrentActiveObjectName_values()
    {
        await using var a = new AsyncNamed(BoomState.A, "first");
        await using var b = new AsyncNamed(BoomState.A, "second");

        await a.GoAsync();
        await b.GoAsync();

        // Each worker's AsyncLocal context carries its own instance name.
        Assert.Equal("first", a.CapturedName);
        Assert.Equal("second", b.CapturedName);
    }

    [Fact]
    public async Task OnDisposing_hook_runs_exactly_once_on_dispose()
    {
        var ao = new AsyncDisposeHook(BoomState.A);
        Assert.Equal(0, ao.OnDisposingCalls);

        await ao.DisposeAsync();
        Assert.Equal(1, ao.OnDisposingCalls);
    }

    [Fact]
    public async Task Wait_false_trigger_completes_immediately_without_faulting_and_worker_survives()
    {
        await using var ao = new AsyncNoWaitBoom(BoomState.A);

        // A Wait=false trigger returns an already-completed task even though the entry action throws;
        // awaiting it must not fault.
        await ao.GoAsync();

        // The worker still processed the message and survived the thrown exception.
        Assert.True(ao.Entered.Wait(TimeSpan.FromSeconds(5)));
    }
}

[ActiveStateMachine.Attributes.ActiveObjectAsync(typeof(BoomState), typeof(BoomTrigger))]
public partial class AsyncNoWaitBoom : IAsyncDisposable
{
    public readonly ManualResetEventSlim Entered = new();

    [ActiveStateMachine.Attributes.StateTrigger("BoomTrigger.Explode", Wait = false)]
    public partial Task GoAsync();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        _machine.Configure(BoomState.B)
            .OnEntry(() => { Entered.Set(); throw new InvalidOperationException("boom"); });
    }
}

[ActiveStateMachine.Attributes.ActiveObjectAsync(typeof(BoomState), typeof(BoomTrigger))]
public partial class AsyncDisposeHook : IAsyncDisposable
{
    public int OnDisposingCalls;

    [ActiveStateMachine.Attributes.StateTrigger("BoomTrigger.Explode")]
    public partial Task GoAsync();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);
    }

    partial void OnDisposing() => OnDisposingCalls++;
}

[ActiveStateMachine.Attributes.ActiveObjectAsync(typeof(BoomState), typeof(BoomTrigger), Name = "async-fixture")]
public partial class AsyncNamed : IAsyncDisposable
{
    public string? CapturedName;

    [ActiveStateMachine.Attributes.StateTrigger("BoomTrigger.Explode")]
    public partial Task GoAsync();

    partial void ConfigureStateMachine()
    {
        _machine.Configure(BoomState.A)
            .Permit(BoomTrigger.Explode, BoomState.B);

        // Runs on the worker, whose async context carries CurrentActiveObjectName.
        _machine.Configure(BoomState.B)
            .OnEntry(() => CapturedName = CurrentActiveObjectName.Value);
    }
}
