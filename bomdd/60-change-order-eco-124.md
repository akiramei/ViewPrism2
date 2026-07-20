# ECO-124 — 左ペイン(コレクション)開閉が母集合規模の再構築を通る — 26 万件ビューで 1〜2 秒(26 万件経路の 7 経路目)

- 起票日: 2026-07-21
- 報告者: maintainer 実機観測(画像タブ・ビュー画像 26 万件表示中の左ペイン開閉で 1〜2 秒)
- 種別: 不具合是正候補(性能・実装層。視覚/意味論不変)
- baseline: ViewPrism2 main `88904a3`

---

## 1. 症状

画像タブで左ペイン(コレクション)を開閉(276px⇄64px)すると、ビュー画像が 26 万件表示されている
状態で 1〜2 秒かかる。左ペイン開閉は表示幅の切替のみで、画像集合・選択・チップ・ソートの
いずれのデータ意味論も変えない操作である。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(image_tab.md layoutInvariant) | 健全 | 「左ペイン折り畳みで中央が破綻しない」= 幅の伸縮とレール可視のみを要請。母集合再構築を要請する記述なし |
| BOM | 健全(invariant 拡張のみ) | E-UI-BROWSE-022 の ECO-114 invariant(モード開始/終了は母集合パイプライン非通過)と同種の適用範囲追記が accept 時に必要 |
| 実装 | **逸脱確定** | `ImageTabViewModel.cs:1675` `ToggleSidebar() { _collapsed = !_collapsed; Recompute(); }` = 全件条件評価+チップ再計算+全件ソート+26 万 ImageItemVM 再構築+26 万 Add の母集合パイプラインへ無条件結合 |

**結論: 実装層の単独欠陥。裁定不要。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **結合サイト**: `ImageTabViewModel.cs:1675` — サイドバー状態(`_collapsed`)しか変えないのに
   `Recompute()` を呼ぶ。Collapsed/Expanded/SidebarWidth は算出プロパティで、必要なのは通知のみ。
2. **正解が隣の面に既在**(ECO-115 型・逸脱側= ImageTab): `WorkTabViewModel.cs:749`
   `ToggleSidebar() => Collapsed = !Collapsed;` = ObservableProperty+`NotifyPropertyChangedFor(SidebarWidth)`
   の**通知のみ**。作業タブの同操作は母集合を通らない。
3. **混入**: `6f7b4f9`(2026-06-18・M3a 初版)= ECO-113/114/115/118 と同根。潜伏約 1 ヶ月。
4. **マスキング**: 通常規模では体感不能+ECO-114(モード開始/終了)・ECO-115(パネル状態)の
   経路棚卸しは**モードコマンドとパネルサイトに限定**されており、サイドバー開閉は選択系モードで
   ないため両スコープ外= 「規模系 NFR は経路ごとに独立して破れる」(ECO-113 教訓)の 7 例目
   (064 起動/062 検索/113 選択/114 モード遷移/115 パネル操作/118 タグ付与/124 ペイン開閉)。
5. **ECO-110 との関係**: グリッドスクロールアンカーの適用範囲は**右ペイン開閉のみ**(サブ裁定 a=
   左ペイン折り畳みへの一般化は効果確認後の拡張裁定)。本 ECO はアンカー拡張ではなく
   Recompute 結合の除去= ECO-110 の裁定範囲に触れない。

### 未検証(疑い)

- 是正後の挙動差分: 現状は Recompute の Items 再構築でスクロール位置がリセットされている
  可能性が高い(ECO-114 の旧挙動と同型)。是正後は View 側の幅変化リフローのみ=
  スクロール位置が概ね維持される方向の変化(ECO-114 golden 承認済み挙動差分と同方向)。
  グリッド列数変化による内容シフトは ECO-110 左ペイン拡張裁定のスコープ外のまま。
- 1〜2 秒の内訳(条件評価/ソート/VM 構築/Add)は未計測 — 是正が結合除去のため内訳計測は不要見込み。

## 4. 是正方針(案・着手時確定)

**案A(推奨)**: `Recompute()` を通知のみへ置換(WorkTab と対称化= ECO-115 と同じ「正解既在側へ寄せる」)。
ImageTab は手書きプロパティのため `OnPropertyChanged(string.Empty)` 一括通知(ECO-114 の
ApplyModeTransition と同流儀・Items インスタンス不変なので再構築は起きない)か、
Collapsed/Expanded/SidebarWidth の個別通知 3 本。選択は fix 時(既存流儀との整合で確定)。

プローブ(ECO-114 様式): ToggleSidebar 往復で Items/Chips インスタンス同一性(是正前赤)。

diff 規模: src 1 行+probe。挙動不変(幅切替の視覚は従来どおり・速度のみ改善)。

## 5. 影響 BOM

- **src**: ImageTabViewModel.cs:1675(Recompute→通知のみ)
- **tests**: 構造 probe(ToggleSidebar 往復の Items/Chips インスタンス同一性= CpUiG1ModeTransitionTests
  へ追記見込み・是正前赤→緑転)。既存固定 Oracle 行は変更しない(R6)
- **ebom**: E-UI-BROWSE-022 invariant へ適用範囲追記(サイドバー開閉も母集合パイプライン非通過)
- **CP**: CP-UI-G1 刻印(accept 時・潜伏実績つき)

## 6. 残ゲート

- **gate①(裁定)**: 不要(実装層確定・WorkTab に正解既在・CAD/BOM 健全)
- **gate②(golden)**: 必要(26 万件実機= 開閉の体感+視覚不変+スクロール挙動差分の確認)

## 7. 停止点

裁定は不要です。`/eco-fix eco-124` で是正に着手できます。
