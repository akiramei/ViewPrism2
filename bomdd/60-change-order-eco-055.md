# Change Order — ECO-055(staged): 整理トレイの条件検索が CAD/E-BOM 定義の 5 条件中 2 条件しか配線されていない(ECO-041 型の部分欠落)

> 整理マージ v2 モック受入(Phase 0・2026-07-06)の三者差分で発見した既存ギャップの分離起票
> (maintainer 裁定: v2 追随 ECO とは分離・順序実施=本 ECO が先)。「条件が増えた」というユーザー認知の
> 正体は v2 の新設ではなく、v1 CAD の時点から続く実装欠落の顕在化だった。

## 1. 症状(2026-07-06・Phase 0 三者差分)

- 整理トレイ「似た画像を探す」の条件検索に入力欄が **2 つしかない**(ファイル名・拡張子)。
- CAD(v1 モックから一貫)は **5 条件**: ハッシュ値/拡張子/サイズ/ファイル名/更新日
  (v1・v2 モックの `condDefs` は完全同一 — v2 で増えたのではない)。
- 実害: 完全重複の検出(ハッシュ値一致)・日付や サイズでの絞り込みが整理トレイから使えない。

## 2. 工程診断 — 実装欠落(CAD・E-BOM・order・エンジンすべて健全)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全・5 条件定義済み** | v1 モック(ECO-014 時の権威)から `condDefs = hash/ext/size/name/date`。散文(image_tab.md 検索表)も「ハッシュ値、拡張子、サイズ、ファイル名、更新日」 |
| E-BOM | 健全 | E-UI-SIMILARITY-035 の ECO-014 invariant「②条件検索(**E-CRITERIA-037 を消費**・hash/拡張子/サイズ/名前/mtime)」 |
| order(ECO-014) | 健全・縮退記録なし | 60-change-order-eco-014.md L25「hash/拡張子/サイズ/名前/mtime」— 2 条件へ絞る指定は無い |
| エンジン(Core) | **健全・5 条件対応済み** | SearchCriteria(Hash/NameContains/Extension/MtimeFrom/MtimeTo/サイズ範囲)+CriteriaMatcher(REQ-068・S-27 凍結) |
| 実装(トレイ UI/VM) | **欠落** | §3 — 両タブとも Name/Ext の 2 欄のみ |

- **ECO-041 と同型**(CAD 定義済み機能の実装欠落)の部分欠落版。裁定不要 → /eco-fix 可。
- v2 追随 ECO との関係: 条件 5 種は v1=v2 で同一のため、本是正は v2 レイアウト変更と独立に先行できる
  (順序実施の根拠)。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定:

- 実装の欠落面(両タブ対称):
  - ImageTabOrganizeViewModel: `CriteriaName`/`CriteriaExt` のみ(:85-86)。`BuildCriteria()`(:161)は
    NameContains/Extension のみ充填 — Hash/Mtime/サイズは常に null。
  - WorkTabViewModel: 同型(`BuildCriteria()` :929)。
  - XAML: ImageTabView.axaml:429/433・WorkTabView.axaml:403/406 の 2 欄のみ。
- エンジン側は無改変で足りる: SearchCriteria は Hash(完全一致)/NameContains(部分一致)/
  Extension(完全一致)/MtimeFrom・To(範囲)/サイズ範囲を実装済み(REQ-068・M-CRITERIA-024。
  空条件非実行・AND・安定順は S-27 で凍結済み)。
- 混入と潜伏: `0cf44ba`(整理モード製造①・ECO-014 系)で 2 欄のみ配線 → `81500a8`(作業タブ β)へ
  転写。ECO-050(しきい値 80)と**同一コミット・同族様式**(工場が order/CAD の全数を配線せず、
  検査面に全数観点がなく潜伏)。
- マスキング: G-9/G-1 チェックリストに条件検索の**条件全数**の観点なし(「候補+一致率」のみ)・
  CP unit(CpUiG1OrganizeTests の条件検索 fact)も name/ext 経由のみ =「全数がどの検査面にもない」
  (ECO-050 数値既定・ECO-052 チェックリスト読み替えと同族の検査面の谷間)。

未検証(fix 時に確定):

- 入力 UI の形状: モック(v1/v2 同一)は条件をトグルチップで on/off する形。単一値入力か範囲入力か
  (エンジンは mtime/サイズとも From/To 範囲)の UI 写像は **v2 モックの実装詳細を正として** fix 時に
  確定する(CAD 権威)。
- 更新日の入力形式(日付ピッカー vs テキスト)— 同上。

## 4. 是正方針(案 — 着手時確定)

- 両タブの VM に欠落 3 条件のプロパティ+`BuildCriteria()` 充填を追加し、XAML に入力欄を追加
  (v2 モックの入力形状に従う — 直後の v2 追随 ECO と同じ CAD を参照するため、**入力欄の配置は
  v2 の 3 ゾーンレイアウト内での位置を先取りしない**よう現行レイアウト内に最小追加。配置の
  v2 化は次 ECO の仕事= diff 混濁回避)。
- プローブ(R5): ハッシュ値/サイズ/更新日で検索できないこと(VM にプロパティ不在= CS 型 or
  BuildCriteria が null を返すことの実測)を是正前に固定。
- 受入: 既定値 pin(ECO-050 教訓)は該当なし(条件は空既定)。**条件全数の pin テスト**
  (BuildCriteria が 5 条件すべてを写像する)を追加=「全数がどの検査面にもない」様式の封止。
- 再発防止(クローズ時): CP-UI-G9/G1 へ「条件検索 5 種の全数」観点。

## 5. 影響 BOM

- impacted: M-UI-ORGANIZE-034(ImageTabOrganizeViewModel)/ M-UI-IMAGETAB-035(XAML)/
  M-UI-WORKSPACE-029(WorkTabViewModel+XAML)。
- 不変: Core(E-CRITERIA-037/M-CRITERIA-024・S-27)・E-BOM・CAD・spec(REQ-068 既存)・DB。
- クローズ時: CP-UI-G9/CP-UI-G1 観点明記。

## 6. 残ゲート

1. ~~工程診断~~ → 完了(実装欠落・**裁定不要** — ECO-041 前例と同型)
2. /eco-fix eco-055 — プローブ → 3 条件の配線(両タブ・最小追加)→ 機械受入
3. golden(maintainer 実機): ハッシュ値/サイズ/更新日でも候補が絞れる+既存 2 条件の回帰
4. クローズ時: CP 観点明記+register applied+教訓
