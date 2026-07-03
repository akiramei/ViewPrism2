# ECO-034 — change register のリストキーを ref-v0 セレクタへ整合(`change_orders:` → `changes:`)

- **type**: 台帳是正(doc-only 1 行 — bom_sync_gap)
- **status**: applied(起票と同時適用 2026-07-03)
- **golden**: n/a
- **baseline**: main `30f2ed9`
- **出所**: ECO-028〜033 起票時の検証(bomdd-lint の ledger 出力)で発見。

## 背景 / 課題
60-change-register.yaml の ECO リストのキーが `change_orders:` だったが、ref-v0 の定義サイトセレクタは
`changes[].id`(BomDD/method/schemas/draft/ref-edges.draft.yaml)。このため:
- register の全エントリ(ECO-001〜033)が**定義サイトとして読まれず**、ECO ID は 60-*.md ファイル名の
  candidate fallback でしか索引に入らなかった。
- PLM の台帳ビュー(ledger.json)に **status / affected_refs が一切載らない**(register の存在意義=
  「PLM が本文推定でなく台帳から読める」が機能していなかった)。
- R-051(影響参照の eco ゲート検査)も register 不在扱い= skip。

## 裁定(実物 vs スキーマ)
「実物が正」の draft 規律に照らして検討したが、本件は**スキーマ側に寄せる**:
キー名は台帳1箇所の記法で移行コスト1行、他方スキーマへの別名追加は全消費者(製品実装・fixture・文書)に
分岐を持ち込む。ref-v0.4 の裁定ループ(スキーマ5補正)と違い、慣行としての合理性が「たまたまの命名」以上にない。

## 変更内容(適用済み)
- `change_orders:` → `changes:`(1 行)。既存エントリに affected_refs キーは無いため R-051 の新規発火なし。

## verification(適用時実測)
- bomdd-lint 再走行: error 124 / warn 137 **不変**。info 340→373(register 33 エントリの正定義化による
  可視化=正当)。ledger.json: eco 35 件中 **33 件に status が付与**(ECO-001=applied〜ECO-034)。
