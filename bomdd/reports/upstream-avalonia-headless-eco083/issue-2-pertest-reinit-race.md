# Issue 2(関連・PerTest 再初期化レース)— そのまま GitHub へ貼れる形

**Title:** `Headless: intermittent "The calling thread cannot access this object" during PerTest per-dispatch platform re-initialization (DefaultRenderLoop.Add → Dispatcher.VerifyAccess)`

**Labels(あれば):** bug, headless

---

## Summary

Under the default `AvaloniaTestIsolationLevel.PerTest`, every `HeadlessUnitTestSession.Dispatch` re-initializes the Avalonia platform (`Dispatcher.ResetBeforeUnitTests` + `AppBuilder.SetupUnsafe()` → new `Compositor` / render loop registration). In a parallel test suite this re-initialization **intermittently** fails with a dispatcher-affinity violation:

```
System.InvalidOperationException: The calling thread cannot access this object because a different thread owns it.
   at Avalonia.Threading.Dispatcher.<VerifyAccess>g__ThrowVerifyAccess|17_0()
   at Avalonia.Threading.Dispatcher.VerifyAccess()
   at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask i)
   at Avalonia.Rendering.Composition.Server.ServerCompositor..ctor(...)
   at Avalonia.Rendering.Composition.Compositor..ctor(...)
   at Avalonia.Headless.AvaloniaHeadlessPlatform.Initialize(AvaloniaHeadlessPlatformOptions opts)
   at Avalonia.AppBuilder.SetupUnsafe()
   at Avalonia.Headless.HeadlessUnitTestSession.EnsureIsolatedApplication()
   at Avalonia.Headless.HeadlessUnitTestSession.<>c__DisplayClass12_0`1.<DispatchCore>b__0()
   at Avalonia.Headless.HeadlessUnitTestSession.<>c.<StartNew>b__18_1(Object a)
```

Captured on 12.0.4 in a ~650-test xUnit v3 suite (single shared session created by `StartNew(Type)`, i.e. default `PerTest`), firing in roughly 2 of 15 full parallel runs. The failure is timing-dependent; we could not turn it into a deterministic repro — filing primarily as a documented failure mode with a full stack, since the stack pinpoints the racing pair (a `DefaultRenderLoop` whose dispatcher affinity does not match the freshly reset dispatcher during re-initialization).

## Severity depends on version

- **On 12.0.4** this exception escaped from the then-unprotected application-construction stage and killed the session's consumer loop → the whole suite hung permanently with all tests green (see #ISSUE1 for the amplification defect; construction-stage protection was added by #21688).
- **On 12.1.0** the construction stage is protected, so this race should now surface as an intermittent *test failure* instead of a hang — better, but still a flaky-suite source, and any similar affinity exception thrown in *cleanup* still causes the permanent hang of #ISSUE1.

## Repro conditions (as observed; not deterministic)

- Avalonia.Headless 12.0.4, .NET 10.0.9, Windows 11 x64
- One `HeadlessUnitTestSession` shared by all UI tests (`StartNew(typeof(App))` → default `PerTest`)
- xUnit v3 running ~650 tests in parallel (mixed UI and non-UI tests, CPU saturated)
- Frequency: ~2 / 15 full runs; the failing dispatch varies run to run

A minidump of the hung process (12.0.4) shows the session thread already gone and all worker threads idle, consistent with the loop-death amplification in #ISSUE1.

## Circumstantial note

A similar `RenderLoop.Add` → `Dispatcher.VerifyAccess` failure has been reported in another Avalonia-related project (Avalonia.Controls.Maui#74). Not claiming the same root cause — only that this affinity failure mode is not specific to our project.

## Workaround

Pinning `AvaloniaTestIsolationLevel.PerAssembly` (single Application/Dispatcher reused across tests, no per-dispatch re-initialization) eliminated the failure entirely in our suite (12 consecutive parallel full runs, previously ~2/15).

## Environment

- Avalonia.Headless 12.0.4 (stack captured); 12.1.0 changes where the exception lands but the underlying re-initialization race is version-independent as far as we can tell from the 12.1.0 sources
- .NET 10.0.9, Windows 11 Pro 10.0.26200 (x64), xUnit v3 3.2.2
