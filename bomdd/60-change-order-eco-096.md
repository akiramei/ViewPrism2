# ECO-096 — 階層定義保存後も画像タブの選択中ビューが旧グラフを保持する — (applied)

- type: 不具合(タブ間の永続変更反映漏れ・選択中ビューのグラフ再構築漏れ)
- status: applied
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
- `/eco-fix eco-096`: **完了**(§7)。
- gate②(golden): **合格**(2026-07-16 maintainer 実機)。本再現手順で、画像タブへ戻った時点で
  0 件ノードが消え、別ビュー往復が不要で、現在ビュー/階層位置/表示モードが維持されることを確認。

## 7. 実施記録(2026-07-16 /eco-fix)

### プローブ先行(R5)

既存 `CP-DEFEXP-086` の `CpDefExp086UiTests` に、次の実操作を 1 本追加した。

1. `hide_empty_values=false` の定義値ビューを画像タブで開き、「都道府県」へ潜る。
2. 0 件の「青森県」が表示中であることを前提確認。
3. DB の同一階層を `hide_empty_values=true` へ保存し、MainWindow の画像タブ復帰時と同じ
   `ReloadTagCatalogAsync()` だけを実行する(別ビュー切替は行わない)。
4. チップが「北海道・蝦夷」だけになることを期待する。

是正前は **1/1 不合格**: 期待 `北海道, 蝦夷` に対し実際は
`北海道, 青森県, 沖縄県, 蝦夷`。旧 `_viewRoot` を `Recompute()` しているという診断を実測裏取りした。

### 是正と裁定理由

`ImageTabViewModel` の view graph 構築を `ReloadViewGraphAsync` へ単一化し、次の2経路から共有した。

- 通常のビュー選択: 従来どおり保存済み home path から開始。
- ECO-096 の stale 再読込: タグ/ビュー/画像タグ再取得後、**選択中ビューの階層・条件・定義値を
  DB から再取得して graph を新規構築**。表示モード等は保持。

現在 path は旧 `GraphNode` 参照を持ち越さず、`HierarchyNodeId + NodeKind + Value` の論理同一性で
新 graph へ再束縛する。表示名を identity に含めないため、同じ保存で別名が変わっても現在位置を保ち、
パンくずは新しい別名へ追随する。構造変更で段が消えた場合は一致する最長 prefix まで縮退し、
先頭ノード自体が消えれば root へ戻る。選択中ビュー自体が削除済みなら FS root へ安全退避する。

これは `hide_empty_values` の判定に通知を足す対症療法ではなく、**軽量再読込が active view graph を
更新しない真因構造を消す**案。`NodeGraphBuilder`・DB・XAML・style・i18n・CAD は不変。
横断規約(ECO-080)は新規文言/サーフェスがないため追加適合事項なし。

### read-across と回帰固定

同一プローブで以下を追加固定した。

- `hide_empty_values false→true`: 別ビュー往復なしで 0 件チップが消える。
- 同時に別名 `都道府県→地域`: 現在 path を維持しつつパンくずが「地域」へ即追随。
- 現在 path の階層ノード削除: 旧 graph 参照を残さず root へ縮退し、view 軸自体は維持。
- headless 実描画: 保存後は「地域・北海道・蝦夷」が可視、「青森県・沖縄県」は不可視。

条件・展開モード・ホーム・並べ替えも同じ保存済み hierarchy/view 再取得を通るため同一真因として
再同期される。今回の走査で別真因・別サーフェスの所見はなく、R3 分離項目なし。

### 機械受入

- `dotnet build`: **0 warning / 0 error**。
- `dotnet test tests/ViewPrism2.Tests --no-build`: **751/751 合格**(既存750+新規probe1)。
- `dotnet test tests/ViewPrism2.Oracle --no-build`: **109 合格+既知2 skip**。既存固定行・期待値不変(R6)。
- `python bomdd/validate_bom.py`: 0 error / 0 warning(記録・status 更新後に再確認)。

### セルフゴールデン(R7)

検査面は画像タブの view 軸チップ+パンくず(CAD `image_tab.md` VC-IMG-6)。
headless 実描画を CAD 契約「0 件の値ノードは設定オン時のみ非表示」と並置した。

| 差分 | 分類 | 結果 |
|---|---|---|
| 保存後も青森県/沖縄県(0件)が残る | 転写漏れ(旧 graph) | probe 赤→是正後は不可視 |
| 北海道(1件)・蝦夷(未定義値1件)は残る | CAD 一致 | 可視 |
| 同時保存した別名「地域」 | 既存の別名契約 | パンくずへ追随 |
| XAML/style/色/余白/文言 | 視覚不変 | diff 0(変更ファイルなし・既存 ChipVM/View を同じ Recompute で描画) |

**転写漏れ 0**。ダイアログ共通言語は未変更のため適用面マトリクス read-across 対象なし。

## 8. クローズ(2026-07-16 /eco-accept)

maintainer が実機で gate②を承認した。タグタブで選択中ビューの配置タグ設定を
`hide_empty_values=true` へ保存し、画像タブへ戻るだけで 0 件ノードが即時に消えること、
別ビューへの往復が不要なこと、現在ビュー/階層位置/表示モードが維持されることを確認した。

再発防止は `CP-DEFEXP-086` に、永続変更後の軽量再読込が台帳だけでなく選択中ビューの
派生 graph まで再構築する観点として刻印した。回帰 probe は同一ビューの
`hide_empty_values false→true`、別名への path 再束縛、削除済み path の root 縮退、
headless 実描画での 0 件ラベル不在を固定する。

**教訓**: 永続データの一部だけを差し替える「軽量再読込」は、そこから導出した graph/cache を
旧参照のまま残すと、保存通知が正しく届いていても画面は旧状態を表示し続ける。更新境界では
一次台帳と派生成果物を一つの整合単位として再構築し、ナビゲーションはオブジェクト参照でなく
論理 identity へ再束縛する。この一般形は ECO-038 の「通知リストではなく真因構造を消す」教訓の
read-across であり、個別フラグへの通知追加ではなく再読込経路の単一化を選んだ。

M4 同期は不要。変更は `ImageTabViewModel` 内部の stale graph 是正と既存 CP の回帰強化に閉じ、
spec §2.6、E-BOM、M-BOM、DSBOM の as-built 契約に変更はない。R3 の残課題もない。
