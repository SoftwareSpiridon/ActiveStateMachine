using ActiveStateMachine.Example.Sync;

Console.WriteLine("Starting Classic (synchronous) .NET Active Object Telephone Simulation...\n");

// A plain 'using' — the Active Object is IDisposable, not IAsyncDisposable. Every call below is
// a blocking, synchronous method: it returns only after the worker thread has applied the trigger.
using (var phone = new PhoneActiveObject(PhoneState.OffHook))
{
    phone.Dial("555-0199");
    Thread.Sleep(500); // Simulating time between user actions

    phone.ConnectCall();
    Thread.Sleep(500);

    phone.PutOnHold();
    Thread.Sleep(500);

    phone.TakeOffHold();
    Thread.Sleep(500);

    // Attempting an invalid action (dialing while connected). The unhandled-trigger callback
    // catches this gracefully without crashing the worker thread.
    phone.Dial("123-4567");

    phone.HangUp();
    Thread.Sleep(500);
}

Console.WriteLine("\nSimulation Complete.");
