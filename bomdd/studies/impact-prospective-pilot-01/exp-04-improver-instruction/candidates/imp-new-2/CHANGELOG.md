# ECO-139 / ECO-140 BOM 反映点検

- 点検基準: `eco/ECO-139.md`, `eco/ECO-140.md`, `eco/ECO-139.diff`, `eco/ECO-140.diff`
- 実装観測 stamp: `(observed@79123df)`
- 制約に従い `code/` と `eco/` は変更せず、ビルド・テスト・git は実行していない。

## 設計変更の要約

1. ECO-139 初版の高信頼一括裁定は `pending/new + candidate_link_id` を対象とする accept として実装されたが、GF-139-01 で最終的に「1 missing : 1 new の一意組だけを候補 missing へ原子一括 relink」へ再裁定された。元 `image_id`/タグを保持し、pending 行と missing を解消する。曖昧組は個別裁定へ回す（`eco/ECO-139.md:L170-L199`）。
2. ECO-140 は状態別の `RepairWindow` / `PendingReviewWindow` を、事象別 3 群（自動裁定できる／個別に確認／見つからない）の `IntegrityReviewWindow` へ置換した。旧 2 Window、ViewModel、画像タブの旧 2 入口は撤去される（`eco/ECO-140.md:L76-L96`, `L116-L124`, `L169-L180`）。
3. relink の選別（hash/folder/status/タグ安全/一意性）と確定（単発 T4／原子 batch）は `E-RELINK-007` へ一本化され、統合裁定面は consumer に徹する（`eco/ECO-140.md:L88-L90`, `L134-L159`）。
4. PEND-005 の reappeared は裁定時にだけ SHA-256 を再計算し、旧 hash と一致すれば T13 accept。不一致・読取失敗は個別へ残し、ScanJudge/ScanStaging の判定ロジックへ hash I/O を持ち込まない（`eco/ECO-140.md:L80-L85`, `L116-L118`）。
5. 実装時、`UpdateMetaAndPend` が記録 hash を上書きして比較を空虚化することが判明し、migration 011 の `pending_baseline_hash` と scan apply 時の旧 hash 保全が追加された。これは凍結した境界仮説の「前提疑義」であり、当初予測どおりのクラスタ完結ではない（`eco/ECO-140.md:L181-L187`, `L200-L207`, `L251-L257`）。
6. Control Plan は旧 pending/repair の検査ベクタを `CP-INTEGRITY-036` へ移管し、ECO-075 の大量 missing 応答性、GF-140-01〜03、relink 一本化用 `CpRelinkUnifiedBatchTests` を後継面で保持する（`eco/ECO-140.md:L230-L248`, `L265-L330`）。

## 編集記録

- `bom/30-ebom.yaml` / `E-HASH-006.graph_edges.consumers` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:L80-L85,L103-L108`; `eco/ECO-140.diff` `IntegrityReviewFileHashProvider.cs @@ -0,0 +1,27 @@`; `code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:10-25`。
- `bom/30-ebom.yaml` / `E-UI-INTEGRITY-050.depends_on` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:L80-L85,L103-L108`; `eco/ECO-140.diff` `IntegrityReviewService.cs @@ -0,0 +1,380 @@`, `IntegrityReviewFileHashProvider.cs @@ -0,0 +1,27 @@`; `code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:169-225`。
- `bom/30-ebom.yaml` / `E-RELINK-007.graph_edges` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:L88-L90,L134-L154`; `eco/ECO-140.diff:L67-L112`; `code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:78-109,243-245,378`。
- `bom/30-ebom.yaml` / `E-CRITERIA-037.graph_edges.consumers` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:L87,L100-L108`; `eco/ECO-140.diff` `IntegrityReviewViewModel.cs @@ -0,0 +1,781 @@`; `code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:635-670`。
- `bom/30-ebom.yaml` / `E-DESIGN-028.graph_edges.consumers` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:L119-L124`; `eco/ECO-140.diff` `IntegrityReviewWindow.axaml @@ -0,0 +1,577 @@`; `code/src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml:1`。
- `bom/30-ebom.yaml` / `E-UI-REPAIR-039.superseded_scope` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:L78-L90,L105-L108`; `eco/ECO-140.diff` `RepairWindow.axaml @@ -1,149 +0,0 @@`, `RepairViewModel.cs @@ -1,437 +0,0 @@`; `code/src/ViewPrism2.App/Views/RelinkWindow.axaml:1`, `code/src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs:1`。
- `bom/30-ebom.yaml` / `E-UI-PENDING-049.status/superseded_by/depends_on` / 種別: 陳腐化訂正 / 根拠: relink 最終裁定=`eco/ECO-139.md:L170-L199`; 面統合=`eco/ECO-140.md:L78-L90,L105-L108,L173-L174`; diff=`eco/ECO-140.diff` の旧 PendingReview 4 file 削除 hunk。
- `bom/32-mbom.yaml` / `M-SCAN-005.ebom_refs/artifact/interface_contract` / 種別: 陳腐化訂正+観測追記 / 根拠: `eco/ECO-140.md:L83-L85,L143-L154`; `eco/ECO-140.diff` `ScanService.cs @@ -312...`, `@@ -603...`, `@@ -802...`; `code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:312-320,608-616,812-822`。
- `bom/32-mbom.yaml` / `M-DB-007.artifact/interface_contract` / 種別: 契約更新+観測追記 / 根拠: `eco/ECO-140.md:L181-L187,L200-L207`; `eco/ECO-140.diff` の `IImageRepository`/`ITagRepository`/`DatabaseSchema`/`ImageRepository`/`TagRepository` hunks; `code/src/ViewPrism2.Core/Repositories/IImageRepository.cs:40-127`; `code/src/ViewPrism2.Infrastructure/Database/ImageRepository.cs:292-342,430-638`; `code/src/ViewPrism2.Infrastructure/Database/DatabaseSchema.cs:324-330`; `code/src/ViewPrism2.Infrastructure/Database/TagRepository.cs:288-305`。
- `bom/32-mbom.yaml` / `M-RELINK-025` 見出し注記・`artifact/interface_contract/acceptance_refs` / 種別: 陳腐化訂正+契約更新+観測追記 / 根拠: `eco/ECO-140.md:L88-L90,L134-L159`; `eco/ECO-140.diff` `RelinkService.cs @@ -14...`, `@@ -35...`, `@@ -113...`, `@@ -259...`; `code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:35-128,214-290,299-390`。
- `bom/32-mbom.yaml` / `M-UI-REPAIR-027.superseded_scope/artifact.path_note/interface_contract` / 種別: 陳腐化訂正+契約更新+観測追記 / 根拠: `eco/ECO-140.md:L78-L90,L173-L174,L238-L240`; `eco/ECO-140.diff` の `RepairWindow*`/`RepairViewModel.cs` 削除 hunk、`IntegrityReviewViewModel.cs @@ -0,0 +1,781 @@`、`RelinkWindow.axaml`/`RelinkViewModel.cs` 変更 hunk、i18n の repair key 削除 hunk; 現行 `code/src/ViewPrism2.App/Views/RelinkWindow.axaml:1`, `code/src/ViewPrism2.App/ViewModels/RelinkViewModel.cs:19,87`, `code/src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs:1`, `code/src/ViewPrism2.App/Assets/i18n/ja.json:505-512`。
- `bom/32-mbom.yaml` / `M-UI-PENDING-054.status/superseded_by/artifact.path_note/interface_contract` / 種別: 陳腐化訂正+契約更新+観測追記 / 根拠: `eco/ECO-140.md:L78-L90,L173-L174`; `eco/ECO-140.diff` の `PendingReviewWindow*`/`PendingReviewViewModel.cs` 削除 hunk と `IntegrityReviewViewModel.cs @@ -0,0 +1,781 @@`; 個別遷移の残存=`code/src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs:13-77`。
- `bom/32-mbom.yaml` / `M-UI-INTEGRITY-055.artifact.path_note/interface_contract.*_observed` / 種別: 欠落追加+契約更新+観測追記 / 根拠: `eco/ECO-140.md:L119-L124,L169-L187`; `eco/ECO-140.diff` の新規 `IntegrityReviewViewModel`/`IntegrityReviewWindow*`/`IntegrityReviewService`/`IntegrityReviewFileHashProvider` hunks; `code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:8-116,145-225,231-378`; `code/src/ViewPrism2.App/Services/IWindowService.cs:57-87`; `code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:277-336,526-719`。
- `bom/33-control-plan.yaml` / `CP-PENDING-AUTO-035.fixture/fixture_note` / 種別: 陳腐化訂正+観測追記 / 根拠: `eco/ECO-140.md:L232-L248`; `eco/ECO-140.diff` の `CpPendingAutoAdjudicationTests.cs @@ -1,672 +0,0 @@`, `GfPendingReviewVisualParityTests.cs @@ -1,352 +0,0 @@`, `GfConfirmDialogVisualParityTests.cs @@ -159,50 +159,4 @@`; 後継=`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1`, `CpRelinkUnifiedBatchTests.cs:1`, `GfIntegrityReviewVisualParityTests.cs:1`。
- `bom/33-control-plan.yaml` / `CP-INTEGRITY-036.fixture/fixture_note` / 種別: 欠落追加+観測追記 / 根拠: `eco/ECO-140.md:L232-L248,L327-L331`; `eco/ECO-140.diff` `CpRelinkUnifiedBatchTests.cs @@ -0,0 +1,298 @@`; `code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:1-298`。
- `bom/33-control-plan.yaml` / `CP-REPAIR-AUTOALL-023.fixture/fixture_note` / 種別: 陳腐化訂正+観測追記 / 根拠: `eco/ECO-140.md:L232-L240`; `eco/ECO-140.diff` `CpUiRepairViewModelTests.cs @@ -1,499 +0,0 @@`; 後継=`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1`, `CpRelinkUnifiedBatchTests.cs:1`。
- `bom/33-control-plan.yaml` / `CP-REPAIR-CARD-021.fixture/fixture_note` / 種別: 陳腐化訂正+観測追記 / 根拠: `eco/ECO-140.md:L232-L240`; `eco/ECO-140.diff` `CpUiRepairViewModelTests.cs @@ -1,499 +0,0 @@`; 後継=`code/tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs:1`, `GfIntegrityReviewVisualParityTests.cs:1`。

## 公開契約と artifact の実装写像

| E 品目 / M unit | 現行 artifact と公開呼び出し | 判定 |
|---|---|---|
| `E-UI-INTEGRITY-050` / `M-UI-INTEGRITY-055` | `IntegrityReviewService.Classify(source,uniqueIds,relinkableIds?,outcomes?)`, `LoadAsync(folder,ct,progress,interim,reuse?)`, `ApplyAutomaticAsync(events)`; `IIntegrityReviewHashProvider.ComputeSha256Async(path,ct)`; `IWindowService.ShowIntegrityReviewAsync(collectionId)`, `ConfirmListAsync(...)`; `IntegrityReviewViewModel.LoadAsync(reuse?)`, `CancelLoading()` と生成 command 群。artifact は `IntegrityReviewWindow*`, `IntegrityReviewViewModel`, `IntegrityReviewService`, hash provider, WindowService, ImageTab 入口, ConfirmDialog, i18n, DI（`observed@79123df`）。 | 追記した |
| `E-RELINK-007` / `M-RELINK-025` | `IRelinkService` の `SelectUniquelyRelinkable`, `GetUniquelyRelinkableAsync`, `GetRelinkSelectionAsync`, `GetAutoRepairablePairsAsync`, `GetCandidatesAsync`, `CommitRelinkAsync`, `ApplyRelinkBatchAsync`, `ApplyIntegrityReviewBatchAsync`。実装は `RelinkService`; transaction 境界は `IImageRepository`/`ImageRepository`（`observed@79123df`）。 | 追記した |
| `E-DB-010` / `M-DB-007` | `GetIntegrityReviewByFolderAsync`, `CountIntegrityReviewEventsAsync`, `GetIntegrityReviewImageTagsByFolderAsync`, `ApplyIntegrityReviewBatchAsync`; `pending_baseline_hash` と index/migration/scan batch clear（`observed@79123df`）。 | 追記した |
| `E-SCAN-005` / `M-SCAN-005` | `ScanFileMetaUpdate.PreservePendingBaselineHash` と `ScanBatchBuffer.AddFileMeta(...preserve...)`。Relink/hash provider は本 unit から除外（`observed@79123df`）。 | 追記した |
| `E-UI-PENDING-049` / `M-UI-PENDING-054` | `PendingReviewWindow*`/VM/入口は削除。`PendingReviewService.AcceptAsync/TreatAsNewAsync/DeleteAsync` と pending バッジのみ後継から継続消費（`observed@79123df`）。 | 追記した（superseded） |
| `E-UI-REPAIR-039` / `M-UI-REPAIR-027` | `RepairWindow*`/VM/入口は削除。`RelinkWindow`/`RelinkViewModel` と画像タブ内 trash は存続（`observed@79123df`）。 | 追記した（部分 superseded） |

`IntegrityReviewViewModel` の private RelayCommand 実体（`Select`, `AutoAdjudicate`, `Accept`, `TreatAsNew`, `Delete`, `RelinkMoved`, `SearchCandidates`, `CommitCandidate`, `ExcludeMissing`, `Defer`, `CloseWindow`）は生成 command が surface contract を担うため `M-UI-INTEGRITY-055.view_model_observed` に束ねた。個々を E-BOM の独立品目にはしない。

## 実装写像の棚卸し

表の `hunks` 内はセミコロン区切りの各 `@@ ... @@` をそれぞれ 1 hunk として分類している。`bomdd/` は入力 diff 上の旧パスであり、本点検先は `bom/`。テスト内部 helper や XAML の個別 style は公開 API ではないため、対応する M unit の artifact または CP fixture へ束ね、独立契約にはしない。

### ECO-139.diff

| file | hunks | E / M または CP | 分類 |
|---|---|---|---|
| `bomdd/30-ebom.yaml` (先行追加) | `@@ -794,6 +794,24 @@` | `E-UI-PENDING-049` | 反映済み（履歴） |
| `bomdd/20-spec.md` | `@@ -1121,7 +1121,7 @@`; `@@ -1133,13 +1133,29 @@`; `@@ -1147,12 +1163,14 @@` | REQ-101/PEND-003 | 反映済み（設計根拠） |
| `bomdd/30-ebom.yaml` (本体) | `@@ -778,22 +778,23 @@` | `E-UI-PENDING-049` | 追記した（最終 relink dependency と superseded） |
| `bomdd/33-control-plan.yaml` | `@@ -128,6 +128,7 @@`; `@@ -777,6 +778,22 @@` | `CP-SCAN-004`, `CP-PENDING-AUTO-035` | 反映済み、後継 fixture 注記を追記 |
| `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -220,11 +220,23 @@` | `E/M-UI-PENDING`→`M-UI-INTEGRITY-055` | 反映済み（後に統合） |
| `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -220,11 +220,23 @@` | 同上 | 反映済み（後に統合） |
| `src/ViewPrism2.App/Services/IWindowService.cs` | `@@ -24,6 +24,14 @@`; `@@ -45,6 +53,20 @@` | `M-UI-INTEGRITY-055` (`ConfirmationListItem`, `ConfirmListAsync`) | 追記した |
| `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -89,6 +89,31 @@` | `M-UI-INTEGRITY-055` (`ConfirmListAsync`) | 追記した |
| `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -12,12 +12,18 @@`; `@@ -35,6 +41,10 @@`; `@@ -43,8 +53,9 @@`; `@@ -83,6 +94,36 @@`; `@@ -140,8 +181,17 @@`; `@@ -160,12 +210,24 @@`; `@@ -291,6 +353,55 @@`; `@@ -306,12 +417,30 @@` | `M-UI-PENDING-054` | 反映済み（ECO-140 で file 削除、superseded 注記を追記） |
| `src/ViewPrism2.App/Views/ConfirmDialog.axaml` | `@@ -1,5 +1,7 @@`; `@@ -9,8 +11,59 @@` | `M-UI-INTEGRITY-055` の CMP-011 artifact | 追記した |
| `src/ViewPrism2.App/Views/ConfirmDialog.axaml.cs` | `@@ -1,5 +1,8 @@`; `@@ -18,7 +21,8 @@`; `@@ -26,6 +30,23 @@` | 同上 | 追記した |
| `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -4,15 +4,50 @@`; `@@ -45,6 +80,11 @@`; `@@ -98,6 +138,34 @@`; `@@ -116,41 +184,42 @@` | `M-UI-PENDING-054` | 反映済み（後に削除） |
| `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -36,12 +36,40 @@` | `M-DB-007`; `GetByIdsAsync`, `AdjudicatePendingBatchAsync` | 意図的に契約未追記（ECO-140 後に production consumer 0。superseded accept 経路の残存 API） |
| `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -5,8 +5,9 @@`; `@@ -18,6 +19,42 @@` | `M-UI-PENDING-054` | 反映済み（`IsHighConfidence`/accept batch は ECO-139 後半で relink 化、ECO-140 で削除） |
| `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -277,6 +277,35 @@`; `@@ -293,6 +322,44 @@` | `M-DB-007` | 意図的に契約未追記（`GetByIdsAsync`/`AdjudicatePendingBatchAsync` は現行 consumer 0） |
| `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -0,0 +1,373 @@` | `CP-PENDING-AUTO-035` | 反映済み（後に削除、後継 fixture 注記を追記） |
| `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -5,6 +5,7 @@`; `@@ -157,4 +158,43 @@` | `CP-PENDING-AUTO-035` | 反映済み（PD-6 専用 hunk は後に削除） |
| `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -15,8 +15,8 @@`; `@@ -241,6 +241,64 @@` | `CP-PENDING-AUTO-035` | 反映済み（後に削除） |
| `bomdd/20-spec.md` (GF-139-01) | `@@ -1144,32 +1144,40 @@` | T4/REQ-017 | 反映済み（最終設計） |
| `bomdd/30-ebom.yaml` (GF-139-01) | `@@ -790,8 +790,8 @@` | `E-UI-PENDING-049`→`E-RELINK-007` | 追記した（dependency） |
| `bomdd/33-control-plan.yaml` (GF-139-01) | `@@ -128,7 +128,7 @@`; `@@ -779,7 +779,7 @@`; `@@ -788,9 +788,10 @@` | `CP-SCAN-004`, `CP-PENDING-AUTO-035` | 反映済み |
| `src/ViewPrism2.App/Assets/i18n/en.json` (relink copy) | `@@ -220,17 +220,18 @@` | `M-UI-PENDING-054` | 反映済み（後に統合） |
| `src/ViewPrism2.App/Assets/i18n/ja.json` (relink copy) | `@@ -220,17 +220,18 @@` | 同上 | 反映済み（後に統合） |
| `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` (relink 化) | `@@ -17,13 +17,15 @@`; `@@ -43,7 +45,8 @@`; `@@ -54,7 +57,7 @@`; `@@ -67,6 +70,7 @@`; `@@ -184,14 +188,21 @@`; `@@ -210,8 +221,10 @@`; `@@ -373,14 +386,14 @@`; `@@ -417,15 +430,34 @@` | `M-UI-PENDING-054`, consumer=`E-RELINK-007` | 反映済み（後に削除） |
| `src/ViewPrism2.Core/Repositories/IImageRepository.cs` (relink batch) | `@@ -70,6 +70,15 @@` | `M-RELINK-025`/`M-DB-007` | 追記した |
| `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` (relink 化) | `@@ -6,7 +6,7 @@`; `@@ -32,27 +32,51 @@` | `M-RELINK-025`（当時は pending service） | 反映済み（ECO-140 で `RelinkService` へ移管） |
| `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` (relink batch) | `@@ -360,6 +360,146 @@`; `@@ -391,6 +531,8 @@` | `M-RELINK-025`/`M-DB-007` | 追記した |
| `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` (relink 改訂) | `@@ -12,7 +12,7 @@`; `@@ -84,78 +84,197 @@`; `@@ -174,17 +293,29 @@`; `@@ -224,6 +355,9 @@`; `@@ -253,7 +387,7 @@`; `@@ -279,12 +413,19 @@`; `@@ -299,6 +440,136 @@`; `@@ -317,7 +588,29 @@`; `@@ -336,6 +629,9 @@`; `@@ -347,6 +643,9 @@` | `CP-PENDING-AUTO-035` | 反映済み（後に削除、`CP-INTEGRITY-036`/`CpRelinkUnifiedBatchTests` へ移管） |
| `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` (relink copy) | `@@ -167,16 +167,19 @@`; `@@ -190,6 +193,11 @@` | `CP-PENDING-AUTO-035` | 反映済み（後に PD-6 hunk 削除） |
| `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` (relink copy) | `@@ -42,6 +42,25 @@`; `@@ -51,7 +70,7 @@`; `@@ -262,7 +281,7 @@` | `CP-PENDING-AUTO-035` | 反映済み（後に file 削除） |
| `bomdd/33-control-plan.yaml` (golden) | `@@ -794,6 +794,7 @@` | `CP-PENDING-AUTO-035` | 反映済み、後継注記を追記 |

### ECO-140.diff

| file | hunks | E / M または CP | 分類 |
|---|---|---|---|
| `bomdd/20-spec.md` | `@@ -1085,7 +1085,7 @@`; `@@ -1121,7 +1121,7 @@`; `@@ -1180,6 +1180,40 @@` | REQ-102/103 | 反映済み（設計根拠） |
| `bomdd/30-ebom.yaml` | `@@ -98,17 +98,38 @@`; `@@ -756,6 +777,7 @@`; `@@ -794,6 +816,7 @@`; `@@ -814,6 +837,25 @@` | `E-RELINK-007`, 旧 2 surface, `E-UI-INTEGRITY-050` | 追記した（graph/current/superseded） |
| `bomdd/33-control-plan.yaml` | `@@ -795,6 +795,26 @@` | `CP-INTEGRITY-036` | 反映済み、fixture を追記 |
| `bomdd/32-mbom.yaml` | `@@ -743,7 +743,21 @@` | `M-UI-INTEGRITY-055` | 追記した（observed contract/artifact） |
| `bomdd/20-spec.md` (案a) | `@@ -1198,10 +1198,15 @@` | REQ-103 baseline | 反映済み（設計根拠） |
| `bomdd/30-ebom.yaml` (案a) | `@@ -850,7 +850,7 @@` | `E-UI-INTEGRITY-050` | 反映済み |
| `bomdd/33-control-plan.yaml` (案a) | `@@ -808,7 +808,8 @@` | `CP-INTEGRITY-036` | 反映済み |
| `bomdd/32-mbom.yaml` (案a) | `@@ -94,7 +94,7 @@`; `@@ -111,6 +111,7 @@`; `@@ -122,9 +123,9 @@`; `@@ -749,13 +750,13 @@` | `M-SCAN-005`, `M-DB-007`, `M-UI-INTEGRITY-055` | 追記した（設計所有と observed を分離） |
| `bomdd/33-control-plan.yaml` (R8) | `@@ -803,7 +803,7 @@`; `@@ -815,7 +815,8 @@`; `@@ -830,13 +831,13 @@` | `CP-INTEGRITY-036`, 旧 repair CP | 追記した（削除 fixture と後継） |
| `src/ViewPrism2.App/App.axaml` | `@@ -69,7 +69,6 @@` | `M-UI-REPAIR-027` | 反映済み（旧 RepairIcon 削除、独立 contract 不要） |
| `src/ViewPrism2.App/App.axaml.cs` | `@@ -259,6 +259,12 @@` | `M-UI-INTEGRITY-055` DI | 追記した |
| `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -120,6 +120,69 @@`; `@@ -218,47 +281,7 @@`; `@@ -269,32 +292,6 @@`; `@@ -496,7 +493,6 @@` | `M-UI-INTEGRITY-055`, 旧 2 surface | 追記した（artifact）、個別 key は意図的未契約 |
| `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -120,6 +120,69 @@`; `@@ -218,47 +281,7 @@`; `@@ -269,32 +292,6 @@`; `@@ -496,7 +493,6 @@` | 同上 | 同上 |
| `src/ViewPrism2.App/Services/IWindowService.cs` | `@@ -82,11 +82,9 @@`; `@@ -148,10 +146,4 @@` | `M-UI-INTEGRITY-055`; 旧 `ShowPendingReview/ShowRepair` 削除 | 追記した |
| `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -27,8 +27,8 @@`; `@@ -45,8 +45,8 @@`; `@@ -62,8 +62,8 @@`; `@@ -153,7 +153,7 @@`; `@@ -166,11 +166,17 @@`; `@@ -452,7 +458,7 @@`; `@@ -501,23 +507,4 @@` | `M-UI-INTEGRITY-055`; `M-UI-REPAIR-027` surviving relink | 追記した |
| `src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs` | `@@ -188,7 +188,7 @@` | `M-UI-REPAIR-027` surviving trash | 追記した（path_note）。コメントだけの hunk は独立契約なし |
| `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -52,8 +52,9 @@`; `@@ -432,8 +433,11 @@`; `@@ -442,6 +446,7 @@`; `@@ -477,6 +482,7 @@`; `@@ -538,7 +544,7 @@`; `@@ -1979,21 +1985,21 @@`; `@@ -2003,18 +2009,6 @@`; `@@ -2919,7 +2913,7 @@`; `@@ -2938,6 +2932,9 @@`; `@@ -2945,6 +2942,7 @@` | `M-UI-INTEGRITY-055` entry/count/reload | 追記した |
| `src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs` | `@@ -0,0 +1,781 @@` | `M-UI-INTEGRITY-055` (`IntegrityReviewItemViewModel`, VM、全 command) | 追記した |
| `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -1,488 +0,0 @@` | `M-UI-PENDING-054` | 追記した（superseded/deleted） |
| `src/ViewPrism2.App/ViewModels/RelinkViewModel.cs` | `@@ -16,13 +16,60 @@`; `@@ -46,7 +93,7 @@`; `@@ -98,7 +145,7 @@`; `@@ -156,7 +203,7 @@` | `M-UI-REPAIR-027` surviving `RelinkWindow` | 追記した（path_note）。locale 再解決は既存 surface 実装詳細として独立契約なし |
| `src/ViewPrism2.App/ViewModels/RepairViewModel.cs` | `@@ -1,437 +0,0 @@` | `M-UI-REPAIR-027` | 追記した（部分 superseded/deleted） |
| `src/ViewPrism2.App/Views/ConfirmDialog.axaml.cs` | `@@ -34,7 +34,8 @@` | `M-UI-INTEGRITY-055` | 追記した（混在確認 copy） |
| `src/ViewPrism2.App/Views/ImageTabView.axaml` | `@@ -953,23 +953,19 @@` | `M-UI-INTEGRITY-055` 入口 | 追記した |
| `src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml` | `@@ -0,0 +1,577 @@` | `M-UI-INTEGRITY-055` | 追記した |
| `src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml.cs` | `@@ -0,0 +1,39 @@` | `M-UI-INTEGRITY-055` | 追記した |
| `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -1,290 +0,0 @@` | `M-UI-PENDING-054` | 追記した（superseded/deleted） |
| `src/ViewPrism2.App/Views/PendingReviewWindow.axaml.cs` | `@@ -1,23 +0,0 @@` | 同上 | 追記した（superseded/deleted） |
| `src/ViewPrism2.App/Views/RelinkWindow.axaml` | `@@ -48,7 +48,7 @@` | `M-UI-REPAIR-027` surviving | 追記した（path_note）。binding 名変更は実装詳細 |
| `src/ViewPrism2.App/Views/RepairWindow.axaml` | `@@ -1,149 +0,0 @@` | `M-UI-REPAIR-027` | 追記した（部分 superseded/deleted） |
| `src/ViewPrism2.App/Views/RepairWindow.axaml.cs` | `@@ -1,12 +0,0 @@` | 同上 | 追記した（部分 superseded/deleted） |
| `src/ViewPrism2.Core/Models/Entities.cs` | `@@ -44,6 +44,12 @@` | `M-DB-007` (`ImageRecord.PendingBaselineHash`) | 追記した |
| `src/ViewPrism2.Core/Models/ScanMutationBatch.cs` | `@@ -5,7 +5,8 @@` | `M-SCAN-005`/`M-DB-007` (`PreservePendingBaselineHash`) | 追記した |
| `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -1,4 +1,5 @@`; `@@ -36,6 +37,43 @@`; `@@ -79,6 +117,15 @@` | `M-DB-007`, mixed batch=`M-RELINK-025` | 追記した（read model/mixed batch）。旧 `GetByIds/AdjudicatePendingBatch` は意図的未追記 |
| `src/ViewPrism2.Core/Repositories/ITagRepository.cs` | `@@ -56,6 +56,13 @@` | `M-DB-007` (`GetIntegrityReviewImageTagsByFolderAsync`) | 追記した |
| `src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs` | `@@ -0,0 +1,380 @@` | `M-UI-INTEGRITY-055` + `IRelinkService` 部分は `M-RELINK-025` | 追記した |
| `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -6,7 +6,7 @@`; `@@ -19,66 +19,6 @@`; `@@ -114,6 +54,7 @@` | `M-UI-PENDING-054` の個別 T13/T14/T15 を後継が消費 | 追記した（relink API 削除を superseded 注記） |
| `src/ViewPrism2.Infrastructure/Database/DatabaseSchema.cs` | `@@ -37,11 +37,13 @@`; `@@ -318,5 +320,13 @@` | `M-DB-007` migration 011/index | 追記した |
| `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -1,6 +1,7 @@`; `@@ -9,16 +10,19 @@`; `@@ -74,8 +78,15 @@`; `@@ -230,7 +241,8 @@`; `@@ -277,6 +289,59 @@`; `@@ -314,7 +379,8 @@`; `@@ -344,7 +410,8 @@`; `@@ -362,28 +429,43 @@`; `@@ -396,6 +478,9 @@`; `@@ -431,6 +516,23 @@`; `@@ -445,54 +547,90 @@`; `@@ -529,7 +667,7 @@`; `@@ -549,6 +687,7 @@`; `@@ -570,6 +709,7 @@` | `M-DB-007`; relink/mixed commit=`M-RELINK-025` | 追記した（全 read/aggregate/SQL/transaction/clear hunk） |
| `src/ViewPrism2.Infrastructure/Database/TagRepository.cs` | `@@ -285,6 +285,26 @@` | `M-DB-007` bulk tag read | 追記した |
| `src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs` | `@@ -0,0 +1,27 @@` | `M-UI-INTEGRITY-055` / `E-HASH-006` consumer | 追記した |
| `src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs` | `@@ -14,10 +14,7 @@`; `@@ -35,6 +32,102 @@`; `@@ -113,15 +206,6 @@`; `@@ -259,4 +343,49 @@` | `M-RELINK-025` | 追記した。`CountAutoRepairableAsync` 削除も契約訂正 |
| `src/ViewPrism2.Infrastructure/Scanning/ScanService.cs` | `@@ -312,7 +312,12 @@`; `@@ -603,7 +608,12 @@`; `@@ -802,8 +812,14 @@` | `M-SCAN-005` baseline 保全 | 追記した（observed）。設計境界の疑義は下節 |
| `tests/ViewPrism2.Tests/CpDisplayParity022Tests.cs` | `@@ -119,7 +119,7 @@` | `CP-DISPLAY-PARITY-022` | 反映済み（surviving RelinkViewModel constructor 追随） |
| `tests/ViewPrism2.Tests/CpI18n010AssetLintTests.cs` | `@@ -172,7 +172,6 @@` | i18n lint | 反映済み（削除 key の台帳追随、独立 BOM 契約なし） |
| `tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs` | `@@ -0,0 +1,1367 @@` | `CP-INTEGRITY-036` | 反映済み、fixture mapping を追記 |
| `tests/ViewPrism2.Tests/CpPackage073Tests.cs` | `@@ -419,7 +419,6 @@` | package CP stub | 反映済み（旧 `ShowRepairAsync` stub 削除、独立契約なし） |
| `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -1,672 +0,0 @@` | `CP-PENDING-AUTO-035` | 追記した（deleted/successor） |
| `tests/ViewPrism2.Tests/CpRecomputeCouplingLintTests.cs` | `@@ -86,8 +86,7 @@` | `CP-INTEGRITY-036`/UI coupling lint | 反映済み（新入口への置換） |
| `tests/ViewPrism2.Tests/CpRegistryLintTests.cs` | `@@ -161,12 +161,11 @@` | registry lint | 反映済み（旧 window 台帳除去・新 window 登録） |
| `tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs` | `@@ -0,0 +1,298 @@` | `CP-INTEGRITY-036`, `M-RELINK-025` | 追記した（fixture） |
| `tests/ViewPrism2.Tests/CpUiG1CrossTabRefreshTests.cs` | `@@ -79,8 +79,8 @@`; `@@ -137,7 +137,7 @@` | `CP-UI-G1` | 反映済み（事象合計件数へ置換） |
| `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -12,10 +12,8 @@`; `@@ -26,7 +24,7 @@`; `@@ -48,7 +46,11 @@`; `@@ -81,7 +83,7 @@`; `@@ -90,11 +92,11 @@`; `@@ -110,11 +112,11 @@` | `CP-UI-G1`, `M-UI-INTEGRITY-055` | 反映済み（入口統合） |
| `tests/ViewPrism2.Tests/CpUiG1PendingGuardTests.cs` | `@@ -69,8 +69,8 @@`; `@@ -116,7 +116,7 @@`; `@@ -129,12 +129,12 @@`; `@@ -152,10 +152,10 @@` | `CP-UI-G1`, `M-UI-INTEGRITY-055` | 反映済み（return bool/reload guard） |
| `tests/ViewPrism2.Tests/CpUiG1TrashPopupTests.cs` | `@@ -216,7 +216,7 @@` | `CP-UI-G1`, surviving trash | 反映済み（stub rename のみ） |
| `tests/ViewPrism2.Tests/CpUiRepairViewModelTests.cs` | `@@ -1,499 +0,0 @@` | 旧 repair CP | 追記した（deleted/successor） |
| `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -159,50 +159,4 @@` | 旧 PD-6 | 追記した（削除を `CP-PENDING-AUTO-035` に記録） |
| `tests/ViewPrism2.Tests/GfEntryE1VisualParityTests.cs` | `@@ -192,8 +192,8 @@` | `CP-UI-G1` | 反映済み（入口表示名） |
| `tests/ViewPrism2.Tests/GfFileOpsVisualParityTests.cs` | `@@ -102,7 +102,7 @@`; `@@ -115,25 +115,25 @@` | `CP-UI-G1` | 反映済み（隣接メニュー行の置換） |
| `tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs` | `@@ -0,0 +1,417 @@` | `CP-INTEGRITY-036` | 反映済み、fixture mapping を追記 |
| `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -1,352 +0,0 @@` | `CP-PENDING-AUTO-035` | 追記した（deleted/successor） |
| `src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs` (GF-140-01) | `@@ -368,9 +368,13 @@` | `M-UI-INTEGRITY-055`, `CP-INTEGRITY-036` | 反映済み（既定選択 fallback） |
| `tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs` (GF-140-01) | `@@ -1080,6 +1080,28 @@` | `CP-INTEGRITY-036` | 反映済み |
| `src/ViewPrism2.App/Assets/i18n/en.json` (GF-140-02) | `@@ -122,6 +122,7 @@` | `M-UI-INTEGRITY-055` | 反映済み（artifact mapping 済み） |
| `src/ViewPrism2.App/Assets/i18n/ja.json` (GF-140-02) | `@@ -122,6 +122,7 @@` | 同上 | 反映済み |
| `src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml` (GF-140-02) | `@@ -268,6 +268,11 @@` | `M-UI-INTEGRITY-055` | 反映済み |
| `src/ViewPrism2.App/Assets/i18n/en.json` (GF-140-03) | `@@ -149,7 +149,7 @@`; `@@ -165,7 +165,7 @@` | `M-UI-INTEGRITY-055` | 反映済み（copy 予算、独立契約なし） |
| `tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs` (GF-140-03) | `@@ -1080,6 +1080,30 @@` | `CP-INTEGRITY-036` | 反映済み |
| `bomdd/33-control-plan.yaml` (GF-140-01〜03) | `@@ -816,7 +816,10 @@` | `CP-INTEGRITY-036` | 反映済み |

## 実装の逸脱の疑い

- `RelinkService.cs:9` の XML コメントは現在も「M-SCAN-005 + M-RELINK-025」と所有を二重記載するが、ECO-140 の設計裁定は relink 選別+確定を `E-RELINK-007` / `M-RELINK-025` へ一本化している（`eco/ECO-140.md:L88-L90,L143-L154`）。BOM は設計どおり `M-SCAN-005` から relink を外し、コードコメントは変更禁止のため未編集。
- `ScanService` が `pending_baseline_hash` 保全フラグを渡す変更は、凍結予測の「ScanJudge/ScanStaging・scan 経路に無接触」から外れる。最終 ECO はこれを BDR の「前提疑義」と明記しており、hash 計算/I/O や判定は scan に移していない（`eco/ECO-140.md:L200-L207,L251-L257`; `code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:312-320,608-616`）。設計そのものをコードに合わせて再解釈せず、M-BOM の通常 invariant と観測欄を分離した。
- `IImageRepository.GetByIdsAsync` / `AdjudicatePendingBatchAsync` と `ImageRepository` の実装は ECO-139 初版 accept 経路の public API として残るが、現行 `code/src` に consumer がない。最終設計の契約として M-BOM に昇格せず、棚卸しで「意図的に未編集」とした（`eco/ECO-139.diff` 該当 hunks; 現行宣言=`IImageRepository.cs:81-109`, `ImageRepository.cs:345-427`）。削除可否は別裁定が必要。

## 未編集・要裁定

- `bom/34-routing.yaml` の `ROUTING-V4REPAIR-001` は過去の v4 製造ルートと dry-run 記録であり、現行面の artifact 台帳ではない。ECO-139/140 に現行 routing 追加の裁定がなく、履歴を書き換える根拠がないため未編集。
- `M-UI-REPAIR-027` は RepairWindow 部分だけが統合され、RelinkWindow と trash 部分は存続するため unit 全体を `status: superseded` にはしなかった。`superseded_scope` で範囲を限定した。
- `CP-DISPLAY-PARITY-022` の A1/A6 は存続する `RelinkViewModel`/missing 表示契約も検査しており、旧 repair fixture と同一視して retired にはできないため未編集。

## 全 hunk exact index

上の分類表を機械照合できるよう、diff の 212 hunk を exact header で再掲する。各行の分類・E/M/CP は直前 2 表の同一 file/phase 行を参照する。

- ECO-139 | `bomdd/30-ebom.yaml` | `@@ -794,6 +794,24 @@ ebom:`
- ECO-139 | `bomdd/20-spec.md` | `@@ -1121,7 +1121,7 @@ OC-16 後方互換のため同一コレクション全体を候補とする。`
- ECO-139 | `bomdd/20-spec.md` | `@@ -1133,13 +1133,29 @@ pending=「ファイルは存在するが ViewPrism 上の扱いが未裁定」`
- ECO-139 | `bomdd/20-spec.md` | `@@ -1147,12 +1163,14 @@ pending=「ファイルは存在するが ViewPrism 上の扱いが未裁定」`
- ECO-139 | `bomdd/30-ebom.yaml` | `@@ -778,22 +778,23 @@ ebom:`
- ECO-139 | `bomdd/33-control-plan.yaml` | `@@ -128,6 +128,7 @@ control_plan:`
- ECO-139 | `bomdd/33-control-plan.yaml` | `@@ -777,6 +778,22 @@ control_plan:`
- ECO-139 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -220,11 +220,23 @@`
- ECO-139 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -220,11 +220,23 @@`
- ECO-139 | `src/ViewPrism2.App/Services/IWindowService.cs` | `@@ -24,6 +24,14 @@ public sealed record NodeSettingsRequest(`
- ECO-139 | `src/ViewPrism2.App/Services/IWindowService.cs` | `@@ -45,6 +53,20 @@ public interface IWindowService`
- ECO-139 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -89,6 +89,31 @@ public sealed class WindowService : IWindowService`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -12,12 +12,18 @@ namespace ViewPrism2.App.ViewModels;`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -35,6 +41,10 @@ public sealed partial class PendingItemVM : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -43,8 +53,9 @@ public sealed partial class PendingItemVM : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -83,6 +94,36 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -140,8 +181,17 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -160,12 +210,24 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -291,6 +353,55 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -306,12 +417,30 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/Views/ConfirmDialog.axaml` | `@@ -1,5 +1,7 @@`
- ECO-139 | `src/ViewPrism2.App/Views/ConfirmDialog.axaml` | `@@ -9,8 +11,59 @@`
- ECO-139 | `src/ViewPrism2.App/Views/ConfirmDialog.axaml.cs` | `@@ -1,5 +1,8 @@`
- ECO-139 | `src/ViewPrism2.App/Views/ConfirmDialog.axaml.cs` | `@@ -18,7 +21,8 @@ public partial class ConfirmDialog : Window`
- ECO-139 | `src/ViewPrism2.App/Views/ConfirmDialog.axaml.cs` | `@@ -26,6 +30,23 @@ public partial class ConfirmDialog : Window`
- ECO-139 | `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -4,15 +4,50 @@`
- ECO-139 | `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -45,6 +80,11 @@`
- ECO-139 | `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -98,6 +138,34 @@`
- ECO-139 | `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -116,41 +184,42 @@`
- ECO-139 | `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -36,12 +36,40 @@ public interface IImageRepository`
- ECO-139 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -5,8 +5,9 @@ using ViewPrism2.Core.Repositories;`
- ECO-139 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -18,6 +19,42 @@ public sealed class PendingReviewService`
- ECO-139 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -277,6 +277,35 @@ public sealed class ImageRepository : IImageRepository`
- ECO-139 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -293,6 +322,44 @@ public sealed class ImageRepository : IImageRepository`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -0,0 +1,373 @@`
- ECO-139 | `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -5,6 +5,7 @@ using Avalonia.LogicalTree;`
- ECO-139 | `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -157,4 +158,43 @@ public sealed class GfConfirmDialogVisualParityTests`
- ECO-139 | `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -15,8 +15,8 @@ using Xunit;`
- ECO-139 | `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -241,6 +241,64 @@ public sealed class GfPendingReviewVisualParityTests : IDisposable`
- ECO-139 | `bomdd/20-spec.md` | `@@ -1144,32 +1144,40 @@ pending=「ファイルは存在するが ViewPrism 上の扱いが未裁定」`
- ECO-139 | `bomdd/30-ebom.yaml` | `@@ -790,8 +790,8 @@ ebom:`
- ECO-139 | `bomdd/33-control-plan.yaml` | `@@ -128,7 +128,7 @@ control_plan:`
- ECO-139 | `bomdd/33-control-plan.yaml` | `@@ -779,7 +779,7 @@ control_plan:`
- ECO-139 | `bomdd/33-control-plan.yaml` | `@@ -788,9 +788,10 @@ control_plan:`
- ECO-139 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -220,17 +220,18 @@`
- ECO-139 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -220,17 +220,18 @@`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -17,13 +17,15 @@ public sealed partial class PendingItemVM : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -43,7 +45,8 @@ public sealed partial class PendingItemVM : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -54,7 +57,7 @@ public sealed partial class PendingItemVM : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -67,6 +70,7 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -184,14 +188,21 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -210,8 +221,10 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -373,14 +386,14 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -417,15 +430,34 @@ public sealed partial class PendingReviewViewModel : ObservableObject`
- ECO-139 | `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -70,6 +70,15 @@ public interface IImageRepository`
- ECO-139 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -6,7 +6,7 @@ namespace ViewPrism2.Core.Services.Repair;`
- ECO-139 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -32,27 +32,51 @@ public sealed class PendingReviewService`
- ECO-139 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -360,6 +360,146 @@ public sealed class ImageRepository : IImageRepository`
- ECO-139 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -391,6 +531,8 @@ public sealed class ImageRepository : IImageRepository`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -12,7 +12,7 @@ namespace ViewPrism2.Tests;`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -84,78 +84,197 @@ public sealed class CpPendingAutoAdjudicationTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -174,17 +293,29 @@ public sealed class CpPendingAutoAdjudicationTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -224,6 +355,9 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -253,7 +387,7 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -279,12 +413,19 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -299,6 +440,136 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -317,7 +588,29 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -336,6 +629,9 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -347,6 +643,9 @@ public sealed class CpPendingAutoAdjudicationViewModelTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -167,16 +167,19 @@ public sealed class GfConfirmDialogVisualParityTests`
- ECO-139 | `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -190,6 +193,11 @@ public sealed class GfConfirmDialogVisualParityTests`
- ECO-139 | `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -42,6 +42,25 @@ public sealed class GfPendingReviewVisualParityTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -51,7 +70,7 @@ public sealed class GfPendingReviewVisualParityTests : IDisposable`
- ECO-139 | `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -262,7 +281,7 @@ public sealed class GfPendingReviewVisualParityTests : IDisposable`
- ECO-139 | `bomdd/33-control-plan.yaml` | `@@ -794,6 +794,7 @@ control_plan:`
- ECO-140 | `bomdd/20-spec.md` | `@@ -1085,7 +1085,7 @@ OC-16 後方互換のため同一コレクション全体を候補とする。`
- ECO-140 | `bomdd/20-spec.md` | `@@ -1121,7 +1121,7 @@ OC-16 後方互換のため同一コレクション全体を候補とする。`
- ECO-140 | `bomdd/20-spec.md` | `@@ -1180,6 +1180,40 @@ pending=「ファイルは存在するが ViewPrism 上の扱いが未裁定」`
- ECO-140 | `bomdd/30-ebom.yaml` | `@@ -98,17 +98,38 @@ ebom:`
- ECO-140 | `bomdd/30-ebom.yaml` | `@@ -756,6 +777,7 @@ ebom:`
- ECO-140 | `bomdd/30-ebom.yaml` | `@@ -794,6 +816,7 @@ ebom:`
- ECO-140 | `bomdd/30-ebom.yaml` | `@@ -814,6 +837,25 @@ ebom:`
- ECO-140 | `bomdd/33-control-plan.yaml` | `@@ -795,6 +795,26 @@ control_plan:`
- ECO-140 | `bomdd/32-mbom.yaml` | `@@ -743,7 +743,21 @@ mbom:`
- ECO-140 | `bomdd/20-spec.md` | `@@ -1198,10 +1198,15 @@ missing/pending の正常化裁定を**状態別 2 画面(§2.11.5 修復 UI・`
- ECO-140 | `bomdd/30-ebom.yaml` | `@@ -850,7 +850,7 @@ ebom:`
- ECO-140 | `bomdd/33-control-plan.yaml` | `@@ -808,7 +808,8 @@ control_plan:`
- ECO-140 | `bomdd/32-mbom.yaml` | `@@ -94,7 +94,7 @@ mbom:`
- ECO-140 | `bomdd/32-mbom.yaml` | `@@ -111,6 +111,7 @@ mbom:`
- ECO-140 | `bomdd/32-mbom.yaml` | `@@ -122,9 +123,9 @@ mbom:`
- ECO-140 | `bomdd/32-mbom.yaml` | `@@ -749,13 +750,13 @@ mbom:`
- ECO-140 | `bomdd/33-control-plan.yaml` | `@@ -803,7 +803,7 @@ control_plan:`
- ECO-140 | `bomdd/33-control-plan.yaml` | `@@ -815,7 +815,8 @@ control_plan:`
- ECO-140 | `bomdd/33-control-plan.yaml` | `@@ -830,13 +831,13 @@ control_plan:`
- ECO-140 | `src/ViewPrism2.App/App.axaml` | `@@ -69,7 +69,6 @@`
- ECO-140 | `src/ViewPrism2.App/App.axaml.cs` | `@@ -259,6 +259,12 @@ public partial class App : Application`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -120,6 +120,69 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -218,47 +281,7 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -269,32 +292,6 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -496,7 +493,6 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -120,6 +120,69 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -218,47 +281,7 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -269,32 +292,6 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -496,7 +493,6 @@`
- ECO-140 | `src/ViewPrism2.App/Services/IWindowService.cs` | `@@ -82,11 +82,9 @@ public interface IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/IWindowService.cs` | `@@ -148,10 +146,4 @@ public interface IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -27,8 +27,8 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -45,8 +45,8 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -62,8 +62,8 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -153,7 +153,7 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -166,11 +166,17 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -452,7 +458,7 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/Services/WindowService.cs` | `@@ -501,23 +507,4 @@ public sealed class WindowService : IWindowService`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs` | `@@ -188,7 +188,7 @@ public sealed partial class ImageTabTrashViewModel : ObservableObject`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -52,8 +52,9 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -432,8 +433,11 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -442,6 +446,7 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -477,6 +482,7 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -538,7 +544,7 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -1979,21 +1985,21 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -2003,18 +2009,6 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -2919,7 +2913,7 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -2938,6 +2932,9 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs` | `@@ -2945,6 +2942,7 @@ public sealed partial class ImageTabViewModel : ObservableObject, IChipStripHost`
- ECO-140 | `src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs` | `@@ -0,0 +1,781 @@`
- ECO-140 | `src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs` | `@@ -1,488 +0,0 @@`
- ECO-140 | `src/ViewPrism2.App/ViewModels/RelinkViewModel.cs` | `@@ -16,13 +16,60 @@ namespace ViewPrism2.App.ViewModels;`
- ECO-140 | `src/ViewPrism2.App/ViewModels/RelinkViewModel.cs` | `@@ -46,7 +93,7 @@ public sealed partial class RelinkViewModel : ObservableObject`
- ECO-140 | `src/ViewPrism2.App/ViewModels/RelinkViewModel.cs` | `@@ -98,7 +145,7 @@ public sealed partial class RelinkViewModel : ObservableObject`
- ECO-140 | `src/ViewPrism2.App/ViewModels/RelinkViewModel.cs` | `@@ -156,7 +203,7 @@ public sealed partial class RelinkViewModel : ObservableObject`
- ECO-140 | `src/ViewPrism2.App/ViewModels/RepairViewModel.cs` | `@@ -1,437 +0,0 @@`
- ECO-140 | `src/ViewPrism2.App/Views/ConfirmDialog.axaml.cs` | `@@ -34,7 +34,8 @@ public partial class ConfirmDialog : Window`
- ECO-140 | `src/ViewPrism2.App/Views/ImageTabView.axaml` | `@@ -953,23 +953,19 @@`
- ECO-140 | `src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml` | `@@ -0,0 +1,577 @@`
- ECO-140 | `src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml.cs` | `@@ -0,0 +1,39 @@`
- ECO-140 | `src/ViewPrism2.App/Views/PendingReviewWindow.axaml` | `@@ -1,290 +0,0 @@`
- ECO-140 | `src/ViewPrism2.App/Views/PendingReviewWindow.axaml.cs` | `@@ -1,23 +0,0 @@`
- ECO-140 | `src/ViewPrism2.App/Views/RelinkWindow.axaml` | `@@ -48,7 +48,7 @@`
- ECO-140 | `src/ViewPrism2.App/Views/RepairWindow.axaml` | `@@ -1,149 +0,0 @@`
- ECO-140 | `src/ViewPrism2.App/Views/RepairWindow.axaml.cs` | `@@ -1,12 +0,0 @@`
- ECO-140 | `src/ViewPrism2.Core/Models/Entities.cs` | `@@ -44,6 +44,12 @@ public sealed record ImageRecord`
- ECO-140 | `src/ViewPrism2.Core/Models/ScanMutationBatch.cs` | `@@ -5,7 +5,8 @@ public sealed record ScanFileMetaUpdate(`
- ECO-140 | `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -1,4 +1,5 @@`
- ECO-140 | `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -36,6 +37,43 @@ public interface IImageRepository`
- ECO-140 | `src/ViewPrism2.Core/Repositories/IImageRepository.cs` | `@@ -79,6 +117,15 @@ public interface IImageRepository`
- ECO-140 | `src/ViewPrism2.Core/Repositories/ITagRepository.cs` | `@@ -56,6 +56,13 @@ public interface ITagRepository`
- ECO-140 | `src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs` | `@@ -0,0 +1,380 @@`
- ECO-140 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -6,7 +6,7 @@ namespace ViewPrism2.Core.Services.Repair;`
- ECO-140 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -19,66 +19,6 @@ public sealed class PendingReviewService`
- ECO-140 | `src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs` | `@@ -114,6 +54,7 @@ public sealed class PendingReviewService`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/DatabaseSchema.cs` | `@@ -37,11 +37,13 @@ public static class DatabaseSchema`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/DatabaseSchema.cs` | `@@ -318,5 +320,13 @@ public static class DatabaseSchema`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -1,6 +1,7 @@`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -9,16 +10,19 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -74,8 +78,15 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -230,7 +241,8 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -277,6 +289,59 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -314,7 +379,8 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -344,7 +410,8 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -362,28 +429,43 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -396,6 +478,9 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -431,6 +516,23 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -445,54 +547,90 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -529,7 +667,7 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -549,6 +687,7 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/ImageRepository.cs` | `@@ -570,6 +709,7 @@ public sealed class ImageRepository : IImageRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Database/TagRepository.cs` | `@@ -285,6 +285,26 @@ public sealed class TagRepository : ITagRepository`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs` | `@@ -0,0 +1,27 @@`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs` | `@@ -14,10 +14,7 @@ namespace ViewPrism2.Infrastructure.Scanning;`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs` | `@@ -35,6 +32,102 @@ public sealed class RelinkService`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs` | `@@ -113,15 +206,6 @@ public sealed class RelinkService`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs` | `@@ -259,4 +343,49 @@ public sealed class RelinkService`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/ScanService.cs` | `@@ -312,7 +312,12 @@ public sealed class ScanService`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/ScanService.cs` | `@@ -603,7 +608,12 @@ public sealed class ScanService`
- ECO-140 | `src/ViewPrism2.Infrastructure/Scanning/ScanService.cs` | `@@ -802,8 +812,14 @@ public sealed class ScanService`
- ECO-140 | `tests/ViewPrism2.Tests/CpDisplayParity022Tests.cs` | `@@ -119,7 +119,7 @@ public sealed class CpDisplayParity022Tests`
- ECO-140 | `tests/ViewPrism2.Tests/CpI18n010AssetLintTests.cs` | `@@ -172,7 +172,6 @@ public sealed class CpI18n010AssetLintTests`
- ECO-140 | `tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs` | `@@ -0,0 +1,1367 @@`
- ECO-140 | `tests/ViewPrism2.Tests/CpPackage073Tests.cs` | `@@ -419,7 +419,6 @@ public sealed class CpPackage073Tests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpPendingAutoAdjudicationTests.cs` | `@@ -1,672 +0,0 @@`
- ECO-140 | `tests/ViewPrism2.Tests/CpRecomputeCouplingLintTests.cs` | `@@ -86,8 +86,7 @@ public sealed class CpRecomputeCouplingLintTests`
- ECO-140 | `tests/ViewPrism2.Tests/CpRegistryLintTests.cs` | `@@ -161,12 +161,11 @@ public sealed class CpRegistryLintTests`
- ECO-140 | `tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs` | `@@ -0,0 +1,298 @@`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1CrossTabRefreshTests.cs` | `@@ -79,8 +79,8 @@ public sealed class CpUiG1CrossTabRefreshTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1CrossTabRefreshTests.cs` | `@@ -137,7 +137,7 @@ public sealed class CpUiG1CrossTabRefreshTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -12,10 +12,8 @@ using Xunit;`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -26,7 +24,7 @@ public sealed class CpUiG1MaintenanceMenuTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -48,7 +46,11 @@ public sealed class CpUiG1MaintenanceMenuTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -81,7 +83,7 @@ public sealed class CpUiG1MaintenanceMenuTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -90,11 +92,11 @@ public sealed class CpUiG1MaintenanceMenuTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1MaintenanceMenuTests.cs` | `@@ -110,11 +112,11 @@ public sealed class CpUiG1MaintenanceMenuTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1PendingGuardTests.cs` | `@@ -69,8 +69,8 @@ public sealed class CpUiG1PendingGuardTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1PendingGuardTests.cs` | `@@ -116,7 +116,7 @@ public sealed class CpUiG1PendingGuardTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1PendingGuardTests.cs` | `@@ -129,12 +129,12 @@ public sealed class CpUiG1PendingGuardTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1PendingGuardTests.cs` | `@@ -152,10 +152,10 @@ public sealed class CpUiG1PendingGuardTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiG1TrashPopupTests.cs` | `@@ -216,7 +216,7 @@ public sealed class CpUiG1TrashPopupTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/CpUiRepairViewModelTests.cs` | `@@ -1,499 +0,0 @@`
- ECO-140 | `tests/ViewPrism2.Tests/GfConfirmDialogVisualParityTests.cs` | `@@ -159,50 +159,4 @@ public sealed class GfConfirmDialogVisualParityTests`
- ECO-140 | `tests/ViewPrism2.Tests/GfEntryE1VisualParityTests.cs` | `@@ -192,8 +192,8 @@ public sealed class GfEntryE1VisualParityTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/GfFileOpsVisualParityTests.cs` | `@@ -102,7 +102,7 @@ public sealed class GfFileOpsVisualParityTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/GfFileOpsVisualParityTests.cs` | `@@ -115,25 +115,25 @@ public sealed class GfFileOpsVisualParityTests : IDisposable`
- ECO-140 | `tests/ViewPrism2.Tests/GfIntegrityReviewVisualParityTests.cs` | `@@ -0,0 +1,417 @@`
- ECO-140 | `tests/ViewPrism2.Tests/GfPendingReviewVisualParityTests.cs` | `@@ -1,352 +0,0 @@`
- ECO-140 | `src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs` | `@@ -368,9 +368,13 @@ public sealed partial class IntegrityReviewViewModel : ObservableObject`
- ECO-140 | `tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs` | `@@ -1080,6 +1080,28 @@ public sealed class CpIntegrityReviewTests : IDisposable`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -122,6 +122,7 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/ja.json` | `@@ -122,6 +122,7 @@`
- ECO-140 | `src/ViewPrism2.App/Views/IntegrityReviewWindow.axaml` | `@@ -268,6 +268,11 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -149,7 +149,7 @@`
- ECO-140 | `src/ViewPrism2.App/Assets/i18n/en.json` | `@@ -165,7 +165,7 @@`
- ECO-140 | `tests/ViewPrism2.Tests/CpIntegrityReviewTests.cs` | `@@ -1080,6 +1080,30 @@ public sealed class CpIntegrityReviewTests : IDisposable`
- ECO-140 | `bomdd/33-control-plan.yaml` | `@@ -816,7 +816,10 @@ control_plan:`
