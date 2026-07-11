# Change Order — ECO-068(applied): 作業タブの画像ダブルクリックでビューアーが起動しない

> maintainer実機報告(2026-07-11)を受け、`/eco-file`で工程診断した既存機能の欠陥是正。

## 1. 症状・要求(maintainer報告・2026-07-11)

- 作業タブの中央画像一覧で画像をダブルクリックしても、画像ビューアーが起動しない。
- 期待結果: 閲覧モードで画像をダブルクリックすると、現在の作業スペースで絞り込み・ソート後の表示順を
  ビューアーへ渡し、ダブルクリックした画像を開始位置として起動する。
- 報告者: maintainer。
- 再現手順: 作業タブで画像を含む作業スペースを選択し、グリッドまたはリストの画像を閲覧モードで
  ダブルクリックする。観測結果は無反応。画像タブの同操作ではビューアーが起動する。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **正典は健全・追随漏れ1件** | `docs/screens/work_tab.md` L14は中央ブラウズを画像タブと「同一部品・同一意味論」、L83はグリッド/リスト行を同一部品と定義。`image_tab.md` L47/L580は閲覧モードのダブルクリックで表示順ビューアー起動を明記する。一方、`docs/review_points.md` IMG-004の「通常クリック想定・モック外」は古いままで正典screen仕様と矛盾するため、同一要求の文書追随漏れとして同期対象にする。操作選択の新規裁定は不要。 |
| 要求・仕様 | **健全** | REQ-041はグリッドのダブルクリックでREQ-044ビューアーを起動し、タグ編集モード中は無効と明記。REQ-044/`20-spec.md` §2.6は呼出元一覧の整列結果と開始位置を含むビューアー契約を定義する。作業タブE-UI-WORKSPACE-043はREQ-041を参照し、中央ブラウズをE-UI-BROWSE-022へ依存させる。 |
| M-BOM・検査 | **部分欠測** | M-UI-WORKSPACE-029は作業タブ中央grid/listを製造対象にするが、interface contractにビューアー起動経路を明記していない。CP-UI-G1も作業タブの多数のread-acrossを持つ一方、ダブルクリック起動を作業タブ固有観点として固定していない。CpUiG1WorkTabTestsのStubWindowService.ShowViewerは空実装で呼出しを検査せず、DoubleClickDetectorTestsは共有検出器単体のみ。 |
| 実装 | **欠陥** | ImageTabViewは`DoubleClickDetector`と`ClickCount`を併用し`isDoubleClick`をVMへ渡し、閲覧モードで`ShowViewer`を呼ぶ。WorkTabViewは修飾キーだけを読み`HandleItemClick(item, ctrl, shift)`へ渡すためダブルクリック情報を捨てる。WorkTabViewModelの同メソッドも整理/編集/作業/削除の選択分岐しかなく、閲覧モードは無処理。`IWindowService`は注入済みだがShowViewerには未使用。 |

工程帰属は、**作業タブsurfaceの実装転写漏れ + M-BOM/CPの作業タブ固有検査欠測**である。
CADの操作契約とREQは既に確定しているためgate①裁定は不要。

## 3. 切り分け済みの事実

### 3.1 確定

- 混入コミットは`f211fa9`(2026-06-29、ECO-020/021作業タブ導入)。`git blame`で
  `WorkTabView.OnItemPressed`と`WorkTabViewModel.HandleItemClick`が同コミットから現在まで
  ダブルクリック引数なしであることを確認した。後退ではなく**導入時からの潜伏**。
- ImageTab側は`ImageTabView.axaml.cs` L15/L102-103で共有`DoubleClickDetector`+OSダブルクリック時間+
  `ClickCount`を使用し、`ImageTabViewModel.cs` L1640以降で閲覧モードだけ`OpenViewer`へ分岐する。
- WorkTab側は`WorkTabView.axaml.cs` L40-47で全ポインタ押下を単クリックとして転送し、
  `WorkTabViewModel.cs` L732-740は閲覧モードで何もしない。
- WorkTabViewModelには`IWindowService _windows`が既に注入されるが、現利用は完全削除確認のみで、
  `ShowViewer`呼出しは0件。DB/Core/ViewerWindowの新機能は不要。
- 作業タブの表示母集合は`Recompute()`でworkspace normal画像→タグ絞り込み→ImageSorterの順に確定し、
  `Items`へ反映される。ビューアーへ渡す一覧もこの可視順と一致させる必要がある。

### 3.2 疑い・未検証

- 実装後にグリッドとリストの両方でAvaloniaの`ClickCount`が同様に届くかは未実測。
  ImageTabと同じ共有検出器を配線し、headlessまたは実機goldenで両surfaceを確認する。
- タグ絞り込み中・降順ソート中の開始index/前後移動が可視順と一致するかは未実測。
  `/eco-fix`の先行probeで固定する。

### 3.3 未確定事項との関係

- IMG-004の古い記述は正典`docs/screens/image_tab.md`とREQ-041に追随していない文書不整合であり、
  機能自体の未確定を意味しない。maintainerの本要求も既存のダブルクリック契約と一致する。
- FL-001/FL-002/FL-004(列・ソート・表示形式の共有/永続)とは無関係。ビューアーへ渡すのは
  その時点の可視順であり、永続範囲の裁定を変更しない。

## 4. 是正方針(案 — `/eco-fix`着手時にプローブで確定)

1. `WorkTabView`へImageTabと同じ`DoubleClickDetector`、OSダブルクリック時間、
   `ClickCount >= 2 || detector`の転送を追加する。Ctrl/Shift付きクリックはダブル判定対象外。
2. `WorkTabViewModel.HandleItemClick`へ`isDoubleClick=false`を追加し、整理/編集/作業/削除の既存意味論を
   優先したまま、通常閲覧モードのダブルクリックだけ`OpenViewer`へ送る。
3. 現workspaceのnormal画像をタグ絞り込み→現在sortした`ImageEntry`列へ変換し、クリック画像のindexで
   `_windows.ShowViewer`を呼ぶ。単クリック、空/消失id、モード中ダブルクリックは起動しない。
4. R5先行probe: 記録可能なWindowService spyを用い、通常ダブルクリック=1回、単クリック=0回、
   タグ絞り込み+降順sortのordered/startIndex exact、各文脈モード=0回を先に不合格化する。
   View配線はClickCount経路または共有検出器使用をheadless/構造検査で固定する。
5. M-UI-WORKSPACE-029、CP-UI-G1、CAD IMG-004を同じ既存契約へ同期する。

## 5. 影響BOM

- CAD: `ViewPrismUI/docs/review_points.md` IMG-004追随同期。`docs/screens/image_tab.md`、
  `docs/screens/work_tab.md`の意味変更なし。
- 要求/仕様: REQ-041/REQ-044、`20-spec.md` §2.6は参照のみで意味変更なし。
- E-BOM: E-UI-WORKSPACE-043/E-UI-BROWSE-022は健全。新elementなし。
- M-BOM: M-UI-WORKSPACE-029のinterface contractへ作業タブviewer起動経路を明記。
- 実装: `WorkTabView.axaml.cs`、`WorkTabViewModel.cs`。Viewer/Core/DB/schema/i18nは不変予測。
- 検査: `CpUiG1WorkTabTests`、必要ならWorkTab headless配線probe、CP-UI-G1 golden。
- オラクル: 既存固定Oracle行は変更しない(R6)。Viewer自体の意味論は既存のため新Oracle不要予測。

## 6. 残ゲート

1. gate①: **不要**。操作はCAD正典+REQ-041/044で確定済み。
2. ~~`/eco-fix ECO-068`: 先行赤probe→最小是正→M-BOM/CP/CAD追随同期→機械受入。~~ → 完了(§7)
3. gate② golden: 作業タブのグリッド/リスト双方で閲覧時ダブルクリック起動、クリック画像から開始、
   前後移動が現在の絞り込み・ソート表示順と一致。単クリックと各文脈モードでは起動しない。
4. `/eco-accept ECO-068`: CP潜伏履歴・As-Built・registerをクローズする。

## 7. 実施記録(2026-07-11 — 機械受入完了・golden待ち)

### 7.1 先行probe(R5)

- `CpUiG1WorkTabTests`のWindowService stubを記録spyへ変更し、次の2件を製品コード変更前に追加した。
  1. 閲覧single click=0回、tag絞り込み`a,b`+名前降順`b,a`で`a`をdouble clickすると
     `ShowViewer([b,a], startIndex=1)`が1回。
  2. タグ編集/作業/整理/削除モード中のdouble clickはShowViewer 0回。
- 是正前実測は`CS1739`: `WorkTabViewModel.HandleItemClick`に`isDoubleClick`引数が存在せず6箇所でcompile fail。
  起票時診断どおり、VM契約とView転送の欠落を確認してから製品コードへ着手した。

### 7.2 是正裁定とdiff

- `WorkTabView.axaml.cs`: ImageTab同型の`ClickCount >= 2 || DoubleClickDetector`を配線し、Ctrl/Shift時は
  検出状態をresetする。Windows OSのdouble-click時間を使用する。
- `WorkTabViewModel.cs`: `HandleItemClick(..., isDoubleClick=false)`を追加。既存の整理/編集/作業/削除分岐を
  先に維持し、通常閲覧のdouble clickだけ`OpenViewer`へ送る。
- 表示とviewer順の二重実装を避けるため、workspace normal→tag絞り込み→current sortを
  `VisibleImagesInDisplayOrder()`へ集約し、`Recompute()`と`OpenViewer()`で共用する。Viewer入力は
  `ImageEntry(record, absolutePath, EvalTagValue[])`、対象idが可視列にない場合は起動しない。
- M-UI-WORKSPACE-029へviewer launch契約、CP-UI-G1へ潜伏履歴+grid/list+ordered/startIndex+各mode非起動を追加。
- CAD IMG-004の古い仮置きを正典screen契約へ同期し、ViewPrismUI commit=`0e13056`。
  画像/作業タブとも通常閲覧double click、single click/文脈mode非起動としてlive specもdone化した。
- DB/Core/ViewerWindow/schema/i18n/既存Oracle期待値は変更していない。

### 7.3 機械受入

- 先行probeを含む`ViewPrism2.Tests`: **605/605 pass**(filter指定はMTPで無視されたため全件実行)。
- `dotnet build ViewPrism2.sln --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Oracle`: **109 pass / 2 known skip**。既存固定期待値変更なし(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。
- `git diff --check`: clean。

### 7.4 gate②操作

1. 作業タブで画像が複数あるworkspaceを開き、grid画像をsingle clickして無反応、double clickしてviewer起動。
2. クリックした画像が開始画像で、前後移動が現在の表示順と一致する。
3. tag chipで絞り込み、名前/日付/サイズのsort方向を変更して再度起動し、絞り込み外画像がviewer列へ
   入らず、前後移動が画面の順と一致する。
4. listへ切替えて同じ起動/開始/順序を確認する。
5. タグ編集・作業・整理・削除の各modeではdouble clickしてもviewerが開かず、従来の選択/
   マージ先・整理対象割当が維持される。mode終了後は再びdouble click起動する。
6. 画像タブのgrid/list double click、viewerのnormal/scroll/spread、閉じる/前後移動に回帰がない。

## 8. gate②合格・クローズ(2026-07-11)

maintainerが`/eco-accept ECO-068`で§7.4の実機goldenを承認した。作業タブのgrid/list双方で、
single clickは無反応、通常閲覧double clickはクリック画像からviewerを起動し、tag絞り込みと
名前/日付/サイズsort後もviewer列・開始位置・前後移動が画面の可視順と一致することを確認した。
タグ編集/作業/整理/削除mode中はviewerを起動せず既存の選択/マージ割当を維持し、mode終了後は
再び起動できる。画像タブgrid/listとviewer normal/scroll/spread/閉じる/前後移動にも回帰なし。

再発防止はCP-UI-G1の潜伏履歴+golden観点、CpUiG1WorkTabTestsのShowViewer spyによる
ordered/startIndex/非起動mode exact、M-UI-WORKSPACE-029のviewer_launch契約、50-as-builtの承認証拠へ固定した。
CADの古いIMG-004はViewPrismUI `0e13056`でscreen正典へ同期済み。M4はfix時にM-BOM/CAD/CPまで同期し、
REQ-041/044・仕様§2.6・E-UI-WORKSPACE-043は既存契約どおり、Design System BOMは新部品なしのため追加改訂不要。

教訓: 「同一部品・同一意味論」の再利用宣言があっても、surfaceを複製実装するとイベント→VM→外部サービスの
接続は自動では継承されない。末端部品や検出器の単体テストだけでは未配線を検出できず、呼出しを捨てる空stubは
検査の沈黙点になる。read-acrossでは見た目の同等性だけでなく、provider/consumer境界を記録spyで観測し、
入力列・開始位置・禁止modeまで両surfaceに同じconformance契約を適用する。これはECO-038の作業タブ転写漏れを、
プロパティ通知からイベント配線へ拡張したread-acrossである。

残課題なし。共通surfaceへのDRY統合は本欠陥の必要条件ではなく、別の実測された保守性要求がない限り行わない。
