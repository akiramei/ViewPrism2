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

## §7 実施記録(2026-07-19・/eco-fix)

### 7.1 プローブ先行(R5)と赤の実測

- 構造プローブ(CpUiG1ModeTransitionTests・ECO-114 と同型のインスタンス同一性): タブ切替/行展開/
  畳み/タブ復帰の 4 遷移で Items(+チップ)を再構築しないこと+パネル意味論(展開で NumCells・
  Expanded 状態・タブ復帰)→ **是正前 1/1 不合格を実測**(TabAdd の Recompute で最初の Same が破れる)。
  既存 828 全緑。

### 7.2 是正の構造(案A 採用)

- TabCurrent= 通知のみ / TabAdd・ClickAddRow(展開/畳み)= `BuildContextPanels`+通知へ置換
  (AddQuery setter=ECO-041 の既存様式・WorkTab と構造対称化)。EnsureSettingsAsync は従来どおり先行。
- E-BOM: E-UI-BROWSE-022 の ECO-114 invariant へ適用範囲拡張(右ペイン内パネル状態変更も
  母集合パイプライン非通過)を同時記帳。

### 7.3 機械受入(2026-07-19・全緑)

- build 0 error(`--no-incremental` 警告 0)・Tests **829/829**(既存 828+プローブ 1)・
  Oracle 109+2skip(R6 不変)・validate_bom 0/0。R7= 対象外(XAML 不変)。

### 7.4 セルフレビュー(R8)+処置

fresh context の独立レビュー(旧 Recompute 副作用の悉皆裏取り+全テスト独立実行)。
**スコープ内欠陥= 0**。処置つき所見:

| 所見 | 分類 | 処置 |
| --- | --- | --- |
| CurrentTags 鮮度(TabCurrent 通知のみ化)— データ変更系全数(Apply系/Remove/scan/外部変更)が ReloadTagsAsync 等でパネル再構築を伴うことを悉皆確認 | 問題なし確認 | — |
| ClearCopyFeedback/ColumnPicker 閉鎖の不在 — モード排他+ApplyModeTransition 経由で構造的に到達不能 | 問題なし確認 | — |
| プローブに Chips 同一性がない(invariant 文言は「Items/チップ/件数」) | 軽微(任意) | Chips[0] 同一性 1 行を採用追加 |
| WorkTab との通知粒度差(狭通知 vs string.Empty 全通知=本ファイル支配様式) | 記録のみ | 実害なし(全通知は母集合非依存)・構造対称の意で許容 |
| HashSet comparer 表記ゆれ/ImageTab の部分再構築メソッド未分化 | スコープ外(記録) | 機能差なし・将来の掃除候補(起票不要の粒度) |

## §8 クローズ(2026-07-19 golden 合格)

- **golden**: approved(2026-07-19 maintainer 実機・26 万件ビュー基準 4 点): ①タブ切替が即時
  ②行展開/畳みが即時 ③パネル挙動不変(候補値チップ/★セル/検索/付与反映) ④中央一覧
  (グリッド/スクロール/チップ/件数)が一切動かない。
- **再発防止**: CP-UI-G1 へ適用範囲拡張(パネル状態変更も母集合パイプライン非通過)+潜伏実績
  (正解様式が同一ファイルと隣の面に既在だった逆向き非対称)を刻印。E-BOM invariant は fix 時に
  拡張済み。機械側= CpUiG1ModeTransitionTests(4 遷移)で pin。
- **M4 同期**: fix 時の E-BOM 拡張のみ(spec/M-BOM 変更なし=実装層の計算量是正)。

### 教訓

1. **残余の予告記帳は次の診断を即決させる**: ECO-114 R8 で「症状が出たら分離起票」と経路・是正型
   まで cheat-log に書いてあったため、本 ECO は症状報告から起票まで工程診断の再作業ゼロだった。
   R3 の「分離起票 or 記帳」の記帳側は、**経路名+是正型まで書くと再診断コストがほぼ消える**
   (ECO-113 教訓 3「性能是正記録には残存計算量を明記」の運用実証)。
2. **面間非対称は逸脱側の同定から**: 通常の read-across(是正を他面へ展開)と逆に、本件は
   **正解が隣の面(WorkTab)と同一ファイル内(AddQuery)に既在**で ImageTab だけが逸脱していた。
   非対称を見つけたら「どちらが正か」をまず同定する — 既存 golden・契約(ECO-041)を持つ側が正。
   26 万件経路の是正はこれで 5 経路目(064 起動/062 検索/113 選択/114 モード遷移/115 パネル操作)=
   経路棚卸し様式(ECO-114 教訓 1・昇格候補)の N がさらに増えた。
