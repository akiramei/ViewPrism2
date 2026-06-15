# Backlog(後続対応・TODO)

> golden ウォークスルー等で発見した、**現ループ/ECO のスコープ外**の不具合・改善を記録する。
> 各項目は帰属(原因の所在)・場所(file:line)・重大度・スコープ(既存/新規)・発見経緯を持つ。
> 着手時は ECO もしくは次ループへ昇格する。

| ID | 区分 | 内容 | 重大度 | 状態 |
|----|------|------|--------|------|
| BL-001 | 既存不具合(表示) | 画像タブ左ペイン「階層構造」でビュー名と空状態が重なる | low〜med | **closed(ECO-006・2026-06-15)** |

---

## BL-001 — 階層構造ナビでビュー名と空状態プレースホルダが重なる

- **発見**: 2026-06-15・ECO-003/004/005 の golden 再ウォークスルー中に maintainer が発見(My View 選択時)。
- **現象**: 画像タブ左ペインの「階層構造」セクションで、選択中ビュー名(例「My View」)と空状態文言「階層が定義されていません」(`nodeGraph.empty`)が**重なって表示**される。
- **帰属**: **spec_omission ではなく既存実装の表示不具合(V1/V2 ナビ)**。ECO-003/004/005 とは**無関係**(MainWindow ナビ・`MainWindowViewModel`・`HierarchyEditorViewModel` はいずれも当該 ECO で未変更=git diff で確認済み)。
- **場所**: `src/ViewPrism2.App/Views/MainWindow.axaml:202-214`。`<Panel>` が
  `TreeView#NodeTree`(`ItemsSource={Binding TreeRoots}`)と
  `TextBlock`(`{Binding Loc[nodeGraph.empty]}` / `IsVisible={Binding IsTreeEmpty}`)を
  **z-order で重ねている**。階層を持たないビューを選択した状態で TreeView 内容(またはルート)と空プレースホルダが同時に可視になり重なる(=`IsTreeEmpty` と TreeView 表示が排他でない)。
- **修正案(着手時)**:
  - TreeView 側にも `IsVisible="{Binding !IsTreeEmpty}"` を付け、空状態と排他にする。または
  - `Panel` をやめて空状態は TreeView の中身が無いときだけ別行に出す(重ね合わせをやめる)。
  - あわせて `IsTreeEmpty` が「ビューは選択済みだが階層が空」を正しく表すか `MainWindowViewModel.TreeRoots`/`IsTreeEmpty` の算出を確認する。
- **重大度**: low〜med(視覚のみ・機能影響なし。ただし「階層未定義」の誤読を招く)。
- **再現**: 階層を保存していないお気に入りビュー(home_tag/条件/階層が空)を選択 → 階層構造ナビで重なりが出る。
- **是正(closed・2026-06-15・ECO-006)**: 帰属=**spec_omission**(画像タブ NodeGraph ナビの「ルートのみ(子ノード無し)ビュー」表示契約が §2.6 空状態規則に欠落・`nodeGraph.empty` プレースホルダは未トレースの実装追加)。**仕様 §2.6 に表示契約を追加**(ルート常在・ツリーとプレースホルダ排他・画像タブナビにプレースホルダを出さない)→ E-UI-NODEGRAPH-025 invariant / M-UI-013 nav_empty / CP-UI-G1 を同期 → 隔離工場(factory)が contract から製造(MainWindow.axaml の重ねを撤去・未参照化した `IsTreeEmpty` も除去・`MainWindowViewModel` の遷移/評価は不変)。受入: Tests 395/Oracle 74 PASS+2 skip 回帰ゼロ。**golden CP-UI-G1 承認(2026-06-15・maintainer 実機=重なり解消)**。詳細 60-change-order-eco-006.md。
