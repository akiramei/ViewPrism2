# Change Order — ECO-005(修復UX完全性: すべて自動修復 + 除外)

> read-across B-2(maintainer 裁定=**V4 スコープ内**)の是正。原典 RepairModal の主要動線 **autoRepairAll / autoRepairSingle / excludeSelectedImages** が ViewPrism2 RepairWindow に欠落していた(V4 修復ライフサイクルが実は未完だった)。
> 帰属: **spec_omission**(REQ-072 が修復 UI の「候補提示・確定」までで、一括自動修復・除外を要件化していなかった。**除外=新しい状態遷移 missing→deleted** が状態機械 T1-T8 に存在しなかった)。
> 是正は spec-first → 隔離工場製造(ECO-004 と同方式)。**除外は core(新遷移 T9 + 新オラクル S-31)**、自動修復は VM オーケストレーション(新遷移なし)。

## 0. 変更前 baseline
- As-Built: Loop V4 / ECO-003/004 適用後。固定オラクル `tag:loop-v4-r1`(S-01〜S-30)は不変。本 ECO は**新規行 S-31 を追加**(既存行は変更しない)。
- データ fixture: N/A(status 遷移のみ・物理非破壊)

## 1. 変更要求
- ECO-ID: **ECO-005**
- 発生契機: read-across B-2 → maintainer 裁定「V4 スコープ内」
- 内容: (a) すべて自動修復 (b) 単一自動修復 (c) 除外(missing→deleted)を RepairWindow に追加
- 種別: **欠陥修正(spec_omission・機能欠落)**。除外は**新状態遷移**を伴う
- 原因が宿った上流: REQ-072/§2.11(修復 UI の動線が不完全)・状態機械(missing→deleted 欠落)

## 2. 設計(原典挙動からのリバース)
原典 RepairModal.tsx(NewUI):
- `autoRepairSingle(id)`: その missing の auto-candidate(useHash+useExtension+useSize で**ちょうど1件**)へ relink(`images:relink`)。
- `autoRepairAll()`: auto-repairable な全 missing を順に relink。失敗はスキップ(try/catch per item)。
- `excludeSelectedImages()`: 選択 missing の status を `deleted` に更新(`images:updateStatus id 'deleted'`)。= **missing→deleted**(トラッシュへ。復元可)。

ViewPrism2 への落とし込み(merged RepairWindow):
- auto-repairable 判定は既存 `RepairViewModel.DeriveAutoCriteria`(hash+拡張子+サイズ)+ `RelinkService.GetCandidatesAsync` が候補**ちょうど1件**。`AutoRepairableCount`(既存)と同基準。
- relink は既存 `RelinkService.CommitRelinkAsync`(T4・タグ安全ガード込み)。タグ付き候補で拒否されたら**その1件をスキップ**(原典の失敗握り潰しと同義)。
- 除外は新 `TrashService.ExcludeAsync`(T9)。

## 3. 状態機械の追加
| # | 遷移 | トリガ | 効果 | 前提/拒否 |
|---|---|---|---|---|
| **T9 △** | missing→deleted | 除外(repair UI) | status=deleted(物理非破壊・トラッシュへ。復元で T6/T7 経路) | **missing 以外は拒否**(ValidationError) |

T5(normal→deleted=マージ)と対称。除外した missing は deleted としてトラッシュに入り、RestoreAsync(T6/T7)で戻せる。

## 4. 実装契約(製造パッケージの中核)

### C-EXCLUDE-001 (core / T9): TrashService.ExcludeAsync
- `Task<Result> ExcludeAsync(string imageId)`(TrashService.cs に追加。PermanentDeleteAsync と同型)
- 検証: image 存在・**status==Missing**(他は ValidationError「除外できるのはリンク切れ(missing)画像のみです」)。
- 効果: `UpdateStatusAsync(imageId, Deleted)` のみ。物理ファイルに触れない(INV-009)。タグ/ID/特徴量不変。
- 層規律: Core 内で status 更新のみ(probe 不要)。

### C-AUTOREPAIR-001 (surface VM orchestration): RepairViewModel
- `Task<int> AutoRepairAllAsync()`: MissingImages を走査し、各 missing の DeriveAutoCriteria 候補が**ちょうど1件**なら `RelinkService.CommitRelinkAsync(missing.Id, candidate.ImageId)`。成功数を数える。タグ付き拒否等の失敗はスキップ(数えない)。完了後 LoadAsync で再読込+結果文言。
- `Task AutoRepairSingleAsync(MissingImageViewModel missing)`: その missing の auto-candidate(ちょうど1件)を relink。1件でなければ何もしない。
- `Task ExcludeAsync(MissingImageViewModel missing)`: `TrashService.ExcludeAsync(missing.Id)` を呼ぶ。成功で再読込+結果文言。RepairViewModel に TrashService を注入(WindowService が `_trashService` を渡す)。
- いずれも awaitable・unit 検査可能。

### C-REPAIR-UI-001 (surface): RepairWindow.axaml
- missing 行: auto-repairable なら **「自動修復」ボタン**(行内)→ AutoRepairSingle。
- フッター: **「すべて自動修復 (M件)」ボタン**(AutoRepairableCount>0 で活性)→ AutoRepairAll。**「除外」ボタン**(SelectedMissing 選択時に活性)→ Exclude(SelectedMissing)。
- 除外は破壊的でないが status 変更なので結果文言を出す(「N 件を除外しました(トラッシュへ移動・復元可)」)。
- i18n 新規キー(ja 正/en 併記): repair.autoRepair / repair.autoRepairAll(count 埋め込み)/ repair.exclude / repair.autoRepair.result / repair.exclude.result。
- 注: 選択は単一(SelectedMissing)で開始。原典のマルチ選択一括除外は golden 改善余地(本 ECO はスコープ外と明記)。

## 5. BOM 改訂(同期)
- 仕様: 20-spec.md §2.11.0 に T9 追加・§2.11.6(自動修復一括/単一・除外)新設
- 要件: **REQ-073(除外=T9)新設**・REQ-072 に動線(自動修復一括/単一・除外)追記
- E-BOM: E-TRASH-038 に T9(ExcludeAsync)・E-UI-REPAIR-039 に自動修復一括/単一・除外 UI invariant
- M-BOM: M-TRASH-026 に exclude 契約・M-UI-REPAIR-027 に UI・FMEA-033
- Control Plan: **CP-TRASH-021**(T9 exclude unit)・**CP-REPAIR-AUTOALL-023**(自動修復一括 VM unit)・CP-UI-G10(視覚)
- 固定オラクル: **S-31(cross-factory)新設** — T9 exclude(missing→deleted・missing 限定・物理非破壊)。既存 S-01〜S-30 不変
- bom_rev: v4.0 → v4.0(eco:ECO-005)

## 6. 部分再製造(隔離工場・spec-first)
- 製造パッケージ: 本 ECO-005(§2-4)+ 改訂 BOM + 既存 src。
- 非開示: 原典 view-prism / tests/ViewPrism2.Oracle / 41-fixed-oracle.yaml(S-31 含む)。
- 工場は §4 実装契約から製造。S-31 は設計者が製造後に tests/ViewPrism2.Oracle へ実装(S-26〜30 と同方式)。

## 7. 受入(緑)
- unit: CP-TRASH-021(ExcludeAsync: missing→deleted・missing 限定・他 status 拒否)+ CP-REPAIR-AUTOALL-023(AutoRepairAll が auto-repairable 集合のみ修復・タグ付き拒否はスキップ)→ **新規 8 Facts 緑**
- 固定オラクル: **S-31(cross-factory・設計者実装)= Oracle 74 PASS+2 skip**(S-01〜S-30 回帰ゼロ)
- 単体: **Tests 395/0**(既存 387+新規 8)・build 0 警告(Debug)
- **castability 合格**: 隔離工場 factory-07 が原典・固定オラクル(S-31 含む)非開示で §4 実装契約から製造。cheat 4(全 非 blocker=UI 設計裁量/i18n キー再利用/タグ安全ガードの観測。BOM 許容内)
- golden(残): すべて自動修復・除外ボタンの動線+結果文言(CP-UI-G10 再ウォークスルー)

## 8. 製造記録
- 工場: factory-07(fresh 隔離・general-purpose)。core=TrashService.ExcludeAsync(T9)/ surface=RepairViewModel(AutoRepairAll/Single・Exclude)+RepairWindow.axaml+i18n+WindowService。
- 設計判断(cheat C 分類・受容): 行ごと auto-repairable ボタンでなくフッター一括+選択行単一方式(マニフェスト許容「実装しやすい方」)/ タグ付き唯一候補は GetCandidates のタグ安全ガードで候補0件→未修復として表面化(スキップ実装は保持)/ i18n count キーは repair.summary 慣習に倣い {count} 再利用。
- first-pass green・収束再製造なし・blocker 0。
