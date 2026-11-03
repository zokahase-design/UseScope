using System.Collections.Concurrent;

namespace UseScope.Desktop;

internal sealed class AccountMonitor : IDisposable
{
    private sealed class Session(IAccountSource source)
    {
        internal IAccountSource Source = source;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal DateTimeOffset Due;
        internal DateTimeOffset NotBefore;
        internal long Epoch;
        internal Reading? Last;
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<Identity, IAccountSource> _source;
    internal AccountMonitor(Func<Identity, IAccountSource>? source = null) => _source = source ?? (identity => identity.Vendor switch {
        "codex" => new CodexSource(), "claude" => new ClaudeSource(), "nous" => new NousSource(), _ => new ApiAccountSource() });
    internal event Action<Reading>? Changed;
    private Session GetSession(Identity identity) => _sessions.GetOrAdd(identity.Key, _ => new Session(_source(identity)));
    internal bool IsCurrent(Reading reading) => _sessions.TryGetValue(reading.Identity, out var session)
        && Interlocked.Read(ref session.Epoch) == reading.Epoch;
    internal async Task Authenticate(Identity identity, Func<CancellationToken, Task> action)
    {
        var session = GetSession(identity);
        Interlocked.Increment(ref session.Epoch);
        await session.Gate.WaitAsync(_lifetime.Token);
        try { await action(_lifetime.Token); }
        finally
        {
            session.Last = null;
            session.Due = default;
            session.Source.Dispose();
            session.Source = _source(identity);
            session.Gate.Release();
        }
    }
    internal async Task Refresh(IReadOnlyList<Identity> accounts, bool force, string? only = null)
    {
        await Task.WhenAll(accounts.Where(item => item.Enabled && (only is null || only == item.Key)).Select(async identity =>
        {
            var session = GetSession(identity);
            if (!await session.Gate.WaitAsync(0, _lifetime.Token)) return;
            try
            {
                if ((!force && DateTimeOffset.Now < session.Due) || DateTimeOffset.Now < session.NotBefore || _lifetime.IsCancellationRequested) return;
                var epoch = Interlocked.Read(ref session.Epoch);
                session.Due = DateTimeOffset.Now.AddSeconds(identity.Interval);
                using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                request.CancelAfter(TimeSpan.FromSeconds(45));
                Reading result;
                try { result = await session.Source.Query(identity, request.Token); }
                catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested)
                { result = Error("Zeitlimit der Abfrage erreicht"); }
                catch (RemoteFailure failure)
                {
                    if (failure.Retry is DateTimeOffset due) session.NotBefore = due;
                    result = Error(failure.Message);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    result = Error(exception is FileNotFoundException ? "Anbieter-CLI oder Anmeldung fehlt" : "Verbindung derzeit nicht verfügbar");
                }
                if (epoch == Interlocked.Read(ref session.Epoch))
                {
                    result = result with { Epoch = epoch };
                    session.Last = result;
                    if (!_lifetime.IsCancellationRequested) Changed?.Invoke(result);
                }
                Reading Error(string message) => new(identity.Key, session.Last?.Periods ?? [], session.Last?.Balance,
                    message, session.Last?.Observed, true, Detail: session.Last?.Detail, DetailContainsBalance: session.Last?.DetailContainsBalance ?? false);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            finally { session.Gate.Release(); }
        }));
    }
    public void Dispose()
    {
        _lifetime.Cancel();
        // In-flight operations release their leases before resources are disposed.
        foreach (var session in _sessions.Values)
            _ = Task.Run(async () => {
                await session.Gate.WaitAsync();
                try { session.Source.Dispose(); } finally { session.Gate.Release(); }
            });
    }
}
