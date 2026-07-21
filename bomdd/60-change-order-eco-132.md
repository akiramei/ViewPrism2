# ECO-132 — pending 裁定ダイアログの一覧が stale 行を保持する — plain List バインドで削除が UI へ伝播しない(superseded=won't-fix・R5 反証)

- 起票日: 2026-07-21
- 報告者: Codex review(6fa8834..HEAD・2026-07-21・P1①)
- 種別: 不具合(UI/VM 層・可変バインドリストの idiom 逸脱)
- baseline: ViewPrism2 main `2d71237`
- 関連: ECO-129(pending 裁定 UI の新設元=混入コミット `4deebc2`) / GF-129-01(同型=同一インスタンス通知は再評価されない)

---

## 1. 症状(所見)

Codex review 指摘(P1①): pending 裁定ダイアログ(PendingReviewWindow)の左ペイン一覧で、

- 2 件以上の pending が表示されているとき、1 件を裁定(受け入れる/別画像/削除)すると、
  VM は plain `List` から該当行を `Remove` するが、`ItemsControl` が**削除された行(裁定済み)を
  UI に保持し得る**。
- その stale 行を選択して再裁定すると、既に normal/deleted 化した画像を裁定しようとする
  (`PendingReviewService` の pending 限定ガードで**失敗メッセージ**になる=無害だが不可解な UX)。
- 「修復で再リンク…」から戻った後の `LoadAsync`(一覧の作り直し)も同型。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(pending_review.md PD-2〜4) | 対象外 | 視覚仕様の欠陥ではなく、可変一覧の通知配線の欠陥 |
| BOM(E-UI-PENDING-049) | 対象外 | surface 宣言は健全。受入観点(裁定後の一覧更新)が golden の谷間だった(§3-5) |
| 実装(PendingReviewViewModel/Window) | **欠陥(idiom 逸脱)** | `Items` が plain `List`(INotifyCollectionChanged 非実装)で `ItemsControl.ItemsSource` に直結。`Remove`/`Clear`+`OnPropertyChanged(nameof(Items))` は同一 List インスタンスを返すため、Avalonia の `ItemsSource` プロパティ設定が参照同値で no-op=コンテナ再構築が起きない |

**結論: UI/VM 層の実装欠陥(可変バインドリストの idiom 逸脱)。実装追随で是正。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **plain List バインド**: [PendingReviewViewModel.cs:84](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs)
   `public List<PendingItemVM> Items { get; } = [];`。
   [PendingReviewWindow.axaml:119](../src/ViewPrism2.App/Views/PendingReviewWindow.axaml)
   `<ItemsControl ItemsSource="{Binding Items}" …>`。
2. **同一インスタンス通知の 2 サイト**:
   - `LoadAsync`([:142-167](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs)): `Items.Clear()`+`Add()`+`OnPropertyChanged(nameof(Items))`。
   - `Complete`([:307-310](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs)): `Items.Remove(item)`+`OnPropertyChanged(nameof(Items))`。
   どちらも同一 List インスタンスを返す。`OpenRepairAsync`([:285-292](../src/ViewPrism2.App/ViewModels/PendingReviewViewModel.cs))も `LoadAsync` 経由で同型。
3. **コードベースの idiom は ObservableCollection**: 可変バインドリストは他 VM が全て `ObservableCollection<T>`
   を用いる — ImageTabViewModel.Items([:888](../src/ViewPrism2.App/ViewModels/ImageTabViewModel.cs))・
   TrashPopupItems([ImageTabTrashViewModel.cs:71](../src/ViewPrism2.App/ViewModels/ImageTabTrashViewModel.cs))・
   ImageTabSeedViewModel.Items([:285](../src/ViewPrism2.App/ViewModels/ImageTabSeedViewModel.cs))・ChipStrip 各種。
   **PendingReviewViewModel.Items だけが plain List = 唯一の逸脱**。
4. **混入コミット**: `4deebc2`(ECO-129 fix・pending 裁定 UI 新設時)。潜伏期間= ECO-129 fix 以降。

### 未検証(疑い)

- **stale 行の UI 残存の実測**(疑い・強): Avalonia 12.0.4 の `ItemsControl` が同一インスタンスの
  `ItemsSource` 再設定でコンテナを再構築しないことは強く疑われる(参照同値で property system が no-op)が、
  実挙動は fix 時の headless probe で確定する(GF-129-01 と同型の機序=同一インスタンス通知は再評価されない)。
- **単一件のマスキング**: 最後の 1 件を裁定すると `Items.Count==0`→`HasSelection` が false 化して
  裁定面ごと非表示(空状態へ)=stale 行が隠れる。**症状は 2 件以上のとき**に顕在化(Codex シナリオと一致)。
  golden が少数件で空状態遷移に隠れた可能性(§3-5 の谷間)。

## 4. 是正方針(案・着手時確定)

**案A(推奨・idiom 復帰)**: `Items` を `ObservableCollection<PendingItemVM>` へ変更し、`Remove`/`Clear`/`Add`
がコレクション変更通知を発する状態にする。`OnPropertyChanged(nameof(Items))` は不要化(または残置無害)。
`IsEmpty` は Count 依存のため `CollectionChanged`→手動 `OnPropertyChanged(nameof(IsEmpty))` の配線を確認。

diff 規模: PendingReviewViewModel の Items 型+通知配線・probe(裁定後に該当行が Items から消える/
2 件中 1 件裁定で残 1 件になる headless 検査)。視覚変更なし見込み。

## 5. 影響 BOM

- **src**: PendingReviewViewModel.cs(Items 型+通知)。axaml は不変見込み(ObservableCollection も ItemsSource 互換)。
- **tests**: 裁定後の Items 反映(2 件→1 件)+空状態遷移の VM probe。
- **CAD**: 視覚変更なし見込み。
- **CP**: CP 刻印は accept 時(CP-UI-G1= pending 裁定後の一覧更新観点を追加見込み)。

## 6. 残ゲート

- **gate①(裁定)**: 不要見込み(実装の idiom 逸脱=CAD 未定義ではない)。着手条件=なし。
- **gate②(golden)**: 是正後に提示(2 件以上の pending で 1 件裁定 → 一覧が即座に 1 件になる・実機)。

## 7. 起票時の申し送り

- **P2(ScanSummary の部分適用後 stale 再試行)は別 ECO(ECO-133)で分離起票**(Codex review 同一バッチの別欠陥)。
- 是正は src のため R8(独立レビュー)必須。視覚不変なら R7 は対象外宣言。

## 8. クローズ(2026-07-22・won't-fix=R5 反証)

- **R5 プローブで症状が再現せず**: `GfPendingReviewVisualParityTests.裁定後の一覧は該当行を残さない_複数件で1件裁定`
  を追加(headless 実 UI・PendingReviewWindow を実際に開く)。2 件 pending → 1 件裁定後、実現コンテナの
  **実表示ファイル名**が `b.jpg` のみ(裁定済み `a.jpg` の stale 行なし)。2 通りの検査(コンテナ数・
  表示ファイル名)で一致=**是正前から緑**。
- **診断の前提が否定**: §3 の「未検証(疑い)= stale 行の UI 残存」は成立しない。Codex の静的推論
  (「plain List + 同一インスタンス通知 → ItemsControl が削除行を保持」)は **WPF の挙動**であり、
  Avalonia 12.0.4 の `ItemsControl` は本経路(`List.Remove`/`Clear`+`OnPropertyChanged(nameof(Items))`)でも
  表示を正しく更新する。eco-fix の R5 規律「プローブが不合格にならない=診断が誤り。コードに触らない」に従い
  **src は無変更**。
- **idiom 逸脱は残置(バグではない)**: `PendingReviewViewModel.Items` が plain List なのは事実だが症状を
  起こしていない。ObservableCollection 化は品質/一貫性クリーンアップであってバグ修正ではないため、
  本 ECO では実施しない(必要なら別途 /simplify 等の品質改善で)。
- **処置= won't-fix でクローズ(maintainer 裁定 2026-07-22)**。追加した回帰ガードは**残す**
  (裁定後に該当行が UI から消える正しい挙動を pin=将来の通知配線変更に対する保護)。
- **教訓**: 静的解析由来の UI 所見(特に他フレームワーク〔WPF〕の挙動前提)は、是正前に必ず実 UI probe で
  再現裏取りする。フレームワーク差(WPF の ItemsControl 短絡 vs Avalonia の再評価)で偽陽性になる。
  R5 プローブ先行が「診断は正しいが的外れ」を出荷前に止めた実例(BomDD 昇格候補)。
