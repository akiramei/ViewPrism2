# ECO-129 — pending 意味論の再定義 — 「relink 候補」から「存在するが未裁定」の管理状態へ(スキャン判定の pending 化+裁定導線)(applied)

- 起票日: 2026-07-21
- 報告者: maintainer 設計まとめ(2026-07-21「画像の状態管理・あるべき状態管理」§2/§3/§7/§13)の投入。3 分割の (b)
- 種別: 仕様改訂候補(状態機械の中核意味論変更+UI 新設。実装逸脱ではない)
- baseline: ViewPrism2 main `390e1f4`
- 関連: **ECO-130(スキャン二段階化)の先行または同時を前提**(maintainer 指示済み)/ ECO-128 は本 ECO に依存

---

## 1. 症状(変更要求)

3 分割の中核。現行の pending は「新規発見ファイル側に付く再リンク候補」(candidate_link_id 付き・
未タグ前提・消えたら行削除)という狭い意味論。設計まとめはこれを
**「ファイルは存在するが、ViewPrism 上の扱いが未裁定」の管理状態**へ再定義する:

| 事象 | 現行 | 新モデル |
| --- | --- | --- |
| 内容変更(パス同一・ハッシュ不一致) | hash/size/日時を黙って更新・status 不変(規則 2) | **pending**(編集か差し替えかは機械判定しない — §3) |
| 新規発見 | normal で即登録(規則 3b / T1) | **新規 pending**(§2/§13) |
| pending のファイル消失 | **行削除**(手順 5) | **missing 保持**(§7: pending→missing→pending) |
| pending の裁定 | relink 確定(T4)のみ | **裁定導線新設**: 編集後として受入れ / 別画像として扱う / 削除 / 保留(§3) |

運用原則(§13): 機械観測とユーザー裁定を分離・不確実は pending に倒す・自動判定より候補提示。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
| --- | --- | --- |
| CAD(ViewPrismUI) | **未定義 — mock 先行が必要** | pending 裁定 UI(受入れ/別画像/削除/保留)の mock・screen_spec が不在。pending の一覧表示(バッジ等)も未定義 |
| BOM/仕様(20-spec §2.1 遷移表・§2.11.0 T1/T2・手順 5・OC-5・INV-010/INV-015) | **改訂対象(上流)** | 現仕様 v4.0 は旧意味論で自己整合しており欠陥ではない。新モデルへの改訂 |
| 実装 | 健全(現仕様に忠実) | [ScanJudge.cs:64](../src/ViewPrism2.Core/Services/ScanJudge.cs)(規則 2)・:87(規則 3b)・[ScanService.cs:163](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs)(手順 5)= 全て仕様どおり |

**結論: 仕様層の設計変更+CAD 新設。CAD mock → spec 改訂 → 実装追随の順。**

## 3. 切り分け済みの事実

### 確定(証拠あり)

1. **現行実装サイト**: ScanJudge 規則 2(status 不変の UpdateMeta)・規則 3b(AddNormal)・
   ScanService 手順 5(pending 行削除)・手順 4(normal→missing)。いずれも spec v4.0 と一致。
2. **固定オラクル衝突(R6)**: OC-5 は「遷移 4 ケースを exact」で pin(spec:661)。
   S-01(リネーム追跡 E2E)は「初回スキャン 3 枚→タグ付与→…」の前提が**新規=normal 登録**に
   依存(新規= pending だと未裁定画像へのタグ付与という別問題になる)。規則 3a
   (同ハッシュ missing への candidate 付き pending)自体は新モデルでも整合するが、
   周辺の exact 行は旧意味論を pin = **処置裁定が必要**(ECO-128 の S-29 と同一論点)。
3. **INV-010 との衝突が新規に生じる**: 内容変更→pending にすると、既定一覧・ビュー評価は
   normal のみ対象のため、**タグ付き画像がファイル編集しただけで全ビューから消える**。
   現行モデルでは起きない UX(規則 2 が status 不変のため)。pending の可視化方針の裁定が必要。
4. **INV-015 の前提が破れる**: 「pending は新規スキャンで未タグ」は新モデルでは偽
   (内容変更→pending は既存タグを保持したまま pending 化する)。relink のタグ安全ガード自体は
   実タグ照会なので堅牢だが、**手順 5 の行削除は無条件のため、タグ付き pending の行ごと消滅=
   タグ損失**を起こす。行削除の廃止(missing 保持)は安全性の要(§7)。
5. **candidate_link_id は新モデルでも有用**: 「同フォルダ同ハッシュの missing あり」は
   pending の裁定候補ヒント・自動修復候補として引き続き機能する(規則 3a は「pending の亜種」
   から「pending +候補ヒント」へ自然に再解釈できる)。
6. **規模の含意**: 新規→pending は、適用前確認(ECO-130)なしでは初回登録や大量追加で
   裁定対象が爆発する(26 万件フォルダ登録= 26 万 pending)。maintainer 指示済み=
   **ECO-130 を先行または同時**。

### 未検証(疑い・着手時確定)

- 初回スキャン(isInitialScan)の特例扱いの要否(§6 裁定事項 1)。
- pending 裁定 4 操作の遷移先詳細(「別画像として扱う」= 旧レコードとの関係をどう記録するか。
  設計まとめ §7 は「状態履歴の厳密管理はしない」方針)。
- NodeGraph・類似検索・エクスポート等、status=normal 前提の全消費サイトの棚卸し
  (spec INV-010 の適用面。fix 時に grep 悉皆)。

## 4. 是正方針(案・着手時確定)

段階: **CAD mock(ViewPrismUI)→ spec 改訂(遷移表・INV 改訂・REQ 採番)→ 実装追随**。

- ScanJudge: 規則 2 → UpdateMeta+**status=pending**(normal 起点のみ。missing/deleted 起点の扱いは
  spec で規定)・規則 3b → AddPending(候補ヒントなし)。判定器は純粋関数のまま(OC-5 の器は維持)。
- ScanService: 手順 5 廃止 → pending のファイル消失は missing へ(手順 4 の対象拡張)。
- 裁定導線: pending 一覧+4 操作(受入れ=normal 化 / 別画像=新レコード分離等 / 削除=deleted /
  保留)。CAD mock 確定後に BOM 展開。
- INV-010/INV-015/T1/T2 改訂+新遷移(pending→normal 裁定・pending→missing・missing→pending 再出現)。

diff 規模: 大(spec 中核+UI 新設+テスト群)。**26 万件経路の性能規律を適用**
(pending 件数に比例する処理を裁定画面外へ漏らさない)。

## 5. 影響 BOM

- **CAD**: pending 裁定 UI+pending 可視化の mock/screen_spec 新設(ViewPrismUI・先行)
- **spec**: §2.1 判定規則 2/3b・手順 5・§2.11.0 遷移表(T1/T2 改訂+裁定遷移追加)・
  INV-010/INV-013/INV-015 改訂・REQ 新設(採番は spec 改訂時)
- **src**: ScanJudge・ScanService・裁定サービス/VM/View 新設・INV-010 消費サイト追随
- **tests**: OC-5 追随+oracle 新規行(旧行処置は gate① 裁定)・裁定遷移 unit・UI probe
- **CP**: 新設は accept 時

## 6. 残ゲート

- **gate①(裁定)— §7 で裁定済み(2026-07-21)。残= CAD mock**:
  1. ~~初回スキャンの新規の扱い~~ → **裁定済み(§7= 初回のみ normal)**。
  2. ~~pending の可視化~~ → **裁定済み(§7= 既定一覧に出す+バッジ)**。
  3. ~~固定オラクル旧行の処置~~ → **裁定済み(§7= skip 化+刻印+新規行)**。
  4. **CAD mock の作成**(ViewPrismUI 側 gate: 裁定 UI・pending バッジ表示)— **未・維持者作業**。
- **gate②(golden)**: §9 の基準で維持者確認待ち。

## 8. 実施記録(2026-07-21 fix)

- **mock 承認**: maintainer の明示承認(「mock 承認。/eco-fix eco-129」)。**PEND-001 は着手時に追加裁定**
  (AskUserQuestion 2026-07-21): 提案「旧行 missing 化+新規 normal 登録」は**パス一意性
  (UNIQUE(sync_folder_id, relative_path)・スキャン byPath 前提・T4)と衝突し不成立**と判明 →
  **「新画像として作り直し」= 原子的な行置換(T14)を裁定**。
- **オラクル露出の悉皆(着手時)**: **凍結オラクルは新意味論でも全行無傷**(S-01= 初回 normal・
  3a 候補 pending 包含で全アサーション成立を机上+実測で確認)。**gate① の skip 化裁定は 129 でも
  行使不要**(130 教訓 1 の再現。真の衝突は 128 の S-29 のみ)。可変の CpScan004Tests 4 本のみ
  v5.0 へ書き換え(旧規則の否定形→新仕様の肯定形+origin/candidate クリア検査を追加)。
- **spec/REQ/BOM 改訂**: §2.1 判定規則 v5.0(規則 1 例外=missing 再出現/規則 2 起点別 4 分岐/
  3-初回・3-再)・手順 5= pending→missing(行削除廃止)・遷移表改訂+§2.11.0 T10〜T15 新設・
  §2.11.7 pending 裁定 新設・INV-010/INV-015 v5.0・REQ-101 新設+REQ-012/016 v5.0 注記・
  E-UI-PENDING-049/M-UI-PENDING-054 登記・migration 010(images.pending_origin)。
- **R5(プローブ先行)**: CpPendingSemanticsTests 8 本= **是正前赤 8/8 実測**(①内容変更→pending
  ②再スキャン新規→pending+候補ヒント ③missing 再出現→pending ④pending 消失=missing 保持+タグ保全
  ⑤初回のみ normal ⑥裁定 3 遷移+pending 限定拒否+T14 原子性〔1 パス 1 行・新 ID・タグ CASCADE 消滅〕)
  → 実装で全緑転。視覚 probe= GfPendingReviewVisualParityTests 5 本(CTA 出し分け/destructive/
  保留左端分離/琥珀破線バッジ/空状態+DB 確定)を visualContract から生成。
- **実装**: ScanJudge v5.0(Skip/UpdateMeta/UpdateMetaAndPend/PendInPlace/AddNormal/AddPending)・
  ScanService 一段階+ステージング両経路(判定器・列挙器共有=パリティ)・PendingReviewService
  (T13/T14/T15= repository の WHERE で pending 限定を原子強制)・ImageRepository
  (pending_origin 配管+AdjudicatePendingAsync+ReplacePendingAsync〔単一 Tx の DELETE→INSERT〕+
  GetPendingByFolderAsync)・ImageTab= pending の FS 軸並置(チップ集計確定後に追加=未裁定タグの
  漏出防止)+未裁定バッジ(グリッド/リスト)+⋯メニュー入口(CMP-010 件数)・
  PendingReviewWindow/VM(PD-2〜4)・ECO-130 面の語彙追随(内容変更 normal→pending・再出現行新設・
  候補消失 pending→missing・裁定対象=PendingTotal)。i18n 約 40 キー(ja/en)。
- **予防 lint の実働 3 件**: Recompute 台帳(OpenPendingReview=A 分類で登録)・CMP-011 検査C
  (一覧行セレクタ=根拠つき台帳)・CpDefExp086 視覚 probe(破線族バッジ常在テンプレートとの干渉→
  実効可視での計数へ精密化=検査意図保存)。
- **機械受入(4 点)**: フルビルド 0 error・0 警告/Tests **915/915**/Oracle **109+2skip(凍結行 無接触=R6)**/
  validate 0/0。
- **R7(セルフゴールデン)**: CaptureHarness へ pending_review 4 面(PD-1〜4)を追加し CAD captures と並置。
  **転写確認**= バッジ様式(琥珀破線+ドット)・由来チップ 3 色・裁定 4 CTA(受け入れる=primary/
  削除する=destructive/保留=左端分離)・候補あり=青カード+「修復で再リンク…」・空状態。
  **ECO-130 面の再並置で暫定差分が mock へ収束**(SC-3= missing 9,860/3.8%・「内容変更 normal→pending」
  「新規 pending」・「適用後、9,860 件が修復対象・140 件が裁定対象」= mock SC-3 と数値・語彙が完全一致)。
  残差分の分類: ①タイトルバー=Window.Title(L1) ②プレビュー=プレースホルダ地(実ファイルなし=
  クローム比較・ECO-109 様式) ③比較テーブル=現在値のみ(旧メタ非保存=「過去状態を厳密に保存しない」
  原則。mock の旧→新 2 値は作画=CAD as-built 注記済み) ④リスト行の未裁定チップ=行末
  (名前セルは列テンプレート内のため。CAD 追記=golden 判断材料) ⑤由来チップ「再出現」= mock 3 種に
  ない 4 種目(spec 遷移 T11 由来・golden 判断材料)。
- **R8(セルフレビュー・fresh context 独立)**: 所見 12 件全処置。
  - **スコープ内欠陥 2= プローブ先行で是正**: ①**修復を開く前の暗黙一段階スキャンを撤去**
    (旧 GF-V4-02。v5.0 では同意なく内容変更を pending 化し REQ-100 を迂回+裁定ダイアログの
    「修復で再リンク…」からも誘発される消費サイト read-across 漏れ。修復は開いた時点の DB を表示=
    golden 基準 7 で担保) ②**pending をモード操作(選択・整理)の対象外に**(CpUiG1PendingGuardTests
    4 本=是正前赤 3 実測→CreateImageItem+ApplyModeTransition〔ECO-114 その場更新経路=面内対称〕+
    HandleItemClick の 3 点ガードで緑転。「選択できるのに実行段で黙殺」の穴を選択段で封鎖)。
  - **スコープ内軽微 8= 全是正**: 裁定ダイアログの N+1(フォルダ単位 1 クエリ化)・サマリー数値パリティ
    (PendInPlace=Updated 計上・deleted 規則 2 の二重計上排除=DeletedUnchanged/DeletedMetaRefreshed/
    PendedWithoutMeta 分離)・StageAsync の AddNormal 到達= throw で封鎖・T14 CASCADE 全範囲の spec
    明文化(workspace_images/merge_compensation_log/Notes=R8 所見 7)・RelinkService の旧前提コメント
    v5.0 化・ステージング再出現の被覆テスト追加・mbom 表記 2 件・(作業/タグ編集の pending 交差=
    ②のガードで同時閉鎖)。
  - **スコープ外 2= cheat-log 記帳**: 初回スキャン中断時の last_scan 更新が v5.0 で pending 崖になる
    (REQ-015 改訂を伴う分離起票候補)・PendingTotal の文言境界。
- **是正後の機械受入(再)**: Tests **915/915**・Oracle 109+2skip・validate 0/0・R7 撮り直し済み。
- **diff 規模**: spec/REQ/E-BOM/M-BOM 改訂・Core 6 ファイル+migration・App 8 ファイル・
  tests 新規 3 クラス+書き換え 4 クラス+lint 台帳 3 件・harness 1 面群・i18n 約 40 キー×2。

## 9. 停止点= golden 合格基準(gate②・実機)

1. **内容変更→pending(v5.0 の核)**: 画像を外部エディタで編集→再スキャン→サマリーに
   「内容変更 normal→pending」→適用。画像は一覧に**未裁定バッジつきで残る**(消えない=INV-010 v5.0)。
   タグチップ絞り込み・ビュー軸に切り替えると現れない(未裁定タグの漏出なし)。
2. **裁定フロー**: ⋯メニュー「未裁定の画像… N」(件数バッジ)→裁定ダイアログ。
   「受け入れる」→normal 化+タグ残存/「別画像として扱う」→タグが外れ新画像として normal
   (作業スペース所属・マージ Undo も消える=T14 の意図的 CASCADE)/「削除する」→ゴミ箱へ
   (タグ保持・復元可)/「保留して次へ」→残る。全件裁定で空状態。
3. **新規=pending**: 再スキャンで追加したファイルが pending(バッジ)になる。リネームは
   missing+pending(候補つき)→裁定ダイアログの青カード「同じ内容の見つからない画像があります」
   →「修復で再リンク…」→relink でタグ引き継ぎ(従来 T4)。
4. **再出現**: missing のパスへファイルを戻す→再スキャン→「再出現 missing→pending」
   (無条件 normal 化しない)。
5. **初回スキャン無傷**: 新規フォルダ登録→全件 normal(pending にならない・段階的公開も従来どおり)。
6. **モード操作のガード**: タグ編集/作業/削除/ファイル操作モードで pending タイルに選択枠が出ず、
   クリックしても選択されない。整理モードでマージ先にもならない。閲覧ダブルクリックのビューアは開ける。
7. **修復の無同意スキャン撤去**: ⋯メニュー「修復」を開くだけでは DB・一覧が変わらない
   (missing 検出は再スキャン〔二段階〕で明示的に)。
8. **スキャンサマリーの新語彙**(ECO-130 面の収束): 「内容変更 normal→pending」「新規 pending」
   「再出現」「候補消失 pending→missing」・適用後作業量「N 件が修復対象・M 件が裁定対象」。
9. **視覚**: pending_review mock(PD-1〜4)との並置確認。判断材料= §8 R7 残差分 5 点
   (Window.Title/プレビュー地/比較=現在値のみ/リスト行末チップ/由来チップ「再出現」)。ja/en 両言語。
10. **回帰**: 初回スキャン・二段階スキャン(破棄=無変更/レッドでも適用有効)・修復 relink・
    ゴミ箱・整理マージ・エクスポート・26 万件の体感(バッジ追加によるブラウズ劣化なし)。

合格なら `/eco-accept eco-129` を指示してください(基準 9 の許容裁定も添えて)。
不合格所見(GF-*)は本 ECO の手順 1 から。

## 10. gate② 所見と是正(GF-129-01・2026-07-21)

- **所見(maintainer 実機・基準 2 の不合格)**: 未裁定 11 件を全件裁定したのに ①⋯メニューの
  「未裁定の画像… 11」が残存 ②一覧の未裁定バッジが残存し裁定済み(修復済み)画像と並ぶ。
  ダイアログ再開は「未裁定の画像はありません」(=DB は正しく更新済み)・ゴミ箱 1 件は正。
- **工程診断**: 実装層の欠陥(仕様・CAD は正)。裁定遷移 T13/T14/T15 と DB は正常。真因=
  裁定ダイアログ閉じ後の `ReloadImagesAsync` が `_allNormal` のみ再取得し **`_allPending` を
  再取得しない**(ECO-129 fix で `LoadContentAsync` には追加したが軽量再読込への read-across 漏れ。
  §3 未検証に書いた「消費サイト棚卸し=grep 悉皆」の残穴)。stale `_allPending` により
  件数バッジ(`PendingCount`)・一覧バッジ(`_pendingEntries`)が残存し、裁定済み画像は
  fresh `_allNormal` と stale `_allPending` の**両母集合へ重複**する。
- **プローブ(R5・是正前赤の実測)**: CpUiG1PendingGuardTests
  「裁定後の再読込で未裁定件数とバッジが消え裁定済みは二重表示しない」= 是正前赤。
  実測は所見より重篤で、重複 ID が `SortFiles` の `ToDictionary` で **duplicate key 例外**
  (ソート経路によりクラッシュ)に至る潜伏欠陥も同時に実証。
- **是正(最小)**: `ReloadImagesAsync` に `GetPendingByFolderAsync` 再取得 1 行を追加
  (`LoadContentAsync` と対)。共有経路のためスキャン適用直後・修復閉じ後・マージ後の
  pending 反映漏れ(同型潜伏)も同時に閉じる。
- **機械受入(再)**: build 0/0・Tests **916/916**(プローブ +1)・Oracle 109+2skip(凍結行無接触)・
  validate 0/0。R7= 視覚変更なし(既存バッジ様式のデータ鮮度のみ)= captures 撮り直し不要。
- **スコープ外所見**: CpUiG6SaveBarTests が同一実行内で 1 回 flake(ObjectDisposedException
  'TestContext'・headless dispatch・再実行で緑)= 51-cheat-log へ記帳(検査器ライフサイクル系)。

## 11. クローズ(2026-07-21 golden 合格)

- **実機確認(maintainer・§9 基準 10 点)**: 実コレクションで
  内容変更→pending+バッジ・裁定 4 操作(未裁定 11 件を全件裁定=受け入れ/再リンク/削除 1 件ゴミ箱)・
  新規/再出現・初回無傷・モードガード・修復無スキャン・新語彙・視覚並置 ja/en・回帰。
  gate② 中に **GF-129-01 を顕在化**(§10)→ プローブ先行 1 行是正(`879da14`)→ 再確認で合格。
  golden 中の仕様確認 2 件も正と裏取り: ①移動= missing+pending の併発(pending 側から再リンクで
  同時解消が正ルート) ②missing の一覧非表示= INV-010 どおり(修復の管轄・不具合ではない)。
- **基準 9 の許容裁定**: 判断材料 5 点(Window.Title=L1/プレビュー=プレースホルダ地/比較テーブル=
  現在値のみ/リスト行の未裁定チップ=行末/由来チップ「再出現」= 4 種目)は合格報告をもって許容と裁定。
  CP-UI-G1 へ「以後の golden で差分扱いしない」込みで刻印。
- **再発防止**: CP-UI-G1 へ pending 意味論の観点+**GF-129-01 潜伏実績(母集合の取得サイトを拡張したら
  全再読込経路へ read-across)**を刻印。機械 pin= CpPendingSemanticsTests(8+3 本)・
  CpUiG1PendingGuardTests(ガード 4 本+鮮度 probe)・GfPendingReviewVisualParityTests(5 本)。
- **M4 同期**: 不要 — REQ-101・E-UI-PENDING-049・M-UI-PENDING-054・spec §2.1/§2.11.0/§2.11.7・
  INV-010/015 v5.0・migration 010 は fix 時に登記済みで as-built 乖離なし。
- **教訓**(read-across 明記):
  1. **母集合の取得サイトを拡張したら、同じ母集合を再取得する全経路を悉皆する** — GF-129-01 は
     `LoadContentAsync` に pending 取得を足して `ReloadImagesAsync` を漏らした消費サイト棚卸しの残穴。
     「フル読込」と「軽量再読込」の二系統がある VM では、母集合の追加は必ず両系統への同時適用が単位。
     ECO-079 の「XAML 層と VM 層の 2 層漏れ」・ECO-108/109 の「面間複製の同時追随」と同族=
     **変更の単位は『症状の場所』でなく『複製・分岐している構造の全肢』**(BomDD 昇格候補・既存
     playbook §8.3 read-across 節の「取得経路の分岐」への拡張)。
  2. **裁定/編集系モーダルの鮮度契約は「閉じ後の呼び出し側再読込」に置き、モーダル内の局所状態更新を
     信用しない** — ダイアログ自身は正しく空になり DB も正しく、呼び出し側だけが stale という
     「三者不一致」は、閉じ後再読込が形だけ(不完全な再取得)でも成立してしまう。プローブは
     モーダル往復を含む VM 統合レベルで書く(ダイアログ単体テストでは捕捉不能だった)。
  3. **凍結オラクル無接触の中核意味論変更は 2 例目**(130 に続き)— 判定器の純粋関数化と
     「判定/適用の分離」が効いており、v5.0 級の意味論改訂でも旧 exact 行と非衝突にできる。
     skip 化裁定は 3 ECO 一括で得たが実際の行使は 128 の S-29 のみ(見込み)。

## 7. gate① 裁定記録(2026-07-21・maintainer)

1. **実施順序= 130→129→128 を承認**(ECO-130 先行=適用前確認の器が先)。
2. **初回スキャンの新規= 初回のみ normal(案 ii)**。フォルダ登録という明示操作を裁定とみなし、
   初回スキャン(isInitialScan)の新規登録だけ normal。**通常スキャンの新規は pending**(設計まとめ
   どおり)。26 万件登録の裁定爆発を回避しつつ「再出現しただけで無条件 normal にしない」原則は維持。
3. **pending の可視化= 既定一覧に出す+未裁定バッジ(INV-010 改訂)**。外部編集しただけで画像が
   一覧から消える UX を回避する。ビュー評価・NodeGraph・類似検索・エクスポート等の各消費サイトでの
   pending の扱いは spec 改訂時に面ごとに規定(必要な追加裁定は spec 改訂中に個別提示)。
   バッジの視覚は CAD mock で確定。
4. **固定オラクル旧行の処置(3 ECO 一括)= skip 化+理由刻印+新意味論の新規行追加**
   (ECO-130 §7 と同一裁定。対象= OC-5 exact 行・S-01 の旧前提部ほか、spec 改訂時に全数特定)。
5. 残 gate①= CAD mock(pending 裁定 UI= 受入れ/別画像/削除/保留+一覧バッジ)。受領後に /eco-fix eco-129。
