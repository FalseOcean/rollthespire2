using System.Threading.Channels;

internal static class TerminalQueueAccountingChecks
{
    internal static int Run() => RunAsync().GetAwaiter().GetResult();
    private static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks++; }
        for (int iteration = 0; iteration < 100; iteration++)
        {
            var channel = Channel.CreateBounded<int>(new BoundedChannelOptions(1)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
            int oldOutstanding = 0;
            Interlocked.Increment(ref oldOutstanding);
            await channel.Writer.WriteAsync(1);
            Interlocked.Increment(ref oldOutstanding);
            Task secondOffer = channel.Writer.WriteAsync(2).AsTask();
            Check(!secondOffer.IsCompleted && channel.Reader.Count == 1, "second offer must block at capacity one");
            var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var allowAccounting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task consumer = Task.Run(async () =>
            {
                int item = await channel.Reader.ReadAsync();
                if (item != 1) throw new InvalidDataException("first item order");
                taken.SetResult();
                // Deterministically widen the real gap between channel removal
                // and the old consumer-side bookkeeping; do not alter the channel.
                await allowAccounting.Task;
                Interlocked.Decrement(ref oldOutstanding);
            });
            await taken.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await secondOffer.WaitAsync(TimeSpan.FromSeconds(5));
            int sampledOutstanding = channel.Reader.Count + 1;
            Check(Interlocked.Increment(ref oldOutstanding) == 3, "old accounting deterministically reaches three");
            Task thirdOffer = channel.Writer.WriteAsync(3).AsTask();
            Check(!thirdOffer.IsCompleted && channel.Reader.Count == 1 && sampledOutstanding == 2,
                "actual state is one queued plus one blocked offer, not three queued");
            allowAccounting.SetResult();
            await consumer;
            Check(await channel.Reader.ReadAsync() == 2, "second item order");
            await thirdOffer.WaitAsync(TimeSpan.FromSeconds(5));
            using var cancel = new CancellationTokenSource();
            Task fourthOffer = channel.Writer.WriteAsync(4, cancel.Token).AsTask();
            cancel.Cancel();
            try { await fourthOffer; throw new InvalidDataException("cancel lost"); }
            catch (OperationCanceledException) { checks++; }
            Check(channel.Reader.Count == 1 && await channel.Reader.ReadAsync() == 3, "cancel preserves admitted third item");
            var fault = new InvalidOperationException("injected-terminal-fault");
            channel.Writer.TryComplete(fault);
            try { await channel.Reader.Completion; throw new InvalidDataException("fault lost"); }
            catch (InvalidOperationException ex) when (ReferenceEquals(ex, fault)) { checks++; }
        }
        Console.WriteLine($"TERMINAL_ACCOUNTING PASS checks={checks};forcedOldOutstanding=3;queued=1;pendingOffer=1;newOutstanding=2;iterations=100");
        return 0;
    }
}
