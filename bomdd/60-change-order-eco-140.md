# ECO-140 — 修復と未裁定裁定の役割重複 — 事象中心の統合再設計(PEND-005 reappeared 自動裁定を束ねる)

- 種別: 機能改善(UI/UX 設計再編)+機能拡張(PEND-005)。CAD 先行(画面正典の分担再定義=gate① 必須)
- status: implemented(2026-07-24 /eco-fix 完了・機械受入全緑・実機 golden 待ち)
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

## §4 是正方針(**gate① 裁定確定 2026-07-24=案A・hash 厳格〔裁定時再計算〕・E-RELINK-007 一本化**)

- **案A(事象中心の統合)= 採用**: 状態別 2 画面(missing 起点=修復/pending 起点=未裁定)を、
  **事象別の単一裁定面**へ再設計する:
  - **移動**(missing↔pending ハッシュ一致ペア)→ relink が既定の裁定(自動一括+個別)。**PEND-005 は
    reappeared をこの事象タイプへ昇格させる配線**として本 ECO 内で実装。**高信頼基準=hash 厳格**
    (旧 hash との一致のみを同一とみなす。size+mtime のみは不採用=ECO-139 案B 不採用と同根)。
    **hash 再計算タイミング=裁定時(on-demand)**: スキャン経路(ScanJudge/ScanStaging)へ hash 計算を
    持ち込まず、裁定面が対象 reappeared のみ再計算して旧 hash と比較する(スキャン性能封止
    ECO-134/137/138 への波及回避。詳細配線は /eco-fix で確定)。
  - **新規出現**(pending 単独: new 無候補・changed・restored)→ 受け入れ/削除/保留の裁定(現・未裁定の出口)。
  - **行方不明**(missing 単独)→ 条件検索による候補探索+手動 relink/除外(→deleted)(現・修復の出口)。
  - relink 確定は **E-RELINK-007 へ一本化**(裁定済み=境界仮説 review_trigger 発火の帰結として
    relink 選別+確定を独立部品へ切り出す)。ハッシュ一致判定も単一実装へ収束。
  - 修復ダイアログの CAD 正典化負債は統合後の姿で解消(旧 2 画面の正典を新面で置換)。
- **案B(段階: サービス層一本化+入口温存)= 不採用**: 2 入口の混乱の根本が残り、PEND-005 追加で
  重複が再拡大する懸念のため。
- **案C(パイプライン純化への復帰)= 不採用**: GF-139-01 の裁定(受け入れだと一致 missing が残る→relink 化)
  に逆行するため。
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

- **gate①(裁定)= 裁定済み(2026-07-24 maintainer)**: ①統合粒度=**案A(事象中心統合)採択**
  ②PEND-005 高信頼基準=**hash 厳格・裁定時再計算**(スキャン経路へ持ち込まない)
  ③部品境界=**relink 選別+確定を E-RELINK-007 へ一本化**(bdr-01 review_trigger の裁定)。
- **CAD 是正= 完了・CAD/mock golden 承認済み(2026-07-24 maintainer)**: 新画面正典
  `integrity_review.md`「要確認の画像」(IR-1〜8・ViewPrismUI `3a1bb37`・承認記録+Provisional 解除
  `3b4fb54`)。設計= 事象 3 グループ(自動裁定できる/個別に確認/見つからない)・自動裁定=移動 relink+
  再出現(裁定時 hash 確認)受け入れの混在明示(IR-6)・旧修復吸収(IR-5 条件検索 4 項目圧縮)・
  入口統合「要確認の画像…」。未確定の申し送り= INT-001〜005(ViewPrismUI review_points)。
  製品コードはこの CAD が正(/eco-fix)。
- **bdr-01(2 系列目)**: /eco-fix 前に新 boundary_hypothesis(E-RELINK-007 一本化境界の予測)を
  BomDD protocol に従い凍結し、30-ebom へ転記する(1 系列目=ECO-139 §7 の型)。
- **gate②(golden)= 複数インスタンス**: CAD/mock golden と実機 golden は別インスタンス
  (change-management 2026-07-24 改訂)。実機 golden は GF-139-01 教訓に従い**データ後始末
  (残存 missing・孤立タグ)まで**確認範囲に含める。
- **R3 分離**: PEND-004(各消費サイトの pending 扱い)・F-2(pending 一覧の非仮想化)は本 ECO に混ぜない。

## §7 BDR(bdr-01 予測凍結・2 系列目 — 2026-07-24・/eco-fix 前)

BomDD 実験 bdr-01(EXP-20260723-01)の **2 系列目**。ECO-139 §7 boundary_hypothesis の review_trigger
(PEND-005 到来)が正式発火し、gate① 裁定③で **relink 選別+確定の E-RELINK-007 一本化**(分割=切り出し)を
裁定した。本 BDR はその**一本化後の境界がどう持つか**の予測であり、**/eco-fix の影響分析・実装より前に
固定**する(protocol §事前凍結=予測として採点。実績を見た後の書き換えは行わない)。
対象条件= ②(一本化 vs 2 サービス並存の双方に合理性があった)+④(見直し可能性の認識= review_trigger 参照)。
記録先規約= 案a(owner 品目 1 件へ記録)— owner= **E-RELINK-007**(30-ebom への転記は /eco-fix の E-BOM
設計時・予測は本凍結時点で固定)。

```yaml
boundary_hypothesis:   # 対象条件②/④ — relink 一本化(E-RELINK-007)後の部品境界(owner=E-RELINK-007)
  premise: >
    relink の「選別(ハッシュ一致・一意性=曖昧除外)」と「確定(原子バッチ・image_id/タグ保持・
    missing 解消)」は E-RELINK-007 に一本化して宿り、統合裁定 UI(integrity_review クラスタ=
    新 VM+裁定サービス)はその消費者に徹する。再出現の裁定時 hash 再計算も裁定側 on-demand の
    入力供給であり、ScanJudge/ScanStaging(scan 経路)は candidate_link_id の在庫化のみを担う
    現行契約から変わらない。同一性判定の基準は hash 一致のみで、閾値・類似度等の合成判定は持たない。
  expected_change: >
    ECO-140 の実装・是正は integrity_review クラスタ(新 VM/裁定サービス+WindowService/i18n)と
    E-RELINK-007(選別+確定 API の統合)+旧 2 系統(RelinkService 逐次経路/PendingReviewService
    relink 部分)の E-RELINK-007 への吸収で完結し、ScanJudge/ScanStaging の判定ロジックへ触れない
    想定(hash 再計算はスキャン経路に持ち込まない= gate① 裁定②)。凍結オラクルにも無接触。
  review_trigger: >
    ①relink の第 3 の消費者が現れ、選別基準が消費者ごとに分岐し始めた時(基準の部品化を再裁定)。
    ②裁定時 on-demand hash 再計算が性能要件(大量 reappeared)でスキャン時計算へ移る要求が出た時
    (scan との境界を再裁定)。③同一性判定が hash 一致以外(類似度・部分一致等)へ拡張された時
    (「同一性判定」の独立ドメイン部品化= ECO-139 §7 で見送った分割の再考)。
```

**/eco-fix 時の 4 値照合予告**: 実 diff が integrity_review クラスタ+E-RELINK-007(+旧 2 系統の吸収)で
完結し ScanJudge/ScanStaging の判定ロジックに触れなければ「想定範囲内」。scan 側へ hash 計算や選別が
漏れれば「前提疑義」。UI/サービス以外の変化軸(DB スキーマ大改変等)が要れば「強度超過」。
- 関連: ECO-139/GF-139-01(重複の混入元・relink 再裁定)・ECO-129/REQ-101(pending 意味論)・
  ECO-003/005(修復ダイアログ補完)・PEND-001〜005(CAD 裁定履歴)・CMP-011(確認ダイアログ)・
  INV-001(image_id 不変)・ECO-134/137/138(スキャン性能封止 read-across)。

## §8 /eco-fix 実装記録(2026-07-24・未コミット handoff)

### 8.1 red-first と製造結果

- 旧 2 Window/入口を統合 `IntegrityReviewWindow` へ置換し、pending∪missing の事象分類、
  E-RELINK-007 選別+単発/原子バッチ確定、reappeared 裁定時 SHA-256、CMP-011 混在確認を実装した。
- maintainer 案a 裁定後、CP-INTEGRITY-036 へ migration 011、実スキャン
  `missing→UpdateMetaAndPend→pending`、PendInPlace、baseline 比較、T13/T14/T15/relink clear を
  red-first 追加。初回 red は **949/953 green・4 fail**(列/保全/比較未実装)で、実装後 green。
- R8 残所見は保持タグ件数、ja/en 即時追随、hash provider token 観測を red-first
  (**954/957 green・3 fail**)で固定して green 化。IR-7 は途中加算せず完了時一括の裁定を維持し、
  確認中は自動 CTA を隠す一方、既に分類済みの自動行は一覧に保持する。
- 独立 R8 で追加検出した条件ゼロ検索、候補検索後着、タグ N+1、件数相関 query、
  個別裁定ごとの O(N²) 再hash、曖昧 relink 再分類、pending 候補消費後の stale 行を専用 probe で
  red 化。最終は DB 母集合再読+E-RELINK-007 再選別を正とし、基準/記録hash/path/size/mtime が
  不変な成功済み hash outcome だけを再利用する方式で **967/967 green**。
- migration 011 は `pending_baseline_hash TEXT NULL`(既存行/PendInPlace=NULL)と、
  CMP-010 相関 lookup 用 `idx_images_candidate_link(sync_folder_id,status,candidate_link_id)`。
  `EXPLAIN QUERY PLAN` で inner lookup が同 index を使用することを R8 で実測した。

### 8.2 R7/R8

- R7 は CAD `integrity_review.md` IR-1〜8/captures と headless 実装 capture を全状態並置。
  IR-5 候補選択のテーマ青塗りを透明化、IR-7 spinner/自動群表示、IR-8 幅480、
  候補0件の淡色 i18n 表示を是正した。最終 capture は
  `impl-integrity-IR-1.png`〜`IR-8.png` で再確認(一時生成物は `.artifacts/` として git 対象外)。
- R8 独立レビューで in-scope High は全て probe 化して是正。Microsoft.Data.Sqlite の
  in-flight query interrupt は共有 DB 基盤の別設計事項であり、本 ECO の明示対象
  `HashDataAsync(stream, ct)` は中断済み。分離候補を `51-cheat-log.md` へ記帳した。
- 凍結 Oracle、`ScanJudge`/`ScanStaging` 判定、`ConfirmDialog` 基底は無変更。

### 8.3 bdr-01 4 値照合

- **判定=前提疑義**。一本化された E-RELINK-007 と integrity_review クラスタ、旧 2 系統吸収、
  ScanJudge/ScanStaging 判定不変、Oracle 無接触は予測どおり。一方、承認初版の「記録 hash」は
  `UpdateMetaAndPend` 適用で既に上書きされるため裁定基準にならず、migration 011 と
  ScanService apply の旧 hash 保全書込みが必要だった。scan 境界を「判定」だけで捉え、
  判定結果の適用が裁定基準を破壊する可能性を premise が落としていたため、強度超過でなく
  前提疑義と記録する。hash 計算/追加 I/O は scan へ持ち込んでいない。

### 8.4 lifecycle

- ユーザー指示により本 handoff では **commit しない**。したがって register は `staged` のまま、
  E14〜E19 が要求する `BomDD-ECO-Fix:` trailer 付き遷移コミットも未作成。レビュー後の commit 時に
  `/eco-fix` の implemented 遷移を行い、その後は実機 gate② golden → `/eco-accept` の順とする。

### 8.5 機械受入

- `dotnet build --no-restore`: **0 warning / 0 error**。通常 restore は sandbox の NuGet network 禁止で
  NU1301 となるため、既存ローカル package cache を明示して offline restore 後に実行。
- `dotnet test tests/ViewPrism2.Tests --no-restore`: **967 passed / 0 failed / 0 skipped**。
- `dotnet test tests/ViewPrism2.Oracle --no-restore`: **109 passed / 0 failed / 4 skipped**
  (計113、凍結 Oracle diff 0)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。

## §8.6 設計者 R8(独立レビュー・fresh context)+是正記録(2026-07-24)

工場の自己 R8(§8.2)とは別系統の独立受入検査(Claude fresh context subagent・53 tool 呼び出し)を実施。
機械受入 4 点は検査者が独立再実行し工場申告と一致。**総合判定= 条件付き出荷可**(コア意味論=混在原子
バッチ・baseline 保全・hash 厳格・E-RELINK-007 一本化・入口統合・scan 判定無接触は裁定どおり・ブロッカーなし)。

### 所見と処置(全列挙)

- **Med(a) 旧修復系 CP エントリの as-built 乖離** → **是正済み**: CP-REPAIR-AUTOALL-023/
  CP-REPAIR-CARD-021/CP-UI-G10 へ superseded 注記(検査実体の移管先= CP-INTEGRITY-036 を明記・
  CP-PENDING-AUTO-035 と対称化)。
- **Med(b) ECO-075/GF-075-01 大量 missing 応答性プローブの消失(黙って消えたベクタ)** → **是正済み**:
  CpIntegrityReviewTests へ移植 2 本(2000 移動ペア/1999 missing 単独+一意 1 組の LoadAsync 単一ロード
  相当= O(M×N) 封止。歴代承認済みの粗い上限を維持)。CP-INTEGRITY-036 へベクタ追記。
- **Low dead code 3 点** → **是正済み**: RelinkService.CountAutoRepairableAsync 削除(消費者ゼロ)・
  App.axaml RepairIcon 削除・ImageTabTrashViewModel の stale コメント(OpenRepair→OpenIntegrityReview)。
  GetAutoRepairablePairsAsync は温存(spec 逐次意味論+テスト消費・是正中の巻き込み削除は HEAD 版で復元済み)。
- **Low default 実装乖離**(IImageRepository.CountIntegrityReviewEventsAsync 既定実装がタグ付き pending の
  遮蔽差を持つ)→ **明示コメント化**(件数パリティ検査は production 実装で行う旨= stub 素通し防止)。
- **Low `_verifiedHashes` の理論的競合**(クローズ済み旧 Load の最終 write と再開扉 Clear の交錯)→
  **軽微残置**: 誤った hash 再利用は適用時の repository 再検証(baseline/hash/path/size/mtime 不変チェック+
  stale 全 rollback)が下流で遮断するため正しさへの影響なし。改善(VM 保有化/lock)は任意。
- **IR-7 動的挙動(確認完了時の一括反映=途中加算なし・確認中の自動グループ見出し表示)**= CAD 両義の
  沈黙次元 → **実機 golden の明示確認項目へ昇格**(CP-INTEGRITY-036 golden 観点に刻印)。
- 範囲外 diff(ついで修正)= 検出 0。意味論ベクタ移行= 上記 Med(b) 以外は全て移行済みを突合確認。
  工場自己申告の乖離= 実質なし(red-first 中間 fail 数のみ tree から検証不能=申告のみ)。

### bdr-01 4 値照合の設計者確認

工場記録(§8.3)の**「前提疑義」判定を設計者として追認**する。判定理由の要点: 一本化・クラスタ完結・
ScanJudge/ScanStaging 判定無接触・凍結オラクル無接触は予測どおりだが、premise が scan 境界を「判定」でのみ
捉え、**判定結果の適用(メタ上書き)が裁定基準を破壊する経路**を落としていた= migration 011+scan apply の
保全書き込みが必要になった(boundary_hypothesis は凍結どおり無変更・書き換えなし)。2 系列目にして
review_trigger 弁別力の反証側データ点を獲得(1 系列目は 3/3 想定範囲内で反証側未検証だった)。
§42 測定 3 点は ECO クローズ後に BomDD へ記帳。

### 機械受入(最終・設計者実測)

build **0 warning/0 error**・Tests **969/969**(工場 967+ECO-075 移植 2)・Oracle **109 pass/4 skip・
凍結 diff 0**(R6)・validate_bom **0/0**。
