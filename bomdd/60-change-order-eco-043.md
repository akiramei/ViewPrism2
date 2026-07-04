# Change Order — ECO-043(applied): VE-004 裁定の取り込み — ビュー名は必須(現状追認+受入行の機械ガード化)

> ECO-025 の残未確定 VE-004 の maintainer 裁定(2026-07-05)の取り込み。ECO-042(FL-001)と同型の
> 「設計確定の取り込み・現状追認」。src 変更ゼロ・**受入の新規行追加のみ**(R6 準拠)。

## 1. 裁定(maintainer 2026-07-05・CAD `85d1f57`)

- **ビュー名は必須** — 空白のみの名前では保存を許可しない(作成・更新とも検証エラー)。
- CAD 記録: review_points.md VE-004 → 決定・docs/screens/view_edit.md「バリデーション」の
  「空名の保存可否は未確定」を決定に更新(ViewPrismUI `85d1f57`)。

## 2. 適合判定 — 現状追認(src 変更ゼロ)

- Core は既に強制済み: ViewService.CreateAsync(35-37)/UpdateAsync(60-62)が
  `IsNullOrWhiteSpace(name)` → `ValidationError("ビュー名が空白のみです。")`。
- UI(ViewEditDialogViewModel.SaveAsync)は Result 失敗を ErrorMessage 表示(71-104)= 保存されない。
- spec §2.5 も「ビュー作成/編集ダイアログ = 名前(必須)+説明+お気に入り」と**既記載**(L216)。
- 未確定だったのは **CAD 台帳の表記だけ**(仕様・実装は当初から名前必須)— ECO-041 の
  「spec 既記載」と同じ三者不整合の軽症版(こちらは実装も揃っていた)。

## 3. 同期内容

- 受入の新規行(R6): CpView012HierarchySaveTests に「空白のみのビュー名は作成も更新も
  拒否される」(作成/更新の ValidationError+拒否時に元の名前が無傷)を追加 —
  裁定を prose だけでなく機械ガードに固定。
- 30-ebom E-VIEWSVC-009 invariants へ since ECO-043(VE-004 明文)を追記。
- spec: 変更不要(既記載)。62 不要(DB 不変)・35-dsbom 不要・golden 不要(視覚不変・変更ゼロ)。
- register: ECO-043 = applied(golden: n/a)。

## 4. 残ゲート

なし(裁定=gate① 受領済み・現状追認につき golden 不要)。**ECO-025 系の未確定はこれで全解消**
(VE-001/002/003=ECO-025・FL-003=ECO-025 β・FL-002/004=ECO-039・FL-001=ECO-042・VE-004=本 ECO)。
