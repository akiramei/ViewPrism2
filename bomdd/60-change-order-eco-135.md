# ECO-135 — スキャン失敗の理由が判別できない(root 改名/不在も一律「ファイルの読み書きに失敗しました」)

- 種別: 不具合+仕様改訂候補(エラー語彙の設計上の欠落。実装は現仕様に忠実)
- status: staged
- baseline: main `607cfb6`
- 報告者: maintainer(2026-07-22・手動テスト中)
- 優先度: 中(手動テストの支障=失敗の切り分け不能)

## §1 症状

登録済み同期フォルダ(例: `<ドライブ>:\...\<監視フォルダ名>`。本件は OneDrive 配下フォルダで観測)を
ディスク上でリネームしてから
「スキャン」を押すと、フォルダ行下に **「ファイルの読み書きに失敗しました」** とだけ表示される。
画像の状態は変わらない(missing にならない)。**なぜ失敗したのか(ルート改名/権限/一時 I/O)が
判別できず**、手動テストで原因追跡ができない。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 未定義(疑い) | `docs/screens/` にフォルダ管理面の単独 spec 無し。スキャン失敗文言/root 失効導線の mock 不在 |
| BOM/仕様(M-CORE-001) | **欠落** | エラー語彙に「スキャンルート不在/改名」を表す語がなく、actionable な失敗が generic `IoError` に潰れる |
| 実装 | **仕様に忠実(逸脱ではない)** | `ErrorMessages.Resolve` が code→i18n で `Result.Message` を捨てるのは silence_sweep の decision どおり |

結論: **仕様/設計層の欠落(エラー語彙)**。実装は M-CORE-001 の「Result<T>+ErrorCode で全列挙・
UI 文言は `error.<code>`」([32-mbom.yaml:818](32-mbom.yaml#L818) silence_sweep=specified/decision)に
忠実で逸脱していない。したがって是正は**エラー語彙の設計拡張**を伴う → gate①(裁定)が必要。
一方、失敗時に画像状態が変わらない挙動自体は REQ-100 の設計どおりで**意図通り**(下記事実 5)。

## §3 切り分け済みの事実(確定と未検証を分離)

### 確定(コード実測)

1. [ErrorMessages.cs:11-15](../src/ViewPrism2.App/ViewModels/ErrorMessages.cs#L11) の `Resolve` は
   `ErrorCode?` のみ受け取り `localization.T(KeyOf(code))` を返す=スキャンが返す具体的な
   `Result.Message` を**捨てる**。全スキャンエラー表示サイトが同型:
   [FolderManagementViewModel.cs:229](../src/ViewPrism2.App/ViewModels/FolderManagementViewModel.cs#L229)
   (`ScanAsync` 経路の行メッセージ=画面の表示元)・
   [ScanSummaryViewModel.cs:200](../src/ViewPrism2.App/ViewModels/ScanSummaryViewModel.cs#L200)
   (二段階 `StageAsync` 失敗)。
2. スキャンは失敗理由別に異なる `Message` を返すが**全て同一 ErrorCode(`IoError`)**に潰れる →
   表示は一律「ファイルの読み書きに失敗しました」([ja.json:38](../src/ViewPrism2.App/Assets/i18n/ja.json#L38))。
3. root 不在は [ScanService.cs:156-158](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L156)
   (二段階 `StageCoreAsync`)/ [ScanService.cs](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs)
   の一段階 `ScanCoreAsync` 同型 check で `Result.Fail(ErrorCode.IoError, "フォルダ '{root}' にアクセスできません。")`。
   → 「フォルダが見つからない」という切り分け可能な情報が generic 文言に埋もれる。
4. [ErrorCode.cs](../src/ViewPrism2.Core/Common/ErrorCode.cs) は現状 10 種。ディスク上の
   「スキャンルート不在/改名」を表す語なし(`NotFound` は DB 行不在用=「同期フォルダが存在しません」)。
5. 二段階スキャン(ECO-130)は Stage 中 DB 無変更。`StageAsync` が失敗を返すと差分未適用 →
   画像状態は不変(REQ-100「破棄で無かったことにできる」)。**「missing にならない」は設計どおり=意図通り**
   (mass-missing 事故防止の安全側)。
6. 列挙 [CreateEnumerable](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L627) は
   `EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = ReparsePoint }`
   (OneDrive クラウド専用プレースホルダ=reparse point をスキップ)。per-file ループは
   `IOException`/`UnauthorizedAccessException`(`DirectoryNotFoundException` 含む)を catch し
   `readFailures++` で継続([ScanService.cs:317-322](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L317))。
   → **単体ファイルの読めないでは全体は止まらない**。

### 未検証(疑い)

- **H1(最有力)**: 登録フォルダをディスク上でリネームしたため DB 保存パスが失効 →
  `Directory.Exists(root)=false` が本症状の真因。画面のパス表示・最終スキャン時刻は前回成功時の記録
  (Stage は last_scan 非更新)。
- **H2(対抗)**: 列挙 `foreach ... MoveNext` 中の I/O 例外が per-file try の外に抜け、
  [StageAsync:138-142](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L138) の外側 catch で
  全体中断。
- **どちらが真因かで是正対象(文言タクソノミ)が変わる** → /eco-fix のプローブで確定
  (リネーム→再スキャン再現・root 不在注入 vs 列挙時例外注入)。
- CAD にフォルダ管理面のエラー文言/root 失効導線の定義があるか(`docs/screens` に単独 md 無し=未定義の疑い)。

## §4 是正方針(案・着手時確定)

**前提**: まず /eco-fix のプローブで H1/H2 を確定してから文言タクソノミを決める。

- **案A(推奨)**: エラー語彙に「スキャンルート不在/改名」の専用 ErrorCode(例 `ScanRootMissing`)を追加し、
  actionable な i18n 文言(「フォルダが見つかりません。移動または改名された可能性があります」)を割り当て。
  M-CORE-001 の code→i18n モデルを維持したまま識別性を回復。root check を新コードで返す。
- **案B**: スキャン失敗に限り `Result.Message` を UI へ透過(silence_sweep decision の局所例外)。
  一貫性を崩すため要裁定。
- **案C(導線・独立検討可)**: root 失効時にパス更新/再登録/再リンクの導線を提示。CAD mock 必要。
- H2 が真なら列挙時 I/O 例外も per-file と同様に握って継続 or 明示中断メッセージ、を追加検討。

## §5 影響 BOM

- `spec/BOM`=M-CORE-001 interface_contract.errors(ErrorCode 追加)・32-mbom silence_sweep 追随
- `src`=ErrorCode.cs(新コード)・ScanService(root check の返却コード)・ErrorMessages.cs(KeyOf マッピング)・
  i18n ja/en(新キー)
- `CAD`=フォルダ管理面のスキャン失敗文言/root 失効導線(案C 採用時)= ViewPrismUI 申し送り
- `tests`=root 不在→新コード/新文言の VM probe・(H2 確定時)列挙時例外の扱い

## §6 残ゲート

- ~~gate①(裁定)= 必要~~ → **裁定済み(§7)**。
- ~~/eco-fix 着手時に H1/H2 をプローブで確定~~ → **H1 確定・H2 不再現(§8)**。
- **gate②(golden)**: 是正後、リネーム→再スキャンで actionable な文言が出ること+回帰。

## §7 裁定(gate①・2026-07-22 maintainer)

- **案A を採択**: エラー語彙に「スキャンルート不在/改名」の専用 ErrorCode(`ScanRootMissing` 想定)を
  追加し、actionable な i18n 文言(ja「フォルダが見つかりません。移動または改名された可能性があります」/
  en 相当)を割り当てる。M-CORE-001 の code→i18n モデルは維持する(局所例外を作らない=案B 不採用)。
- **CAD 裁定は不要**: code→i18n の既存モデル内の語彙追加であり、フォルダ管理面の視覚/導線 mock を
  伴わない(案C=root 失効時のパス更新/再登録/再リンク導線は本 ECO のスコープ外=必要なら別 ECO)。
- **着手条件**: /eco-fix でまず H1/H2 をプローブ確定 → root 不在(H1)経路を新コードで返す是正へ。
  H2(列挙時 I/O 例外の全体中断)が同時に真と判明した場合は、その扱い(継続 or 明示中断文言)を
  §4 の追加検討として本 ECO 内でプローブ先行是正(スコープ内=同じ「スキャン失敗の識別性」)。

## §8 実施記録(fix)

- **プローブ先行(R5)+H1/H2 確定**: `CpScanRootMissingTests` 新設(二段階 `StageAsync`・一段階
  `ScanAsync` の両経路)。フォルダ登録後にディスク上のルートを削除=移動/改名でパス失効を模擬し、
  返却 ErrorCode を検査。**是正前=`Expected ScanRootMissing, Actual IoError` で両経路不合格**を実測
  → **H1(root 失効→Directory.Exists=false)を真因確定**。**H2(列挙 MoveNext の I/O 例外が全体中断)は
  本シナリオでは不再現**(root 失効時は `Directory.Exists` で先に検出され列挙に到達しない)=投機的是正はしない
  (列挙時例外の扱いは本 ECO スコープ外・将来報告があれば別 ECO)。
- **是正(案A)**: 専用 `ErrorCode.ScanRootMissing` を追加し、root 不在の2サイト
  ([ScanService.cs:156-159](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L156) StageCore・
  [ScanService.cs:487-492](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L487) ScanCore)を新コードで返却。
  [ErrorMessages.cs](../src/ViewPrism2.App/ViewModels/ErrorMessages.cs) の KeyOf に case 追加・
  i18n ja/en に `error.scanRootMissing`(ja「フォルダが見つかりません。移動または改名された可能性があります」)。
  code→i18n モデル維持(案B 不採用)・CAD 非接触。
- **as-built 同期(doc)**: [32-mbom.yaml:41](32-mbom.yaml#L41) の M-CORE-001 エラー語彙列挙へ ScanRootMissing を
  追加。**その際、既存記載漏れ(ECO-002 `Database`・ECO-045 `TagInUse`=旧「8 種」表記のまま stale)も
  as-built へ同期**(語彙の全列挙を回復=11 種)。silence_sweep note も 11 種へ更新。これは ScanRootMissing
  追加で列挙が「9 種」に見えて実態 11 種とさらにズレるのを避けるための台帳正確化であり、挙動変更は伴わない。
- **diff 規模**: src 5 ファイル(ErrorCode +6・ScanService +8/-4〔2 サイト〕・ErrorMessages +1・ja/en 各 +1)、
  tests 2 ファイル(CpScanRootMissingTests 新設・CpScan004 の期待コード追随 2 行)、doc 1(32-mbom 2 行)。
- **機械受入(4 点・全緑)**: `dotnet build` 0 error ・`ViewPrism2.Tests` **926/926**(プローブ 2 本合格に反転)・
  `ViewPrism2.Oracle` 109 pass/4 skip/0 fail=**凍結オラクル無接触(R6)** ・`validate_bom` 0-0。
- **R6 補足**: 期待値改訂は [CpScan004Tests.cs:656](../tests/ViewPrism2.Tests/CpScan004Tests.cs#L656)
  (root 不在の付随 ErrorCode を IoError→ScanRootMissing)のみ。これは **ViewPrism2.Tests の CP テスト
  (凍結オラクル非該当)**で、本 ECO が意図的に変える挙動の追随。Oracle 側に root 不在→IoError の pin なし。
- **R7(セルフゴールデン)= 対象外**: 新規/変更した視覚サーフェスなし(エラー語彙=i18n テキスト追加のみ・
  gate① で CAD 不要と裁定)。表示文言は CpI18n010 lint(ja/en 整合)で担保。
- **R8(セルフレビュー)= 実施・所見0**: fix diff を fresh-context の独立レビュアーで精査(実コードベース検証)。
  ①網羅漏れなし(root 不在サイトは2箇所のみ・SnapshotService は不在時 `[]` で IoError にしない) ②ErrorCode 分岐は
  KeyOf(default 付き)のみで非網羅化なし ③リグレッションなし(一括 missing 化しない契約・last_scan 更新・
  呼び出し側に IoError 特別分岐なし) ④i18n 両ロケール ⑤命名/配置=既存流儀。スコープ内欠陥 0。
  スコープ外の非欠陥1件(補間 Message は silence_sweep 設計上 UI へ届かず捨てられる=旧 IoError 行と同型・
  リグレッションでない)を記録=処置不要。

## §残ゲート(更新)

- gate①=裁定済み(§7・案A)。H1 確定・H2 不再現(§8)。
- **gate②(golden)= 是正後提示(下記)**。合格報告を受けたら /eco-accept eco-135。
