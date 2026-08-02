using ActiveStateMachine.Example;

Console.WriteLine("Starting Modern .NET Active Object Telephone Simulation...\n");

// 'await using' ensures proper disposal and draining of the message queue.
await using (var phone = new PhoneActiveObject(PhoneState.OffHook))
{
    // Because of the Active Object pattern, callers simply await logical methods.
    // Under the hood, this converts to immutable records traversing an asynchronous queue.

    await phone.DialAsync("555-0199");
    await Task.Delay(500); // Simulating time between user actions

    await phone.ConnectCallAsync();
    await Task.Delay(500);

    await phone.PutOnHoldAsync();
    await Task.Delay(500);

    await phone.TakeOffHoldAsync();
    await Task.Delay(500);

    // Attempting an invalid action (dialing while connected). The unhandled-trigger callback
    // catches this gracefully without crashing the worker loop.
    await phone.DialAsync("123-4567");

    await phone.HangUpAsync();
    await Task.Delay(500);
}

Console.WriteLine("\nSimulation Complete.");
