---
name: eco-accept
description: golden 合格後の ECO クローズ。クローズ 3 点セット(CP 観点明記=再発防止・register applied 化+golden 承認記録・ECO 本文クローズ節+教訓)→accept コミット→完了報告まで行う。golden 合格の報告を受けてから使う。
---

# /eco-accept — golden 合格後のクローズ 3 点セット

典拠: [bomdd/change-management.md](../../../bomdd/change-management.md) §3.3/§4。
引数: ECO 番号+golden 結果(合格/所見)。

## 前提確認(実行可能チェック — ECO-061 で散文から昇格)

- **fix 証拠の機械確認(必須・先に実行)**: 以下が空でなければ fix コミット済み。
  空なら**早期クローズの試み** — 停止して /eco-fix へ差し戻す(ECO-060 違反様式):
  ```
  git log --format="%h %s" --grep="BomDD-ECO-Fix: ECO-NNN"
  ```
- register の status が `implemented`(golden 待ち)であること。
- **golden 不合格(所見あり)の場合はこのスキルを使わない**: 所見を GF-* として ECO 本文へ
  記録し、/eco-fix(同一欠陥)か /eco-file(別欠陥の分離起票 — R3)へ。

## 手順(クローズ 3 点セット)

1. **CP 観点明記(再発防止)**: `bomdd/33-control-plan.yaml` の該当 golden CP
   (surface の acceptance_refs から特定。作業タブ系= CP-UI-G1)の characteristic へ、
   今回の観点を**潜伏実績つきで**追記する(ECO-037 が CP-UI-G9 に完了パネル観点を刻んだ方式)。
2. **register 更新**: `status: applied`+承認記録(日付・approver・確認内容)を status 注記へ。
   golden フィールドを `approved(日付 maintainer 実機: <確認内容>)` に書き換える。
3. **ECO 本文クローズ節**: タイトルの (staged)→(applied)、クローズ節に
   実機確認内容・再発防止・**教訓**(一般化できる形で 1 段落。既存教訓との関係=read-across を明記)。
4. **検証+コミット**: `python bomdd/validate_bom.py` 0-0 → `accept(eco-NNN): golden 合格 — <要約>`。
   **ライフサイクル trailer 必須(ECO-061)**: implemented→applied の遷移コミットには
   `BomDD-ECO-Accept: ECO-NNN` trailer を携行する(commit-msg hook が fail-closed で強制):
   ```
   git commit -m "accept(eco-NNN): golden 合格 — <要約>" -m "BomDD-ECO-Accept: ECO-NNN"
   ```
5. **post-condition(ECO-061 受入条件7)**: accept コミット**後**に
   `python bomdd/validate_bom.py`(0-0)と `python bomdd/validate_bom.py --selftest-lifecycle`(OK)
   を実行し、履歴証拠(E15〜E17)を含めてクローズ状態が成立していることを確認する。
   FAIL したらクローズ不成立 — 原因を是正するまで完了報告しない。
6. **M4 同期の要否判定**: spec §2.6 / E-BOM / M-BOM / 35-dsbom に as-built 乖離が生じた ECO
   (surface 新設・挙動仕様変更)は M4 同期まで行う。VM 内部の欠陥是正のみなら不要(ECO-038 は不要だった)。
7. **メモリ/残課題**: ECO 中に送付したスコープ外事項(後続裁定など)があれば完了報告に再掲する。

## 完了報告

3 コミット(起票/fix/accept)のハッシュ・機械受入サマリ・再発防止の場所・教訓・
残った後続事項(未確定裁定など)を 1 つの報告にまとめる。

## 教訓の昇格

教訓が ViewPrism2 固有でなく一般形(方法論レベル)なら、方法論リポ(BomDD)への昇格候補として
完了報告に明記する(昇格自体は方法論側の変更 = 別リポ・別オーダー)。
