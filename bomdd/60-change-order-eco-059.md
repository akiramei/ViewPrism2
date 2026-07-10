# Change Order — ECO-059(applied): 26万画像のHDD初回スキャンが長時間化 — 1件1トランザクションと過剰進捗通知

> maintainer の性能改善要求を受け、2026-07-11 に起票・工程診断した。
> 起票時点では `src/tests` を変更せず、既存仕様・BOM・実装・履歴を突合した。

## 1. 症状（maintainer 報告・2026-07-11）

- HDD 上の約260,000画像を含むフォルダをコレクションとして追加し、初回スキャンすると相当時間が掛かった。
- 端末、画像総バイト数、ファイルサイズ分布、実時間、DB配置ドライブは未採取であり、壁時計の内訳は未確定。
- 本ECOは画像ファイルを読み取るスキャン本体を対象とする。スキャン完了後の画像タブ再読込・サムネイル生成は別区間であり、混同しない。

## 2. 工程診断（R2）

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD（ViewPrismUI） | **非該当・裁定不要** | スキャンの永続化粒度と進捗通知頻度は非表示の実装詳細。画面構造・文言・操作意味論を変更しない |
| 要求／仕様 | **機能意味論は健全** | REQ-011〜015／仕様§2.1は対象拡張子、判定順、SHA-256全体読取、状態遷移、サマリを固定する。SHA-256全体読取は省略不可。一方、DB書込トランザクション粒度と同一進捗値の通知回数は固定していない |
| E/M-BOM | **性能不変条件と検査が不足** | E-SCAN-005/M-SCAN-005/CP-SCAN-004は判定・状態遷移のexact検査のみ。大量新規画像を1件ずつ自動コミットしない、進捗通知を百分率変化時に限定する、というスケール事故のガードがない |
| 実装 | **細粒度I/O構造が長時間化要因** | `ScanService` は新規1件ごとに `IImageRepository.AddAsync` をawaitする。`ImageRepository.AddAsync` は各回 `DatabaseManager.RunAsync` 上でtransactionなしの単一INSERTを実行するためSQLiteの暗黙トランザクションが画像件数分発生する。260,000件なら260,000 INSERT/暗黙commitとなる。さらに `progress.Report` を全ファイルで呼び、整数百分率が同じでも最大260,000件のUI dispatchを生成する |

### 2.1 診断分岐

- 既定のスキャン結果・SHA-256・状態遷移を変えず、永続化と通知を束ねる**実装性能是正**として進める。
- SHA-256はREQ-013の正しさと再リンク判定の根拠なので、先頭部分だけのハッシュ、mtimeだけでの初回登録、ハッシュ後回しは本ECOでは採用しない。
- バッチ件数は結果意味論ではなく実装定数。メモリを画像件数に比例させず、キャンセル時の未反映量も限定する有界バッチとするため、human gate①の設計裁定は不要。

## 3. 切り分け済みの事実

### 3.1 確定

1. 初回スキャンでは既存画像行がないため、対象画像は全件SHA-256を計算して`AddNormal`になる（REQ-013／`ScanJudge`規則3b）。画像総バイト数をHDDから一度読む下限コストは残る。
2. 現行の初回スキャンは対象画像数Nに対して、DB書込入口`AddAsync`をN回、SQLite暗黙トランザクションをN回実行する。バッチAPI・明示トランザクションは存在しない。
3. `DatabaseManager`は単一共有接続を`SemaphoreSlim(1,1)`で直列化する。CPU並列ハッシュを加えてもDB書込は直列であり、HDDのヘッド移動を増やす危険があるため、本ECOでファイル並列読取は行わない。
4. `progress.Report(processed * 100 / files.Count)`は全ファイルで実行される。26万件では表示値100段階に対して26万通知となる。
5. この構造は初版製造コミット`b1f13ec2`（2026-06-11）から存在し、CP-SCAN-004が小数ファイルの正しさだけを検査していたため潜伏した。

### 3.2 未検証

- maintainer端末における総時間のうち、ディレクトリ列挙／SHA-256読取／SQLite commit／完了後UI再読込が占める割合。
- 実HDD・26万件での是正前後の壁時計。入力データが私有画像で再配布不可のため、まず合成小ファイルでDB書込構造を決定論的に封止し、実機再走はgoldenで観測する。
- 画像が大容量中心なら、是正後はSHA-256の連続読取が支配的になり、改善倍率は小ファイル中心より低くなる。

## 4. 実施した是正

1. `IImageRepository.ApplyScanBatchAsync`と`ScanMutationBatch`を追加し、追加・メタ更新・status更新・pending削除を単一明示トランザクションで適用する。
2. `ScanService`は変更を最大512件だけ蓄積してflushする。26万件なら、初回追加のDB transactionは260,000回から**508回**へ減る。バッチ失敗は`SqliteTransaction`の未commit disposeで当該バッチ全体をrollbackする。
3. 進捗は整数百分率が増えたときだけ通知し、非空入力ではDB最終flush後に100を通知する。26万通知から最大100通知へ減る。
4. `FileSystemEnumerable`でディレクトリ列挙時にOSから得たサイズ・作成日時・更新日時を保持し、各ファイルの`new FileInfo`によるメタデータ再照会を除去した。
5. ファイル内容はHDDのランダムseekを増やさない直列読取のまま維持し、`FileOptions.SequentialScan`+64KiBバッファを指定した。SHA-256全体、対象拡張子、exclude、missing先行、リネーム、サマリの意味論は変更していない。

### 4.1 プローブ先行（R5）

- 製品修正前に新規2テストを追加し、既存573件合格に対して次の2件だけが不合格になることを確認した。
  - 8新規画像が`IImageRepository.AddAsync`を**8回**呼んだ（期待0）。
  - 250新規画像が整数100段階に対して進捗を**250回**通知した（期待100回以下）。
- 是正後はテストを有界性まで強化し、513新規画像が単行追加0回・バッチ2回・各バッチ512件以下であることをexact検査する。
- 重複relative_pathを同じバッチへ入れて2件目を失敗させ、1件目も残らない全rollbackをL2で確認する。
- 進捗は重複なし・単調増加・最大100回・100終端をexact検査する。

### 4.2 DB区間の比較計測（合成5,000行・Release）

同一の`ImageRepository`とSQLiteスキーマで、5,000行を単行`AddAsync`する旧経路と512件バッチ経路を別のfresh DBへ投入した。DB初期化とレコード生成は計測外。3回の結果:

| round | 単行commit | 512件バッチ | 短縮倍率 |
|---:|---:|---:|---:|
| 1 | 4,306.3 ms | 92.8 ms | 46.4x |
| 2 | 4,117.0 ms | 102.1 ms | 40.3x |
| 3 | 4,548.7 ms | 98.8 ms | 46.1x |

これはDB書込区間だけの比較であり、実HDDの総時間短縮倍率ではない。大容量画像中心ではREQ-013のSHA-256全体読取が支配的になり、小ファイル中心より総時間の改善倍率は低くなる。

## 5. 影響BOM

- `E-SCAN-005`：有界バッチ永続化と進捗通知上限を不変条件として追加。
- `M-SCAN-005`：ScanService／IImageRepository／ImageRepositoryのバッチ境界を製造契約化。
- `E-DB-010`／`M-DB-007`：スキャンバッチを明示トランザクションで原子適用。
- `CP-SCAN-004`：バッチ集約・ロールバック・進捗通知のexactベクタを追加。
- `FMEA-039`（新設予定）：大量初回スキャンを1件1暗黙トランザクションで永続化し、同一百分率を全件通知することで長時間化。
- 実装候補：`IImageRepository.cs`、`ImageRepository.cs`、`ScanService.cs`、`CpScan004Tests.cs`。
- CAD、表示文言、DBスキーマ、既存固定Oracle行は変更しない。

## 6. 機械受入と残ゲート

- gate①：不要（非表示の実装粒度であり、既存意味論を維持）。
- build：0 warning / 0 error。
- `ViewPrism2.Tests`：577/577 pass（ECO-059新規4観点を含む。GF-059-01再是正後）。
- `ViewPrism2.Oracle`：109 pass / 既知2 skip。既存固定Oracle行は無変更。
- `validate_bom.py`：0 error / 0 warning、`--selftest` OK。
- gate②（approved）：隔離profileの実HDD約260,000件で件数整合・35分完了、GF-059-01是正後にスキャン中もUI操作可能をmaintainerが確認した。物理画像はINV-009により読取専用。

## 7. GF-059-01 — 26万件スキャン中にUIが35分間操作不能（golden不合格）

### 7.1 maintainer実機観測（2026-07-11）

- §6の隔離profileから実HDDフォルダを初回スキャンし、約260,000件を**35分**で完了した。
- 旧版の正確な壁時計は不明。最大でも約1時間と記憶しており、総時間の改善倍率は確定不能。
- スキャン結果件数は約260,000件で機能結果は整合したが、**35分間UIを操作できなかった**。
- 大規模処理の総時間が媒体速度に依存することと、UIスレッドを全時間占有することは別問題である。後者は許容困難としてgolden不合格、ECO-059を`implemented / golden pending`のまま是正ループへ戻す。

### 7.2 真因仮説と追加プローブ

- `ScanService.ScanAsync`は`ConfigureAwait(false)`を使うが、最初の`ISyncFolderRepository.GetByIdAsync`以下が同期完了した場合、`await`はスレッドを切り替えず呼出元UIスレッド上で`ScanCoreAsync`へ進む。
- Microsoft.Data.Sqliteのasync APIは同期的に完了し得るため、`ConfigureAwait(false)`は「UI contextへ戻らない」だけであり、「必ず背景スレッドで実行する」保証ではない。
- 同期完了するfake repositoryを使い、呼出元thread idと最初のscan batch適用thread idが異なることを要求した。是正前は両方thread id `6`で、既存576件合格に対して追加1件だけが不合格となり、真因を実測した。

### 7.3 追加是正と機械受入

- `ScanService.ScanAsync`の`ScanCoreAsync`呼出しを`Task.Run`境界へ移した。フォルダ存在・active検証は従来どおり先に行い、長時間区間（列挙・SHA-256・DB batch）だけをbackground化する。
- `Progress<int>`は`FolderManagementViewModel`がUI threadで生成するため、backgroundからの`Report`は既定どおりUI `SynchronizationContext`へpostされる。進捗最大100回の初回是正も維持する。
- 最終回帰プローブはthread id比較を、より直接的な「同期scan batchを意図的に停止しても`ScanAsync`呼出しがbatch解放前に戻る」検査へ強化した。是正後は呼出元が先に解放され合格する。
- build 0 warning / 0 error、`ViewPrism2.Tests` 577/577 pass、`ViewPrism2.Oracle` 109 pass / 既知2 skip、`validate_bom.py` 0 error / 0 warning、`--selftest` OK。既存固定Oracle行は無変更。
- golden再確認でスキャン中のUI操作可能をmaintainerが確認した。総時間の固定上限ではなく、進捗・最終件数とcaller非占有を合格特性とした。DB操作は単一接続gateを短時間共有するためbatch commit中の瞬間的待ちは許容するが、処理全時間の操作不能は不合格とする。

## 8. クローズ（2026-07-11 — golden合格/applied）

- **実機確認**: maintainerが隔離profileから実HDD約260,000件を初回スキャンし、35分で完了・件数整合を確認した。初回goldenの全時間UI操作不能はGF-059-01として不合格に戻し、明示background化後の再確認でスキャン中もUI操作可能になったことを確認した。
- **承認範囲**: 旧版の正確な時間は不明（最大約1時間の記憶）のため、35分や短縮倍率を固定性能目標にはしない。狭義目標である「画像1件ごとの暗黙commit除去」「重複進捗通知除去」「長時間処理がUI callerを占有しない」を達成として承認した。
- **再発防止**: CP-SCAN-004へ、約26万件・35分完了でも全時間UI操作不能となった潜伏実績を明記した。bounded batch/rollback/progress exactに加え、同期完了repository+停止batchでも`ScanAsync` callerが先に解放されるプローブで、`ConfigureAwait(false)`だけの擬似非同期を封止する。
- **M4判定**: 既存REQ-011〜015のSHA-256・状態遷移・サマリ意味論を変えない内部性能是正。E/M-BOM・FMEA・CPはfix時同期済み、surface/CAD/20-spec/35-design-system-bomのas-built乖離はないため追加M4は不要。golden権威は50-as-builtへ転記した。
- **教訓/read-across**: `Async` APIと`ConfigureAwait(false)`は、長時間同期区間を呼出threadから分離する保証ではない。UI応答性の受入は「asyncメソッドであること」で代用せず、同期完了する依存と意図的に停止する長時間区間を組み合わせ、callerが先に解放されることを直接検査する。これはサムネイル・pHash等の重いI/O consumerへread-acrossする一般則である。
- **後続事項（R3分離）**: スキャン中コレクションの段階的公開は本ECOへ混ぜない。次ECOでは、類似画像検索をスキャン完了まで無効化することを採用し、`images.hash`未確定表現はNULL・特殊値・別状態列を不変条件、索引、再リンク、検索、migration互換まで比較して裁定する。特殊値を事前に不採用とはしない。
- **コミット履歴注記**: 起票時はcommit指示が無かったため起票単独commitを作らず、起票記録+fixを`b217025`へ同梱した。受入は本クローズcommitで分離する。
