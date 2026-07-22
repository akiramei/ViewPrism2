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
- **/eco-fix 着手時に H1/H2 をプローブで確定**(真因が H2 なら是正対象が変わる)。
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
