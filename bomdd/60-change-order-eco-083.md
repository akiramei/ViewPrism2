# ECO-083 — Headless ディスパッチループの静黙死で以後の UI テストが全て無限待ちになる(並列フル run 間欠ハングの真因)

- status: staged
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
