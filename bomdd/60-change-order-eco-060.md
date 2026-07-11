# Change Order — ECO-060(applied): スキャン中コレクションの段階的公開 — fully-hashed batchの先行利用

> ECO-059 golden後のmaintainer提案を受け、2026-07-11に起票・工程診断した機能拡張。
> 本ECO起票では`src/tests`を変更しない。ECO-059は狭義目標（大規模スキャンのDBバッチ化・
> UI caller非占有）を達成して`2c485a3`で先にクローズ済み。本件をR3に従い分離する。

## 1. 要求（maintainer・2026-07-11）

約260,000件のHDD初回スキャンはECO-059後も35分を要する。UI自体は操作可能になったが、
コレクションの画像を利用できるのは全スキャン完了後であり、長時間処理の途中成果を活用できない。

狙いは次のとおり。

- コレクションに永続的な処理状態を持たせる（未スキャン／スキャン中／利用可能・後処理中／完了／一部エラー）。
- スキャン中も、準備できた画像を順次コレクションへ公開する。
- 公開済み画像ではフォルダ・ファイル名閲覧、タグ編集、作業、条件整理を先行利用可能にする。
- 類似画像検索はスキャン中に部分母集合を黙って検索せず、**無効化して「スキャン完了後に利用できます」と表示する**（maintainer承認済み）。
- 遅延処理はファイルごとの状態を内部保持しつつ、UIへ26万件の個別イベントを流さず、batch/coalesce通知と明確なcollection-level完了イベントを持つ。
- `images.hash NOT NULL`を維持した特殊値案も候補から除外しない。NULL・特殊値・別状態列を総合比較する。

## 2. 工程診断（R2）

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD（ViewPrismUI） | **状態／途中利用契約が未定義・先行裁定が必要** | `docs/screens/image_tab.md`は追加・スキャン・削除入口と「初回スキャンで画像を取り込む」までを定義するが、未スキャン／スキャン中／部分利用／完了の行表示、操作可否、進捗の永続表示、途中項目の並びを定義しない。モックは管理操作自体を視覚のみとしていた旨も明記する |
| 要求／仕様 | **現行は完了サマリ契約のみ** | REQ-011〜015／仕様§2.1は各ファイル判定、SHA-256、状態遷移、完了時last_scan+summaryを固定する。仕様§2.6はコレクション行の「スキャン中表示」を述べるが、途中成果の閲覧・編集・検索可否は未定義 |
| E/M-BOM・CP | **段階公開部品と受入が存在しない** | E-SCAN-005/M-SCAN-005/CP-SCAN-004はscan coreの正しさ・batch性能・caller非占有を担保するが、application-level ScanCoordinator、collection lifecycle、batch公開、partial UIの操作行列は持たない |
| 実装 | **途中DB commitは存在するがUIへ公開しない** | ECO-059で最大512変更をtransaction commitする。ただし`FileSystemEnumerable.ToList()`で全列挙後に処理し、scan batch完了イベントはない。管理画面はmodalでscan状態をwindow-local VMが所有し、画像タブは管理画面を閉じた直後に1回だけ`InitializeAsync`する。閉じた後の完了・batchを再読込する経路がない |

### 2.1 診断分岐

- 既存実装逸脱ではなく、CAD・状態モデル・永続化・操作可否を追加する**機能拡張**。
- ViewPrismUIでcollection lifecycleと途中利用surfaceを裁定してから、要求→E/M-BOM→CP→実装へ進む。
- `images.hash`未確定表現はUIだけで決められない。再リンク・条件検索・pHash freshness・索引・migrationへ波及するCore/DB裁定として本ECOで比較する。

## 3. 切り分け済みの事実

### 3.1 確定

1. **サムネイルはすでに遅延生成**: `ThumbnailImage`が仮想化セルの実体化時だけ`ThumbnailService.GetOrCreateAsync`を呼ぶ。生成前・失敗時は`?`プレースホルダのまま。全画像サムネイル生成は現行scanの35分に含まれない。
2. **類似pHashも既にon-demand**: `SimilaritySearchService.GetOrComputeFeatureAsync`が特徴量なし／stale時に検索経路で計算する。スキャン中に類似検索を許すと「準備中ダミー」ではなく、単に未公開画像を候補母集合から落とした不完全結果になるため、承認済みの無効化が安全。
3. **SHA-256は現行画像行の必須値**: `images.hash TEXT NOT NULL`、`ImageRecord.Hash`非nullable。ScanJudge新規／更新、criteria hash一致、relink同一性、image_features freshnessが消費する。
4. **ECO-059 batchは再利用可能**: 512件ごとに原子commit済みで、fully-hashed batchをそのまま先行公開する案はDB migration不要。
5. **通知ownerが不足**: ScanServiceは`IProgress<int>`しか返さず、FolderManagementViewModelのwindow-local rowへ百分率を送る。collection row・画像タブ・作業タブへ状態をfan-outするapplication serviceはない。

### 3.2 未検証／裁定待ち

- 実HDDで、全列挙完了までの時間と最初の512件hash+commitまでの時間。fully-hashed batch公開だけでtime-to-first-useが十分短いか。
- スキャン中の並び: 現在sortへ逐次挿入して既存項目が動くか、取込順append+完了時最終sortとするか。
- アプリ終了・異常終了後に`scanning`を`interrupted`として復旧する永続状態と再開規則。
- metadata-first二段階を採る場合のhash未確定表現（§4.2）。
- スキャン中に許可する操作の境界。タグ編集・作業・条件整理・viewerは許可候補。類似検索は無効で確定。コレクション削除／include/exclude変更／同一collection再スキャンは安全上無効がlean候補。

## 4. 設計選択肢（gate①）

### 4.1 公開パイプライン

#### 案A — fully-hashed batchの段階公開（lean推奨）

- 列挙をstreaming化し、total確定前は「検出N件／利用可能M件」の件数進捗を表示する。
- 現行どおり各ファイルのSHA-256とScanJudge判定を完了し、512件commitごとに`ScanBatchCommitted`を発行する。
- 公開された行は現行不変条件をすべて満たすため、タグ・作業・条件整理・viewerを即時利用可能。
- DB schema／hash意味論／relinkを変えない。最初の利用可能画像は「列挙開始後、最初のbatch commit時」。
- diff規模: 中（ScanCoordinator+streaming scan event+画像タブ incremental consumer+collection state surface）。

#### 案B — metadata-first登録＋SHA-256後処理

- 列挙メタ（path/name/size/mtime）を先に全件またはbatch登録し、数秒〜列挙時間で全ファイル名・フォルダを公開する。
- SHA-256を単一HDD向けworkerで後処理し、ファイルごとのhash stateを更新する。
- time-to-all-metadataは最短だが、hash未確定行をnormal閲覧・タグ付与可能にする新状態機械が必要。rescan/relink中に後からmissing一致が判明した場合のID・タグ保全も裁定が要る。
- diff規模: 大（schema migration+Core nullable/sentinel監査+状態機械+検索/修復+復旧+UI）。

### 4.2 案Bのhash未確定表現

| 候補 | メリット | デメリット／必須ガード |
|---|---|---|
| B1: `hash NULL` | 「未確定」を値の不在として自然に表現。実SHA値との衝突なし。SQL `IS NULL`が明確 | `NOT NULL`解除はSQLite table rebuild migration。`ImageRecord.Hash`と全consumerをnullable化し、NULL比較・freshness・serializationを全監査 |
| B2: 特殊値のみ（例`''`/`pending`） | `NOT NULL`を維持できmigrationが小さい。既存型のnullable波及を抑制 | 64hexという値域不変条件を破る。filter漏れでrelink/criteria/pHash freshnessへ偽の同値を作る。64個の`0`は理論上有効SHA-256なのでsentinel不適。DBが状態を自己記述できない |
| B3: `hash_state`＋特殊値＋CHECK（本格案の推奨候補） | `NOT NULL`を維持しつつpending/ready/errorを明示。`ready→64hex`、非ready→専用sentinelのCHECKで二重状態をDB拘束可能 | 列追加migrationと全クエリの`hash_state='ready'`ガードが必要。hashとstateの二列更新を必ず同一transactionにする |
| B4: `hash_state`＋nullable hash | 意味論が最も直接的でDB制約も表現しやすい | B1のtable rebuild/nullable波及+B3の状態列を両方負担。安全だがdiff最大 |

特殊値は一律に悪いとは判定しない。採る場合は、実SHA値域外であること、状態列またはCHECKで未確定を自己記述すること、hash consumerがready限定であることを固定オラクル化する。

### 4.3 遅延完了イベント

- ファイル単位: DBに`pending/running/ready/error`を保持（案Bのみ）。UIへ個別26万イベントは送らない。
- batch単位: `ScanBatchCommitted(folderId, ids/counts, discovered, ready)`を発行し、UI側で250〜1000msにcoalesce。
- collection単位: `ScanStarted`／`DiscoveryCompleted`（案B）／`ScanCompleted`／`CompletedWithErrors`／`Interrupted`を明確なmilestoneとして発行・永続化。
- UIの類似検索入口はcollection-level `ScanCompleted`でのみ活性化し、非活性時は「スキャン完了後に利用できます」。

## 5. 影響BOM（裁定後に同期）

- CAD: `ViewPrismUI/docs/screens/image_tab.md`、必要ならモック／`review_points.md`にcollection lifecycle・状態表示・操作行列を追加。
- Requirements: REQ-010/015/053拡張または新REQ（段階公開・永続scan state・partial operation matrix）。
- E-BOM: E-SCAN-005、E-DB-010、E-UI-SHELL-021、E-UI-BROWSE-022、E-UI-SIMILARITY-035。net-new ScanCoordinator部品候補。
- M-BOM: M-SCAN-005、M-DB-007、M-UI-IMAGETAB-035、M-UI-ORGANIZE-034。案Bならmigration 007候補。
- CP: CP-SCAN-004（batch event/order/restart）、CP-DB-006（schema同値/CHECK）、CP-UI-G1（collection state/partial browse）、CP-UI-G9（類似検索gate）。
- 固定Oracle: 案Bでhash意味論・状態遷移を変える場合のみ新規行追加。既存行は改変しない（R6）。

## 6. gate①裁定（2026-07-11・完了）

maintainerは**案Aから実施し、sortは完了時**と裁定した。ViewPrismUIはこの裁定を
`1ffed5bbdbb50dd313ddb5f2c11f4c57e3da559f`でCAD原器へ反映済み。

- SHA-256計算とScanJudge判定を完了したnormal画像だけを、最大512変更のDB commit成功後にbatch公開する。
- スキャン中は公開batchを取込順で末尾へappendし、sort条件を変更しても再配列しない。
- 完了時に最新のsort条件を全画像へ1回適用する。sort未設定なら既定順へ追加sortしない。
- 公開済み画像の閲覧・タグ編集・整理・作業・条件検索は利用可能。類似画像検索だけを無効化し、
  操作時に「スキャン完了後に利用できます」と表示する。
- 進捗ポップアップを閉じてもスキャンとbatch公開は継続し、画像タブ内にスキャン中表示を残す。
- サムネイルは既存の可視セル遅延生成を維持し、batch公開条件に含めない。
- metadata-first、hash NULL/特殊値、`hash_state`、公開後の遅延SHA-256は本ECOで採らない。

CAD側の残課題IMG-016（表示部品・位置・進捗粒度・再表示導線）とIMG-017（再スキャン差分、
キャンセル、失敗/再試行、複数競合）のうち、製造に必要な最小表示はgoldenで確認する。
IMG-017の意味論は未設計のため、本ECOで新規に確定・製造しない。

## 7. 製造境界と受入

- DB schemaと`images.hash NOT NULL`は不変。
- ECO-059の`ApplyScanBatchAsync`成功直後を唯一の公開境界とし、commit失敗batchは通知しない。
- 初回スキャンはファイル列挙をstreaming化し、全件列挙完了を最初の公開より前提にしない。
- application-level `ScanCoordinator`がstarted/batch/completedをfan-outし、管理windowの寿命から切り離す。
- 画像タブは選択中collectionのbatchだけを増分反映し、完了通知でDBを再読込して最新sortを適用する。
- 自動受入はCP-SCAN-004（commit後通知・順序・失敗非公開）とCP-UI-G1/G9 unit部分
  （段階append・完了sort・類似gate）を追加する。既存固定Oracle行は変更しない。

## 8. 製造結果（2026-07-11・golden approved）

### 8.1 実装

- `ScanService`の対象列挙を`ToList()`からstreamingへ変更し、SHA-256と判定を直列のまま実行する。
  総件数を先に確定しないため、管理windowの進捗バーはindeterminateとし、既存percent契約は完了時100を維持する。
- `ScanBatchCommitted`を追加し、`ApplyScanBatchAsync`成功後だけ、そのtransactionに含まれたnormal追加行を
  取込順で通知する。rollback/例外batchとpendingは通知しない。
- singleton `ScanCoordinator`がscan状態をwindowから分離し、started/batch/completedを画像タブへ配信する。
  管理windowを閉じてもcommand continuationとcoordinatorは生存し、scanと公開を継続する。
- 画像タブは新規batchだけを`ObservableCollection`末尾へ追加する。batchごとの全Items再構築は行わず、
  既存一覧・スクロール位置・選択を保持する。開始時と完了時だけ全体を再計算する。
- スキャン中のsort操作は条件だけ更新する。completedでDBを再読込し、最新sortがあれば全体へ適用する。
  sortなしなら最終取込順を保持する。
- コレクション行と中央ペインに「スキャン中」を表示する。類似方式の検索ボタンは無効相当の視覚を持ちつつ
  clickを受け、「スキャン完了後に利用できます」と表示する。条件方式はgateしない。

### 8.2 R5プローブと機械受入

- 先行赤: `ScanBatchCommitted`/`ScanCoordinator`が存在せず、新規プローブはcompile errorとなった。
- CP-SCAN-004追加: 513画像が`[512,1]`の2通知、全行64hex/normal/commit済み、重複なし。
  commit例外batchは通知0・DB行0。
- CP-UI-G1/G9追加: 第2batchを停止した状態で先行512件を表示。名前降順を選んでも順序不変、
  類似検索は実行せず案内表示。解放後513件へ増え、completedで名前降順を全件適用。
- `dotnet build --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Tests`: **580/580 pass**（ECO-060新規3件）。
- `ViewPrism2.Oracle`: **109 pass / 既知2 skip**。既存固定Oracle行は無変更。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。

### 8.3 golden gate（maintainerが行うこと）

1. 未スキャンの大規模collectionでスキャンを開始し、管理windowを閉じる。
2. 画像タブで「スキャン中」表示が残り、最初の512件commit以降、件数と画像が段階的に増えることを確認する。
3. 公開済み画像で閲覧・タグ編集・整理・作業ができ、UIが操作不能にならないことを確認する。
4. スキャン中に名前降順などへsort条件を変更し、その場では既存項目が動かず、完了時に全件が選択条件で
   並ぶことを確認する。
5. スキャン中に整理→類似画像検索を操作し、「スキャン完了後に利用できます」と表示され検索が始まらないこと、
   完了後は同じ入口から類似検索できることを確認する。
6. 可能なら実HDD約26万件で、スキャン開始から最初の画像利用可能までの時間と、完了件数を記録する。

### 8.4 golden結果（2026-07-11・maintainer実機）

- 隔離profileの実HDD約262,046件で確認し、スキャン開始から最初の画像利用可能まで**10秒以内**だった。
- 管理windowを閉じてもスキャンと段階公開が継続し、公開済み画像の操作、スキャン中のsort保留、
  完了時sort、類似検索gateと完了後の入口解禁を確認した。
- 完了後の類似検索は開始可能であり、本ECOのgate解除要件を満たす。初回検索が表示中フォルダ701件ではなく
  collection全体262,046件を対象にpHashを遅延生成するため長時間となる所見は、既存の検索母集合・
  性能/進捗UXに属し、本ECOの回帰または不合格とはしない（別ECO候補）。

### 8.5 教訓

- 長時間処理は総完了時間だけでなく、**最初の有用な成果が利用可能になるまでの時間**を独立特性として測る。
  ECO-059の35分という総時間を変えず、ECO-060はtime-to-first-imageを10秒以内へ短縮した。
- golden中の探索所見は、当該ECOの受入特性と因果を切り分ける。類似検索の既存性能問題は隠さず記録する一方、
  本ECOの「スキャン中gate／完了後の入口解禁」と混同せずR3で分離する。
- UI表示件数と処理母集合は一致するとは限らない。性能所見には表示フォルダ件数だけでなく、collection全体件数と
  cache状態を併記する。

以上によりCP-UI-G1/G9の本ECO追加観点をmaintainerが承認し、ECO-060を`applied`としてクローズする。
