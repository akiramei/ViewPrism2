# ECO-063 (applied) — タグビューのホームノード初期遷移が画像タブで無視される

> maintainer 実機報告(2026-07-11)を受け、`/eco-file` で工程診断した欠陥是正。
> 起票段階では `src/tests` を変更しない(R1)。

## §1 症状(観測 2026-07-11・報告者 maintainer)

タグタブのビュー階層定義で任意ノードをホームに設定して保存しても、画像タブの表示軸でそのビューを選択したとき、
設定したホームノードへ初期遷移せず、常にビューのルートが表示される。

再現手順:

1. タグタブでビューを選び、複数階層のノードを配置する。
2. ルート以外のノードをホームに設定して保存する。
3. 画像タブを開き、表示軸から当該ビューを選択する。
4. 期待: ホームノードまでのパンくず/条件が初期選択され、そのノードの画像集合を表示する。
5. 実際: パンくずはルートのままで、ビューのルート画像集合を表示する。

## §2 工程診断(R2)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 健全・明確 | `docs/screens/tag_tab.md` はホームを「画像タブでビュー適用時に最初に開く場所」、ホームアイコン操作を「その配置をビューのホームにする」と明記。新たな裁定余地なし |
| 要求/仕様 | 健全・明確 | REQ-037 / 仕様 §2.4 は `home_tag_id` が解決できればビューを開いたとき該当ノードへ自動ナビゲートし、不能ならルートへフォールバックすると規定 |
| Core/DB | 健全 | `ViewService.SaveHierarchyAsync`/`ViewRepository.ReplaceHierarchyAsync` は home node id を同一transactionで保存。`NodeGraphBuilder.ResolveHome` は hierarchy node id をDFS解決し、不能/nullならnull。CP-VIEW-012、CP-GRAPH-002、固定Oracle S-12で保存・正/負解決を検査済み |
| **E-BOM** | **欠陥(トレース漏れ)** | E-GRAPH-003/E-UI-NODEGRAPH-025 はREQ-037を参照するが、画像タブの消費面 E-UI-AXIS-NAV-040 の `requirement_refs` は REQ-053/060 のみ。タグタブで設定したホームを画像タブ軸へ渡す接点が部品契約から脱落 |
| **実装(ImageTab)** | **欠陥(未配線)** | `ImageTabViewModel.LoadViewAsync` は view/hierarchy/NodeGraph をロード後、`_viewPath.Clear()`→`Recompute()`のみ。`view.HomeTagId` と `_graphBuilder.ResolveHome` を一度も参照しないため、設定値にかかわらずルート開始 |
| 検査 | **surface接続の欠測** | CP-GRAPH-002 は Core の単一ノード解決のみ。CpL1Smoke/CpUiG1CollectionScope は HomeTagId 未設定のビューを選んでroot→chip遷移を検査するだけ。CP-UI-G1に現行画像タブでのホーム初期遷移をpinする機械プローブがない |

帰属: **E-BOMトレース漏れ + 実装未配線の二層欠陥**。CAD/要求/Coreは改訂不要。設計裁定なしで
`/eco-fix ECO-063` に進める。

混入:

- `45a6c77`(2026-06-18、画像タブM3bタグビュー軸実消費)で `LoadViewAsync` が導入された時点から
  `_viewPath.Clear()` 固定で潜伏。`git blame` でも当該行は同コミット。
- ECO-024でlegacy画像タブを撤去したことが原因ではなく、新ImageTab M3b自身の初版欠落。

マスキング:

- V1のgolden記録にはホーム初期遷移確認があるが、後の新ImageTab surfaceへの置換/再製造後に同観点を
  機械検査へ移管しなかった。
- NodeGraphのホーム解決unitが緑のため、保存→解決→surface path反映の最後の接続欠落を検出できなかった。

## §3 切り分け済みの事実

確定:

- `views.home_tag_id` は階層ノード id であり、タグ定義 id ではない。
- タグタブのホーム設定/解除と保存は unit で永続化まで検査されている。
- 画像タブの現在ノードは `_viewPath`(rootを除くGraphNode列)で表現し、`Recompute` が
  `[root] + _viewPath` を条件/パンくず/子チップへ使う。よってホームノード単体でなくrootからのpathが必要。
- `ResolveHome` はDFSで最初に一致したGraphNodeを返す。textual複数値により同じhierarchy nodeが複製される場合も
  現行Coreの決定的な先頭解決規則を維持し、新しい選択意味論は導入しない。
- 参照切れ/nullのホームは既存契約どおりrootフォールバックとし、エラー表示は追加しない。

疑い(未検証 — `/eco-fix` のプローブで確定):

- `NodeGraphBuilder` がroot→homeのpathを決定的に返すAPIを持ち、`LoadViewAsync` がそのpathを `_viewPath` へ設定すれば、
  既存の `Recompute` を変更せずパンくず・画像集合・子チップが一括してホーム文脈になる。
- view再ロード(コレクション切替・タグ台帳stale反映)でも `LoadViewAsync` を通るため、毎回ホームへ戻るのが
  「ビューを開いたとき」の正規動作となる。現在path保持との優先順位は現仕様上ホーム初期化が正。

## §4 是正方針(裁定不要・着手時にプローブで確定)

推奨最小是正:

1. CP-GRAPH-002へ、home解決を単一nodeだけでなくrootからのpathとして返す決定論APIのベクタを追加する。
   既存 `ResolveHome` は固定Oracle互換のため維持する。
2. `ImageTabViewModel.LoadViewAsync` でNodeGraph構築後、`view.HomeTagId` をpath解決し `_viewPath` へ設定する。
   null/参照切れは空path(root)へフォールバック。
3. 画像タブsurfaceプローブで保存済みhomeのビュー選択直後に、パンくず・HomeActive・表示画像集合がhome文脈となること、
   参照切れ/nullはrootとなることを固定する。

真因構造を消すため、ImageTab側で独自DFSを再実装せず、ホーム解決を所有するNodeGraphBuilderへpath契約を置く。
DB/schema/CAD/XAML/タグタブ保存処理は変更しない。

## §5 影響BOM / 受入計画

- `E-UI-AXIS-NAV-040`: REQ-037参照と「ビュー選択時home path初期化/不能時root」を追加。
- `E-GRAPH-003` / `M-GRAPH-003`: home path決定論API(既存node APIは維持)。
- `M-UI-IMAGETAB-035`: `LoadViewAsync` のhome path配線。
- `CP-GRAPH-002`: nested home path / null / 参照切れ / textual複製時の決定的先頭path。
- `CP-UI-G1` + 製品unit: ビュー選択直後のcrumb/画像集合/HomeActive、root fallback、コレクション切替時再ロード。
- `CP-UI-G6`: タグタブの設定・保存は既存観点で健全。コード変更なしだがgoldenで設定→画像タブ消費を往復確認。
- 既存固定Oracle S-12/行は変更しない(R6)。新規製品unitで接続を固定。
- DB migration / settings / i18n / visual layout: 影響なし予測。

golden再検査:

1. タグタブでnested nodeをホーム設定→保存。
2. 画像タブで当該ビュー選択→ホームまでのパンくず・該当画像集合・下位チップ。
3. パンくずホームでrootへ戻れること。
4. ホーム解除後の再選択はroot開始。
5. 別コレクションへ切替/戻りでNodeGraph再構築後もホーム開始。

## §6 残ゲート

- gate①(設計裁定): **不要**。CAD/REQ-037が明確で、既定動作への実装追随。
- `/eco-fix ECO-063`: surface接続の赤プローブ→Core path契約→ImageTab配線→4点機械受入。
- gate②(golden): §5の設定→画像タブ消費往復をmaintainer実機確認後、`/eco-accept ECO-063`。

## §7 実施記録(2026-07-11・fix)

### 7.1 プローブ先行(R5)

`CpUiG1CollectionScopeTests` に、二段階のsimple nodeを持つビューを作成し、子nodeを `home_tag_id` として
保存してから画像タブで選択するsurface接続プローブを先行追加した。期待は `HomeActive=false`、パンくず
`[親, ホーム]`、表示画像=`a1.jpg` 1件。是正前は **584件中1件不合格**:

- `Assert.False(vm.HomeActive)` が Actual=`true`。保存済みhomeを無視してroot開始する症状を直接再現。

この赤により、Core/DB保存ではなく `LoadViewAsync` の最終接続欠落という診断を実測裏取りした。

### 7.2 是正

- `NodeGraphBuilder.ResolveHomePath(root, homeTagId)` を追加:
  - rootを除く祖先→homeのGraphNode列を返す。
  - null/参照切れは空列(root fallback)。
  - textual値展開で同じhierarchy nodeが複製される場合は、既存 `ResolveHome` と同じDFS先頭を採用。
  - 既存 `ResolveHome` は新pathの末尾を返すfacadeとして公開契約を維持(固定Oracle無改変)。
- `ImageTabViewModel.LoadViewAsync`:
  - graph構築後に `_viewPath.AddRange(_graphBuilder.ResolveHomePath(_viewRoot, view.HomeTagId))` を実行。
  - UI独自DFSは追加せず、Coreの単一決定規則を消費。
- プローブ強化:
  - CP-GRAPH-002にnested/textual複製DFS/null/参照切れのhome path exactを追加。
  - CP-UI-G1にビュー選択直後のcrumb/画像集合/HomeActiveに加え、view軸のままcollection切替→再構築後も
    home pathを再適用する裏面を追加。
- BOM同期:
  - E-UI-AXIS-NAV-040へREQ-037参照とhome初期path invariant。
  - M-GRAPH-003へpath API、M-UI-IMAGETAB-035へLoadViewAsync配線、CP-GRAPH-002へベクタを追加。

変更なし: CAD/REQ-037/仕様、DB/schema、タグタブ保存、XAML、i18n、settings。

### 7.3 機械受入

- `dotnet build --no-restore`: **0 warning / 0 error**。
- `dotnet test tests/ViewPrism2.Tests --no-build`: **585/585 pass**(既存583 + ECO-063新規2)。
- `dotnet test tests/ViewPrism2.Oracle --no-build`: **109 pass + 既知2 skip**(既存行無改変)。
- `python bomdd/validate_bom.py`: status/記録更新後 **0 error / 0 warning**。
- `git diff --check`: errorなし。

## §8 残ゲート(fix後)

- gate② golden: **approved**(2026-07-11、maintainer実機)。

## §9 クローズ(2026-07-11)

### 9.1 golden結果

- nested nodeをホーム設定・保存し、画像タブで当該viewを選択するとhomeまでのパンくず、該当画像集合、下位chipから開始することを確認。
- パンくずhomeでrootへ戻れることを確認。
- ホーム解除後はrootから開始することを確認。
- view軸のcollection切替・再load後もhomeが再適用されることを確認。
- 既存のタグビュー編集・画像タブ遷移に回帰がないことを確認。

### 9.2 再発防止

- `CP-GRAPH-002`: root除外home pathのnested/textual exact、null/参照切れfallbackを機械検査。
- `CP-UI-G1`: view選択とcollection再loadのconsumer surface接続を機械検査・golden観点化。
- `CP-UI-G6`: タグタブの設定・保存から画像タブの消費までを往復golden観点化。
- `50-as-built.yaml`: maintainer実機承認を `golden_2026_07_11_eco063` に記録。

### 9.3 教訓

producer側の単体unitが緑でもconsumerへの配線を証明しない。surface置換時はCore契約だけでなく「設定→保存→消費」の受入経路を移管する。ECO-041の未配線UI、ECO-062の計算境界と同じく、境界を跨ぐ機能は端点ごとの検査だけでなくend-to-endの接続probeを恒久化する。

### 9.4 残差

ECO-063に帰属する残件なし。CAD、DB schema、外部method repositoryの変更なし。
