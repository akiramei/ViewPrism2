# Change Order — ECO-098(applied・クローズ): 画像タブゴミ箱がdeleted以外を全件materializeする

> ECO-097 gate②の実機確認中にmaintainerが知覚した、削除→ゴミ箱表示→復元の引っ掛かりを
> 2026-07-16の指示 `/eco-file 画像タブのゴミ箱がdeleted以外を全件materializeし、表示6枚でも
> 26万missingにより約1.1秒停止する` で正式な工程診断へ移した。P2相当の既存性能欠陥。

## 1. 症状と再現(事実/疑いの分離)

### 1.1 maintainer観測

- 画像6枚と見えるコレクションで、画像のソフト削除、ゴミ箱の確認、復元操作に軽い引っ掛かりがある。
- 反応は概ね1秒以内だったが、表示件数6件に対する感覚として不自然。他コレクションに約26万件あるため、
  コレクション境界を越えたアクセスを疑った。

### 1.2 実DB読み取り専用実測(2026-07-16)

- `%APPDATA%/ViewPrism2/viewprism2.db`を`mode=ro`で調査。画面上normal 6件のコレクションは、DB上では
  `normal=6 / missing=262,045 / deleted=0 / total=262,051`だった。別にnormal 262,046件のコレクションも存在する。
- 現行`GetByFolderAsync`相当の全列クエリを同一DBで5回実行:
  - 262,051行materialize、first 1,085.4ms / median 1,133.8ms / max 1,237.7ms。
  - query plan=`idx_images_folder_status(sync_folder_id=?)`検索+`ORDER BY id`一時B-tree。
- 同じコレクションを`sync_folder_id + status='deleted'`で限定すると、ウォーム時1ms未満
  (初回44.6ms・0行)。normal 6件の通常再読込も1ms未満。
- **確定**: 別コレクションIDの画像は取得していない。しかし同一コレクション内のstatus境界を越え、
  ゴミ箱に不要なnormal/missing全行を取得してからVMでdeletedだけを選別している。
- **未検証**: 「ゴミ箱へ移動」ボタン単体の知覚遅延はこの全件取得を直接通らない。操作列全体の印象か、
  単一DB接続ゲートの競合かは、`/eco-fix`の操作別プローブで切り分ける。

## 2. 工程診断(R2) — CAD/BOM健全、実装のpredicate pushdown漏れ

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **健全** | `docs/screens/image_tab.md`は画像タブ内ゴミ箱、選択コレクションの対象、復元/完全削除を確定済み。IMG-019は巨大profileで全件ImageRecord materializeを避ける設計原則も確定している。本件に新しいUX/視覚判断はない。 |
| BOM | **健全(検査規模の谷間あり)** | `20-spec.md` §2.10.5は「選択中コレクション内のdeleted画像の一覧と件数のみ」、E-UI-REPAIR-039/M-UI-TRASH-032はdeleted一覧とポップアップ操作を宣言する。結果契約は正しいが、CP-UI-G1/CpUiG1TrashPopupTestsは数件fixtureだけで、hidden missing大量時のDB materialize境界を検査しない。 |
| 実装 | **逸脱・真因** | `ImageTabTrashViewModel.LoadTrashItemsAsync:110-112`が`GetByFolderAsync(collectionId)`で全statusをmaterializeし、その後LINQで`Deleted`へ絞る。`IImageRepository.GetByFolderAsync`はスキャン用の「全行(全ステータス)」API。複合索引`idx_images_folder_status`があってもstatus条件を渡さないため262,051行を返す。 |

- 工程欠陥は**表示結果のスコープではなく、DB→VM境界での取得量**。CAD裁定は不要。
- 未確定IMG/FL/VEとの関係なし。ゴミ箱の対象・表示・復元意味論は既決で、変更しない。
- `DatabaseManager`はアプリ全体で単一接続+`SemaphoreSlim(1,1)`のため、長い全件materialize中は
  他DB操作も待機する。ただしこれは遅延を波及させる構造であり、今回の一次真因は不要行を返すクエリである。

## 3. 切り分け済みの事実

### 3.1 発火経路

- ゴミ箱を開く: `OpenTrash`は`LoadTrashItemsAsync`完了を待ってから`TrashOpen=true`にするため、
  262,051行の取得中はポップアップ自体が現れない。
- 復元: `RestoreSelectedTrash`はstatus更新→normal再読込→`LoadTrashItemsAsync`→件数更新の順で、
  復元後にも同じ全件materializeを行う。
- 完全削除/ゴミ箱を空にする: 実行後に同じ`LoadTrashItemsAsync`を行う。
- ゴミ箱へ移動: PKで対象取得/status更新後、`GetNormalByFolderAsync`+normal画像タグ+deleted件数を再読込する。
  こちらはstatus/collection限定済みで、現時点では262,045 missingの直接materializeは見つからない。

### 3.2 安全先例と水平展開

- 作業タブは`WorkspaceRepository.GetDeletedImagesAsync(workspaceId)`でworkspace+`status='deleted'`を
  SQL条件にし、ファイル名順もDBで確定する。同じゴミ箱UI意味論の安全先例。
- `ImageRepository`にも`GetNormalByFolderAsync`と`CountByFolderAndStatusAsync`があり、ECO-064で
  normal一覧/ゴミ箱badgeのpredicate/aggregation pushdownは既に導入済み。deleted**一覧**だけが漏れた。

### 3.3 混入・潜伏・マスキング

- 混入=`2c589d8`(ECO-019、画像タブ内ゴミ箱ポップアップ初版)。
- `fdcf5d9`(ECO-036/1)の子VM切り出しは挙動不変で全件取得を移送。
- `39b0b17`(ECO-064)は巨大profile向けにnormal contentとdeleted countを限定したが、
  `LoadTrashItemsAsync`のread-acrossを漏らした。
- `CpUiG1TrashPopupTests`はnormal/deleted各0〜3件で結果の正しさだけを検査するため、
  26万missingを捨てて同じdeleted結果を返す過剰取得を区別できなかった。
- 実データの262,045 missingはパッケージ取り込み等で許容される状態形状。画面のnormal件数だけを
  性能fixture規模とみなすと、hidden status行が検査から落ちる。

### 3.4 未検証事項(`/eco-fix`で確定)

- 削除ボタン単体を操作段階別に計測し、知覚遅延が残るか。現在コードからは全件取得を確認できない。
- 修復画面等の別`GetByFolderAsync`利用は全statusが意味上必要な経路であり、本ECOへ混ぜない。
- 固定時間閾値は環境依存のため主受入にしない。呼出API/返却行集合を決定論的にpinし、実DB相当規模は
  探索値として補助記録する。

## 4. 是正方針(着手時確定)

| 案 | 内容 | 評価 |
|---|---|---|
| **A: deleted専用repository query(推奨)** | `IImageRepository/ImageRepository`へcollection+deleted限定・file_name安定順の取得APIを追加し、`LoadTrashItemsAsync`はそれだけを消費する。 | 真因のpredicate pushdown漏れを除去。既存複合索引を利用し、VMの後段filter/sortも消せる。最小でWorkTab先例と対称。 |
| B: status汎用query | `GetByFolderAndStatusAsync(folder,status)`を追加しdeletedを渡す。 | 再利用性はあるが、現時点でnormal専用APIが既存。不要な汎用化を増やす。 |
| C: VMキャッシュ/増分更新 | ゴミ箱一覧をcontent load時に保持し、操作ごとに増減する。 | stale/通知/復元missing分岐を複雑化し、DB取得境界の真因を残すため不採用。 |

- `/eco-fix`ではまず既存repository spyを使い、ゴミ箱open/復元後に
  `IImageRepository.GetByFolderAsync`が呼ばれないことを是正前赤で実測する。
- fixtureは同一collectionにnormal少数+missing大量+deleted少数を置き、返却/描画対象はdeletedだけ、
  file_name順、件数一致を固定する。壁時計だけを合否条件にしない。
- read-acrossはopen/restore/purge/empty(同じload関数)と作業タブ既存`GetDeletedImagesAsync`の不変確認。

## 5. 影響BOM

- Core interface: `IImageRepository`(deleted限定一覧API)。Core状態遷移`TrashService`は不変。
- Infrastructure: `ImageRepository`(collection+status predicate、DB file_name安定順、既存索引利用)。schema変更なし。
- Surface: `ImageTabTrashViewModel.LoadTrashItemsAsync`。XAML/文言/配置/視覚は不変見込み。
- Tests: `CpUiG1TrashPopupTests`または`CpUiG1CollectionScopeTests`へrepository呼出境界probe、
  repository integrationへdeleted限定/順序/他status非materializeベクタ。
- E/M/CP: E-UI-REPAIR-039/M-UI-TRASH-032のas-built取得契約、CP-UI-G1へhidden status大量時の
  predicate pushdown潜伏実績を追加。REQ/CAD意味論は変更不要。
- WorkTab、DB schema、i18n、XAML/style、固定Oracle行は変更しない(R6)。

## 6. ゲート完了

1. **gate①不要**: CAD/BOMの結果契約は健全で、案Aは実装の取得境界是正。製品判断なし。
2. **`/eco-fix eco-098`完了**: 先行probe→案A最小是正→機械受入4点合格。UI視覚差分なしのためR7並置対象外。
3. **gate②完了**: 2026-07-16、maintainerが`/eco-accept eco-098`で実機golden合格を明示。
4. 残ゲートなし。

## 7. `/eco-fix`実施記録(2026-07-16)

### 7.1 プローブ先行(R5)

- `CpUiG1TrashPopupTests`へrepository spyを追加し、同一collectionに`normal=1 / missing=256 /
  deleted=2`、別collectionに`deleted=1`を配置した。
- 是正前にECO-098 probeだけを実行し、ゴミ箱open後の`GetByFolderAsync`呼出が
  **期待0回 / 実際1回**で1/1不合格。結果が正しいままhidden statusを全件取得する真因を実測裏取りした。
- 是正後は、削除モードの「ゴミ箱へ移動」→ゴミ箱openを連続実行しても`GetByFolderAsync=0回`、
  `GetDeletedByFolderAsync=1回`。別collection/normal/missingは混入せず、対象collectionのdeletedだけが
  file_name順で返ることを1/1合格へ緑転した。
- 削除ボタン単体は全status APIを通らないことを決定論的に確認した。goldenで単体の引っ掛かりが残る場合は、
  本件のhidden missing materializeとは別真因としてR3分離する。

### 7.2 裁定と是正

- **案Aを採用**。現存consumerに必要な用途だけを表す`IImageRepository.GetDeletedByFolderAsync`を追加し、
  `ImageRepository`で`sync_folder_id + status='deleted'`をSQL predicateへ押し込んだ。
- 並び順はDB側の`ORDER BY file_name COLLATE NOCASE, id`で安定化。既存
  `idx_images_folder_status(sync_folder_id,status)`を利用し、schema/migrationは変更しない。
- `ImageTabTrashViewModel.LoadTrashItemsAsync`は専用APIだけを消費し、全件取得後のLINQ filter/sortを撤去。
  open/restore/purge/emptyは同じload関数へ収束しているため一括して是正される。
- 製品・テスト・BOM差分は機械受入前で9ファイル、`+100/-12`。XAML/style/i18n/CAD/REQ/Core状態遷移/
  WorkTab/固定Oracle行は不変。横断規約(K-AVALONIA)に関わる新規文言・surface変更なし。

### 7.3 BOM/CP同期とread-across

- E-UI-REPAIR-039へDB境界のdeleted限定、M-DB-007へ専用read model、M-UI-TRASH-032へ
  全status API退行禁止、CP-UI-G1へhidden status規模の潜伏実績とexact呼出probeを追記した。
- 既存の復元/完全削除/空操作テストは同じ`LoadTrashItemsAsync`を通して全緑。作業タブの
  `WorkspaceRepository.GetDeletedImagesAsync`は既にworkspace+deleted限定で変更不要。
- R7: 視覚・レイアウト・XAML・文言を変更していないためCAD captures並置の対象外。

### 7.4 機械受入

- `dotnet build`: **0 warning / 0 error**。
- `dotnet test tests/ViewPrism2.Tests`: **755/755合格**(ECO-098 probeを含む)。
- `dotnet test tests/ViewPrism2.Oracle`: **109合格 / 2 skip / 0不合格**。既存固定Oracle行は変更なし(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。

## 8. クローズ記録(2026-07-16)

### 8.1 実機golden

- approver: maintainer。
- normal 6件+hidden missing 262,045件級のコレクションで、画像のゴミ箱移動、ゴミ箱open、復元、
  完全削除後の再表示が引っ掛からず、deleted対象・件数・file_name順が正しいことを承認。
- 復元/完全削除を通じて画像ファイルの物理非破壊が維持され、表示・文言・レイアウトに意図しない変更が
  ないことを承認。golden所見なし。

### 8.2 再発防止

- CP-UI-G1へ、画像タブの削除実行とゴミ箱open/操作後reloadが全status`GetByFolderAsync`へ退行せず、
  `collection + deleted`専用queryだけを消費する観点を潜伏実績つきで刻印した。
- 機械側は`CpUiG1TrashPopupTests`で、同一collectionのnormal少数+missing多数+deleted少数と
  別collection deletedを同時配置し、全status API 0回・専用API exact 1回・対象非混入・安定順をpinする。
- E-UI-REPAIR-039、M-DB-007、M-UI-TRASH-032にも同じ取得境界を同期済み。

### 8.3 教訓とread-across

一覧結果が正しいことだけでは、取得境界の正しさは証明できない。表示対象外statusを後段で捨てる実装は、
小fixtureでは機能等価に見えても、hidden行の規模が大きい実profileで初めて停止として顕在化する。
DB→VM境界を持つ一覧では、結果集合に加えて**不要行をmaterializeしないことをrepository呼出/API単位でpin**し、
openだけでなくrestore/purge/emptyなど同じ再読込関数の全発火点をread-acrossする必要がある。本件は
ECO-064のnormal一覧/count限定がdeleted一覧へ水平展開されなかった例であり、件数表示と実DB総行数の
乖離を性能fixtureの独立次元としてCPへ残す。

### 8.4 M4同期・残課題

- VM/Repository内部の取得境界是正で、CAD/REQ/挙動仕様/視覚契約は不変。E/M/CPのas-built取得契約は
  fix時に同期済みで、`20-spec.md`および`35-dsbom`の追加更新は不要。
- DB schema、Core状態遷移、WorkTab、i18n、XAML/style、固定Oracle行は不変。
- 削除ボタン単体は全status API非呼出を機械確認済み。将来そこだけに遅延が再現する場合は別真因としてR3分離する。
- 教訓は一般化可能だが、既存BomDDのR5プローブ先行・read-across・CP潜伏実績の規律で扱えるため、
  方法論リポへの即時昇格は不要。将来、性能CPに「返却結果だけでなく取得量境界」を必須化する際の候補とする。
