# ECO-094 — 固定クローム チップ行の共有部品化(LabeledChipStrip 抽出・DRY)

- type: リファクタ(DRY・視覚不変が納品条件)
- status: staged
- baseline: main c442726
- 起票日: 2026-07-15
- 起票元: ECO-091 裁定 §7「LabeledChipStrip 抽出は契約一致確定後の別 ECO」の実行。
  姉妹 ECO(ECO-091/092/093)が全て golden approved でクローズ=契約収束済み(実行の適時到来)。

## 1. 要求(症状ではない)

ImageTabView と WorkTabView の**固定クローム チップ行**(IMG-023=ECO-091 の 2 面)は、
容量・到達性契約(最大 2 行+非対話「ほか N 件」→ポップオーバー)を**同一契約・同時反映**で
実装している。しかし計算ロジック層(`ChipStripViewModel`/`ChipRowOverflow`)は既に共有済みである一方、
**プレゼンテーション層(XAML マークアップ+code-behind の実測供給)が 2 面へコピペ**されており、
以後の変更が 2 箇所同期を要する(ECO-090 の「read-across 必須」統制宣言で暫定的に担保している状態=
構造的 DRY ではない)。この重複を共有 UI 部品 **LabeledChipStrip** へ抽出し、視覚不変のまま
同期リスクを構造的に除去する。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全・変更不要** | 契約は VPUI `17dc9f3`(IMG-023 裁定資料 §7・VC-IMG-10/VC-WORK-3)で確定・golden approved 済み。DRY は実装構造の問題で視覚仕様に影響しない |
| BOM(30-ebom/32-mbom) | **健全**(部品昇格の追補のみ) | 共有部品化は E-BOM の shared-component sub-BOM 化(ECO-016/ECO-025 SC-COLUMN-PICKER-001 の前例)に倣う。受入観点は既存 CP-CHIPWRAP-088 を継承 |
| 実装 | **重複(DRY 対象)** | VM 層は共有済み。プレゼンテーション層のみ 2 面コピペ(§3 に実測) |

**未確定事項との関係**: 本 ECO は net-new 機能を持たない(視覚・挙動不変)。未実装/仕様未確定を
バグ視していない。裁定は ECO-091 §7 で先出し済み=**gate① 不要**。

## 3. 切り分け済みの事実

### 確定(実測・c442726 時点)

- **VM 層は既に DRY**: `ChipStripViewModel`(+`ChipRowOverflow`)を `ImageTabViewModel`・
  `WorkTabViewModel`・`TagPaletteViewModel` の 3 VM が共有(grep 実測)。本 ECO は VM 層を触らない。
- **code-behind `EvaluateChipRow` はバイト一致の重複**:
  `ImageTabView.axaml.cs:62` と `WorkTabView.axaml.cs:41` の本体は、`Vm` アクセサの型
  (`ImageTabViewModel?` vs `WorkTabViewModel?`)と doc コメント文言以外**完全一致**
  (`diff` 実測=差分は 3 箇所のみ・いずれも型/コメント)。`OnChipPopoverKeyDown`(Escape 復帰)も同型。
- **XAML チップ行ブロックは近似一致の重複**:
  `ImageTabView.axaml:1000-1068` と `WorkTabView.axaml:805-862`。`ChipStrip.DisplayItems` の
  `ItemsControl`+「ほか N 件」チップ+overflow `Popup`(検索 TextBox+`PopoverItems`+空表示)が
  同構造。差分は 2 点のみ:
  1. **チップ内テンプレート**: ImageTab は未定義値チップの破線枠(REQ-095/096・`Rectangle` 重ね)を
     持つ(WorkTab は持たない)。→ 部品はチップ item テンプレートを差し替え可能にするか、
     未定義値プロパティ(`IsUndef`/`UndefLabel`)を共有 item VM 側で無害化する(fix 時判断)。
  2. **バインド内の VM 型キャスト**: `((vm:ImageTabViewModel)DataContext)` vs
     `((vm:WorkTabViewModel)DataContext)`。→ 部品化で `ChipStrip` を直接 DataContext に取れば解消。

### スコープ境界(本 ECO に**含めない** — 別契約・実測裏取り済み)

- **タグパレット候補値行**(`TagsTabView.axaml:324`・ECO-092)と
  **タグ編集プレビュー帯**(`TagEditorWindow.axaml`・ECO-093)は、
  ECO-092 裁定が明記する通り**別契約**(固定クローム操作面 vs カード内/ダイアログ内の閲覧プレビュー・
  ポップオーバーなし・非対話「ほか N 件」の**静的**表現)。LabeledChipStrip(固定クローム=ポップオーバー付き
  操作面)へ統合すると挙動契約が混線する。→ **統合しない**。これらの様式共通化は必要なら将来別 ECO
  (CandidateValuePreview 級・ECO-092 notes 記載)で判断する。

### 未検証(疑い)

- Avalonia での抽出方式(bindable プロパティ付き `UserControl` / `TemplatedControl` +
  `ControlTemplate` / 共有 `DataTemplate`)のうち、実測供給(`EvaluateChipRow` の code-behind)を
  部品側へ内包できるか(`LayoutUpdated` を部品内で完結させ、親は `ChipStrip` VM を渡すだけにできるか)。
  → fix 時に headless 実測で確定(視覚不変・折畳み挙動不変を probe で担保)。

## 4. 是正方針(案・fix 着手時に確定)

1. `LabeledChipStrip`(共有 UI 部品)を新設。DataContext もしくは bindable プロパティで
   `ChipStripViewModel` を受け取り、DisplayItems 行+「ほか N 件」+overflow Popup を内包。
   実測供給(`EvaluateChipRow` 相当)は可能な限り部品内へ移し、`ImageTabView`/`WorkTabView` の
   code-behind から重複を除去。
2. チップ内テンプレートの差分(未定義値破線枠)は、item テンプレート差し替え or 共有 item VM の
   `IsUndef` 無害化で吸収(視覚不変を維持)。
3. E-BOM に shared-component として昇格登録(SC-CHIPSTRIP-001 級・SC-COLUMN-PICKER-001 前例)。
4. 受入は既存 `CpUi090ChipStripParityTests`(2 面パリティ)を DRY 後も緑で維持し、
   視覚不変(少数/47 件級/幅変更/未定義値チップ)を probe で pin。

## 5. 影響 BOM

- src=`LabeledChipStrip`(新設 UserControl/TemplatedControl)+`ImageTabView.axaml`/`.cs`・
  `WorkTabView.axaml`/`.cs`(重複ブロックを部品呼び出しへ置換)。VM 層(`ChipStripViewModel`/
  `ChipRowOverflow`)は不変。
- テスト=既存 `CpUi090ChipStripParityTests` 維持+DRY 後の視覚不変 probe(未定義値チップ描画の
  ImageTab 保持・WorkTab 非保持・折畳み挙動 2 面一致)。既存固定 Oracle 行は変更しない(R6)。
- E-BOM=shared-component sub-BOM 追補(SC-CHIPSTRIP-001)+E-UI-023 系の realized-by 更新。
- CP=CP-CHIPWRAP-088 を部品面へ継承(検査次元は不変)。
- CAD=**変更なし**(視覚不変・契約は VPUI `17dc9f3` のまま)。
- i18n=**変更なし**(既存キー `chip.*` を部品が参照)。

## 6. 残ゲート

- **gate①(裁定)=不要**。ECO-091 §7 で「契約一致確定後の別 ECO」として先出し済み・
  CAD 変更なし・視覚不変。
- **gate②(golden)=必要**。2 面とも golden approved 済みサーフェスを触るため、DRY 後に
  **視覚・挙動の不変**を maintainer 実機で再承認(R7=CAD captures 並置・両タブ)。
