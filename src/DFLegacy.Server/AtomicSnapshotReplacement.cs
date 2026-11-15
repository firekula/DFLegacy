namespace DFLegacy.Server;

// Only the final same-directory rename belongs here. Serialization and the
// caller's store gate stay outside this narrowly injectable operation.
internal sealed class AtomicSnapshotReplacement
{
    internal const int MaximumAttempts = 5;
    internal static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(25);
    private readonly Action<string, string> _move;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    internal AtomicSnapshotReplacement()
        : this(static (source, target) => File.Move(source, target, overwrite: true), Task.Delay) { }

    internal AtomicSnapshotReplacement(Action<string, string> move,
        Func<TimeSpan, CancellationToken, Task> delay)
    {
        _move = move;
        _delay = delay;
    }

    internal async Task ReplaceAsync(string source, string target, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _move(source, target);
                return;
            }
            catch (UnauthorizedAccessException error) when (OperatingSystem.IsWindows()
                && error.HResult == unchecked((int)0x80070005))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (attempt >= MaximumAttempts) throw;
                await _delay(RetryDelay, cancellationToken);
            }
        }
    }
}
