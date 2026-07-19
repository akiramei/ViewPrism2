# ECO-115: タグ編集パネル操作の応答劣化 — パネル状態変更が全面 Recompute を通る(26 万件で顕在化)

- 起票日: 2026-07-19
- 報告者: maintainer(実機観測)
- baseline: main `7d5e0fd`
- 種別: 不具合是正候補(性能・実装層。視覚/意味論不変)

## §1 症状

26 万画像のビューでタグ編集モードに入り、右ペインの「現在のタグ」⇄「タグ追加」タブを
切り替えると時間が掛かる(2026-07-19・maintainer 実機)。

ECO-114 是正中の R8 レビューで**同族残余として 51-cheat-log に予告記帳済み**
(2026-07-19「タグ編集パネル操作系の全面 Recompute 残余(同族・症状未観測)…症状が出たら分離起票」)
の症状が実機観測された= R3 分離起票の実行。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **健全(非該当)** | パネルのタブ切替・行展開は右ペイン内で完結する操作(CAD 契約)。中央一覧・チップ・母集合に触れる仕様はない。性能は NFR 層 |
| BOM | **概ね健全** | ECO-114 で刻んだ計算量契約(モード遷移)はパネル操作を対象にしていない → fix 時に E-BOM invariant の適用範囲を「右ペイン内のパネル状態変更」へ拡張 |
| 実装 | **逸脱確定(3 サイト・面間非対称の逸脱側= ImageTab)** | §3.1。**WorkTab は既に正しい軽量経路**を持ち、ImageTab だけが全面 Recompute |

結論: **実装層の性能欠陥**。裁定は不要です。

## §3 切り分け済みの事実

### 3.1 確定(起票時実測・コード証拠)

パネル状態(_panelTab/_expandTag)しか変えない 3 コマンドが全面 `Recompute()`
(=26 万件の全件条件評価×(1+子チップ数)+全件ソート+26 万 ImageItemVM 再構築+
CollectionChanged の嵐= ECO-114 §3.1 と同一のコスト)を呼ぶ
([ImageTabViewModel.cs](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs)):

1. **TabCurrent(:2082)**: `_panelTab = "current"; Recompute();`
2. **TabAdd(:2085)**: `_panelTab = "add"; _expandTag = null; Recompute();`
3. **ClickAddRow 展開/畳み(:2102/:2105)**: `_expandTag` 変更後に `Recompute()`(×2 経路)。

パネル状態は右ペインの表示にしか影響せず、中央一覧(Items)・チップ・件数・ソートは不変。

- **対照(正しい側)**: 同一ファイル内の `AddQuery` setter(ECO-041)は
  「タグ編集パネルのみ部分再構築(Items 不変)」= `BuildContextPanels` のみ。
  **WorkTab は 3 サイトとも既に軽量**(TabCurrent/TabAdd= 通知のみ+BuildContextPanels・
  ClickAddRow= RebuildAddPanel)= **面間非対称の逸脱側が ImageTab**(§8.2 の型だが方向が逆=
  読み合わせるべき正解が既に隣の面にある)。
- **混入**: `6f7b4f9`(2026-06-18・M3a 初版)= ECO-113/114 と同根。潜伏約 1 ヶ月・
  マスキングも同一(通常規模では体感不能)。
- **タグ付与/剥奪(ApplyTagAsync 系)は対象外**: データ(image_tags)が実際に変わり、
  タグドット・FS チップ集計・未分類 membership(付与で一覧から抜ける= REQ-094 の確定挙動)に
  波及するため、ReloadTagsAsync→Recompute は正当。本 ECO はパネル状態のみの 3 サイトに限定する。

### 3.2 疑い(未検証)

- なし(コストの構造は ECO-114 で計測済みの同一経路。寄与割合の再計測は不要)。

## §4 是正方針(案・着手時確定)

### 案A(推奨): 3 サイトを既存の部分再構築へ揃える

- TabCurrent/TabAdd/ClickAddRow の `Recompute()` を **`BuildContextPanels(new HashSet<string>(_selected))`
  +`OnPropertyChanged(string.Empty)`** へ(AddQuery setter=ECO-041 の既存様式・WorkTab と対称化)。
  ClickAddRow の EnsureSettingsAsync(タグ設定ロード)は従来どおり先行(BuildAddGroups が消費)。
- 新規部品なし・意味論不変(タブ可視/展開状態/候補値チップ/数値セルは BuildContextPanels →
  BuildAddGroups が全て再構築する)。
- 検査= ECO-114 と同型の構造 probe(タブ切替/展開で Items インスタンス同一性)+
  パネル意味論(展開で ValueChips/NumCells が出る・タブ復帰)の維持。

### 案B: パネルを専用 VM へ分離

構造的には綺麗だが diff 大・ECO-036(子 VM 移送)級の別オーダー。応答劣化の是正には過剰。

## §5 影響 BOM(案A 見込み)

- **src**: ImageTabViewModel 3 サイトのみ(数行)。
- **tests**: 構造 probe+パネル意味論維持。
- **ebom**: E-UI-BROWSE-022 の ECO-114 invariant へ適用範囲追記(パネル状態変更も母集合
  パイプラインを通らない)(fix 時)。
- **CP**: CP-UI-G1 刻印(accept 時)。

## §6 残ゲート

- gate①(裁定): **不要**(実装層確定・WorkTab に正解が既在・視覚/意味論不変)。
- gate②(golden): 必要 — maintainer 実機で 26 万件ビュー×タグ編集モードの
  「現在のタグ」⇄「タグ追加」切替+行展開/畳みの体感確認+パネル挙動不変。
