# ECO-096 — 階層定義保存後も画像タブの選択中ビューが旧グラフを保持する

- type: 不具合(タブ間の永続変更反映漏れ・選択中ビューのグラフ再構築漏れ)
- status: staged
- baseline: main 1e5eed2
- 起票日: 2026-07-16
- 報告者: maintainer
- 観測資料: `スクリーンショット 2026-07-16 030846.png`(配置タグの設定ダイアログ)

## 1. 症状

タグタブの階層ビュー定義で配置タグの展開モードを「定義値」にし、
「画像が 0 件の値ノードを隠す」をオンにして階層を保存する。画像タブがそのビューを既に
表示していた場合、画像タブへ戻っても 0 件ノードが残る。別ビューへ切り替えた後に元のビューへ
戻すと、初めて 0 件ノードが隠れる。

期待: 階層保存後に画像タブへ戻った時点で、選択中ビューにも保存済みの
`hide_empty_values=true` が反映され、追加のビュー切替操作なしで 0 件ノードが隠れる。

- 再現手順: ①画像タブで対象ビューを選ぶ ②タグタブで同ビューの配置タグ設定を開く
  ③定義値+「画像が 0 件の値ノードを隠す」を選んでダイアログを OK
  ④階層ビュー定義を保存 ⑤画像タブへ戻る。
- 観測日: 2026-07-16。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 健全 | `docs/screens/tag_tab.md`「配置タグ設定」は設定ダイアログの適用=保存を定義し、同「展開モード」は 0 件隠しを配置ノードの設定と定義する。`docs/screens/image_tab.md`「定義値展開の見え方」はオン時のみ 0 件ノードを非表示と明記。未確定事項 `TAG-*`/`FL-*`/`VE-*` に反映タイミングの保留なし |
| BOM | 健全 | REQ-096(`10-requirements.yaml`)は `hide_empty_values` をビュー階層ノード属性として宣言。CP-DEFEXP-086(`33-control-plan.yaml`)は「表示側が件数 0 の定義値ノードを隠す」を受入済み。E-UI-NODEGRAPH-025 系も階層保存と画像タブ消費を宣言済み |
| 実装 | **逸脱と確定** | 保存通知は `HierarchyEditorViewModel.SaveAsync`→`Saved`→`TagsTabViewModel.DataChanged`→`MainWindowViewModel._imagesTabStale` まで到達する。しかし画像タブ復帰時の `ReloadTagCatalogAsync()` はタグ台帳・ビュー一覧・画像タグを更新して `Recompute()` するだけで、選択中ビューの `_viewRoot` を再構築しない。`hide_empty_values` 判定は旧 `_viewRoot` を読むため旧表示が残る |

**結論**: CAD/BOM の意味論は確定済みで、選択中ビューのキャッシュ更新だけが欠けた
**実装逸脱**。gate①の裁定は不要。製品側 `/eco-fix eco-096` へ進める。

## 3. 切り分け済みの事実

### 確定(1e5eed2 時点のコード読解・履歴実測)

1. **保存イベントは欠落していない**: `HierarchyEditorViewModel.cs:457-467` は DB 保存成功後に
   `Saved` を発火し、`TagsTabViewModel.cs:80` が `DataChanged` へ中継する。
   `MainWindowViewModel.cs:62-63` は stale を立て、同 `:154-158` が画像タブ復帰時に
   `ReloadTagCatalogAsync()` を呼ぶ。
2. **旧グラフだけが残る**: `ImageTabViewModel.ReloadTagCatalogAsync`(`:470-482`)は
   `_tagById`・`_allViews`・画像タグ・`_entries` を更新するが、階層を取得せず `_viewRoot` を
   置換しない。最後の `Recompute()` は既存 `_viewRoot` を再評価するだけ。
3. **別ビュー往復で直る理由が一致**: 軸選択は `SelectAxis`→`LoadViewAsync`(`:1511-1544`)を通り、
   `GetHierarchyAsync`→`BuildDefinedIndexAsync`→`BuildGraph` で `_viewRoot` を新規構築する。
   このとき保存済み `hide_empty_values=true` が初めて取り込まれる。
4. **表示判定自体は健全**: `Recompute` の `ImageTabViewModel.cs:1158-1162` は
   `child.IsDefinedExpansion && child.HideEmptyValues && matched.Count == 0` を正しくスキップする。
   欠陥は判定式ではなく、その入力グラフが旧版のままなこと。
5. **混入と潜伏**: `ReloadTagCatalogAsync` は `e81a37a`(2026-07-01・ECO-022)で
   「タグ/ビュー永続変更の軽量再読込」として追加されたが、階層グラフ再構築を含めなかった。
   ECO-086(2026-07-14)で `hide_empty_values` が追加されても、CP-DEFEXP-086 は構築・保存往復・
   初回ロード時の表示を検査し、**既に選択中の同一ビューを保存後に再同期する経路**を検査しなかった。
6. **未確定事項との関係なし**: `TAG-*` はタグ定義 UI、`FL-*`/`VE-*` はファイル一覧・表示列の
   裁定であり、本件の「保存済み同一ビューを再読込するか」を保留する項目はない。

### 疑い(未検証・fix 時にプローブで確定)

- `hide_empty_values` 以外の階層属性(展開モード・条件・別名・ホーム・ノード追加/削除/並べ替え)も
  同じ旧 `_viewRoot` により保存後反映されない可能性が高い。read-across は probe で範囲を確定し、
  本 ECO の「階層定義の再同期」内で扱える同一真因だけを対象にする。
- 現在の階層ナビゲーション path を保持したまま新グラフへ再束縛する際、保存で削除されたノードを
  指す path は root/home への安全なフォールバックが必要。既存 `ReloadTagCatalogAsync` の
  「ナビ状態保持」コメントとの両立方法は fix 時にテストで確定する。

## 4. 是正方針(案・着手時にプローブで確定)

1. **プローブ先行(R5)**: 画像タブで定義値ノードを表示した状態を作り、DB 側の同一階層を
   `hide_empty_values=false→true` へ保存後、公開再読込経路を実行する。追加ビュー切替なしで
   0 件チップが消えることを期待し、是正前の不合格で真因を裏取りする。
2. **最小是正**: `ReloadTagCatalogAsync` が選択中の view 軸を検出した場合、保存済み階層・条件・
   定義値を再取得して新しいグラフへ再束縛してから `Recompute()` する。ビュー選択・コレクション・
   表示モード・画像選択は保持する。path は同一ノードが残る範囲で保持し、参照切れだけ安全に
   root/home へフォールバックする。
3. **read-across**: 同じ階層保存で変わる展開モード/条件/別名/ホーム/構造のうち、同一再構築経路で
   自然に直るものをプローブへ追加する。別真因・別サーフェスの所見は R3 に従い分離する。
4. **機械受入**: build 0、Tests 全緑、Oracle 既存固定行不変(R6)、`validate_bom.py` 0 error。

## 5. 影響 BOM

- src: `ImageTabViewModel.ReloadTagCatalogAsync` と、必要なら active view のグラフ再読込/path 再束縛を
  単一化する内部 helper。`NodeGraphBuilder` の展開意味論・DB スキーマは不変。
- tests: 保存前に見えていた 0 件定義値ノードが、階層保存+再読込後に追加ビュー切替なしで消える
  回帰 probe。ナビ/表示モード/選択保持と path 参照切れ fallback の境界を追加。
- CP: CP-DEFEXP-086 に「選択中ビューの階層保存後再同期」を追加。必要ならタブ間 stale 経路の
  既存 smoke 観点を強化。
- CAD: 変更なし(既存の保存結果と 0 件非表示の契約を実現する実装是正)。
- Oracle/DB/i18n: 変更なし見込み。既存固定 Oracle 行は変更しない(R6)。

## 6. 残ゲート

- gate①(裁定): **不要**(CAD/BOM 健全・実装逸脱)。
- 次工程: `/eco-fix eco-096` でプローブ先行の是正へ着手可能。
- gate②(golden): 是正後、maintainer 実機で本再現手順を再走し、画像タブへ戻った時点で
  0 件ノードが消えること、別ビュー往復が不要なこと、現在ビュー/表示モード等が維持されることを確認。
