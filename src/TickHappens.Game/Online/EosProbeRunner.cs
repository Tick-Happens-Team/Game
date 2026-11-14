#if TICKHAPPENS_EOS
namespace TickHappens.Game.Online;

internal static class EosProbe
{
    public static int Run()
    {
        try
        {
            using var transport = new EosTransport(LocalEosConfig.Load(), LocalEosConfig.ForceRelays);
            PumpUntil(transport, transport.SignInAsync());
            Console.WriteLine("EOS Device ID sign-in succeeded.");
            PumpUntil(transport, transport.HostAsync(4));
            Console.WriteLine("EOS lobby created and room code published.");
            PumpUntil(transport, transport.LeaveAsync());
            Console.WriteLine("EOS lobby closed.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error is InvalidOperationException ? error.Message : error.GetType().Name);
            return 1;
        }
    }

    private static void PumpUntil(EosTransport transport, Task operation)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!operation.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(45))
        {
            transport.Pump();
            Thread.Sleep(10);
        }
        if (!operation.IsCompleted) throw new TimeoutException();
        operation.GetAwaiter().GetResult();
    }
}
#endif
