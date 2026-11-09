namespace DFLegacy.Diagnostics;

public static class DiagnosticHelper
{
    // This process only observes registered ownership. It never attaches a debugger, owns a game job, or terminates a process.
    public static async Task RunAsync(DiagnosticSession session,CancellationToken cancellationToken=default,TimeSpan? finalDrainBudget=null)
    {
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);lifetime.CancelAfter(TimeSpan.FromHours(24));
        var watcher=WatchOwners();cancellationToken=lifetime.Token;
        var queue=new DiagnosticQueue(session);
        try
        {
            while(!cancellationToken.IsCancellationRequested&&queue.IsEligible)
            {
                try {
                    session.CollectConfirmed();
                    await queue.UploadPendingAsync(cancellationToken:cancellationToken).ConfigureAwait(false);
                    if(session.IsClosed||!session.OwnerAlive())
                    {
                        // Give a terminated native producer time to finish its atomic rename, within the drain deadline.
                        await Task.Delay(500,cancellationToken).ConfigureAwait(false);session.CollectConfirmed();
                        await queue.UploadPendingAsync(cancellationToken:cancellationToken).ConfigureAwait(false);
                        session.CleanupManagedFiles();return;
                    }
                    await Task.Delay(500,cancellationToken).ConfigureAwait(false);
                }catch(OperationCanceledException){return;}
                catch(Exception error) when(DiagnosticFiles.Recoverable(error)){return;}
            }
        }
        finally {lifetime.Cancel();await watcher.ConfigureAwait(false);}

        async Task WatchOwners()
        {
            try {
                while(!lifetime.IsCancellationRequested) {
                    if(session.IsClosed||!session.OwnerAlive()) {
                        lifetime.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp((finalDrainBudget??TimeSpan.FromSeconds(10)).TotalMilliseconds,100,10000)));return;
                    }
                    await Task.Delay(100,lifetime.Token).ConfigureAwait(false);
                }
            }catch(OperationCanceledException){ }
            catch(Exception error) when(DiagnosticFiles.Recoverable(error)){lifetime.Cancel();}
        }
    }
}
