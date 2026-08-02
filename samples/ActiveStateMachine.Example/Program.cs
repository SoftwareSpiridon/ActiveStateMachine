using ActiveStateMachine.Example;

Console.WriteLine("=== ActiveStateMachine — Phone Example ===");
Console.WriteLine();

await using var phone = new PhoneActiveObject(PhoneState.OffHook);
Console.WriteLine($"Initial state: {phone.State}");

Console.WriteLine("\n> DialAsync(\"555-1234\")");
await phone.DialAsync("555-1234");
Console.WriteLine($"State: {phone.State}");

Console.WriteLine("\n> ConnectCallAsync()");
await phone.ConnectCallAsync();
Console.WriteLine($"State: {phone.State}");

Console.WriteLine("\n> PlaceOnHoldAsync()");
await phone.PlaceOnHoldAsync();
Console.WriteLine($"State: {phone.State}");

Console.WriteLine("\n> TakeOffHoldAsync()");
await phone.TakeOffHoldAsync();
Console.WriteLine($"State: {phone.State}");

Console.WriteLine("\n> HangUpAsync()");
await phone.HangUpAsync();
Console.WriteLine($"State: {phone.State}");

// Demonstrate that an invalid trigger surfaces as a faulted Task (exception propagates
// back to the caller through the mailbox), without tearing down the worker loop.
Console.WriteLine("\n> ConnectCallAsync() while OffHook (invalid transition)");
try
{
    await phone.ConnectCallAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"Caught expected exception: {ex.GetType().Name}");
}
Console.WriteLine($"State after error: {phone.State}");

// Demonstrate ordering / thread-affinity: fire a burst of triggers and confirm they are
// processed serially on the single worker in submission order.
Console.WriteLine("\n> Concurrency check: dialing + connecting from many threads");
await phone.DialAsync("555-9999");
var tasks = new List<Task>
{
    phone.ConnectCallAsync(),
    phone.PlaceOnHoldAsync(),
    phone.HurlAgainstWallAsync(),
};
await Task.WhenAll(tasks);
Console.WriteLine($"Final state: {phone.State}");

Console.WriteLine("\nDisposing (drains mailbox and stops worker)...");
