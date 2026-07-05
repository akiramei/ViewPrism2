# Change Order — ECO-047(applied): TAG-009 裁定の取り込み — タグ定義階層 UI は提供しない(現状追認・doc-only)

> ECO-045 golden 準備時のスコープ外所見(51-cheat-log R3: タグ定義階層 tags.parent_id の
> 編集 UI 不在)への maintainer 裁定(2026-07-05・CAD `7ffd423`)の取り込み。ECO-042/043 と同型の
> 「設計確定の取り込み」で、**現状追認=実装変更ゼロ・doc-only 同期のみ**。

## 1. 裁定(maintainer 2026-07-05・CAD `7ffd423`)

- **タグ定義自体の親子階層は、現リリースの製品概念として採用しない**(編集・表示とも UI に出さない)。
- **画像管理上の階層分類はビュー階層(view_tag_hierarchies)で表現する**(唯一の階層 UI モデル)。
- タグ値に対する意味情報(性別・職業・所属・種別など)は、タグ定義階層ではなく
  **「辞書タグの属性」として表現する** — 将来の設計方向の宣言(「辞書タグ」は現リリース未定義・
  製造仕様ではない。具体設計は必要時に別途)。
- **tags.parent_id は既存 Core 制約を維持**(REQ-022: 単一親・循環拒否・親削除で子ルート化・
  TAG-008 4a= 削除ガード対象外)するが **UI には露出しない。将来的にも意味分類の主要モデルとはしない**。
- CAD 記録: review_points TAG-009 新設(決定)・tag_tab.md 明文・裁定資料
  `docs/decisions/TAG-009-tag-definition-hierarchy-ui.md`。

## 2. 適合判定 — 現状追認(実装変更ゼロ・実測 2026-07-05)

- App 層で `ParentId` を扱うのはビュー階層エディタ(HierarchyEditorViewModel=
  view_tag_hierarchies)のみ。**タグ定義の親子(Tag.ParentId)を露出・編集する UI はゼロ**
  (TagEditor に親指定フィールドなし)= 裁定に既適合。
- Core 制約(REQ-022)・既存オラクル(S-11 子ルート化・S-38 子の親は削除可)も裁定どおり不変。
- よって src/tests 変更なし・オラクル/golden 影響なし。62 不要・35-dsbom 不要。

## 3. 同期内容(doc-only)

- 20-spec §2.2: 階層(REQ-022)行の直後に TAG-009 の 1 項を追記(Core 制約維持・UI 非露出・
  意味分類の主要モデルにしない)。
- 30-ebom E-TAGSVC-008 invariants へ since ECO-047(TAG-009 明文)を追記。
- 51-cheat-log: ECO-045 R3 所見(UI 不在)へ解決注記(→ TAG-009 裁定・本 ECO)。
- register: ECO-047 = applied(golden: n/a)。

## 4. 残ゲート

なし(裁定=gate① は受領済み・現状追認につき golden 不要)。TAG-008 系(045/046)+TAG-009 で
ECO-045 発の未決事項は全解消。将来項目(未起票のまま保留): 無効化/非推奨化の別操作(TAG-008)・
辞書タグの属性(TAG-009 の方向づけ)。
