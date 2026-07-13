# Issue 1(主・故障増幅)— そのまま GitHub へ貼れる形

**Title:** `Headless: exception escaping a work item's cleanup permanently hangs HeadlessUnitTestSession — pending and all subsequent Dispatch calls never complete (12.1.0)`

**Labels(あれば):** bug, headless

---

## Summary

`HeadlessUnitTestSession` has a failure-amplification reliability defect: a single unhandled exception thrown during a work item's **cleanup stage** kills the internal dispatch consumer loop silently. The work item's own `TaskCompletionSource` never transitions, and **every subsequent `Dispatch` call also never completes**. Nothing is reported to the caller — a full test suite simply stops making progress while all executed tests remain green.

The **application-construction** variant of this problem was fixed by #21688 (included in 12.1.0): exceptions from `EnsureIsolatedApplication` / `EnsureSharedApplication` are now routed to the TCS. However, the same amplification structure remains for exceptions thrown during **cleanup** (and any other unprotected work-item lifecycle stage). Verified against the shipped 12.1.0 binaries.

Any user can be affected when an exception escapes from an unprotected work-item lifecycle stage. In our case (a ~650-test xUnit v3 suite), roughly 1 in 8 full runs stalled with 8 UI tests "running" forever and no failure reported; the trigger was an intermittent platform re-initialization race under `PerTest` isolation (filed separately: #ISSUE2 — that exception now surfaces as a test failure on 12.1.0, but any cleanup-stage exception still reproduces the permanent hang deterministically, see below).

## Deterministic repro (12.1.0)

```csharp
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

var session = HeadlessUnitTestSession.StartNew(typeof(Application), AvaloniaTestIsolationLevel.PerAssembly);

// 1) Sanity: a normal dispatch completes.
var sanity = await session.Dispatch(() => 42, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"1) sanity dispatch: completed ({sanity})");

// 2) Poison: the dispatched action itself succeeds, but leaves a job on the dispatcher
//    queue that throws. The job runs during the work item's cleanup stage
//    (Dispatcher.UIThread.RunJobs() inside the finally), outside any try/catch that
//    routes exceptions to the work item's TaskCompletionSource.
var poison = session.Dispatch(
    () => Dispatcher.UIThread.Post(
        () => throw new InvalidOperationException("poison: thrown by a posted job during cleanup RunJobs")),
    CancellationToken.None);
try { await poison.WaitAsync(TimeSpan.FromSeconds(10)); Console.WriteLine("2) poison dispatch: completed"); }
catch (TimeoutException) { Console.WriteLine($"2) poison dispatch: NOT completed after 10s (Status={poison.Status})"); }

// 3) Any subsequent dispatch never completes.
var next = session.Dispatch(() => 1, CancellationToken.None);
try { var v = await next.WaitAsync(TimeSpan.FromSeconds(10)); Console.WriteLine($"3) subsequent dispatch: completed ({v})"); }
catch (TimeoutException) { Console.WriteLine($"3) subsequent dispatch: NOT completed after 10s (Status={next.Status}) => permanent hang"); }
```

Observed output on Avalonia.Headless **12.1.0** (identical for `PerAssembly` and `PerTest`):

```
1) sanity dispatch: completed (42)
2) poison dispatch: NOT completed after 10s (Task.Status=WaitingForActivation) — its own TCS never transitioned
3) subsequent dispatch: NOT completed after 10s (Task.Status=WaitingForActivation) => permanent hang reproduced
[UnobservedTaskException] poison: exception thrown by a posted job during cleanup RunJobs
```

The only trace of the original exception is a `TaskScheduler.UnobservedTaskException` after a GC — nothing surfaces to the `Dispatch` caller or the test framework.

## Expected behavior

- A work item's `Task` must always transition (success / failure / cancellation), regardless of which lifecycle stage throws.
- If the consumer loop cannot continue, pending and subsequent `Dispatch` calls should fail fast with the stored cause (e.g., `ObjectDisposedException`-style), never hang forever.

## Analysis (HeadlessUnitTestSession.cs, 12.1.0)

Three structural points combine into the amplification:

1. In `DispatchCore`, the cleanup runs in `finally { disposable.Dispose(); }` **before** the TCS completion block. For the shared application this calls `Dispatcher.UIThread.RunJobs()`; for the isolated one it disposes services and resets the dispatcher. An exception thrown here propagates out of the queued delegate — the TCS is never completed (and if the test body had already failed, the original exception in `ex` is also lost).
2. The consumer loop in `StartNew` catches **only** `OperationCanceledException`. Any other exception terminates the loop's task.
3. There is no terminal-failure handling: nothing rejects further `_queue.Add` calls, and nothing fails already-queued items, so all of them wait forever.

## Suggested fix (two layers)

1. **Per-work-item completion guarantee** — make the queued item aware of its completion (carry the TCS or a `Fail(Exception)` callback), and wrap the *entire* lifecycle (setup → body → cleanup) in one exception boundary so the TCS always transitions. If both the body and the cleanup throw, don't let the cleanup exception overwrite the body's failure — aggregate them (primary + suppressed cleanup exception).
2. **Session-level terminal failure** — if the consumer loop still dies for any reason, atomically transition the session to a Faulted state: fail all queued items and make subsequent `Dispatch` calls throw immediately with the stored cause.

(1) removes the known amplification path; (2) turns any residual unknown path from "permanent silent hang" into "immediate, diagnosable failure".

## Impact / context

- Possibly the concrete mechanism behind reports like #21467 (tests stuck awaiting `Dispatch`).
- Real-world trigger we hit on 12.0.4: intermittent `InvalidOperationException` ("The calling thread cannot access this object...") from per-dispatch platform re-initialization under default `PerTest` isolation (#ISSUE2). On 12.0.4 that exception killed the loop via the then-unprotected construction stage (fixed by #21688); the cleanup stage shown above still reproduces the hang on 12.1.0 deterministically.
- Workarounds we currently use, for other affected users: pin `AvaloniaTestIsolationLevel.PerAssembly`, plus a reflection-based watchdog on the session's private `_dispatchTask` that calls `Environment.FailFast(cause)` when the loop faults (turns the silent hang into an immediate diagnosable crash), plus MTP HangDump as a last-resort activity timeout.

## Environment

- Avalonia.Headless 12.0.4 and 12.1.0 (repro above verified on 12.1.0)
- .NET 10.0.9, Windows 11 Pro 10.0.26200 (x64)
- xUnit v3 3.2.2 (real suite); repro above is a plain console app, framework-independent
