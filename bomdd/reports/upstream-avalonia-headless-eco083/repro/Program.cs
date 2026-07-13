using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

// Repro for: an exception escaping a work item's cleanup stage permanently hangs
// HeadlessUnitTestSession (pending and subsequent Dispatch calls never complete).
// Isolation level selectable via args[0]: PerAssembly (default) | PerTest.

var isolation = args.Length > 0 && args[0] == "PerTest"
    ? AvaloniaTestIsolationLevel.PerTest
    : AvaloniaTestIsolationLevel.PerAssembly;
Console.WriteLine($"Avalonia.Headless 12.1.0 / isolation={isolation}");

TaskScheduler.UnobservedTaskException += (_, e) =>
    Console.WriteLine($"[UnobservedTaskException] {e.Exception.GetBaseException().Message}");
AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    Console.WriteLine($"[UnhandledException] {e.ExceptionObject}");

var session = HeadlessUnitTestSession.StartNew(typeof(Application), isolation);

// 1) Sanity: a normal dispatch completes.
var sanity = await session.Dispatch(() => 42, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
Console.WriteLine($"1) sanity dispatch: completed ({sanity})");

// 2) Poison: the dispatched action itself succeeds, but leaves a job on the dispatcher
//    queue that throws. The job runs during the work item's cleanup stage
//    (Dispatcher.UIThread.RunJobs() inside the finally), outside any try/catch that
//    routes exceptions to the work item's TaskCompletionSource.
var poison = session.Dispatch(
    () => Dispatcher.UIThread.Post(
        () => throw new InvalidOperationException("poison: exception thrown by a posted job during cleanup RunJobs")),
    CancellationToken.None);
try
{
    await poison.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine("2) poison dispatch: completed normally");
}
catch (TimeoutException)
{
    Console.WriteLine($"2) poison dispatch: NOT completed after 10s (Task.Status={poison.Status}) — its own TCS never transitioned");
}
catch (Exception ex)
{
    Console.WriteLine($"2) poison dispatch: faulted with {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
}

// 3) Any subsequent dispatch: on an affected version this never completes.
var next = session.Dispatch(() => 1, CancellationToken.None);
try
{
    var v = await next.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine($"3) subsequent dispatch: completed ({v}) => defect NOT reproduced");
}
catch (TimeoutException)
{
    Console.WriteLine($"3) subsequent dispatch: NOT completed after 10s (Task.Status={next.Status}) => permanent hang reproduced");
}

GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
Console.WriteLine("done (no exception ever surfaced to the caller or the process)");
Environment.Exit(0);
