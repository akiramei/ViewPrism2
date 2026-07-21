# ECO-133 — 二段階スキャン適用の部分失敗後、同一 staging の再試行が UNIQUE 衝突で残余へ到達できない(staged)

- 起票日: 2026-07-21
- 報告者: Codex review(6fa8834..HEAD・2026-07-21・P2)
- 種別: 不具合(UI/VM 層・失敗回復モデルの不整合)
- baseline: ViewPrism2 main `07e8a0a`
- 関連: ECO-130(二段階スキャン=ScanSummaryViewModel/ApplyStagedAsync 新設元) / ECO-059/060(512 件バッチ)

---

## 1. 症状(所見)

Codex review 指摘(P2): 二段階スキャンの適用(Apply)が、**1 つ以上の add バッチを commit した後に失敗**
すると、VM は `Summary` 面へ戻り Apply を同じ stale な staging のまま**再有効化**する。再試行は先頭の
add バッチから再実行され、**既に INSERT 済みの行に UNIQUE(id / sync_folder_id+relative_path)制約が当たって
再び失敗**するため、残りの未適用バッチへ到達できない。安全な回復には再スキャン(差分再計算)を強制するか、
部分失敗の可能性があるときは再試行を無効化する必要がある。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(scan_summary.md) | 対象外(見込み) | 失敗回復の遷移仕様は CAD 未定義だが、視覚ではなく状態機械の欠陥。文言追加は要確認 |
| BOM/仕様(REQ-100) | 部分整合 | 「バッチは 512 件独立トランザクション=部分適用があり得る・残余は次回スキャンが収束」は明記済み(=回復は再スキャン)。しかし VM が**同一 staging 再試行**を許す点が仕様の回復モデルと矛盾 |
| 実装(ScanSummaryViewModel) | **欠陥(回復モデル不整合)** | 適用失敗時 `Phase=Summary`+`CanApply` 真のまま=同一 staging で Apply 再有効化。設計の回復(再スキャン)へ導かない |

**結論: UI/VM 層の実装欠陥(失敗回復の状態遷移が仕様の回復モデルと不整合)。実装追随で是正。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **512 件独立トランザクションの部分適用**: [ScanService.cs:398-442](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs)
   `ApplyCoreAsync` は StatusUpdates→Deletes→MetaUpdates→**Adds** を `Chunk(_, 512)` ごとに
   `ApplyScanBatchAsync`(バッチ内は単一 Tx だがバッチ間は独立)で適用。コメント自身が
   「部分適用があり得る・残余は次回スキャンの差分計算が収束させる」と明記([:387-388](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs))=**回復は再スキャン**が設計意図。
2. **Adds は INSERT**: [ImageRepository.cs](../src/ViewPrism2.Infrastructure/Database/ImageRepository.cs)
   `ApplyScanBatchAsync` の Adds は `InsertSql`。images は PK(id)+UNIQUE(sync_folder_id, relative_path)。
   既適用行の再 INSERT は制約違反で throw。
3. **失敗時に同一 staging で再試行可能**: [ScanSummaryViewModel.cs:317-321](../src/ViewPrism2.App/ViewModels/ScanSummaryViewModel.cs)
   `if (!result.IsSuccess) { StatusMessage=…; Phase=ScanStagePhase.Summary; return; }`。
   `IsSummaryPhase`([:115](../src/ViewPrism2.App/ViewModels/ScanSummaryViewModel.cs))が Apply 面を制御し、
   `CanApply = s.TotalChanges > 0`([:403](../src/ViewPrism2.App/ViewModels/ScanSummaryViewModel.cs))は
   BuildSummary 時に真のまま=**失敗後に Apply が同じ staging で再クリック可能**。staging は再計算されない。
4. **混入**: ECO-130(二段階スキャン新設・ScanSummaryViewModel 作成)。ECO-130 R8 所見4 で
   「適用例外→throw せず Result+Summary 復帰」は処置したが、**同一 staging の再試行禁止/staging 無効化**は
   未処置=本 ECO の残余。

### 未検証(疑い)

- **StatusUpdates/Deletes/MetaUpdates は再試行しても冪等**(疑い・強): UPDATE by id / DELETE by id は
  再実行無害。**破綻するのは Adds(INSERT)の再実行**に限る=部分失敗が Adds フェーズに達していれば必ず再現、
  Status/Delete/Meta フェーズでの失敗なら Adds 未着手で再試行が概ね成功し得る(=間欠的に見える)。fix 時に切り分け。
- **実害の頻度**: 適用失敗自体が稀(I/O・DB ロック等)。ただし発生時は「破棄と報告しつつ残余適用不能」で
  ユーザーが Apply を連打して同じ失敗を繰り返す=不可解な行き詰まり。

## 4. 是正方針(案・着手時確定)

**案A(推奨・再スキャンへ誘導)**: 適用が失敗したら**同一 staging の再試行を禁止**する。最小形は
`CanApply=false`(または専用の失敗終端 Phase)へ遷移し、「再スキャンしてください」の文言+閉じる導線を出す
(設計の回復モデル=次回スキャンの差分再計算に合わせる)。staging は破棄。

**案B(自動再ステージング)**: 失敗時に `StageAsync` を自動再実行して差分を作り直し、残余だけの新 staging で
Apply 可能にする。UX は滑らかだが、失敗直後の自動再スキャンの妥当性(同じ失敗の再発)に注意。

diff 規模(案A 最小): ScanSummaryViewModel の失敗ハンドラ(Phase/CanApply/StatusMessage)+probe
(部分適用失敗→再試行が同一 staging で走らない/走っても UNIQUE で残余を潰さない)。視覚は文言追加のみ見込み。

## 5. 影響 BOM

- **src**: ScanSummaryViewModel.cs(失敗回復の遷移)。必要なら i18n(再スキャン誘導文言・K-AVALONIA 経由)。
- **tests**: 部分適用失敗(add バッチ commit 後に失敗を注入)→再試行が UNIQUE 衝突で残余を潰さないことの VM probe。
- **CAD**: 失敗回復文言を scan_summary.md へ追記(乖離あれば ViewPrismUI へ申し送り)。
- **CP**: CP 刻印は accept 時。

## 6. 残ゲート

- **gate①(裁定)**: 案A/案B の選択は診断上 A 推奨(仕様の回復モデルに整合)。CAD 文言追加が要るなら軽微裁定。
  基本は実装追随=裁定不要見込み。
- **gate②(golden)**: 是正後に提示(適用途中失敗を注入し、Apply 再試行が行き詰まらない・再スキャンへ導かれる・実機/probe)。

## 7. 起票時の申し送り

- Codex review 同一バッチの P1① は ECO-132 で分離起票済み。
- 是正は src のため R8(独立レビュー)必須。文言のみ視覚変更なら R7 は対象外宣言。
