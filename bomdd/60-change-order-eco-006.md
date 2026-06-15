# Change Order — ECO-006(画像タブ NodeGraph ナビの空階層 表示契約)

> BL-001(画像タブ左ペイン「階層構造」でビュー名と「階層が定義されていません」が重なる)の是正。
> **帰属: spec_omission** — 画像タブ NodeGraph ナビで「子ノードの無い(ルートのみの)ビュー」をどう表示するかの**表示/空状態契約が仕様に欠落**していた。実装はタグタブの空文言に倣って未規定のプレースホルダ(`nodeGraph.empty`)を足し、ルートノードと z-order で**重ねて**しまった。GF-V4-04 と同じ「表示・空状態契約の脱漏」クラス。
> 是正は v1.3 method どおり **仕様→E-BOM→M-BOM→Control Plan を同期 → fresh factory 再製造**(直接修正しない)。

## 0. 変更前 baseline
- As-Built: commit c035e59(ECO-003/004/005 適用後)。固定オラクル `tag:loop-v4-r1`(S-01〜S-31)不変。
- 本 ECO は**表示のみ**(状態遷移・スキーマ・永続データ変更なし)。固定オラクル追加行なし(表示は golden + VM 不変条件で担保)。

## 1. 根本原因(コードで確定)
`src/ViewPrism2.App/ViewModels/MainWindowViewModel.cs:533-537`:
```csharp
TreeRoots.Clear();
var rootVm = new GraphNodeViewModel(result.Root, null, view.Name);  // ルート=ビュー名("My View")
TreeRoots.Add(rootVm);                                               // TreeRoots は常にルート1件を持つ
IsTreeEmpty = rootVm.Children.Count == 0;                            // ルートに子が無ければ true
```
`src/ViewPrism2.App/Views/MainWindow.axaml:202-214`:
```xml
<Panel>
  <TreeView x:Name="NodeTree" ItemsSource="{Binding TreeRoots}" .../>     <!-- 常にルートを表示 -->
  <TextBlock Text="{Binding Loc[nodeGraph.empty]}" IsVisible="{Binding IsTreeEmpty}" .../>  <!-- 重なる -->
</Panel>
```
`<Panel>` が TreeView(ルート常在)と空プレースホルダ(`IsTreeEmpty`=ルート子無し)を z-order で重ねるため、子ノードの無いビューでは**ルート「My View」とプレースホルダが同時表示=重なる**。`IsTreeEmpty`(空状態)と TreeView 表示が**意味的に排他でない**のが原因。

## 2. 帰属(5分類)
- **spec_omission**: §2.6 空状態の規則は「grid 0件画像」「NodeGraph 選択ノード結果0件→grid空状態」「タグタブ階層ペインの空」しか規定せず、**画像タブ NodeGraph ナビの「ルートのみ(子ノード無し)ビュー」表示を規定していない**。`nodeGraph.empty` プレースホルダは仕様にトレースされない実装追加。
- 注: この画像タブナビは原典に対する**意図的差分**(§2.6「原典はフォルダ風表示+パンくずだが本実装は左ペインのツリー…golden で承認判定」)のため、原典パリティではなく**設計者が表示契約を確定**する。

## 3. 表示契約(spec 決定 — 製造パッケージの中核)
**画像タブ NodeGraph ナビは選択ビューの階層ツリーを表示し、ルートを常に表示する(REQ-036 ルート=無条件)。ツリーと空状態プレースホルダは排他とし、同時表示しない。子ノードの無い(ルートのみの)ビューは ルートノードのみを表示し、「階層が定義されていません」等のプレースホルダを画像タブナビでは出さない(ルートと重ねない)。** 空状態は §2.6 既存規則(grid 0件・ビュー未選択)で表現する。
- 根拠: ルートは無条件で選択可=ビューのフィルタ集合(home_tag/条件あり時は全画像とは異なる)を見られる**意味のあるノード**なので、隠さず常に見せる。プレースホルダはルートと矛盾するため画像タブナビには置かない。

## 4. BOM 改訂(同期)
- 仕様: 20-spec.md §2.6 空状態の規則に「画像タブ NodeGraph ナビ」行を追加(§3 の契約)。
- E-BOM: E-UI-NODEGRAPH-025 に invariant(画像タブナビ=ルート常在・ツリーとプレースホルダ非重ね)。
- M-BOM: M-UI-013 interface_contract に画像タブナビ空階層の表示契約。
- Control Plan: CP-UI-G1(golden)に「画像タブナビでビュー名と空文言が重ならない」視覚項目を追加。VM 不変(TreeRoots は選択ビューでルート1件を常に持つ)を CP-UI-G1 の補助に。
- 固定オラクル: 追加なし(表示は論理オラクル対象外)。S-01〜S-31 不変。
- bom_rev: v4.0 → v4.0(eco:ECO-006)。

## 5. 部分再製造(隔離工場・spec-first)
- 製造パッケージ: 本 ECO-006(§3 契約)+ 改訂 BOM + 既存 src。非開示: 原典 view-prism / tests/ViewPrism2.Oracle / 41。
- 改修: MainWindow.axaml:202-214 の `<Panel>` から空プレースホルダの重ねを解消(契約=ルート常在・プレースホルダ非表示)。`MainWindowViewModel` の遷移・評価ロジックは不変。

## 6. 受入
- 回帰: 既存 Tests + Oracle(S-01〜S-31)不変(表示のみ・挙動不変)。
- golden: 画像タブで階層なしビューを選択 → ビュー名と空文言が重ならない(ルートのみ表示)。CP-UI-G1 再確認。
- 完了で BL-001 を closed にする。
