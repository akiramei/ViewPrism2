# Change Order — ECO-068(staged): 作業タブの画像ダブルクリックでビューアーが起動しない

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
2. `/eco-fix ECO-068`: 先行赤probe→最小是正→M-BOM/CP/CAD追随同期→機械受入。
3. gate② golden: 作業タブのグリッド/リスト双方で閲覧時ダブルクリック起動、クリック画像から開始、
   前後移動が現在の絞り込み・ソート表示順と一致。単クリックと各文脈モードでは起動しない。
4. `/eco-accept ECO-068`: CP潜伏履歴・As-Built・registerをクローズする。
