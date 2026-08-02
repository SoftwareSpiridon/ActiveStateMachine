using ActiveStateMachine.Example;
using Xunit;

namespace ActiveStateMachine.Tests;

public class PhoneActiveObjectTests
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
}
