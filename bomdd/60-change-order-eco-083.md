# ECO-083 — Headless ディスパッチループの静黙死で以後の UI テストが全て無限待ちになる(並列フル run 間欠ハングの真因)

- status: applied(2026-07-13 gate② 合格・クローズ)
- type: 不具合(テストハーネス基盤。上流= Avalonia.Headless 12.0.4 の設計脆弱性+自リポの防御不在。ECO-082 診断中に実測捕獲・R3 分離起票)
- baseline: main 2f967ce(+ECO-081 の HangDump 恒久化が捕獲装置として前提)
- 発端: ECO-082(InvalidCast フレーク)の再現診断中、dotnet test フル run 反復で**別種の間欠ハングを 2/15 回捕獲**(2026-07-13)

## §1 症状(実測・証拠完備)

`dotnet test` フル run 中、**Avalonia Headless を使う UI テスト 8 本が「実行中」のまま無活動**(528/665 成功で進捗停止・4 分 18 秒+観測)→ ECO-081 の HangDump(5 分無活動)が発火し exit=1。**テスト失敗としては一切報告されない**(全テスト緑のまま止まる)。発火率 2/15(~13%)・ハング集合は 2 回ともほぼ同一:

- GfPackageVisualParityTests / GfEntryE1VisualParityTests / GfSnapshotVisualParityTests / GfPillTextBoxCaretAlignTests / GfViewerDrawerScrollTests(視覚 probe 群)
- CpPackage074Tests(picker 自動起動)/ CpUiEco077EntryTests / CpUi056OrganizeV2Tests(高さ)/ CpUiG1WorkTabTests(ソート UI)

= **全て共有 `HeadlessApp.Session`(HeadlessUnitTestSession)の Dispatch を使うテスト**。

過去に観測された「AI サブタスクでのテスト実行が応答なしのまま」(ECO-081 §1)の実体は本件の可能性が高い。

## §2 診断(確定=ダンプ+デコンパイルの二段実測)

### ダンプ解析(HangDump 自動採取の mini ダンプ・clrstack -all)

マネージドスレッド 13 本を全列挙した結果: **ハング中テストを実行しているスレッドが存在しない**。スレッドプールワーカー×3=全てアイドル(仕事待ち)・xunit MessageBus=アイドル・**Avalonia のセッション/Dispatcher スレッドが不在**(既に終了)。= ロック競合(デッドロック)ではなく、**キュー消費者の死亡による滞留**。

### 真因構造(Avalonia.Headless 12.0.4 `HeadlessUnitTestSession` のデコンパイル読解)

1. ディスパッチループ(`StartNew` 内)の catch は **`OperationCanceledException` のみ**。他の例外はループを即死させる(`Task.Run` のタスクが fault・再起動なし)。
2. `DispatchCore` はテスト本体を try/catch で包み TCS へ流すが、**保護外のコードが 2 箇所**ある:
   - (a) `EnsureSharedApplication()`(try ブロックの前)
   - (b) **finally 内の `disposable.Dispose()` → `Dispatcher.UIThread.RunJobs(null)`** — テスト完了後に**残留ディスパッチャジョブ**(テストが `Dispatcher.UIThread.Post` した継続・タイマー・フォーカス/レイアウト系の遅延ジョブ等)をまとめて実行する。ここで未処理例外が投げられると **finally からループへ伝播しループ死亡**。
3. ループ死亡後、`Dispatch` はキューに積むだけで TCS は永遠に未完 → **後続の Headless テストが全員無限待ち**。例外は誰の TCS にも届かないため**テスト失敗として観測されない**(静黙死)。

この構造は観測事実を全て説明する: 「テスト緑のまま停止」(例外がテストに帰属しない)・「間欠」(RunJobs 時に危険な残留ジョブがあるかはタイミング依存)・「並列フル run で発火しやすい」(負荷でジョブタイミングが変動)・「ハング集合=Headless 利用テスト全員」(消費者共有)。

### 未確定(次回発火で決着)

**ループを殺した具体的な例外**は未確定(mini ダンプ=ヒープ非含有・heap ダンプでの再現 15 run は不発)。是正案 D はこの未確定を「次回発火時に例外全文が顕在化する」形へ変える。

## §3 切り分け済みの事実

- 発火率: dotnet test フル run 15 回中 2 回(同日・同マシン)。直後の heap ダンプ狙い 15 回では不発(間欠・負荷依存)。
- ECO-081 の HangDump は 2 回とも正しく発火し、ハング中テスト名+ダンプを自動採取(fail-closed 化の初実運用・設計どおり)。
- `HeadlessApp.Session` は static readonly・Dispose 経路なし=ループ死亡は異常経路のみ。
- ECO-082 の InvalidCast とは別欠陥(あちらはテスト失敗として報告される・こちらは報告されない)。

## §4 是正方針(案)

- **案D(推奨・fail-fast 化)**: `HeadlessApp` で `_dispatchTask` をリフレクション取得し、`ContinueWith(OnlyOnFaulted)` で**原因例外全文つき `Environment.FailFast`**。沈黙ハング(5 分待ち+原因不明)→ 即時クラッシュ+真犯人例外の顕在化(stderr)へ。フィールド不在(Avalonia 更新)時は監視スキップ=安全。真因の運び手例外が次回発火で確定し、案E へ接続する。
- **案E(D の後続)**: D で顕在化した例外の発生源(残留ジョブを post したテスト/コンポーネント)を特定して潰す。
- **案F(上流)**: Avalonia へ issue 報告(ループ catch の網羅化/DispatchCore finally の保護)。D/E と独立・並行可。

プローブ(R5): ループ死亡→無限待ちは**決定論的に再現可能**(`Dispatch` 内で `Dispatcher.UIThread.Post(() => throw ...)` を仕込むと、finally の RunJobs で例外が飛びループが死ぬ)。ただし共有セッションを殺すため**恒久テストとしては残せない**(プロセス隔離が必要)— 一時注入で赤(現状=後続 Dispatch が無限待ち)→ 案D 適用後は FailFast(即死+診断)へ挙動が変わることを実測する(ECO-081 の陽性対照方式)。

## §5 影響 BOM(見込み)

- tests: `HeadlessApp.cs`(監視追加)。製品 src 不変。
- 台帳: M-HARNESS-015(Headless セッションの fail-fast 契約)・CP 系は fix 時判断。
- 既存固定 Oracle 行は変更しない(R6)。

## §6 残ゲート

- gate① 裁定: 不要(テストハーネスの欠陥是正・ECO-061/078 前例)。
- gate②: maintainer 実行確認(フル run 反復で「5 分沈黙ハング」が根絶され、発火時は即時 FailFast+原因例外が出ること — 間欠のため「発火した場合の挙動」は一時注入の実測記録で代替)。

## §7 関連

- ECO-081: HangDump(捕獲装置)— 本件の発見はその初実運用の成果。5 分の最終安全弁として引き続き有効(D は一次防衛=即時化)。
- ECO-082: 診断中の副産物として本件を捕獲(§7)。InvalidCast 本体は再現不能で停止。
- HeadlessApp.cs コメント「Dispatch は単一 UI スレッドへ直列化されるため、テストクラス間の並列実行とも安全」— **ループが生きている限り**という暗黙前提が破れていた(検査の暗黙前提の一種)。

## §8 実施記録(2026-07-13 /eco-fix)

### プローブ先行(R5)= 赤の実測(一時注入 `HangProbe_TEMP_ECO083`・実測後に削除)

`Dispatch(() => Dispatcher.UIThread.Post(() => throw new InvalidOperationException("ECO-083 probe")))` の一時テストで、§2 の真因構造を**決定論的に再現**:

- **是正前(赤)**: 残留ジョブの例外 1 発で、①post した Dispatch **自身も未完**(`WaitingForActivation` のまま=TrySetException に到達しない → finally 内で例外がループへ伝播した診断の直接証拠)②後続 `Dispatch(() => 1)` も 10 秒経過で未完=**静黙死→無限待ちを実証**。
- **是正後**: 同一プローブが **3.2 秒で exit=35(FailFast)**。stderr に「ECO-083: HeadlessUnitTestSession のディスパッチループが未処理例外で死亡。…原因例外: System.AggregateException … ---> **System.InvalidOperationException: ECO-083 probe: 残留ジョブの未処理例外**」= **ループを殺した真犯人の例外全文が顕在化**(本 ECO 唯一の未確定=「実発火時の具体例外」が次回発火で自動確定する状態になった)。

### 是正内容(案D)

1. `HeadlessApp.Session` の生成を `Start()` へ切り出し、`HeadlessUnitTestSession` の内部 `_dispatchTask` をリフレクション取得。`ContinueWith(OnlyOnFaulted)` で**原因例外全文つき `Environment.FailFast`**(即時クラッシュ=沈黙ハング 5 分待ちの前段の一次防衛+真犯人顕在化)。フィールド不在(Avalonia 更新)時は監視スキップ=実行時は安全側。
2. **恒久 pin** `CpHarnessEco083Tests`: 監視が前提とする `_dispatchTask` フィールドの存在+Task 型互換を機械検査(実行時スキップ+機械ゲートで前提崩れを顕在化=ECO-080 の 3 層原則)。症状再現テストは共有セッションを殺すため恒久化せず(§4 の宣言どおり一時注入の実測記録で代替)。
3. `HeadlessApp` クラスコメントへ「並列安全はループが生きている限り」の但し書きを追記(暗黙前提の明示化)。
4. 台帳同期: M-HARNESS-015 `fail_closed` へ ECO-083 節(Headless fail-fast 契約)。

### 機械受入(第 1 段・全緑)

- `dotnet build ViewPrism2.sln`: 0 warning / 0 error。
- `dotnet test tests/ViewPrism2.Tests`: 全緑(正本経路=HangDump 宣言込み)。
- `dotnet test tests/ViewPrism2.Oracle`: 109 pass / 2 known skip(R6 不変)。
- `python bomdd/validate_bom.py`: 0 error / 0 warning。

R7 セルフゴールデン: 対象外(UI 不変・製品 src 不変)。一時プローブは削除済み。

## §9 実発火の捕獲と真因の深化(2026-07-13・fix 第 2 段=真因除去)

### 監視(案D)が初回フル run で実発火を捕獲=真犯人と発生源が完全確定

第 1 段コミット直後の exe フル run で監視が**本物のループ死を即時捕獲**し、原因例外+完全スタックを顕在化:

```
InvalidOperationException: The calling thread cannot access this object because a different thread owns it.
  at Avalonia.Threading.Dispatcher.VerifyAccess()
  at Avalonia.Rendering.DefaultRenderLoop.Add(IRenderLoopTask)
  at ...ServerCompositor..ctor → Compositor..ctor
  at Avalonia.Headless.AvaloniaHeadlessPlatform.Initialize
  at Avalonia.AppBuilder.SetupUnsafe()
  at Avalonia.Headless.HeadlessUnitTestSession.EnsureIsolatedApplication()   ← §2 仮説(a)=try 外
  at HeadlessUnitTestSession.DispatchCore.b__0 → StartNew ループ
```

**診断の更新**: 発生源は §2 仮説 (b)(RunJobs の残留ジョブ)ではなく**仮説 (a) 側=`EnsureIsolatedApplication`**。真因の核心は **`StartNew(Type)` の既定 isolation が `PerTest`** であること — Dispatch **ごと**に `Dispatcher.ResetBeforeUnitTests + SetupUnsafe`(Avalonia プラットフォーム再初期化=Compositor/RenderLoop 再構築)が走り、この再構築が間欠的にスレッドアフィニティ違反(`VerifyAccess` 失敗)を起こし、保護外のためループごと死んでいた。**HeadlessApp のクラスコメント(「プロセス共有・Setup はプロセス 1 回制約」=共有前提)と実装(既定 PerTest=毎回再初期化)が乖離していた**。

### fix 第 2 段(真因構造そのものを消す)

`HeadlessUnitTestSession.StartNew(typeof(Entry), AvaloniaTestIsolationLevel.PerAssembly)` へ明示 — 単一 Application/Dispatcher を全テストで再利用(`EnsureSharedApplication` 経路=Setup は初回 1 回)。毎 Dispatch 再初期化の構造自体が消える。本セッションの元来の設計意図(App リソース共有)とも一致。FailFast 監視(第 1 段)は**多層防御として維持**(将来別経路でループが死んだ場合の即時診断)。

### 実測(第 2 段後)

- dotnet test フル run **×12: ハング/FailFast 発火 0**(是正前 2/15)。
- 全テスト挙動不変: 665/665 緑の run が 10/12(PerAssembly 化による恒常的な赤なし)。
- 非ハング fail 2 回は**別欠陥(ECO-082 ファミリー=並列フレーク)**: run2=`SqliteCommand.DisposePreparedStatements` の NRE(CpUiRepairViewModelTests・TempDb.Dispose とテスト内 background タスクの競合疑い=**新証拠として ECO-082 へ記録**)・run7=詳細不明(後続 run がログを上書き・非ハングのみ確認)。R3 により本 ECO の diff には含めない。

### 機械受入(第 2 段・全緑)

- build 0/0・`dotnet test` Tests **665/665**(単発確認・正本経路)・Oracle 109+2skip・validate_bom 0/0。

**次 gate=②**(maintainer 確認)。

## §10 クローズ(2026-07-13 gate② 合格)

### 確認内容(maintainer・2026-07-13)

手元 `dotnet test tests/ViewPrism2.Tests` の全緑完走(テスト概要)を確認し合格。実測記録(フル run ×12 ハング/FailFast 0=是正前 2/15・是正前後の挙動変化・実発火スタックによる真因確定・PerAssembly 化の挙動不変)を承認。

### 再発防止の所在

- **M-HARNESS-015 `fail_closed`**: ①PerAssembly 明示=**既定値(PerTest)に戻してはならない**の契約化(戻すと毎 Dispatch 再初期化レースが復活) ②FailFast 監視=多層防御+診断装置。
- **機械 pin**: `CpHarnessEco083Tests`(監視の前提=`_dispatchTask` フィールドの存在を検査。Avalonia 更新で監視が黙って無効化される事態を顕在化)。
- 防御の層構造(ECO-081 と合わせた完成形): **PerAssembly(構造除去)→ FailFast 監視(即時診断)→ HangDump 5 分(最終安全弁)→ 基盤プロセスツリー上限**。

### 教訓

1. **fail-fast 監視は防御であると同時に診断装置**。第 1 段(FailFast 化)を入れた直後のフル run で実発火を捕獲し、原因例外+完全スタックが真因確定の決め手になった(「沈黙する故障は、まず喋らせてから治す」)。ECO-081 教訓①「fail-closed は実測で選定」の続編=**fail-fast 化は真因調査の加速装置を兼ねる**。
2. **ライブラリ既定値とコード内コメントの意図の乖離は実測でしか出ない**。HeadlessApp のコメントは「プロセス共有」を謳いながら、StartNew の既定 isolation=PerTest が毎回再初期化していた。既定値は「書かれていない設定」であり、横断規約(ECO-080)と同じく**沈黙する決定事項** — 重要なライブラリ挙動は既定に任せず明示する。
3. **再現ループは発火時に即停止し、ログ/ダンプを先に退避する**。同一パスへの上書きで証拠を 2 回失った(run4 の初回・run7)。証拠の揮発性を前提に採取を自動化・先行させる。

### 残課題(スコープ外)

- ECO-082(保留静置): 本 ECO 検証中に新証拠(SqliteCommand.Dispose の NRE=background 残タスク×TempDb.Dispose 競合疑い)を §8 へ記録済み。再開時の起点。
- Avalonia 上流への issue 報告(案F)→ **投稿済み(2026-07-14 裁定変更)**: 主 issue(cleanup 経路の故障増幅=消費者ループ静黙死→永久ハング・**12.1.0 でも決定論的に再現を実測**・構築時のみ #21688 で修正済み)を maintainer が投稿= [AvaloniaUI/Avalonia#21770](https://github.com/AvaloniaUI/Avalonia/issues/21770)。関連 issue(PerTest 再初期化レース)は**保留**(求められたら対応)。ドラフト・12.1.0 実測記録・最小再現・Issue 2 投稿手順は [bomdd/reports/upstream-avalonia-headless-eco083/](reports/upstream-avalonia-headless-eco083/README.md)。上流修正時はリフレクション監視の撤去を ECO 起票で検討。
