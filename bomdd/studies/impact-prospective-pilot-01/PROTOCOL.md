# impact-prospective-pilot-01 — BOM からの変更予測(ECO-142 予備試験)・手順(実行前に固定)

目的: 「BOM の改善が、BOM だけからの変更予測(M unit の変更予測・調査候補)を改善するか」を測る手順が
運用として回るかを、過去事例 1 件で確かめる。**探索的試験** — A/B 差の有無を成功条件にしない。
主評価は前向き(次の実変更)で行う。

## 事例と baseline

- 事例: ECO-142(コレクション切替の遅延・2026-08-03)。変更要求は maintainer 原報告ベースの R1 のみ
  (order §1〜§3 の診断・実測・箇所名は解答混入のため不使用)。
- baseline: `79123df`(起票直前)。A と B の BOM はともにこの revision のコードを記述する。

## 役割と入力(すべて履歴のない許可ファイル集合・.git なし・パッケージ外の読取は契約で禁止し events.jsonl で監査)

| 役割 | 設備 | 入力 | 出力 |
|---|---|---|---|
| 改善者 | Codex gpt-5.6-sol high・独立セッション(workspace-write) | bom/(A)+ code/(79123df)+ ECO-139/140 の order と diff | bom/(B)+ CHANGELOG(編集ごとの根拠) |
| 裁定者 | Codex gpt-5.6-sol high・独立セッション(read-only) | code/(79123df)+ mbom-artifacts(id/path)+ R1 | 初回裁定(YAML)→ 封印 |
| 予測者 A/B | Codex gpt-5.6-sol medium × 2 反復(read-only) | R1 + bom/(A または B)。コード不可 | prediction YAML |
| 突合 | 製造者(claude) | 封印裁定・予測・55c491d の diff | 評価 receipt・訂正履歴 |

- 改善者には評価事例(ECO-142)の存在を知らせない。パッケージに ECO-141 以降の記述は含めない(監査済み: 0 件)。
- 裁定者は予測を見ない(予測より先に封印)。予測者は裁定もコードも見ない。
- 製造者(claude)は本調査で ECO-142 の diff を既に見ているため、改善者・裁定者・予測者を担当しない。

## 主評価

- M unit の **変更予測**(prediction.change_units ⇔ 裁定 role=change)と **調査候補**(change∪investigate ⇔ 裁定 change∪investigate)
  の under / over。E 品目・CP は副次記録。
- 記録は effort-calibration(harness ECO-058/059)の trial 様式(trial.yaml / execution・evaluation receipt / projection)。
  A・B は別 trial(treatment は同一= Sol medium・入力だけが異なる)。effort 比較は行わない。
- 裁定の訂正は `ADJUDICATION-HISTORY.md` に追記(初回裁定は不変)。未確定は裁定 YAML の `unresolved` に保持。

## 言えないこと(事前宣言)

- N=1 事例・反復 2。A/B の差は事例選定と改善者の判断に依存する。
- 到達モデル・到達 effort は unknown。裁定者・改善者の盲検は構造(入力の分離)でしか担保しない。
- 改善集合(ECO-139/140)は評価事例の直接の隣接領域(近距離転移)。
