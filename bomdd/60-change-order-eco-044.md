# Change Order — ECO-044(applied): IMG-011 裁定の取り込み — マージ永続化ルール確定(タグ統合トグル撤去+補償 Undo 新設)

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
- ~~DB スキーマ変更を伴う ECO は本 ECO が初~~ **【起票時の事実誤認・是正時訂正(2026-07-05)】**:
  スキーマ変更は初ではない — Migrations 001〜004 が既存(001 views.description / 002 類似テーブル /
  003 hash_adapter / **004 workspaces=ECO-020**)。確立済みの前例= **増分マイグレーション+
  LatestDdl 併記+CP-DB-006(新規 DB とマイグレーション適用 DB のスキーマ同値検査)**。
  62-migration-oracle.md の「皆無」注記は ECO-001〜015 時点の記述が未更新だったもの。
  本 ECO は **ECO-020 前例に従う**(追加テーブルのみ・既存データ無変換= 62 の重装備
  [fixture 凍結・較正]でなく CP-DB-006 スキーマ同値+マイグレーション適用検査で受ける)。
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
8. **移行検査(ECO-020 前例)**: migration 005 を Migrations へ追加し LatestDdl へ併記。
   CP-DB-006 のスキーマ同値検査(新規 DB=マイグレーション適用 DB)が両経路の同値を機械検査。
   既存データ無変換(追加テーブルのみ)につき 62 の重装備は不採用(§3 の訂正参照)。

## 5. 影響 BOM

- Core: E-MERGE-034(補償 Undo+実行可能条件)・E-DB-010(merge_operations スキーマ)。
- UI: E-UI-MODE-041/M-UI-IMAGETAB-035・M-UI-013(画像タブ 整理トレイ+完了パネル)/
  E-UI-WORKSPACE-043/M-UI-WORKSPACE-029(作業タブ 同)。
- 台帳: 62-migration-oracle.md 初実施・spec §2.10.5(マージ)+完了パネル節の M4 同期・
  CP(CP-UI-G9 ほか)クローズ時明記。
- R6: 既存固定オラクル行(S-19〜24 等)は不変。受入は新規行を追加。

## 6. 残ゲート

1. ~~是正実施(§4 の順序で)~~ → 完了(§7)
2. ~~機械受入: build 0 / Tests / Oracle / validate_bom 0-0 + 移行検査~~ → 完了(§7)
3. ~~golden(maintainer 実機)~~ → 合格(§8・2026-07-05 approved)
4. ~~クローズ時: CP 観点明記+register 更新+M4 同期~~ → 完了(§8)

## 8. クローズ(2026-07-05 golden 合格)

- maintainer 実機: タグ統合チェックの消滅・マージ→取り消すの往復(source 一覧復帰+タグ復元)・
  完了パネル表示正常 ×画像タブ/作業タブ OK。条件破れの不活性+理由表示は機械受入
  (CpMerge044UndoTests/CpUiG1OrganizeTests)で担保(UI 導線では再現しにくいため)。
- 再発防止:
  - **CP-UI-G9** へ「タグ統合チェック非搭載=常時 union+取り消す=補償 Undo の往復・
    条件破れ不活性」を**見せかけトグルの潜伏実績(ECO-041『未配線 UI』同族)つき**で明記。
  - **CP-MERGE-018** へ Undo 系 test_vectors 3 行+fixture(CpMerge044UndoTests)を明記。
- M4 同期: **spec §2.10.5 as-built**(操作 4= 操作ログ・タグ統合常時 ON・取り消す=補償操作の
  意味論と実行可能条件)。E-BOM は fix 時宣言済み・35-dsbom 不要(surface 新設なし)。
- 教訓(2 点・一般化):
  1. **「取り消す」には 2 つの実装様式がある** — セッション揮発の UI Undo スタックと、
     **操作ログに基づく補償操作**。永続状態(DB)を変える操作の Undo は後者が正しい:
     ログが正本になり、実行可能条件(他変更との衝突)を**データで**判定できる。
     内容指紋(スナップショット突合)は revision カラム新設なしで「変化していない」を
     直接検証する軽量な実装写像(fail-closed=疑わしければ取り消し不可)。
  2. **affordance を先に置く(CanUndo=false のプレースホルダ)運用は、裁定が下りた時に
     「見せかけ UI」を検出・清算する仕組み(工程診断での UI⇄Core 突合)とセットでのみ安全**
     — 本 ECO のトグルは affordance でなく機能を装った未配線で 18 日潜伏した(ECO-041 同族)。
- 事実訂正の教訓(§3): 台帳の「〜は皆無」系の注記は時点付きで書く(62 の「スキーマ変更皆無」
  は ECO-015 時点の記述が ECO-020 で古くなっていた=起票時の誤認を誘発)。

## 7. 実施記録(2026-07-05 — 機械受入完了・golden 待ち)

- **順序**: R2 上流先行どおり — E-BOM 宣言(E-MERGE-034 裁定②③ invariant / E-DB-010
  migration 005 宣言)→ プローブ → Core/Infra → UI 配線+トグル撤去。
- **実測裏取り(プローブ先行)**: 受入テスト 6 件(CpMerge044UndoTests — ログ記録/補償往復/
  destination 変化後拒否/source 完全削除後拒否/二重 Undo 拒否/後続マージで先行ログ失効)を
  先に追加し、**是正前に不合格(CS1061= GetLatestOperationAsync/EvaluateUndoAsync/
  UndoMergeAsync が MergeService に不在)を確認**。
- **実装(裁定③・net-new)**:
  - `merge_operations` 新テーブル= migration 005+LatestDdl 併記(ECO-020 前例・
    CP-DB-006 スキーマ同値検査が両経路の同値を機械検査=緑)。
  - `MergeOperationRecord`(Core モデル)+ `MergeUndoCalculator`(決定論: 内容指紋=
    status+hash+タグ集合の SHA-256・タグ差分=追加行+NULL/空補完行と元値・実行可能条件判定)。
  - IMergeRepository を **default interface method で optional 拡張**(CHEAT-02 前例=
    既存スタブ無改変)・MergeRepository がログ同梱 ApplyMerge/取得/補償 ApplyUndo を実装
    (いずれも単一トランザクション)。GetLatest は executed_at+rowid 降順(固定時計のタイ決着)。
  - MergeService: **ctor へ optional IClock(CHEAT-01 前例= 固定オラクルの 3 引数構築を
    無改変で維持)**・MergeAsync がログを同一トランザクションで記録(戻り値 Result は不変=
    既存 call site 無改変)・GetLatestOperationAsync/EvaluateUndoAsync/UndoMergeAsync 新設。
  - UI: 両 VM に CanUndo(マージ直後に初期評価)+UndoMerge コマンド(失敗= 理由を
    UndoNote 表示+不活性化 / 成功= トレイを畳んで再読込= sources が一覧へ復帰)。
    「取り消す」ボタンへ Command 配線+ToolTip を条件説明へ差し替え+UndoNote TextBlock 追加。
- **実装(裁定②)**: 見せかけトグル撤去 — 両 XAML の CheckBox・両 VM の IncludeTags/
  ToggleIncludeTags・Organize 子 VM の _includeTags を削除(Core は元々常に union=挙動不変)。
- **既存テストの更新(意図した挙動変更)**: CpUiG1OrganizeTests の
  `Assert.False(vm.CanUndo) // 取り消しは IMG-011(別 ECO)` → `Assert.True`(裁定③の本実装)。
  UI 受入 3 件追加(画像タブ: Undo 往復/条件破れ不活性+理由表示・作業タブ: Undo 往復)。
- **§3 事実訂正**: 「スキーマ変更初」は誤り(Migrations 001〜004 既存)→ ECO-020 前例に従う
  (増分マイグレーション+LatestDdl 併記+CP-DB-006 同値検査。62 の重装備は不採用)。
- 機械受入: build 0 error/0 warning・**Tests 544/544**(プローブ 6+UI 受入 3 が合格転化/追加)・
  Oracle 100+2skip(既存行無改変= R6。optional 拡張により構築シグネチャも不変)・
  validate_bom 0/0。
