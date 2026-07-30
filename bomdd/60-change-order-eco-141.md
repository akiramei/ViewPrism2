# ECO-141 — Avalonia 12.1.1 への更新と ECO-083 FailFast 監視の撤去判断(上流修正の取り込み)

- 種別: 保守(依存更新)+回避策の撤去判断。上流欠陥の修正取り込みであり製品欠陥の是正ではない
- status: staged
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
  **minor 更新のため再評価不要**の判断を注記)。**Avalonia.Diagnostics の台帳/as-built 乖離を棚卸し**
  (実参照を追加するか台帳から落とすか= 本 ECO 内の doc-only 是正)。
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
- **golden**: 機械受入後に判定。**Gf 視覚パリティ 43 件が全緑なら golden n/a を提案**(ECO-134 前例=
  挙動同値の機械裏取りで実機 golden 不要化)。1 件でも赤化すれば差分裁定+実機 golden へ。
- **R3 分離**: `CpUiG6SaveBarTests` の別失敗モードは本 ECO で解決を約束しない(観測のみ・
  再発すれば別起票)。
