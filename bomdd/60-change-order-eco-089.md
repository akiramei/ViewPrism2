# Change Order — ECO-089(WrapPanel×横 StackPanel の同型残存 2 面 — タグパレット候補値行+作業タブチップ行。ECO-088 の read-across 閉包)

- 起票日: 2026-07-15
- 報告者: maintainer(実機スクリーンショット=タグパレットの編集アイコンずれ・候補値見切れ)
- 種別: 不具合(実装の CAD 乖離 — ECO-088 と同一様式の残存。全数走査による閉包起票)
- status: applied(2026-07-15 gate② 合格でクローズ)

## 1. 症状

**面 A(実機確認済み)**: タグタブ右ペインのタグパレットで、テキストタグの候補値が多い場合
(職種= 先鋒/前衛/重装/術師/…の 5 件級以上)にカードの**編集アイコンの位置が右へずれ、
削除アイコンが見切れる**。候補値チップ自体も右端で見切れる。候補値が少ないカード
(地域= 3 件・性別= 2 件)は正常(2026-07-15 maintainer 実機スクリーンショット)。

**面 B(構造から確定・症状は未顕在)**: 作業タブのチップ行
[WorkTabView.axaml:796-799](../src/ViewPrism2.App/Views/WorkTabView.axaml#L796)が
**ECO-088 で是正した ImageTabView チップ行と同一構造のまま残存** — 同じ条件
(チップ数が可視幅を超える。定義値展開 47 件級等)で同じ切り捨てが発生する。

## 2. 工程診断(R2)

| 工程 | 判定 | 証拠 |
|---|---|---|
| CAD | **健全** | タグ管理 mock の候補値行コンテナ= `display:flex;align-items:center;gap:6px;flex-wrap:wrap;…;padding-left:20px`・作業タブ mock のチップ行= `display:flex;…;flex-wrap:wrap`(いずれも 2026-07-15 抽出実測)= **両面とも折返しが CAD 正典** |
| BOM | 健全(検査の谷間は ECO-088 で特定済み) | CP-CHIPWRAP-088 は画像タブのチップ行のみを検査面とし、同型の他面は範囲外だった |
| 実装 | **逸脱と確定(2 面)** | ①[TagsTabView.axaml:310-316](../src/ViewPrism2.App/Views/TagsTabView.axaml#L310): 候補値チップの ItemsControl(ItemsPanel= WrapPanel)が `StackPanel Orientation="Horizontal"` 内=無限幅測定で折返し不能 ②[WorkTabView.axaml:796-799](../src/ViewPrism2.App/Views/WorkTabView.axaml#L796): チップ行が ECO-088 是正前の ImageTabView と同一の横 StackPanel 構成 |

- **混入コミット**: 面 A= `3536ffb`(2026-06-16「デザイン言語(B)実証: タグパレットをモック品質へ
  再実装」= ECO-009 系)— 潜伏 29 日。面 B= `f211fa9`(2026-06-29「作業タブ: 作業スペース
  (ECO-020/α)+右ペイン文脈モード(ECO-021/β)」)— 潜伏 16 日。いずれも当該面の初版から。
- **マスキング要因**: ECO-088 と同一 — 内容が 1 行に収まる限り視覚に現れない(機能等価潜伏)。
  面 A はタグパレットに 5 件級の候補値タグが実データとして現れた本日、初可視化。
- **工程所見(ECO-088 の read-across 漏れ)**: ECO-088 は真因様式(WrapPanel×横 StackPanel の
  無限幅測定)を特定したのに、**同型構造の全数走査(read-across)を是正範囲に含めなかった** —
  §8.2「撤去は到達閉包で」/GF-073「同じ失敗は面を変えて連鎖」の様式どおりの漏れ。本 ECO の
  起票時に全数走査を実施し閉包を確定した(下記 §3)。
- **未確定事項との関係**: IMG-023(多量チップの行数上限等)はビュー軸チップ行の設計課題であり
  本 ECO と別。タグパレットの多量候補値の面設計(行数上限等)は mock が flex-wrap のみ定義=
  折返し回復までが本 ECO(ECO-088 と同じスコープ切り)。

## 3. 切り分け済みの事実(確定/未検証の分離)

確定(実測済み):

1. `<WrapPanel` の全数走査(src/ViewPrism2.App/Views/*.axaml)= **14 箇所**。うち横 StackPanel
   直下(無限幅測定=折返し死)は **2 箇所のみ**(§2 表の面 A/B)。他 12 箇所は健全 —
   親が縦 StackPanel/Grid/ScrollViewer(水平 Disabled)/ECO-088 是正済み Grid・ECO-027 設計済み
   LeftCluster・TagEditorWindow カラープリセット(縦 StackPanel)等。
2. CAD は両面とも flex-wrap を定義済み(§2 表)= 未設計ではなく実装逸脱。
3. 面 A の症状機序: 候補値行の無限幅測定がカード内容幅を押し広げ、カード右端の要素
   (行 1 DockPanel の右ドック=編集/削除アイコン・候補値チップ末尾)がペイン可視幅の外へ出る。

疑い(未検証・/eco-fix のプローブで確定させる):

- (a) 面 A の外側 ScrollViewer(TagsTabView:276・水平可視性未指定)が子の測定へ無限幅を渡して
  いる可能性 — その場合、横 StackPanel の撤去だけでは折返しが回復せず、水平測定の制約
  (HorizontalScrollBarVisibility="Disabled" 明示等)も要る。是正前赤プローブの崩れ方
  (カード幅 vs ペイン幅)で確定する。
- (b) 面 B は ImageTabView と同一文脈(DockPanel Dock.Top 内=幅制約あり)のため
  ECO-088 と同一の是正(Grid 化)で回復する見込み。

## 4. 是正方針(案・着手時確定)

ECO-088 と同一の是正様式を 2 面へ適用(真因構造そのものを消す):

- 面 A: 候補値行の横 StackPanel を Grid(Auto=「候補値」ラベル/*= ItemsControl)へ。
  疑い (a) が実測で確定したら外側 ScrollViewer の水平測定制約も併せて明示。
  数値の範囲行・シンプル注記行(同カード内・固定少数)は内容固定のため対象外(最小 diff)。
- 面 B: WorkTabView チップ行を ECO-088 の ImageTabView 是正と同一の Grid(Auto,*)+
  `WrapPanel LineSpacing="8"` へ(共有部品化は本 ECO では行わない — 現状 2 面が別実装で
  存在する事実を尊重し、統合は将来の DRY 判断へ)。

プローブ(是正前赤の実測計画): CpUi088ChipWrapTests の様式を両面へ —
面 A= 候補値 12 件級のタグでカード内チップの実描画矩形がペイン可視幅内+編集/削除アイコンが
可視幅内(是正前=はみ出しで赤)。面 B= 47 チップの折返し(CpUi088 と同一アサーション)。
少数件の 1 行不変 pin も両面に張る。

## 5. 影響 BOM

- 実装: `src/ViewPrism2.App/Views/TagsTabView.axaml`(候補値行コンテナ)+
  `src/ViewPrism2.App/Views/WorkTabView.axaml`(チップ行コンテナ)— いずれもコンテナのみ
- テスト: 両面の折返し probe 新設(CpUi089 系)。既存固定 Oracle 行は変更しない(R6)
- CAD: mock/prose は既に正= 変更なし。visualContract へ lazy 遡及 —
  `tag_tab.md` VC-TAG-9 級(タグパレット候補値行のオーバーフロー)+
  `work_tab.md` VC-WORK-1 級(チップ行=VC-IMG-8 と同文・作業タブ面)
- E-BOM: 該当 surface(タグパレット系・作業タブナビ系)の説明へ ECO-089 追補
- CP: CP-CHIPWRAP-088 の characteristic へ「同型 2 面の残存を閉包是正(ECO-089)」を追記するか、
  CP-CHIPWRAP-089 新設(fix 時に判断 — 同一様式なので 088 への統合が有力)

## 6. 残ゲート

- gate①(裁定): **不要** — CAD 健全(両 mock とも flex-wrap 実測)・実装逸脱と確定。
- gate②(golden): 面 A= タグパレットで候補値多数タグのカード(編集/削除アイコン可視・
  候補値の複数行折返し・少数タグの視覚不変)。面 B= 作業タブのビュー軸で多量チップの折返し+
  少数チップの視覚不変(ECO-088 gate② と同一基準)・en 切替。
- スコープ外: タグパレットの多量候補値の面設計(行数上限・「+N」畳み等)は mock 未定義=
  必要なら review_points へ(TAG-013 級)。チップ行 2 面の共有部品化(DRY)は将来判断。

## 7. 実施記録(2026-07-15 /eco-fix)

**CAD lazy 遡及**: tag_tab.md へ **VC-TAG-9**(パレット候補値行のオーバーフロー=折返し・右端要素の
可視)/work_tab.md へ **visualContract 節新設+VC-WORK-1**(チップ行= VC-IMG-8 と同文。
作業タブ mock の flex-wrap 実測を注記)。

**プローブ先行(R5)**: CpUi089ChipWrapTests 4 本(面 A 2 本+面 B 2 本)。是正前赤 2/2 を実測 —
面 A: 編集/削除アイコンが**可視 12/28px**(right=1368 > 1366)= カードが押し広がりクリップ(§1 の
実機症状と一致)。面 B: チップ right=**1443 > 1366**(1 行のまま溢れ= ECO-088 と同一の崩れ方)。
少数 pin 2 本(候補値 2 件・チップ 3 件)は緑=既存視覚の基準。

**是正(ECO-088 と同一様式を 2 面へ)**: 面 A= 候補値行の横 StackPanel を Grid(Auto=ラベル/
*= ItemsControl)へ(ラベル間隔=旧 Spacing 6 と同値の Margin)。面 B= ECO-088 の ImageTabView
是正と同一の Grid(Auto,*)+`WrapPanel LineSpacing="8"`。diff= 2 ファイル各 1 コンテナ。
**疑い (a) の決着**: 面 A は Grid 化のみで折返しが回復= 外側 ScrollViewer が無限幅を渡している
疑いは**否定**(probe 実測)— 水平測定制約の追加は不要だった。

**横断規約(ECO-080)**: 新規文言なし(i18n 不要)・VM/DB 不変。

**機械受入**: build 0/0・**Tests 713/713**(probe +4 全緑転)・Oracle 109+2skip(R6 不変)・
validate_bom 0/0。CP は **CP-CHIPWRAP-088 へ統合**(同一様式の閉包= fixture/test_vectors/
characteristic へ 089 を追記。新 CP は設けない)。

**セルフゴールデン(R7・面全体並置= 3 分類)**:

| # | 差分/次元 | 分類 |
|---|---|---|
| 面 A: 折返し+右端要素可視(VC-TAG-9) | 転写(是正そのもの・probe 実測=アイコン完全可視+チップ全数+行数≥2) | — |
| 面 A: カードの他次元(色ドット・名前・型チップ・数値範囲行・シンプル注記・アイコン列) | 不変(コンテナのみ変更・少数 pin 緑・既存 CpUiG6/L1 テスト緑= 713 に包含) | — |
| 面 A: 折返し 2 行目の開始位置(「候補値」ラベル列の右) | ECO-088 gate② で同型を許容済み(ヒント列の右= 2026-07-15 裁定)— 面が違うため golden で確認提示 | 裁定済み同型(要確認) |
| 面 B: 折返し(VC-WORK-1) | 転写(probe 実測・ECO-088 是正と同一構造/同一様式) | — |
| 面 B: 2 行目開始位置・LineSpacing 8 | ECO-088 gate② の裁定と同一(同じチップ行部品の対称面) | 裁定済み |
| FS 軸/ビュー軸の他チップ次元(件数・色・ナビ矢印) | 不変(ChipVM/VM 無改修) | — |

転写漏れ 0。

## 8. 残ゲート(更新)

- gate②(golden)のみ。スコープ外(タグパレット多量候補値の面設計・チップ行 DRY 統合)は §6 不変。

## 9. クローズ(2026-07-15 golden 合格)

**gate② 承認(maintainer 実機)**: 面 A= 職種カード(候補値 5 件級)の編集/削除アイコンが定位置・
候補値チップの複数行折返しで全数可視・少数カード(地域 3 件/性別 2 件)の視覚不変・折返し 2 行目=
「候補値」ラベル列の右を許容(ECO-088 gate② と同型の裁定)。面 B= 作業タブの多量チップ折返し+
少数チップの視覚不変。en 切替。

**クローズ 3 点セット**: CP-CHIPWRAP-088(統合先)へ ECO-089 golden 承認+潜伏実績(2 面・
混入 3536ffb/f211fa9・潜伏 29/16 日・read-across 漏れの実証)を明記(再発防止)/
register `applied`+golden approved / 本節。
**M4 同期**: 挙動仕様の変更なし(CAD 既定義の折返しの回復)。E-BOM(E-UI-TAGS-026/
E-UI-WORKSPACE-043)は fix 時に追補済み。CAD 正典= ViewPrismUI `12947cd`(VC-TAG-9+VC-WORK-1)。

**教訓**:

1. **read-across(同型全数走査)は真因様式が確定した時点の必須ステップ — 「様式の特定」と
   「様式の掃討」は別の工程**。ECO-088 は真因様式(WrapPanel×横 StackPanel の無限幅測定)を
   特定・是正・CP 化までしたのに、同型構造の grep 全数走査(1 コマンド・14 ヒット・数分)を
   しなかったため、残存 2 面が翌日に実機顕在化した。走査コストは極小で、漏れの実害
   (再起票・再 golden の 1 サイクル)より常に安い。GF-073「同じ失敗は面を変えて連鎖」・
   GF-086-01 の read-across(dialog language マトリクス)に続く実証 — **是正 ECO のクローズ条件に
   「真因様式の同型全数走査」を含める**(BomDD 昇格候補=playbook §8.3 プローブ規律への追補。
   ECO-077/086 系の「read-across」実績群と合わせ rule of three 相当)。
2. **同型欠陥の 2 面目以降は起票・是正・受入の全工程が初回より軽い — 様式の再利用が効く**。
   本 ECO は診断(全数走査で 2 面特定)・probe(CpUi088 様式の転用)・是正(同一の Grid 化)・
   CP(088 へ統合)まで ECO-088 の資産を再利用し、起票からクローズまで同日で完了した。
   閉包起票(様式単位で 1 ECO)は面単位で N 本立てるより管理・受入とも効率的(観測 1 例目)。
