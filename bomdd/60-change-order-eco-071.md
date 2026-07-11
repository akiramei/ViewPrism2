# Change Order — ECO-071(staged): ビューアーの単一・見開きモードでホイール送りを追加する

> maintainer要求(2026-07-12)「ビューアーにて単一や右開き・左開きでも、マウスホイールで画像を
> 次へ・前へ切り替えたい」を`/eco-file`で受理した既存機能拡張要求。

## 1. 症状・要求(maintainer報告・2026-07-12)

- 縦スクロールモードではマウスホイールで連続画像を閲覧できるが、単一・右開き・左開きでは
  ホイールを回しても次/前へ移動しない。
- 期待は、ホイール下方向で論理的な「次へ」、上方向で「前へ」を操作できること。
- 再現: 画像を複数含む一覧からviewerを開き、単一/右開き/左開きへ切り替えてキャンバス上でホイール操作する。

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
2. CAD裁定コミットを製品へ取り込んだ後、`/eco-fix ECO-071`で先行赤probe→是正→機械受入。
3. gate② golden: 4modeとnormal fit3種、内部scroll/overlay、spread送り規則、端/入力体感を確認。
4. `/eco-accept ECO-071`でCP/As-Built/register/教訓をクローズ。

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
- gate①完了。次の明示入口は`/eco-fix ECO-071`。本裁定ではsrc/testsを変更しない。
