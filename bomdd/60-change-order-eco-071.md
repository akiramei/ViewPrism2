# Change Order — ECO-071(applied / golden approved): ビューアーの単一・見開きモードでホイール送りを追加する

> maintainer要求(2026-07-12)「ビューアーにて単一や右開き・左開きでも、マウスホイールで画像を
> 次へ・前へ切り替えたい」を`/eco-file`で受理した既存機能拡張要求。

## 1. 症状・要求(maintainer報告・2026-07-12)

- 縦スクロールモードではマウスホイールで連続画像を閲覧できるが、単一・右開き・左開きでは
  ホイールを回しても次/前へ移動しない。
- 期待は、ホイール下方向で論理的な「次へ」、上方向で「前へ」を操作できること。
- 再現: 画像を複数含む一覧からviewerを開き、単一/右開き/左開きへ切り替えてキャンバス上でホイール操作する。
- **初回golden不合格所見**: 単一Width/Originalで画像下端からwheel Nextし、次画像の上端からwheel Prevすると、
  前画像の読み終えた末尾ではなく先頭へ着地した。連続した長い画像をwheelで往復する操作として不連続である。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **ホイール契約が未定義・gate①必要** | `docs/screens/image_viewer.md`のinteractionは前/次button、seek、余白click、PageUp/Down/矢印だけ。wheelを規定しない。単一のfit=width/originalには内部ScrollViewer相当のpanがあり、ページ送りとの優先順位も未定義。 |
| 要求・仕様 | **新機能、既存要求はキー/clickのみ** | REQ-044はnormalの→/PageDown=次、←/PageUp=前。REQ-057はspreadの矢印反転・論理Pageキー・半面clickを定義するがwheelなし。仕様§2.9も同じ。scroll(REQ-055)はwheelによる自然な連続scrollと競合し得るためmode境界が必要。 |
| M-BOM・検査 | **明示的な非搭載を変更する必要** | M-UI-018 `excluded`は「ホイールでのページ送り — normal/spreadのホイールは何もしない」を明示し、初版凍結`415ceca`(2026-06-13)から意図的非搭載。CP-UI-G8/CpUiG4ViewerTestsはkey/click/Next/Prevだけでwheel provider→consumer配線を検査しない。 |
| 実装 | **現仕様どおりで逸脱なし** | `ViewerWindow`はKeyDown/Up、normal背景click、spread半面click、scroll位置追跡を配線するがPointerWheelChanged handlerなし。normal width/originalの`NormalScroll`、scroll modeの`ScrollList`、設定drawerは各ScrollViewerがwheelを消費する。VMのNext/Prevとspread/tag-control送り計算は再利用可能。 |

## 3. 切り分け済みの事実

### 3.1 確定

- これは不具合修正ではなく、初期V2で明示的に除外した入力方式の追加である。
- logical Next/Prevは既に端停止、normal=1画像、spread=pageTurnModeまたはSHIFTによるstep、
  tag-control ON時のplan navigationを一元処理する。wheel独自の送り計算は不要。
- spread-rightの矢印は空間方向のため反転するが、PageDown/PageUpは全modeで論理方向を維持する。
  wheelも論理方向に合わせれば、開き方向による反転は不要。
- normal Fitは内部scrollなし。normal Width/Originalは`NormalScroll`内で画像panが必要になり得る。
- scroll mode、設定drawer、タグ制御mapping modalはwheel自体がcontent scroll操作であり、
  viewer page turnに横取りすると既存操作を壊す。
- Ctrl+wheel zoomもM-UI-018で非搭載。今回zoomを同時追加しない。

### 3.2 疑い・未検証

- AvaloniaのPointerWheelChangedがNormalScroll内部でhandledになる境界と、headlessでwheel routingを
  どこまで実イベント検査できるかは`/eco-fix`のprobeで確定する。
- 高分解能touchpadの連続deltaを1 gesture=1 turnへ抑制すべきかは実機体感が必要。方式を固定する場合は
  goldenで飛び越しがないことを確認する。

### 3.3 未確定事項との関係

- **REQ-055**: scroll modeは従来どおり連続scroll。page turnへ変更しない。
- **REQ-057**: spreadのstep、空白開始、右/左開き、SHIFT、tag-control planは既存Next/Prevへ委譲し不変。
- **REQ-058/059**: fit/resize設定と永続化は不変。wheel動作の設定キーは追加しない。
- Ctrl+wheel zoom、Home/End、mode数字キーは引き続き非搭載。

## 4. 是正方針候補(gate①)

### 案A — 内部scroll境界を尊重するcontext-aware送り(推奨)

- viewer canvas上でvertical wheel下(`Delta.Y<0`)=logical Next、上(`Delta.Y>0`)=logical Prev。
- normal Fitとspread-right/leftは常にpage turn。端では既存規則どおり停止。
- normal Width/Originalは、まず`NormalScroll`の画像panを優先する。縦方向の端に既にいる状態で、
  さらに同方向へwheelした**次のevent**だけpage turnする(端へ到達させたeventでは送らない)。
- scroll mode、設定drawer、mapping modalは既存content scrollのまま。horizontal deltaは無視。
- page turnは既存Next/Prevだけを呼び、spreadのpageTurnMode/SHIFT/空白開始/tag-control planを維持。
- 規模: 中。CAD/REQ/BOM、ViewerWindow routing+境界判定、VM/provider配線test+headless、golden。
- golden: 4mode、normal fit3種、drawer/modal、右左開き、single/double page、SHIFT、端、touchpad体感。

### 案B — normal/spreadでは常にwheelをpage turnへ専有

- normalのFit/Width/Originalとspreadで、wheelを常にNext/Prevへ送る。
- 規模: 小。境界判定不要。
- 問題: Width/Originalの大きな画像をwheelで上下panできず、scrollbar dragが必要になる。既存操作を後退させる。

### 案C — normal Fitとspreadだけpage turn、Width/Originalは従来どおり

- 内部scrollを持たないsurfaceだけwheel送りを追加する。
- 規模: 小。既存panとの衝突なし。
- 問題: 同じ「単一」modeでもfit設定によってwheel送りの有無が変わり、maintainer要求を部分的にしか満たさない。

## 5. 影響BOM(案A採用時)

- CAD: ViewPrismUI `image_viewer.md` interaction/state、`review_points.md`にwheel裁定を追加。
- 要求/仕様: REQ-044/054/057、`20-spec.md` §2.9へmode別wheel優先順位を追加。
- E-BOM: E-UI-VIEWER-024。CoreのE-VIEWER-CALC-030/Next/Prev計算は不変。
- M-BOM: M-UI-018の`excluded`からwheelを外し、provider routing/NormalScroll境界契約を追加。
- 実装: `ViewerWindow.axaml(.cs)`中心。必要ならViewerViewModelに描画非依存のwheel action判定。
- 検査: CP-UI-G8、`CpUiG4ViewerTests`または新規headless surface test。scroll/drawer/modal非横取りもpin。
- Oracle: 既存固定Oracle行は変更しない(R6)。DB/schema/i18n/設定永続モデルは不変予測。

## 6. 残ゲート

1. ~~**gate① ViewPrismUI裁定**: 案A/B/Cから選択。推奨は案A。~~ → 案A採用・完了(§7)
2. ~~CAD裁定コミットを製品へ取り込んだ後、`/eco-fix ECO-071`で先行赤probe→是正→機械受入。~~ → 初回完了(§8)、golden補正完了(§9)
3. ~~gate② golden再確認: 4modeとnormal fit3種、内部scroll/着地点/overlay、spread送り規則、端/入力体感を確認。~~ → 合格(§10)
4. ~~`/eco-accept ECO-071`でCP/As-Built/register/教訓をクローズ。~~ → 完了(§10)

## 7. gate①裁定(2026-07-12)

- maintainer裁定: **案A=内部scroll境界を尊重するcontext-aware送りを採用**。
- viewer canvasのvertical wheelは下(`Delta.Y<0`)=logical Next、上(`Delta.Y>0`)=logical Prev。
  horizontal wheelは無視し、開き方向では反転しない。
- normal Fitとspread-right/leftは常にpage turn。端では既存Next/Prevどおり停止する。
- normal Width/Originalは画像内panを優先し、現在方向へまだpan可能なら送らない。既に端にいる状態で
  さらに外向きへ回した次eventだけpage turnする。端へ到達させたeventでは送らない。
- scroll mode、設定drawer、タグ制御mapping modalはcontent scrollを維持し、裏のviewerを送らない。
- wheelは既存Next/Prevへ委譲し、spreadのpageTurnMode/SHIFT/空白開始/tag-control planを再実装しない。
- Ctrl+wheel zoom、Home/End、mode数字キー、設定永続キー追加は対象外。
- ViewPrismUI CAD反映: `ec01a73` (`image_viewer.md`、IMG-022 review point)。
- 初回goldenの着地点所見を案Aの欠けた境界契約として補正し、CAD `153c366`で
  Width/Originalのwheel Next先頭/Prev末尾、viewport内は先頭=末尾、button/key/seek不変を追記した。
- gate①完了。次の明示入口は`/eco-fix ECO-071`。本裁定ではsrc/testsを変更しない。

## 8. 実施記録(2026-07-12 — 機械受入完了・golden待ち)

### 8.1 先行probe(R5)

- `CpUiG4ViewerTests`へ、`ViewerWindow`のwheel provider存在と`ResolveWheelAction`の決定表を
  reflectionで観測するprobeを製品コード変更前に追加した。
- 決定表はFit/spread下=Next/上=Prev、scroll mode=content scroll、Width/Original途中=pan、
  下端外向き=Next、上端外向き=Prev、horizontal-only=無操作を要求した。
- 是正前実測は`ViewPrism2.Tests` **609件中1件不合格(608 pass)**。`OnViewerWheelChanged`がnullで、
  起票診断どおりprovider未配線を確認してから製品コードへ着手した。

### 8.2 是正裁定とdiff

- `ViewerWindow`の`ViewerBody`へ`PointerWheelChanged`のTunnel handlerを追加した。子ScrollViewerが
  offsetを変える前に判定するため、端へ到達させたeventとpage turnを同時発火させない。
- `ResolveWheelAction`を描画非依存の純粋決定表(-1=Prev/0=content scroll/1=Next)として分離した。
  continuous scrollとvertical deltaなしは0。NormalScrollは`maxOffset=Extent-Viewport`とevent前Offsetで
  pan可能なら0、既に端なら±1。Fit/spreadはvertical deltaの符号で±1。
- ±1は既存`PrevCommand`/`NextCommand`だけを実行し`Handled=true`にする。spread step/SHIFT/空白開始/
  tag-control plan/端停止は既存VM/Core経路をそのまま消費する。
- handlerをViewerBodyへ限定したため、兄弟overlayの設定drawer/mapping modalはroute外でcontent scrollを維持する。
- REQ-091新設、仕様§2.9、E-UI-VIEWER-024、M-UI-018、CP-UI-G8へ案Aと明示除外解除を同期した。
  CADはgate①のViewPrismUI `ec01a73`で同期済み。
- XAML描画、Core送り計算、DB/schema/i18n/settings、Design System BOM、既存Oracle期待値は変更していない。

### 8.3 機械受入

- 先行probeを含む`ViewPrism2.Tests`: **609/609 pass**。
- `dotnet build ViewPrism2.sln --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Oracle`: **109 pass / 2 known skip**。既存固定期待値変更なし(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。
- `git diff --check`: clean。

### 8.4 gate②操作

1. 複数画像でviewerを単一Fitにし、canvas上のwheel下で次、wheel上で前へ進み、先頭/末尾では停止することを確認する。
2. 単一Width/Originalでviewportより縦に大きい画像を表示し、中間位置のwheelは画像内panだけを行って画像を切替えない。
3. Width/Originalでwheelにより下端/上端へ到達したeventでは切替わらず、既に端からさらに外向きへ回した次eventで
   Next/Prevになることを確認する。画像がviewport内に収まりpan不能なら最初のeventからpage turnする。
4. spread-right/spread-leftの両方でwheel下=論理Next、上=Prevとなり、右開きだけ方向反転しないことを確認する。
5. 見開きのdouble/single page、SHIFT押下、空白ページ開始、奇数末尾を切替え、wheelがbutton/PageDownと同じstep/端規則になる。
6. タグ制御ON+skip/spread/左右固定/空白mappingでもwheelがplan見開き単位で進み、配置を壊さない。
7. scroll modeではwheelが従来の連続scrollだけを行い、1eventでNext/Prevへ飛ばず、現在位置追跡・仮想化を維持する。
8. 設定drawerとタグ制御mapping modal上でwheel scrollし、内容が動く一方で裏のviewer位置が変わらないことを確認する。
9. horizontal wheelだけでは移動せず、mouse/touchpadで1操作が意図せず複数ページを飛び越えないことを体感確認する。

## 9. golden不合格補正(2026-07-12 — 再機械受入)

### 9.1 所見と先行probe(R5)

- 初回goldenで、単一Width/Originalの上端からwheel Prevした前画像が末尾でなく先頭へ着地した。
  初回契約はpage turnの発火境界だけを規定し、切替先の着地点を沈黙次元としていた。
- `CpUiG4ViewerTests.スクロール可能な単一画像のホイール送りは次の先頭と前の末尾へ着地する`を
  製品コード変更前に追加した。Next=0、Prev=`max(0,Extent-Viewport)`、pan不能=0、Fit/action0=着地点なしを要求。
- 是正前実測は`ViewPrism2.Tests` **610件中1件不合格(609 pass)**。`ResolveWheelLandingOffset`がnullで、
  着地点provider不在を確認した。Avalonia telemetry logのsandbox拒否後、同一コマンドを承認済み環境で再実測した。

### 9.2 是正裁定とdiff

- CAD IMG-022を`153c366`で先行補正し、REQ-091/仕様§2.9/E-UI-VIEWER-024/M-UI-018/CP-UI-G8へ同期した。
- `ViewerWindow`はWidth/Originalのwheel page turnで実際にindexが変わった場合だけ、対象pathとactionを保留する。
  通常のbutton/key/seek、Fit/spread、端停止では保留しない。
- 非同期画像load完了後にLoaded priorityでlayoutを確定し、切替先自身のExtentから
  `ResolveWheelLandingOffset`を計算する。Nextは先頭、Prevは末尾、viewport内は0。対象pathが変われば保留を破棄し、
  遅延loadが後続navigationへ着地点を漏らさない。
- Core/VM送り計算、XAML、DB/schema/i18n/settings、Design System BOM、既存Oracle期待値は変更していない。

### 9.3 再機械受入

- 先行probeを含む`ViewPrism2.Tests`: **610/610 pass**。
- `dotnet build ViewPrism2.sln --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Oracle`: **109 pass / 2 known skip**。既存固定期待値変更なし(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。
- `git diff --check`: clean。

### 9.4 gate②再操作

1. 単一Width/Originalで縦長画像A・Bを連続させ、A下端からwheel下でBへ進むとB先頭に着地する。
2. そのままB上端からwheel上でAへ戻ると、A先頭ではなく末尾に着地する。
3. viewport内に収まる画像を間に置き、先頭=末尾として上下どちらからも余分なpanや飛越しがない。
4. 同じ前後移動を前/次button、PageUp/Down、矢印、seekで行い、wheel専用の末尾着地が漏れない。
5. §8.4のFit/spread/scroll/overlay/horizontal/touchpad回帰を再確認する。

## 10. gate②合格・クローズ(2026-07-12)

maintainerが`/eco-accept ECO-071`で§9.4の再実機goldenを承認した。単一Width/Originalの縦長画像Aを
下端までpanしてwheel下で進むと画像Bの先頭へ着地し、Bの上端からwheel上で戻るとAの先頭ではなく末尾へ
着地した。viewport内に収まる画像は先頭=末尾として余分なpanを生じず、前/次button、PageUp/Down、矢印、
seekへwheel専用着地点は漏れなかった。Fit、spread-right/left、連続scroll、設定drawer、mapping modal、
horizontal/touchpad、端停止、見開きstep/SHIFT/空白開始/タグ制御にも回帰はない。

再発防止はCP-UI-G8の潜伏履歴+golden観点、`CpUiG4ViewerTests`のproviderおよび
mode/fit/offset/delta/landing決定表、M-UI-018の対象path+actionをload/layout確定まで保持する契約へ固定した。
CADはViewPrismUI `153c366`(初回`ec01a73`)、製品側REQ-091/仕様§2.9/E-UI-VIEWER-024/M-UI-018/
CP-UI-G8をfix時に同期済みで、accept時は`50-as-built.yaml`へ実機承認を追記した。新しい視覚部品・設定・
依存はなく、Design System/Service BOMの追加改訂は不要である。

教訓: スクロール端などの境界駆動ナビゲーションは「いつ遷移するか」だけでなく「遷移先のどのanchorへ
着地するか」を対称な往復操作として一組で契約する必要がある。ECO-049の暗黙表示変換、ECO-067の表示値境界と
同じ沈黙次元のread-acrossである。さらに非同期loadをまたぐUI入力意図は、方向だけを大域保持せず対象identityと
組にして遅延結果の漏出を防ぐ。この一般形はBomDD方法論への教訓昇格候補とする。

残課題なし。Ctrl+wheel zoom、Home/End、数字キーmode切替は明示的非採用のままで、必要性が実測された場合は別ECOとする。
