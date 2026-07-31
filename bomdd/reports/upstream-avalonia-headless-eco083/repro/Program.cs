using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;

// Repro for: an exception escaping a work item's cleanup stage permanently hangs
// HeadlessUnitTestSession (pending and subsequent Dispatch calls never complete).
// Isolation level selectable via args[0]: PerAssembly (default) | PerTest.

var isolation = args.Length > 0 && args[0] == "PerTest"
    ? AvaloniaTestIsolationLevel.PerTest
    : AvaloniaTestIsolationLevel.PerAssembly;
var headlessVersion = typeof(HeadlessUnitTestSession).Assembly
    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
    ?? typeof(HeadlessUnitTestSession).Assembly.GetName().Version?.ToString() ?? "unknown";
Console.WriteLine($"Avalonia.Headless {headlessVersion} / isolation={isolation}");

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

// 4) (ViewPrism2 ECO-141) Work item callback itself throwing — is it routed to its own
//    TCS, or does it escape the consumer loop? The loop's only catch is
//    OperationCanceledException, so this determines whether a fail-fast watchdog on the
//    dispatch task still has a job after the #21781 cleanup fix.
// (explicit delegate type: a throw-only lambda is ambiguous between the Func<T>/Func<Task<T>> overloads)
Func<int> throwing = () => throw new InvalidOperationException("direct: exception thrown by the work item callback itself");
var direct = session.Dispatch(throwing, CancellationToken.None);
try
{
    await direct.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine("4) direct-throw dispatch: completed normally (unexpected)");
}
catch (TimeoutException)
{
    Console.WriteLine($"4) direct-throw dispatch: NOT completed after 10s (Task.Status={direct.Status})");
}
catch (Exception ex)
{
    Console.WriteLine($"4) direct-throw dispatch: faulted with {ex.GetBaseException().GetType().Name}: {ex.GetBaseException().Message}");
}

// 5) Liveness after a direct throw: if the loop died, this never completes.
var after = session.Dispatch(() => 2, CancellationToken.None);
try
{
    var v = await after.WaitAsync(TimeSpan.FromSeconds(10));
    Console.WriteLine($"5) dispatch after direct throw: completed ({v}) => loop survived");
}
catch (TimeoutException)
{
    Console.WriteLine($"5) dispatch after direct throw: NOT completed after 10s (Task.Status={after.Status}) => loop died");
}

GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
Console.WriteLine("done (no exception ever surfaced to the caller or the process)");
Environment.Exit(0);
