# ECO-141 — Avalonia 12.1.1 への更新と ECO-083 FailFast 監視の撤去判断(上流修正の取り込み)

- 種別: 保守(依存更新)+回避策の撤去判断。上流欠陥の修正取り込みであり製品欠陥の是正ではない
- status: implemented(2026-07-24 /eco-fix 完了・機械受入全緑・実機 golden 待ち)
- baseline: main `322e0ba`
- 優先度: 中(実害は現時点でなし=回避策が機能中。ただし①上流修正の恩恵〔#21688/#21781〕を取り逃している
  ②リフレクション依存の回避策を抱え続ける保守負債 ③ECO-082 の再測機会を逃す)

## §1 症状 / 要求

maintainer 報告(2026-07-24): **Avalonia 12.1.1 がリリースされ、我々が投稿した不具合報告が対応された**。
現在行っているバグ回避が不要になるかもしれない。

- リリースノート([12.1.1](https://github.com/AvaloniaUI/Avalonia/releases/tag/12.1.1)):
  「Headless – Fix headless session hang when cleanup throws by @NathanDrake2406 in #21781」
- これは我々の [issue #21770](https://github.com/AvaloniaUI/Avalonia/issues/21770)(ECO-083 案F・maintainer 投稿)の修正。
- 要求: 12.1.1 を取り込み、**不要になった回避策を撤去**する。ただし「どの回避策が不要になるか」は
  事象ごとに異なるため切り分けが要る(§3)。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| **CAD(ViewPrismUI)** | **該当なし(ただし副作用の可能性)** | UI 設計の変更を伴わない。ただし minor bump(~35 fixes)でテキストメトリクス・コントロールテンプレートが変われば **Gf 視覚パリティ 11 ファイル/43 件**が赤化し得る=その場合のみ CAD captures との差分裁定(許容差分 or CAD 側改訂)が発生する |
| **BOM** | **更新対象+既存の台帳欠陥** | ①`32-mbom.yaml` の調達部品表がバージョンを pin(更新対象・「これ以外のパッケージ採用はずる報告対象」) ②**M-HARNESS-015 の `fail_closed` 契約に「②多層防御= `_dispatchTask` をリフレクション監視し FailFast」が明文で書かれている**=撤去するなら契約改訂が必須 ③**既存の台帳/as-built 乖離**: procurement に `Avalonia.Diagnostics 12.0.4` があるが csproj に `PackageReference` が存在しない(§3) |
| **実装** | **逸脱なし(回避策は正しく機能中)** | `HeadlessApp.cs` の 4 層防御はいずれも ECO-083/084 の裁定どおりに実装され、現在も機能している。**上流修正により 1 層の存在理由が消える可能性がある**だけで、実装の欠陥ではない=「上流改善に伴う保守」 |

**結論**: 不具合是正ではなく **依存更新+回避策撤去の保守 ECO**。撤去範囲は事象単位で切り分ける必要があり、
**gate① 裁定が必要**(§6)。

## §3 切り分け済みの事実

### 確定(実測・2026-07-24)

- **12.1.1 に PR #21781 が含まれる**(リリースノート実取得で確認)。12.1.0 の #21688(construction 経路)も
  未取得のため、12.0.4 → 12.1.1 で**両方**入る。
- **現行 pin= 12.0.4(実参照 7 箇所)**:
  - [ViewPrism2.App.csproj:11-14](../src/ViewPrism2.App/ViewPrism2.App.csproj) = Avalonia / Desktop / Themes.Fluent / Fonts.Inter
  - [同:17](../src/ViewPrism2.App/ViewPrism2.App.csproj) = Avalonia.Controls.ItemsRepeater **12.0.0**(別 NuGet)
  - [ViewPrism2.Tests.csproj:24](../tests/ViewPrism2.Tests/ViewPrism2.Tests.csproj) / [CaptureHarness.csproj:18](../tools/ViewPrism2.CaptureHarness/ViewPrism2.CaptureHarness.csproj) = Avalonia.Headless
- **ItemsRepeater は 12.1.x が存在しない**(NuGet 実取得: 最新= 12.0.0〔2026-04-07〕・依存は `Avalonia >= 12.0.0`)。
  よって**バージョン据え置きで 12.1.1 と併用可**が宣言上は成立(実挙動は未検証)。
- **回避策は 4 層あり、#21770 に対応するのは 1 層のみ**([HeadlessApp.cs](../tests/ViewPrism2.Tests/HeadlessApp.cs)):

  | 層 | 立地 | 対象事象 | 上流修正との関係 |
  |---|---|---|---|
  | FailFast 監視(`_dispatchTask` リフレクション) | :62-74 | **#21770**= cleanup 例外でループ静黙死→全テスト無限待ち | **#21781 が該当**=撤去候補 |
  | PerAssembly 明示 | :53 | ECO-083 真因= PerTest 再初期化のスレッドアフィニティ違反 | **別事象**(未報告 Issue 2)=維持 |
  | SessionInitFixture(AssemblyFixture) | :29-42 | ECO-084 初期化 race | **別事象**=維持 |
  | HangDump(csproj 既定) | M-HARNESS-015 | 最終安全弁(総括) | **別軸**=維持 |

- **撤去判断の実測トリガは仕込み済み**: [CpHarnessEco083Tests](../tests/ViewPrism2.Tests/CpHarnessEco083Tests.cs) が
  `HeadlessUnitTestSession._dispatchTask` の**存在+Task 互換**を pin。Avalonia 更新で内部構造が変われば赤化する
  (監視自体は実行時スキップ=安全側・ECO-080 の 3 層原則)。
- **上流修正の内容**(#21781 マージ時に受領・2026-07-20): `DispatchCore` が app dispose 例外を捕捉し TCS 完了前に
  work item 結果へ格納・cleanup callback は `finally` で ambient state 復元 → **cleanup 例外は該当 Dispatch task に
  surface しセッションは生存**する(我々の FailFast= プロセスクラッシュより穏当な挙動へ改善)。
- **台帳/as-built 乖離(既存)**: `32-mbom.yaml:769` に `Avalonia.Diagnostics 12.0.4`(rationale「Debug 構成のみ
  参照可」)があるが、csproj に該当 `PackageReference` は**存在しない**(全 csproj の PackageReference 実測)。
  - **【起票時の解釈を /eco-fix で訂正(2026-07-24)】** 調達部品表は「**これ以外のパッケージ採用はずる報告対象**」=
    **許諾ホワイトリスト**であり、「載っているが未参照」は**乖離ではなく許諾枠**(rationale の「参照**可**」も
    その読み)。**真の穴は逆向き**だった: **`Avalonia.Headless` は 2 プロジェクトで実参照されているのに
    調達表へ未記載**(= 未許諾パッケージの採用に相当)。本 ECO でこちらを是正する(§5)。
- **視覚検査の規模**: `tests/ViewPrism2.Tests/Gf*.cs` = **11 ファイル / 43 検査**(実レイアウト計測)。

### 未検証(疑い — gate① 裁定/その後の /eco-fix で実測)

- **12.1.1 で `_dispatchTask` フィールドが残存するか**(= pin が赤化するか)。赤化すれば撤去は必須だが、
  **緑のままでも「不要」とは決まらない**(フィールド存在≠監視の必要性)。撤去可否は**挙動の裏取り**
  (cleanup 例外注入で「セッション生存+該当 task に surface」を実測)まで求めるべき=案A の骨子。
- ItemsRepeater 12.0.0 × Avalonia 12.1.1 の実挙動(宣言上は互換。グリッド仮想化 REQ-041 の回帰要確認)。
- **Gf 視覚パリティ 43 件の赤化有無**(minor bump の主リスク)。赤化ゼロなら golden n/a の可能性
  (ECO-134 の「挙動 bit 一致で実機 golden 不要化」前例)。
- **ECO-082(保留静置)の再現挙動変化**: `SqliteCommand.DisposePreparedStatements` NRE(TempDb.Dispose ×
  テスト内 background 残タスク競合)は上流修正で顔が変わり得る=**再測の好機**。
- **別失敗モードの帰趨**: `CpUiG6SaveBarTests` の単発 flake(`ObjectDisposedException 'TestContext'`)は
  #21770 の「セッション全体ハング」とは別モードで、**#21781 で直る保証はない**(ECO-083 クローズ時に記帳済み)。

## §4 是正方針(**gate① 裁定確定 2026-07-24= 案A・挙動裏取り必須・ECO-082 は再測のみ**)

- **案A(段階撤去)= 採用**: 12.1.1 へ一括更新(ItemsRepeater は 12.0.0 据え置き)→ 機械受入 →
  **FailFast 監視の撤去は実測で分岐**:
  - `CpHarnessEco083Tests` 赤化 → 前提消滅につき撤去(監視は既に無効化されているため)。
  - 緑のまま → **cleanup 例外の一時注入で「セッション生存+Dispatch task へ surface」を実測**してから撤去
    (ECO-083 の是正時と同じ注入手法=実測記録が既にある)。裏取りできなければ**残置**。
  - PerAssembly / SessionInitFixture / HangDump は**いずれの分岐でも維持**(別事象)。
  - ECO-082 は**束ねず**、更新後にフル run で再現有無のみ観測し ECO-082 本文へ追記(状態は保留静置のまま)。
- **案B(更新のみ)= 不採用**: リフレクション依存の保守負債と「契約に死文が残る」状態が続くため。
- **案C(見送り)= 不採用**: 自ら報告した修正の恩恵(#21770/#21688)を取り逃すため。
- 共通: **凍結オラクル(tests/ViewPrism2.Oracle)は無接触**(R6)。撤去する場合は M-HARNESS-015 の
  `fail_closed` 契約②を**同時に**改訂する(実装と契約の同期=契約に死文を残さない)。

## §5 影響 BOM(gate① 後に確定)

- **調達部品表**(`32-mbom.yaml` procurement): Avalonia 系 4 パッケージ+Headless を 12.1.1 へ。
  ItemsRepeater は据え置き(rationale の「archived after 12.0= Avalonia major 更新時に再評価」は
  **minor 更新のため再評価不要**の判断を注記)。**Avalonia.Diagnostics は行ごと除去**
  (実施時に **12 系が NuGet 未発行**=最新 11.3.18 と実測。既存記帳〔App csproj・S-BOM〕と一致。
  発行されたら再許諾)。**`Avalonia.Headless` と `Microsoft.Testing.Extensions.HangDump` を追加**
  (いずれも実参照があるのに未記載だった=未許諾採用に相当・§7.4 D-4)。
- **S-BOM**(`53-service-bom.yaml`・**起票時の §5 宣言漏れ**= R8 D-5 で補完): K-AVALONIA の
  `pinned_version` と部品側の版行 ×8 を 12.1.1 へ同期+Diagnostics の trap を実測値へ更新。
  本 ECO は S-BOM が `drift_source: "Avalonia の minor/major 更新"` として想定していた**ドリフトの実例**であり、
  ここを更新しないと S-BOM の逆引き機能が死ぬ。
- **M-HARNESS-015 `fail_closed` 契約**: 撤去する場合は②(FailFast 監視)の記述を改訂し、
  上流修正後の防御層構造(PerAssembly → HangDump → 基盤上限)へ更新。
- **src/tests**: 3 csproj のバージョン・[HeadlessApp.cs](../tests/ViewPrism2.Tests/HeadlessApp.cs)(撤去時)・
  [CpHarnessEco083Tests.cs](../tests/ViewPrism2.Tests/CpHarnessEco083Tests.cs)(pin の役割変更 or 撤去)。
- **Control Plan**: CP-UI-G1(pin テストの trait 先)。視覚パリティ赤化時は該当 Gf の許容差分裁定。
- **関連 ECO**: ECO-082(再測のみ・束ねない)・ECO-083(本回避策の出自)・ECO-081(HangDump 契約)・
  ECO-084(SessionInitFixture)・ECO-026(ItemsRepeater 採用)。

## §6 残ゲート

- **gate①(裁定)= 裁定済み(2026-07-24 maintainer)= 案A 採択**:
  1. **更新範囲**= 12.1.1 へ一括更新+段階撤去(ItemsRepeater 12.0.0 据え置き)。案B/C は不採用。
  2. **FailFast 監視の撤去基準**= 案A の分岐に従い、**pin 緑の場合は cleanup 例外注入による挙動裏取り
     (セッション生存+Dispatch task へ surface)を必須**とする(変更ログのみを根拠に防御層を外さない=
     ECO-083「沈黙する故障は、まず喋らせてから治す」の逆走を避ける)。裏取り不能なら残置。
  3. **ECO-082**= 束ねず、更新後のフル run で再現有無のみ観測して ECO-082 本文へ追記(保留静置は維持)。
- **golden(判定済み・§7.3)= 実機 golden を要求**。当初の「Gf 43 件全緑 → golden n/a」提案は
  **撤回**した: Gf は全緑(レイアウト数値契約は不変)だが、**同一 UI コードで Avalonia のみ差し替えた
  対照実験で 29 面中 24 面に画素差**(寸法差 0=描画レベルの drift)を実測したため。
  S-BOM が K-AVALONIA へ `reinspect_depth: G` を割り当てた記帳どおりの事象。
- **R3 分離**: `CpUiG6SaveBarTests` の別失敗モードは本 ECO で解決を約束しない(観測のみ・
  再発すれば別起票)。

## §7 /eco-fix 実施記録(2026-07-24)

### 7.1 実施内容

- **版更新**: Avalonia 系 6 参照を 12.0.4 → **12.1.1**(App 4・Tests/CaptureHarness の Headless 各 1)。
  **ItemsRepeater は 12.0.0 据え置き**(12.1.x は NuGet 未発行・依存 `Avalonia >= 12.0.0`)。
- **API 移行(想定外・minor bump の副作用)**: `Bitmap.Save(string, int?)` が 12.1.1 で `[Obsolete]` 化し、
  `TreatWarningsAsErrors=true`(Directory.Build.props)によりビルド失敗。CaptureHarness を
  `Save(path, PngBitmapEncoderOptions.Default)` へ移行。**出力 PNG は旧 API とバイト完全一致**を R8 が実測
  (799B/799B・`CompressionLevel=Optimal`)。
- **FailFast 監視の撤去**(gate① 条件2 の分岐に従い実測で裏取り後・§7.2)。
- **検査の置換**: `CpHarnessEco083Tests` を**内部フィールド(`_dispatchTask`)の pin から挙動契約の pin へ**
  (cleanup 段/work item 本体の例外がいずれも当該 Dispatch へ surface しセッションが生存すること)。
  私的リフレクションへの依存自体を解消。時間上限は性能閾値でなく **liveness 保護**。
- **維持**: PerAssembly 明示・SessionInitFixture・HangDump(いずれも別事象=撤去対象外)。

### 7.2 gate① 条件2 の裏取り(撤去の根拠・証跡)

**pin は緑のまま**(= `_dispatchTask` は 12.1.1 でも残存)だったため、裁定どおり**挙動の実測**へ進んだ。
上流報告時の**独立プロセス再現ハーネス**(`bomdd/reports/upstream-avalonia-headless-eco083/repro`)を
**陽性対照つき**で実行(ECO-141 で版を MSBuild プロパティ化=committed artifact からそのまま再現可能):

| ケース | 12.1.0(陽性対照) | 12.1.1(検証) |
|---|---|---|
| poison(cleanup 段の RunJobs で例外) | 未完了(TCS 未遷移) | **faulted**=呼び出し元へ surface |
| 後続 Dispatch | **未完了=永久ハング再現** | **完了** |
| work item 本体の throw | 未完了(※上で既にループ死亡=独立対照ではない) | **faulted**=自身の TCS へ |
| その後の Dispatch | 未完了=ループ死亡 | **完了=ループ生存** |

**限定(R8 が IL 実測で確定)**: consumer loop の catch は 12.1.0/12.1.1 とも `OperationCanceledException`
のみで**不変**、12.1.1 の追加保護は work item 側の 1 箇所だけ。よって封じ込めが確認できたのは
**外部から到達できる 2 経路(cleanup 段/work item 本体)**に限り、queued action の prologue/epilogue・
`finally` 本体・`ExecutionContext.Run` は依然無保護(残余経路は Avalonia 内部のみ)。
**撤去による診断能力の後退**(FailFast が出していた原因例外全文の即時顕在化の喪失。ECO-082/083 の
真因確定の決め手だった)は HeadlessApp のコメントと M-HARNESS-015 契約へ明記した。

### 7.3 R7(視覚)と golden 判定 — **当初の「golden n/a」提案は撤回**

- `Gf*` 視覚パリティ **43 件(11 ファイル)は全緑** = レイアウト数値契約は不変。
- しかし **CaptureHarness による実描画の PNG 差分は広範**だった。**同一 UI コード(HEAD)で
  Avalonia だけ差し替えた対照実験**(一時 worktree で 12.0.4 撮影 → 12.1.1 と比較):
  **29 面中 24 面に画素差・寸法差 0**(maxdelta 87〜255)。レイアウト構造は不変で、
  差分は**描画レベル**(グリフラスタライズ/アンチエイリアス/色ブレンド)。
- したがって **「Gf 全緑 → golden n/a」は成立しない**。S-BOM(`53-service-bom.yaml`)自身が
  K-AVALONIA に `reinspect_depth: G` を割り当て「描画/バインディング/入力の劣化は unit に出ず
  golden/L1 で顕在化(ECO-002 実証)」と記帳しており、**その記帳どおりの事象が実測された**。
  ECO-134 前例(挙動 bit 一致で実機 golden 不要化)は**同一ライブラリ版での性能是正**への裁定であり、
  レンダラ版更新には転用できない(R8 M-1 の指摘を受理)。
- **→ gate②= 実機 golden を要求**(§6 更新)。比較材料= 12.0.4/12.1.1 の captures 一式(scratchpad)。

### 7.4 R8 独立レビュー(fresh context・50 tool 呼び出し)

**総合判定=条件付き出荷可(コードはブロッカーなし・是正必須 6 件のうち 5 件が台帳/記録)**。
検査者は再現ハーネスの複製実行・IL の `ExceptionHandlingClauses` ダンプ・PNG バイト比較・
NuGet flat-container 実取得まで独立に行った。所見と処置:

| # | 所見 | 処置 |
|---|---|---|
| D-1 中 | 「到達可能な escape 経路は全て封じ込め」は過大主張(IL 実測でループの catch は不変)+診断能力の後退が未記録 | **是正**: コメント/契約を「外部到達 2 経路に限る」へ限定+後退を明記(§7.2) |
| D-2 中 | §7 が存在しないのにコードが「ECO-141 §7」を参照(dangling)・裏取り証跡が正本に不在 | **是正**: 本 §7 を追加 |
| D-3 中 | Avalonia.Diagnostics の「許諾枠」診断が誤り。真因は **12 系が NuGet 未発行**(最新 11.3.18)で、既存記帳(App csproj・S-BOM)への read-across 漏れ | **是正**: procurement から行ごと除去+S-BOM の trap を実測値で更新 |
| D-4 中 | 欠陥クラス(実参照なのに調達表未記載)の掃討が不完全。**Microsoft.Testing.Extensions.HangDump 1.9.1** が Tests/Oracle 両方で参照されているのに未記載(しかも本 ECO が改訂する契約の主役) | **是正**: procurement へ追加(全 PackageReference 突合で残り 0 を確認) |
| D-5 中 | **S-BOM が 12.0.4 のまま**(`pinned_version` + K-AVALONIA 版行 ×8)。S-BOM は「Avalonia の minor/major 更新」を drift_source に持つ**まさにこのドリフト用の台帳** | **是正**: 12.1.1 へ同期(§5 の宣言漏れも本記録で補完) |
| D-6 小〜中 | gate① 条件3(ECO-082 再測)未履行 | **是正**: ECO-082 §9 へ観測を追記(保留静置は維持) |
| M-1 中 | 「Gf 全緑 → golden n/a」提案は S-BOM の記帳と矛盾 | **受理**: §7.3 のとおり提案を撤回し実機 golden を要求 |
| L-1〜L-9 軽微 | stale コメント(12.0.4)3 件/撤去済み機構への現在形参照/`return` 後の墓碑コメント/契約層列挙の SessionInitFixture 欠落/契約テストの根拠注記/repro の陽性対照が committed artifact から再現不能/UnobservedTaskException の将来リスク | **是正**(L-8 は実害なしとして記録のみ・L-9 は cheat-log 記帳・cheat-log/報告書内の**過去形の歴史記述は改変しない**) |
| R3 | CLAUDE.md「Oracle skip 既知 2 件」が実測 4 件(ECO-128 以来の stale・本 ECO 起因でない) | **分離**: cheat-log 記帳 |

**所見 0 の観点**(検査者確認): 影響 BOM 突合(ついで修正なし・**src 製品コードの挙動変更ゼロ**=App は
PackageReference のみ)・凍結オラクル無接触(R6)・PerAssembly/SessionInitFixture/HangDump の維持・
契約テストが実経路に繋がり退行時に確実に赤くなること(12.1.0 実測で 4 つの await すべて未完)・
API 移行のバイト一致・dead code ゼロ(`DispatchTaskFieldName`/`using System.Reflection;` とも残参照 0)。

### 7.5 機械受入(設計者+R8 の二重実測)

build **0 warning / 0 error**(Release も 0/0)・Tests **972/972**(旧 pin 1 本 → 契約 2 本で 971→972)・
Oracle **109 pass / 4 skip・凍結 diff 0**(R6)・validate_bom **0/0**。
