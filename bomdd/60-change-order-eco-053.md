# Change Order — ECO-053(staged): validate_bom の検査器欠陥 2 件 — YAML 重複キー無検査+register 検査(E9/E10/E11)の空回り

> ECO-050 起票時の所見(register 誤挿入事故を validator が素通し)から昇格起票。
> 診断で**第 2 の欠陥(register 検査の no-op 化)**を発見 — 単発の検出漏れではなく検査器の複合欠陥。

## 1. 症状(2026-07-06 実測)

- **事故の実測**: ECO-049 起票時、register の ECO-048 エントリ内部へ ECO-049 エントリが誤挿入され、
  同一マッピングに baseline/findings/impacted_bom/body/notes が二重化(YAML 重複キー)。
  PyYAML の後勝ちで ECO-049 の parsed 値が ECO-048 の値に化けたが、**validate_bom は 0 error/0 warning
  で通過**(pre-commit も素通し)。発見は人手(ECO-050 起票中の末尾確認)・修復= `da155ad`。
- **診断中の追加発見**: `golden: "approved(2026-07-06 …)"` は宣言語彙 {n/a, approved, pending,
  round1-fixed} に一致しないのに E10 が発火していない → register 検査自体が動いていない。

## 2. 工程診断 — 検査器(bomdd 工具)の欠陥 2 件(製品 src/tests・台帳は無関係)

| # | 欠陥 | 真因(ファイル・行) | 混入・潜伏 |
|---|---|---|---|
| 1 | **YAML 重複キー無検査** | validate_bom.py:62-63 `load_yaml = yaml.safe_load`(PyYAML SafeLoader は重複キーを警告なく後勝ちマージ — YAML 仕様のキー一意性を強制しない) | validator 新設(2026-06-22)から。実害顕在化= ECO-049 事故(2026-07-06) |
| 2 | **register 検査の空回り** | validate_bom.py:148 `reg.get("change_orders", [])` — **ECO-034(2026-07-03)がリストキーを `change_orders:` → `changes:` へ改名**したが validator が未追随 → cos=[] で E9/E10/E11 が no-op | `30f2ed9` 系(ECO-034)から 3 日。**マスキング= ECO-034 の検証「error/warn 不変」— 不変だったのは検査が止まったから** |

- 付随所見(欠陥 2 の復活時に露呈する語彙乖離): GOLDEN_VOCAB は素値だが、現運用の golden は
  `approved(日付 maintainer 実機: …)` / `n/a   # コメント` 形式(ECO-022 以降の register(approved)管理)。
  E10 を素値完全一致のまま復活させると歴代エントリが大量エラー → **検査意味論を「宣言語彙のいずれかで
  始まる(prefix 一致)」へ追随させる**のが実態適合(status/E9 は素値運用のため現行のまま)。
- 裁定不要: 検査器の検出漏れ是正(製品コード・台帳・オラクル無関係)。語彙 prefix 化は運用実態への
  追随であり要求・仕様の変更ではない。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定:

- validator の YAML ロードは 4 ファイル(30-ebom/32-mbom/60-register/00-manifest)+ JSON 2 系統。
  重複キーの実害面は register に限らず**全 YAML 台帳**(E-BOM の invariants 追記や M-BOM 編集でも
  同型事故は起こり得る — 今回の事故様式「エントリのフィールド途中への誤挿入」は台帳共通)。
- 事故の再現性: 誤挿入時の Edit アンカーがエントリ末尾(notes)でなくフィールド途中(golden)だった
  ことが直接原因(作業手順)だが、**手順の教訓だけでは再発を防げない**(2 度目は検査器で止める)。
- E9(status)は現運用でも素値(`staged`/`applied`)+行コメントで語彙適合(復活しても衝突しない)。
- E11(superseded_by/reattributed_by)の実在参照検査も同時に復活する(現データで違反なしを fix 時実測)。

未検証(fix 時に実測):

- 欠陥 2 復活後の全 53 ECO エントリに対する E9/E10/E11 の実測結果(prefix 化後に 0 error のはず。
  違反が出た場合はデータ側の実欠陥として個別に扱う — 検査器是正の副産物)。

## 4. 是正方針(案 — 着手時確定・裁定不要)

1. **重複キー拒否ローダ**: `yaml.SafeLoader` 派生(mapping 構築フックで重複キー検出)へ load_yaml を
   差し替え、検出時は新コード **[E13]**(ファイル名+キー+行番号)で ERROR。全 YAML ロードに適用。
2. **register キー追随**: `reg.get("changes", [])` へ修正(ECO-034 の改名に追随。後方互換不要 —
   旧キーは ECO-034 で消滅済み)。
3. **E10 の prefix 一致化**: golden が宣言語彙のいずれかで**始まる**ことを検査(`approved(…` 形式追随)。
   STATUS_VOCAB/E9 は素値のまま。
4. **プローブ(R5・検査器版)**: 是正前に (a) `da155ad` 直前の壊れた register(git show)を現行 validator に
   通し **0-0 素通しを実測** (b) 一時コピーに故意の status 語彙違反を入れても **0-0 を実測**(no-op の証明)
   → 是正後に (a)=E13 発火・(b)=E9 発火へ転化を実測。
5. **恒久検査面(案・着手時確定)**: validator に `--selftest`(合成フィクスチャ 2 件=重複キー/語彙違反を
   内蔵生成して自己検査)を追加し、素通し様式の再発を機械面に載せる — 採否と pre-commit への組込みは
   fix 時判断(最小=実測記録のみでも可)。

## 5. 影響 BOM

- impacted: bomdd/validate_bom.py(検査器 — 製品 M-BOM 外の bomdd 工具)。
- 不変: 製品 src/tests・台帳データ(30/32/60/00)・オラクル・CAD・golden(n/a 見込み — 検査器のみ)。
- 復活する E9/E10/E11 が既存データの実欠陥を検出した場合は R3(分離起票 or 51 記録)。

## 6. 残ゲート

1. ~~工程診断~~ → 完了(検査器の欠陥・**裁定不要**)
2. ~~/eco-fix eco-053 — プローブ → 是正 → 機械受入~~ → 完了(§7)
3. **gate②**: register データ正規化 3 件(§7)の確認(記録の表現変更を含むため)。golden n/a。
4. クローズ時: register applied+教訓。

## 7. 実施記録(2026-07-06 — 是正・機械受入完了・正規化確認待ち)

- **プローブ先行(R5・検査器版・実測)**: 是正前の現行 validator に
  (A)ECO-049 事故の壊れた register(`git show da155ad~1`)→ **0 error/0 warning 素通し**
  (B)故意の status 語彙違反を注入した register → **0-0 素通し(E9 no-op の証明)**。
  → 是正後の再走: (A)= **E13 ×5 発火**(重複 5 キーを行番号つきで検出: 行 903-907・初出 898-902 =
  事故の実況と一致)+当時データの E10 2 件も検出(E9〜E11 復活の証明)・(B)= **E9 発火**。
- **是正(validate_bom.py)**:
  1. [E13] 重複キー検出ローダ(SafeLoader 派生・mapping 構築フック)— 全 YAML 台帳ロードに適用。
     検出してもロード継続(他検査も走らせる)・ファイル名+キー+行番号+初出行を報告。
  2. register リストキーを `changes:` へ追随+**キー不在は明示エラー**(「読めなければ空」で
     no-op 化する構造自体を封止 — キー改名時に validator 同期漏れがあれば即検出)。
  3. [E10] を prefix 一致へ(運用の `approved(日付 …)` 形式に追随・欄なし=None も違反)。
  4. `--selftest` 内蔵: 合成フィクスチャで E13 検出・E10 意味論・changes: 読取を自己検査
     (素通し様式の再発防止を検査器自身の検査面に)。pre-commit は従来どおり通常実行のみ
     (selftest は手動/ECO 時 — 組込みはクローズ時判断でも可)。
- **復活検査が検出した既存データ違反 4 件と正規化(記録の意味不変・本文からの事実転記)**:
  | 対象 | 旧記載 | 正規化 | 転記根拠 |
  |---|---|---|---|
  | ECO-036 status | `verified`(語彙外) | `implemented`(旧記載をコメント保存) | 語彙定義「コード/surface を製造済」・order L172「register を implemented にし」 |
  | ECO-036 golden | 自由文(成功条件) | `approved(各段で再ウォークスルー=視覚不変・期待値改訂ゼロ — 系列完了 2026-07-04)` | register 注記「系列完了・全 5 段」+order §15 |
  | ECO-037 golden | 自由文(残ゲートメモ) | `approved(2026-07-04 maintainer 実機 — 本文 §5 クローズ)` | ECO-037 本文 §5「クローズ(2026-07-04 golden 合格)」 |
  | ECO-053 golden | 欄なし | `n/a`(検査器のみ) | 本 ECO の性質 |
- **機械受入(4 点全緑+selftest)**: build 0/0・Tests 558/558・Oracle 107+2skip(製品コード不変)・
  validate_bom 実台帳 0-0・--selftest OK。
- diff: bomdd/validate_bom.py(+約 80 行)+ 60-change-register.yaml(正規化 4 行)。製品 src/tests 不変。
