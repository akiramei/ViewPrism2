# ECO-139 / ECO-140 BOM 反映点検

## 設計変更の突合基準

- ECO-139 は高信頼を `pending + origin=new + candidate_link_id` の hash 厳格一致に限定し、1 missing:1 new の一意組だけを原子一括 relink する裁定へ確定した。pending 行を消費し、missing 側の image_id/タグを保持して missing を解消する。根拠: `eco/ECO-139.md:170-195`, `eco/ECO-139.diff` の `PendingReviewService` / `ImageRepository.ApplyRelinkBatchAsync` hunks。
- ECO-140 は missing 起点の修復面と pending 起点の裁定面を、移動・新規出現・行方不明という事象中心の単一 `IntegrityReview` 面へ統合した。旧 `RepairWindow` / `PendingReviewWindow` と旧 2 入口は撤去し、「要確認の画像…」1 入口へ置換した。根拠: `eco/ECO-140.md:76-96,116-124,169-180`, `eco/ECO-140.diff` の旧 Window/ViewModel deleted hunksと `IntegrityReviewWindow` / `IntegrityReviewViewModel` new-file hunks。
- relink の hash/一意性/タグ安全の選別と単発 T4・原子バッチ確定は E-RELINK-007 へ一本化され、統合裁定面はその消費者になった。根拠: `eco/ECO-140.md:88-90,105-109,132-160`, `eco/ECO-140.diff` の `30-ebom.yaml @@ -98,17 +98,38` および `RelinkService` / `IRelinkService` hunks。
- reappeared は裁定面で on-demand SHA-256 を計算し、`pending_baseline_hash ?? hash` と厳格比較する。ScanJudge/ScanStaging の判定や hash I/O は変えず、scan apply が上書き前 hash を migration 011 の列へ保全する。根拠: `eco/ECO-140.md:80-85,173-187,200-207`, `eco/ECO-140.diff` の `20-spec.md @@ -1198,10 +1198,15`、`32-mbom.yaml @@ -94,7 +94,7` / `@@ -122,9 +123,9` hunks。
- 旧 pending/repair の検査面は CP-INTEGRITY-036 へ移管され、ECO-075 の大量 missing 応答性ベクタも新 fixture へ移植された。根拠: `eco/ECO-140.md:232-248,327-331`, `eco/ECO-140.diff` の旧 test deleted hunks、`CpIntegrityReviewTests.cs` / `CpRelinkUnifiedBatchTests.cs` / `GfIntegrityReviewVisualParityTests.cs` new-file hunks。

## 編集記録

- ファイル: `bom/30-ebom.yaml` / 編集箇所(id): `E-PACKAGE-047.invariants` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,119-123`; `eco/ECO-140.diff` の `30-ebom.yaml @@ -814,6 +837,25`（統合後継 E-UI-INTEGRITY-050）および旧 2 Window deleted hunks。
- ファイル: `bom/30-ebom.yaml` / 編集箇所(id): `E-CRITERIA-037.graph_edges.consumers` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:86-90,105-106`; `eco/ECO-140.diff` の `30-ebom.yaml @@ -814,6 +837,25`（E-UI-INTEGRITY-050.depends_on に E-CRITERIA-037）。
- ファイル: `bom/30-ebom.yaml` / 編集箇所(id): `E-UI-MODE-041.depends_on/invariants` / 種別: 契約更新 / 根拠: `eco/ECO-140.md:78-90,119-123,313-325`; `eco/ECO-140.diff` の `IWindowService` / `WindowService` hunks（旧 2 入口削除、ShowIntegrityReviewAsync 追加）と `ImageTabView.axaml` hunk（統合行）。
- ファイル: `bom/30-ebom.yaml` / 編集箇所(id): `E-DESIGN-028.graph_edges.consumers` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:119-124,191-194`; `eco/ECO-140.diff` の `30-ebom.yaml @@ -814,6 +837,25`（K-DESIGN を根拠とする新 surface）および `IntegrityReviewWindow.axaml` new-file hunk。
- ファイル: `bom/30-ebom.yaml` / 編集箇所(id): `E-UI-REPAIR-039.external_source_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,100-106`; `eco/ECO-140.diff` の `30-ebom.yaml @@ -756,6 +777,7`（修復面 superseded、トラッシュ存続）。
- ファイル: `bom/30-ebom.yaml` / 編集箇所(id): `E-UI-PENDING-049.external_source_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,100-106`; `eco/ECO-140.diff` の `30-ebom.yaml @@ -794,6 +816,7`（PD surface superseded、意味論継承）。
- ファイル: `bom/32-mbom.yaml` / 編集箇所(id): `bomdd.experiment` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:173-187,200-207`; `eco/ECO-140.diff` の `32-mbom.yaml @@ -122,9 +123,9`（migration 011 と read model/scan apply 更新）。
- ファイル: `bom/32-mbom.yaml` / 編集箇所(id): `M-SCAN-005.ebom_refs/artifact/interface_contract` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:88-90,132-160,173-187`; `eco/ECO-140.diff` の `RelinkService` / `IRelinkService` hunks; `code/src/ViewPrism2.Core/Services/ScanJudge.cs:63`; `code/src/ViewPrism2.Infrastructure/Scanning/ScanService.cs:19`; `code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:17`; `code/src/ViewPrism2.Infrastructure/Scanning/IntegrityReviewFileHashProvider.cs:10`。exact artifact の再帰属は `(observed@79123df)` として記録。
- ファイル: `bom/32-mbom.yaml` / 編集箇所(id): `M-RELINK-025.artifact/interface_contract/invariants/acceptance_refs` / 種別: 契約更新 / 根拠: `eco/ECO-140.md:88-90,132-160,173-184,238-240`; `eco/ECO-140.diff` の `IRelinkService` / `RelinkService` hunks; `code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:77-108`; `code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:35-115,214-290,299-375`。現行メソッド所有と `CountAutoRepairableAsync` 不在は `(observed@79123df)` として記録。
- ファイル: `bom/32-mbom.yaml` / 編集箇所(id): `M-UI-REPAIR-027` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,119-123,173-180`; `eco/ECO-140.diff` の `RepairViewModel.cs` / `RepairWindow.axaml(.cs)` deleted hunksと `32-mbom.yaml` の M-UI-INTEGRITY-055 追加 hunk。削除済み path は履歴として保持し、`status: superseded` と統合先を path_note に明記。
- ファイル: `bom/32-mbom.yaml` / 編集箇所(id): `M-UI-PENDING-054` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:78-90,119-123,173-180`; `eco/ECO-140.diff` の `PendingReviewViewModel.cs` / `PendingReviewWindow.axaml(.cs)` deleted hunks、`PendingReviewService.cs` 縮退 hunk、`32-mbom.yaml @@ -743,7 +743,21`。削除済み path は履歴として保持し、`status: superseded` と統合先を path_note に明記。
- ファイル: `bom/32-mbom.yaml` / 編集箇所(id): `M-UI-INTEGRITY-055.artifact.path_note` / 種別: 観測追記 / 根拠: `eco/ECO-140.diff` の IntegrityReview 新規 artifact/DI hunks; `code/src/ViewPrism2.App/Services/WindowService.cs:156-179`; `code/src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs:1991-2002`; `code/src/ViewPrism2.App/ViewModels/IntegrityReviewViewModel.cs:69,277-311,530-703`; `code/src/ViewPrism2.Core/Services/Repair/IntegrityReviewService.cs:116-139,231-370`。実装写像に `(observed@79123df)` を付記。
- ファイル: `bom/33-control-plan.yaml` / 編集箇所(id): `CP-SCAN-004` の ECO-139 高信頼ベクタ / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:88-90,173-184,232-240`; `eco/ECO-140.diff` の `CpPendingAutoAdjudicationTests.cs` deleted hunkと `CpRelinkUnifiedBatchTests.cs` new-file hunk。
- ファイル: `bom/33-control-plan.yaml` / 編集箇所(id): `CP-PENDING-AUTO-035.fixture/fixture_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:232-248,327-331`; `eco/ECO-140.diff` の旧 pending tests deleted hunks、`CpIntegrityReviewTests.cs` / `CpRelinkUnifiedBatchTests.cs` / `GfIntegrityReviewVisualParityTests.cs` new-file hunks。
- ファイル: `bom/33-control-plan.yaml` / 編集箇所(id): `CP-INTEGRITY-036.fixture` / 種別: 欠落追加 / 根拠: `eco/ECO-140.md:173-184,232-248`; `eco/ECO-140.diff` の `CpRelinkUnifiedBatchTests.cs` new-file hunk（Trait=`CP-INTEGRITY-036`）; `code/tests/ViewPrism2.Tests/CpRelinkUnifiedBatchTests.cs:14`。
- ファイル: `bom/33-control-plan.yaml` / 編集箇所(id): `CP-REPAIR-AUTOALL-023.fixture/fixture_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:232-240`; `eco/ECO-140.diff` の `CpUiRepairViewModelTests.cs` deleted hunkと旧修復 CP superseded hunk。
- ファイル: `bom/33-control-plan.yaml` / 編集箇所(id): `CP-REPAIR-CARD-021.fixture/fixture_note` / 種別: 陳腐化訂正 / 根拠: `eco/ECO-140.md:232-240`; `eco/ECO-140.diff` の `CpUiRepairViewModelTests.cs` deleted hunk、旧候補カード CP superseded hunk、`GfIntegrityReviewVisualParityTests.cs` new-file hunk。

## 未編集・要裁定

- `bom/34-routing.yaml` の `ROUTING-V4REPAIR-001` は M-UI-REPAIR-027 を製造した当時の履歴ルートとして明示保持されている（ファイル先頭にも V1/V2/V3/V4 の記録保持とある）。ECO-139/140 の order/diff に後継 routing 新設・既存 route 改訂の裁定がなく、履歴を書き換えると実績を変えるため未編集。後続工程を routing に追加するかは要裁定。
- `bom/32-mbom.yaml` の FMEA-031/FMEA-034 は旧 M-UI-REPAIR-027 を発生 unit とする歴史記録。ECO-140 は CP 移管を裁定したが FMEA の owner 改訂は裁定していないため未編集。後継 FMEA を新設するか、歴史 FMEA に superseded 注記を加えるかは要裁定。
- `code/src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs:9` の XML コメントは現在も `M-SCAN-005 + M-RELINK-025` と二重所有を記す一方、ECO-140 の設計裁定は E-RELINK-007 一本化であり、現行 BOM は M-RELINK-025 に単独帰属させた。実行挙動ではなく実装注釈の **逸脱疑い**。`code/` は読み取り専用なので未編集。
- `bom/30-ebom.yaml` の E-UI-REPAIR-039 はトラッシュ surface と旧 RelinkWindow 契約を部分的に保持するため、品目全体へ `status: superseded` は付けていない。ECO-140 が supersede したのは RepairWindow/修復入口の範囲だけであり、品目全体廃止へ広げる根拠はない。

## 検証

- ユーザー制約に従い、ネットワーク、git、build、test、製品コード実行は使用していない。
- `eco/` の order/diff、`code/` の静的な行確認、`bom/` の参照突合だけで点検した。
