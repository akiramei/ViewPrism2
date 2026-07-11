# ECO-065 (staged) — 50-as-built(承認証拠面)が validator 検査外 — 無効記録が無音通過する fail-open

- 起票: 2026-07-11(maintainer 指示 — BomDD method 側 transfer-04/ECO-061 スコープ外所見の昇格判定による)
- 出自: ECO-061 スコープ外所見「as-built 構文は validator 検査外」。BomDD method 還元作業(2026-07-11)で
  昇格条件を判定 — **「ECO クローズ・監査証拠が未検査の as-built 記録に依存している」に該当**したため、
  52 候補記録でなく直ちに ECO へ昇格(maintainer 事前指示の昇格基準に従う)。

## §1 症状(実測 2026-07-11)

### 未検査領域(どこが検査されていないか)

- `bomdd/50-as-built.yaml` は validate_bom.py に**一切ロードされない**(ロード対象は
  30-ebom / 32-mbom / 60-change-register / 00-manifest のみ)。したがって:
  - YAML 構文パース・E13 重複キー検査の**対象外**
  - register が引用する承認記録キー(`golden_2026_07_11_ecoNNN` 型)の**実在突合なし**
  - as-built エントリ自体の最小スキーマ(承認者・日付・対象 ECO)検査なし
- 同族: `52-metrics.yaml`・`53-service-bom.yaml` も同様にロードされない(スコープは gate で裁定)。

### 現在 validator が保証しているように見えるもの

- docstring 見出し「ViewPrism2 BomDD 成果物の参照整合性・規律チェック」は成果物全般を示唆する。
- E10 は register 側 `golden` 欄の**語彙 prefix のみ**検査(承認記録本体の実在・内容は非検査)。
- E13 の「**全 YAML 台帳ロードに適用**」の文言は 50/52/53 を含むように読める余地があるが、
  実際の適用範囲は validator がロードする 4 ファイルに限られる(約束と実装の乖離ではなく
  文言の曖昧さ — 検査一覧には as-built の行はない)。

### 無効記録の注入実験(2026-07-11 実測・復元済み)

- `50-as-built.yaml` 末尾へ不正 YAML(未閉括弧・不正インデント)を追記 → `validate_bom.py --quiet`
  = **0 error / 0 warning・exit 0**。構文破壊すら検出されない(fail-open の実証)。

### この未検査面に依存しているクローズ判定

- register は **32 箇所**で as-built を承認記録として引用。直近 ECO-062/063/064 の notes は
  いずれも「50-as-built.yaml: `golden_2026_07_11_ecoNNN` へ承認記録」をクローズ内容に含む
  (accept のクローズ条件の一部)。
- 現状の突合(2026-07-11): `golden_2026_07_11_eco062/063/064` は as-built に実在 — **腐敗は未発生
  (潜在)**。ただし欠落・キー不一致・構文破壊が起きても検出する機械はゼロ。

## §2 工程診断(R2)

- ECO-061 は台帳状態×git 履歴証拠(E14〜E19)を閉じたが、**承認証拠の記録面(as-built)**は
  スコープ外として分離した(同 ECO スコープ外所見)。分離自体は正規 — 記録先が未定のまま
  残っていたのが本起票で解消される欠測。
- BomDD method 側の一般形: 「検査の対照3種と空集合の扱い」(control-plan・transfer-04 還元)の
  対象欠落チャレンジ — **検査対象がそもそもロードされない**は「空集合の vacuous pass」の
  ファイル粒度版。silence §16(a)(対象不在の無音 PASS)の台帳面。

## §3 切り分け済みの事実

- validator は 50-as-built を開かない(コード確認+注入実験で二重確認)。
- register→as-built の参照(golden_* キー)は現在 3/3 一致(手動突合)。
- E10/E14〜E19 は健全(register 側・ライフサイクル側の検査は本件と独立に機能)。

## §4 是正方針(案 — gate 裁定前)

1. validate_bom.py のロード対象へ `50-as-built.yaml` を追加(構文パース fail-closed+E13 重複キー)。
2. 新規 E 検査: register の golden が approved である ECO は、as-built に対応する承認エントリ
   (`golden_*_ecoNNN` 型キー)が**実在**すること(register→as-built の参照整合)。
3. as-built エントリの最小スキーマ検査(承認者・日付・対象 ECO の必須フィールド)— 深さは gate で裁定。
4. 52-metrics / 53-service-bom の同族(ロードすらされない台帳)を同時に E13 対象へ含めるかは
   gate で裁定(スコープ肥大を避けるなら本 ECO は 50 のみ+52/53 は候補記録)。
5. 陽性対照: 本起票の注入実験(不正 YAML・golden_* キー欠落)を selftest へ収載
   (ECO-053/061 の様式 — 検査の新設は陽性対照と対で)。

## §5 影響 BOM / 受入計画

- validate_bom.py(検査追加)・50-as-built.yaml(スキーマ明文化が要る場合)・
  change-management.md(承認記録の規律を明文化する場合)。
- 既存 Oracle/CP/コードは不変予測(validator と台帳のみ)。
- 受入: 注入実験の再実行が FAIL へ転じること(是正前 0 error の再現ログは本 order §1)+
  正常台帳で 0 error 維持+selftest 緑。

## §6 残ゲート

- gate①: 是正方針 1〜5 のスコープ裁定(特に 3 の深さ・4 の同族範囲)。
- 製造・受入は gate① 後。
