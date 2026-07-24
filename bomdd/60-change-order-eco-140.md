# ECO-140 — 修復と未裁定裁定の役割重複 — 事象中心の統合再設計(PEND-005 reappeared 自動裁定を束ねる)

- 種別: 機能改善(UI/UX 設計再編)+機能拡張(PEND-005)。CAD 先行(画面正典の分担再定義=gate① 必須)
- status: staged
- baseline: main `6866b2f`
- 優先度: 中(実害は混乱・二重実装の保守負債。データ破壊なし。ただし PEND-005 着手前に裁定しないと重複が拡大)

## §1 症状 / 要求

maintainer 所見(2026-07-24): 「修復」ダイアログ([RepairWindow](../src/ViewPrism2.App/Views/RepairWindow.axaml))と
「未裁定の画像」ダイアログ([PendingReviewWindow](../src/ViewPrism2.App/Views/PendingReviewWindow.axaml))の
**役割の区別が曖昧**。いずれも missing / pending 状態の画像を正常化する機能であり、細分化よりも統合した方が
使いやすい可能性がある。

観測(実機・同一コレクション): ファイル移動後の再スキャンで生じた**同じ 10 件のハッシュ一致ペア**が、
未裁定ダイアログでは「自動裁定(10 件を再リンク)」、修復ダイアログでは「すべて自動修復(10 件)」として
**両画面に同時に現れる**。ユーザーから見て同一の事象に対し 2 つの入口・2 つの自動ボタンがあり、
どちらを使うべきか判別できない。

要求: 両機能の役割を再裁定し、**事象中心への統合**(または明確な分担の再定義)を行う。
**PEND-005(reappeared〔灰〕の自動裁定=ハッシュ再計算+旧ハッシュ比較の配線)を本 ECO に束ねる**
(maintainer 指示・2026-07-24)。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| **CAD(ViewPrismUI)** | **欠陥=面間分担の崩壊+正典欠落** | ①元設計は 3 段パイプライン「scan_summary(検知)→ pending_review(裁定)→ 修復(relink 実行)」で、[pending_review.md](../../ViewPrismUI/docs/screens/pending_review.md) L68/L204 は「再リンクの実行=既存修復フロー(image_tab §修復)へ誘導」と明示。しかし **GF-139-01(2026-07-23)で自動裁定が relink 実行主体へ再裁定**され、この分担規定と現行 mock(v2 relink 版)が**面内で矛盾したまま併存**。②修復ダイアログは**画面正典外**(専用 mock なし・image_tab §修復への参照のみ・[04_component_registry.md](../../ViewPrismUI/docs/04_component_registry.md) L218 に「修復モードは実装未搭載」の stale 記述)。③ PEND-005 は pending_review.md L235 で次版候補と予告済みだが面未定義 |
| **仕様/BOM** | **部分欠陥=境界の暫定自認+基準未規定** | E-BOM 境界仮説([30-ebom.yaml](30-ebom.yaml) L800-814)自身が「PEND-005 到来時=同一性の確信度が scan と review を横断する第 2 消費者を得るため、独立部品への切り出しを**再裁定する**」と予告=現構造(PendingReviewService 内で relink を受け止める)は**暫定と台帳が自認**。relink 意味論が E-UI-REPAIR-039(REQ-072 系)と E-UI-PENDING-049(REQ-101)の 2 surface に分散。reappeared の高信頼基準(旧 hash 再計算+比較)は spec §2.11.7 未規定 |
| **実装** | **健全(逸脱なし・ただし設計分散の帰結の重複負債)** | 両画面ともそれぞれの承認済み設計に忠実でバグではない。帰結として: relink 確定が 2 系統別実装(§3)・ハッシュ一致判定が 3 か所別実装・競合時挙動が非対称。是正対象は上流(CAD/BOM)確定後の再編 |

**結論**: 実装バグではなく **CAD 先行の設計再編**。GF-139-01 で生じた面間矛盾の解消+修復の正典化+
PEND-005 の面定義を一体で裁定する(gate① 必須)。製品コードは CAD 反映後(BomDD: CAD が正)。

## §3 切り分け済みの事実

### 確定(コード実測・2026-07-24)

- **母集合は排他**: 修復= `ImageStatus.Missing`([RepairViewModel.cs:148](../src/ViewPrism2.App/ViewModels/RepairViewModel.cs))、
  未裁定= `ImageStatus.Pending`([PendingReviewViewModel.cs:192](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs))。
  status は単一 enum([Enums.cs:4](../src/ViewPrism2.Core/Models/Enums.cs))のため**行の重複はない**。
- **しかし「移動」という 1 事象は missing 行+pending 行のペア**として現れ、両画面の自動機能が
  **同じペアを取り合う**: [ScanJudge.cs:106-109](../src/ViewPrism2.Core/Services/ScanJudge.cs) が同ハッシュ missing の id を
  pending の `CandidateLinkId` に格納 → 修復「すべて自動修復」はその **missing 行**を母集合に pending を候補として消費
  ([RelinkService.cs:130](../src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs) `GetAutoRepairablePairsAsync`)、
  未裁定「自動裁定」はその **pending 行**を対象に同じ missing 行へ relink
  ([PendingReviewService.cs:63](../src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs) `RelinkHighConfidenceAsync`)。
- **relink 確定が 2 系統別実装**(共通サービス呼び出しなし):
  - 修復: [RepairViewModel.cs:221](../src/ViewPrism2.App/ViewModels/RepairViewModel.cs) `AutoRepairAllAsync` →
    `CommitRelinkAsync`([RelinkService.cs:215](../src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs))=
    **1 件ずつ別トランザクション・失敗はスキップ続行・確認ダイアログなし**。
  - 未裁定: [PendingReviewViewModel.cs:370](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs) `AutoAdjudicateAsync` →
    `ApplyRelinkBatchAsync`([ImageRepository.cs:363](../src/ViewPrism2.Infrastructure/Persistence/ImageRepository.cs))=
    **単一トランザクション・1 件でも stale なら全件失敗・CMP-011 確認あり**。
  - 再リンクの向き(missing 行を残し pending を消費・image_id/タグ保持=INV-001)は両者一致。
- **ハッシュ一致判定が 3 か所別実装**: ①[RelinkService.cs:130-206](../src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs)
  (auto-repair ペア選抜)②[RepairViewModel.cs:358](../src/ViewPrism2.App/ViewModels/RepairViewModel.cs)(手動プレフィル)
  ③[ScanJudge.cs:106-118](../src/ViewPrism2.Core/Services/ScanJudge.cs)(CandidateLinkId 付与)+
  [PendingReviewService.cs:50-55](../src/ViewPrism2.Core/Services/Repair/PendingReviewService.cs)(裁定時再検証)。
- **競合時挙動の非対称**: 片方が先に消費した場合、修復側は per-item 検証で該当項目のみ失敗スキップ
  ([RelinkService.cs:229](../src/ViewPrism2.Infrastructure/Scanning/RelinkService.cs))、未裁定側は stale 検出で
  **全件失敗表示**([PendingReviewViewModel.cs:397-400](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs))。
- **クロス導線は既設**: 未裁定ダイアログ内「修復で再リンク…」([PendingReviewViewModel.cs:357](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs))
  が同じ `ShowRepairAsync` を呼ぶ=旧パイプライン分担(裁定→修復委譲)の残置導線。
- **PEND-005 の前提**(ECO-139 §3 確定済み): reappeared(灰)は `PendInPlace` で **hash 未計算・未比較**
  (シグナル在庫なし)。自動裁定対象化には ScanJudge/ScanStaging への旧 hash 再計算+比較の配線が必要。

### 未検証(疑い — gate① 裁定/その後の /eco-fix・/cad-mock で確定)

- **統合 UI の情報設計**(事象タイプ別の提示・件数規模 1 万件級での一覧性・修復の条件検索の置き場)は未設計=
  gate① 裁定後に /cad-mock で確定。
- **relink 確定プリミティブの統一先**(原子バッチへ寄せるか・逐次を残すか)による UX/回復性の差は未実測。
- reappeared の hash 再計算コスト(再スキャン時 or 裁定時 on-demand)の性能特性は未実測(26 万件級 read-across=
  ECO-134/137/138 の性能封止観点)。

## §4 是正方針(案 — gate① で裁定)

- **案A(事象中心の統合・推奨)**: 状態別 2 画面(missing 起点=修復/pending 起点=未裁定)を、
  **事象別の単一裁定面**へ再設計する:
  - **移動**(missing↔pending ハッシュ一致ペア)→ relink が既定の裁定(自動一括+個別)。**PEND-005 は
    reappeared をこの事象タイプへ昇格させる配線**(旧 hash 再計算+比較)として本 ECO 内で実装。
  - **新規出現**(pending 単独: new 無候補・changed・restored)→ 受け入れ/削除/保留の裁定(現・未裁定の出口)。
  - **行方不明**(missing 単独)→ 条件検索による候補探索+手動 relink/除外(→deleted)(現・修復の出口)。
  - relink 確定は **E-RELINK-007 へ一本化**(境界仮説 review_trigger の発火=独立部品への切り出し再裁定)。
    ハッシュ一致判定も単一実装へ収束。
  - 修復ダイアログの CAD 正典化負債は統合後の姿で解消(旧 2 画面の正典を新面で置換)。
- **案B(段階: サービス層一本化+入口温存)**: UI 2 枚を温存し、relink 確定プリミティブ+ハッシュ判定のみ
  単一サービスへ一本化。自動 relink 入口はどちらか 1 面に集約(他面はバッジ+誘導)。diff 小だが
  **2 入口の混乱の根本は残り**、PEND-005 を未裁定側へ追加すると重複が再拡大する懸念。
- **案C(パイプライン純化への復帰)= 不採用推奨**: 未裁定から自動裁定(relink)を外し修復へ戻す。
  GF-139-01 の裁定(受け入れだと一致 missing が残る→relink 化)に逆行するため推奨しない。
- 共通: 既存の個別裁定 4 操作(受け入れ/削除/別画像/保留)・CMP-011 確認・可逆性・曖昧ケース
  (1 missing に複数新規)の手動回避は**どの案でも維持**する。

## §5 影響 BOM(gate① 後に確定)

- **CAD(ViewPrismUI)= 先行必須**: 案A なら新画面正典(事象別裁定面)+mock/captures 新設、
  pending_review.md の分担規定(L68/L204)改訂、image_tab §修復の出口再定義、04_component_registry.md L218
  stale 是正、PEND-005 の面定義(reappeared の信頼度提示)。案B でも pending_review.md/L68 の矛盾解消は必須。
- **仕様**: `bomdd/20-spec.md` §2.11.7 拡張=reappeared 高信頼基準(旧 hash 再計算+比較)+事象分類の意味論
  (移動/新規出現/行方不明)+relink 確定プリミティブの統一意味論(原子性・競合時挙動)。
- **REQ / E-BOM / M-BOM**: E-UI-REPAIR-039(REQ-072 系)と E-UI-PENDING-049(REQ-101)の統合/再編。
  **30-ebom.yaml L800-814 境界仮説の再裁定**(relink 選別+確定の E-RELINK-007 への切り出し)。
- **実装**: 統合 VM/Window(案A)・PendingReviewService/RelinkService の再編・ScanJudge/ScanStaging への
  旧 hash 供給配線(PEND-005)・WindowService 入口・i18n。
- **Control Plan**: 事象分類の網羅性・relink 一本化後の競合時挙動・reappeared 自動裁定の誤裁定防止・
  hash 再計算の性能封止観点。
- **bdr-01(BomDD)**: 本 ECO は ECO-139 boundary_hypothesis の **review_trigger を正式に発火**させる
  (2 系列目測定の起点)。gate① 裁定後・/eco-fix 前に新 boundary_hypothesis を凍結する。

## §6 残ゲート

- **gate①(裁定)= 必須・未**: maintainer 裁定事項は 3 点:
  1. **統合粒度**: 案A(事象中心統合)/案B(サービス一本化+入口温存)/案C(不採用推奨)。
  2. **PEND-005 高信頼基準**: reappeared の同一判定=旧 hash 再計算+比較(hash 厳格=案A 系譜)で確定するか。
     hash 再計算のタイミング(スキャン時 or 裁定時)も方針レベルで裁定。
  3. **部品境界の再裁定**(bdr-01 review_trigger): relink 選別+確定を E-RELINK-007(または新独立部品)へ
     切り出すか、現状の 2 サービス並存を続けるか。
- **CAD 是正が先**: gate① 裁定後に /cad-mock で mock/captures+画面正典改訂 → **CAD/mock golden
  (gate② 第 1 インスタンス)**。製品コードはその後(/eco-fix)。
- **gate②(golden)= 複数インスタンス**: CAD/mock golden と実機 golden は別インスタンス
  (change-management 2026-07-24 改訂)。実機 golden は GF-139-01 教訓に従い**データ後始末
  (残存 missing・孤立タグ)まで**確認範囲に含める。
- **R3 分離**: PEND-004(各消費サイトの pending 扱い)・F-2(pending 一覧の非仮想化)は本 ECO に混ぜない。
- 関連: ECO-139/GF-139-01(重複の混入元・relink 再裁定)・ECO-129/REQ-101(pending 意味論)・
  ECO-003/005(修復ダイアログ補完)・PEND-001〜005(CAD 裁定履歴)・CMP-011(確認ダイアログ)・
  INV-001(image_id 不変)・ECO-134/137/138(スキャン性能封止 read-across)。
