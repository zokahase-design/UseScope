using System.Collections.Concurrent;

namespace UseScope.Desktop;

internal sealed class AccountMonitor : IDisposable
{
    private sealed class Session(IAccountSource source)
    {
        internal IAccountSource Source = source;
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal DateTimeOffset Due, ManualAfter;
        internal int Failures;
        internal long Epoch;
        internal Reading? Last;
        internal readonly LocalTokenReader LocalTokens = new();
        internal DateTimeOffset NextLocal;
    }
    private sealed class ProviderWindow
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal DateTimeOffset NextStart, Hold;
        internal int? Status;
    }
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ConcurrentDictionary<string, ProviderWindow> _providers = new();
    private readonly SemaphoreSlim _parallel = new(2, 2);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Func<Identity, IAccountSource> _source;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    internal AccountMonitor(Func<Identity, IAccountSource>? source = null, TimeProvider? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    { _source = source ?? SourceFactory.Create; _clock = clock ?? TimeProvider.System; _delay = delay ?? ((span, token) => Task.Delay(span, _clock, token)); }
    internal event Action<Reading>? Changed;
    private Session GetSession(Identity identity) => _sessions.GetOrAdd(identity.Key, _ => new Session(_source(identity)));
    internal static bool Local(Identity account) => MeterStorage.Enabled(account) || Providers.Get(account.Vendor).Connection == ConnectionKind.LocalMeter;
    internal static string ProviderKey(Identity account) => Local(account) ? "local:" + account.Key : account.Vendor switch {
        "claude" or "anthropic-api" => "anthropic", "codex" or "openai-api" => "openai",
        "qwen" or "wan" or "alibaba-cloud" or "alibaba-coding" or "alibaba-token" => "alibaba",
        "minimax" or "minimax-api" => "minimax", "grok" or "xai" => "xai", _ => account.Vendor };
    internal static TimeSpan Interval(Identity account) => TimeSpan.FromSeconds(Local(account) ? 15 : Math.Max(account.Interval,
        Providers.Get(account.Vendor).Connection == ConnectionKind.Native ? 300 : 600));
    internal bool IsCurrent(Reading reading) => _sessions.TryGetValue(reading.Identity, out var session) && Interlocked.Read(ref session.Epoch) == reading.Epoch;
    internal async Task Reproject(Identity identity)
    {
        var session = GetSession(identity);
        await session.Gate.WaitAsync(_lifetime.Token);
        try
        {
            if (session.Last is not Reading last) return;
            var epoch = Interlocked.Read(ref session.Epoch);
            var result = TokenPeriods.Project(identity, last, _clock.GetUtcNow());
            if (LocalTokenReader.DirectoryFor(identity) is not null) result = await session.LocalTokens.Apply(identity, result, _lifetime.Token);
            else if (session.Source is ILocalTokenProjection local)
            {
                try { result = await local.ProjectTokens(identity, result, _lifetime.Token); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException)
                { result = result with { Tokens = null, TokenNote = "Lokaler Tokenzeitraum nicht auswertbar; keine Werte aus einem anderen Zeitraum übernommen." }; }
            }
            if (epoch != Interlocked.Read(ref session.Epoch) || _lifetime.IsCancellationRequested) return;
            session.Last = result; Changed?.Invoke(result);
        }
        finally { session.Gate.Release(); }
    }
    internal async Task Authenticate(Identity identity, Func<CancellationToken, Task> action)
    {
        var session = GetSession(identity); Interlocked.Increment(ref session.Epoch);
        await session.Gate.WaitAsync(_lifetime.Token);
        try { await action(_lifetime.Token); }
        finally
        {
            session.Last = null; session.Due = session.ManualAfter = session.NextLocal = default; session.Failures = 0; session.LocalTokens.Clear();
            session.Source.Dispose(); session.Source = _source(identity);
            if (_lifetime.IsCancellationRequested) session.Source.Dispose();
            session.Gate.Release();
        }
    }
    internal async Task Refresh(IReadOnlyList<Identity> accounts, bool force, string? only = null)
    {
        await Task.WhenAll(accounts.Where(item => item.Enabled && (only is null || only == item.Key)).Select(async identity => {
            var session = GetSession(identity);
            if (!await session.Gate.WaitAsync(0, _lifetime.Token)) return;
            try
            {
                var epoch = Interlocked.Read(ref session.Epoch); var now = _clock.GetUtcNow();
                if (_lifetime.IsCancellationRequested) return;
                if (now < (force ? session.ManualAfter : session.Due))
                {
                    if (session.Last is Reading last && (LocalTokenReader.DirectoryFor(identity) is not null || last.TokenReport is not null) && now >= session.NextLocal)
                        await Publish(last);
                    return;
                }
                var window = _providers.GetOrAdd(ProviderKey(identity), _ => new());
                await window.Gate.WaitAsync(_lifetime.Token);
                try
                {
                    now = _clock.GetUtcNow();
                    if (window.Hold > now)
                    { session.Due = session.ManualAfter = window.Hold; await Publish(Error("Anbieter pausiert weitere Abfragen" + (window.Status is int status ? " (HTTP " + status + ")" : ""), window.Status)); return; }
                    if (window.NextStart > now) await _delay(window.NextStart - now, _lifetime.Token);
                    if (epoch != Interlocked.Read(ref session.Epoch)) return;
                    await _parallel.WaitAsync(_lifetime.Token);
                    try
                    {
                        now = _clock.GetUtcNow();
                        window.NextStart = now.AddSeconds(Local(identity) ? 0 : 15);
                        session.ManualAfter = now.AddSeconds(Local(identity) ? 1 : 60);
                        using var request = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                        request.CancelAfter(TimeSpan.FromSeconds(45));
                        Reading result;
                        try
                        {
                            result = await session.Source.Query(identity, request.Token);
                            if (result.HttpStatus == 429)
                            {
                                session.Failures++; session.Due = _clock.GetUtcNow().AddMinutes(Math.Min(60, 5 * Math.Pow(2, Math.Min(session.Failures - 1, 4))));
                                if (result.NextAttempt is DateTimeOffset due && due > session.Due) session.Due = due;
                                window.Hold = session.ManualAfter = session.Due; window.Status = 429; result = result with { NextAttempt = session.Due };
                            }
                            else if (result.Failed)
                            {
                                session.Failures++; session.Due = _clock.GetUtcNow().AddMinutes(15);
                                if (result.HttpStatus is 401 or 403)
                                {
                                    session.Last = null; session.LocalTokens.Clear();
                                    result = result with { Periods = [], Balance = null, Tokens = null, TokenReport = null, AccountBinding = null, Observed = null, Detail = result.Status, DetailContainsBalance = false };
                                }
                                // An explicit source result (no plan, no key, no quota) is authoritative.
                                // Only caught transport failures below retain previous values.
                                result = result with { NextAttempt = session.Due };
                            }
                            else { session.Failures = 0; session.Due = _clock.GetUtcNow().Add(Interval(identity)); result = result with { NextAttempt = session.Due }; }
                        }
                        catch (OperationCanceledException) when (!_lifetime.IsCancellationRequested) { result = Failure("Zeitlimit der Abfrage erreicht"); }
                        catch (RemoteFailure failure)
                        {
                            result = Failure(failure.Message, failure.HttpStatus, failure.Authentication, failure.Retry);
                            if (failure.HttpStatus == 429 || failure.Retry is not null)
                            { window.Hold = session.Due; session.ManualAfter = session.Due; window.Status = failure.HttpStatus; }
                        }
                        catch (Exception error) when (error is not OperationCanceledException)
                        { result = Failure(error is FileNotFoundException ? "Anbieter-CLI oder Anmeldung fehlt" : "Verbindung derzeit nicht verfügbar"); }
                        await Publish(result);
                    }
                    finally { _parallel.Release(); }
                }
                finally { window.Gate.Release(); }
                Reading Failure(string message, int? status = null, bool auth = false, DateTimeOffset? retry = null)
                {
                    if (auth) { session.Last = null; session.LocalTokens.Clear(); }
                    session.Failures++;
                    var pause = auth ? TimeSpan.FromMinutes(15) : TimeSpan.FromMinutes(Math.Min(60, 5 * Math.Pow(2, Math.Min(session.Failures - 1, 4))));
                    session.Due = _clock.GetUtcNow().Add(pause);
                    if (retry is DateTimeOffset due && due > session.Due) session.Due = due;
                    if (!auth) session.ManualAfter = session.Due;
                    return Error(message, status);
                }
                Reading Error(string message, int? status = null) => new(identity.Key, session.Last?.Periods ?? [], session.Last?.Balance,
                    message, session.Last?.Observed, true, Detail: session.Last?.Detail, DetailContainsBalance: session.Last?.DetailContainsBalance ?? false,
                    Tokens: session.Last?.Tokens, TokenNote: session.Last?.TokenNote, NextAttempt: session.Due, HttpStatus: status, AccountBinding: session.Last?.AccountBinding, TokenReport: session.Last?.TokenReport);
                async Task Publish(Reading result)
                {
                    result = TokenPeriods.Project(identity, result, _clock.GetUtcNow());
                    session.NextLocal = _clock.GetUtcNow().AddSeconds(15);
                    if (LocalTokenReader.DirectoryFor(identity) is not null)
                    { result = await session.LocalTokens.Apply(identity, result, _lifetime.Token); session.NextLocal = _clock.GetUtcNow().AddSeconds(15); }
                    if (epoch != Interlocked.Read(ref session.Epoch) || _lifetime.IsCancellationRequested) return;
                    result = result with { Epoch = epoch }; session.Last = result; Changed?.Invoke(result);
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            finally { session.Gate.Release(); }
        }));
    }
    public void Dispose()
    {
        _lifetime.Cancel();
        // Stop owned CLI helpers synchronously; deferred cleanup can be lost when the process exits.
        foreach (var session in _sessions.Values) session.Source.Dispose();
    }
}
