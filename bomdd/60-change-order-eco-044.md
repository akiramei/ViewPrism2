# Change Order — ECO-044(staged): IMG-011 裁定の取り込み — マージ永続化ルール確定(タグ統合トグル撤去+補償 Undo 新設)

> ECO-025 系とは別系統の残未確定 IMG-011 の maintainer 裁定(2026-07-05・CAD `49ea874`)の取り込み。
> 裁定 3 点のうち ①=現状追認 / ②=見せかけ UI の是正 / ③=**net-new(スキーマ変更 ECO の初出**=
> 62 移行オラクル初適用**)**。

## 1. 裁定(maintainer 2026-07-05・裁定資料= ViewPrismUI docs/decisions/IMG-011-merge-persistence-undo.md)

1. 整理マージは、source を deleted にし、destination へ source のタグを union する**原子操作**とする。
2. v1 では「マージ時にタグを含める」は**常時 ON** とし、**OFF 選択肢は実装しない**。
3. 「取り消す」は通常の UI Undo ではなく、**マージ操作ログに基づく補償操作**とする。
   **source が物理削除されておらず、destination/source の revision がマージ直後から変化して
   いない場合のみ**実行可能とする。

## 2. 適合判定(2026-07-05 実測)

| 裁定 | 判定 | 根拠 |
|---|---|---|
| ① 原子・union・deleted | **現状追認** | MergeService/IMergeRepository(単一トランザクション・MergeCalculator union・source=Deleted・INV-009 物理非破壊・INV-011)。オラクル S-19〜24 で固定済み |
| ② タグ統合 常時 ON | **是正(UI)** | **見せかけトグルが存在**: ImageTabView.axaml:444 / WorkTabView.axaml:415 の CheckBox(IncludeTags)は切替可能だが、Core は常に union= **OFF が機能しない誤誘導 UI**(ECO-021 β-3 が IMG-011 待ちで残置)。ECO-041 教訓「未配線 UI は検査の谷間」の同族 |
| ③ 補償 Undo | **net-new** | マージ操作ログは**存在しない**(ApplyMergeAsync は記録を残さない)。images に revision カラムも無い。CanUndo は両 VM とも `=> false` のプレースホルダ(ImageTabViewModel:745 / WorkTabViewModel:213) |

## 3. 切り分け済みの事実(確定)

- MergeCalculator は**マージ先優先**(target の既存値は変化しない・NULL 補完・多元 id 昇順
  先勝ち・simple union)→ マージが destination に及ぼす変化は「**新規タグ行の追加+NULL 値の
  補完**」に限られる= 補償対象の差分が小さく決定論的。
- 「物理削除」= ゴミ箱の完全削除のみ(INV-009。マージ自体は物理ファイルに触れない)。
- DB スキーマ変更を伴う ECO は本 ECO が初(ECO-001〜043 でゼロ)= 62-migration-oracle.md
  (未採用テンプレ)の初実施になる。
- CAD 反映済み(`49ea874`): image_tab.md 整理トレイ(タグ統合=チェック UI なし・取り消す=
  補償操作+実行可能条件)。作業タブ整理トレイは同一意味論の読み替え。

## 4. 是正方針(案 — 着手時確定)

順序= R2 上流先行(E-BOM 宣言 → プローブ → Core → UI)。

1. **E-BOM 宣言**: E-MERGE-034 へ裁定 3 点の invariant(補償 Undo の意味論・実行可能条件)。
   E-DB-010 へマージ操作ログのスキーマ宣言。
2. **② トグル撤去(小)**: 両 XAML の CheckBox 撤去+VM の IncludeTags/ToggleIncludeTags 撤去。
   挙動不変(Core は元々常に union)・視覚差分は golden で確認。
3. **③ マージ操作ログ(新テーブル案 `merge_operations`)**: 1 マージ= 1 行。
   destination/sources の id・**タグ差分**(マージで destination に追加された行+NULL 補完で
   値が入った行の旧値)・実行時刻・**内容指紋**(destination/sources それぞれの
   status+タグ集合のハッシュ= マージ直後スナップショット)。
4. **③ revision の実装写像(提案=指紋突合)**: images に revision カラムは**新設しない**。
   Undo 時に destination/sources の現在の内容指紋を再計算しログの指紋と突合=
   「マージ直後から変化していない」を**直接**検証する(裁定意図の保守的実装。
   revision カラン新設は全書込経路への侵襲が大きく v1 では過剰)。
   「source が物理削除されていない」= source 行が存在し status=deleted のまま
   (完全削除で行が消えていれば不可)。
5. **③ 補償操作(Core・原子)**: source を deleted→normal へ戻し、destination から
   ログのタグ差分を除去(追加行の削除+NULL 補完行の value を旧値へ)。単一トランザクション・
   失敗時全ロールバック。実行可能条件の判定は描画から独立した決定論ロジック(unit 検査可能)。
6. **③ UI**: 両タブ完了パネルの CanUndo をログ+条件判定へ配線・「取り消す」実行→
   完了パネル更新(取り消した旨)+一覧再読込。条件を満たさない場合は不活性(現状の
   ToolTip「取り消しは今後対応(IMG-011)」を条件説明へ差し替え)。
7. **プローブ(R5)**: 受入テスト先行 — 補償の往復(マージ→Undo→タグ/status が元どおり)・
   条件破れ(destination へタグ追加後は不可/source 完全削除後は不可)・二重 Undo 拒否。
   是正前不合格(API 不在)を確認してから実装。
8. **62 移行オラクル(初適用)**: 旧スキーマ DB(baseline 個体)で新ビルドが起動し(M01)
   既存データ無傷(M02)+新テーブルが空で作成され新規マージがログされる(M03)。
   fixture 凍結+較正(negative control)の規律は 62 テンプレどおり。

## 5. 影響 BOM

- Core: E-MERGE-034(補償 Undo+実行可能条件)・E-DB-010(merge_operations スキーマ)。
- UI: E-UI-MODE-041/M-UI-IMAGETAB-035・M-UI-013(画像タブ 整理トレイ+完了パネル)/
  E-UI-WORKSPACE-043/M-UI-WORKSPACE-029(作業タブ 同)。
- 台帳: 62-migration-oracle.md 初実施・spec §2.10.5(マージ)+完了パネル節の M4 同期・
  CP(CP-UI-G9 ほか)クローズ時明記。
- R6: 既存固定オラクル行(S-19〜24 等)は不変。受入は新規行を追加。

## 6. 残ゲート

1. 是正実施(/eco-fix eco-044 — §4 の順序で。②は独立に小さく先行可)
2. 機械受入: build 0 / Tests / Oracle / validate_bom 0-0 + **62 移行オラクル(初適用)**
3. golden(maintainer 実機): タグ統合チェックの消滅・マージ→取り消すの往復(復元+タグ差分除去)・
   条件破れ時の不活性 ×画像タブ/作業タブ
4. クローズ時: CP 観点明記+register 更新+M4 同期(spec §2.10.5)
