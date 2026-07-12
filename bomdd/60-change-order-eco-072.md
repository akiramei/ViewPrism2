# Change Order — ECO-072(applied / golden approved): カタログDBスナップショットの作成・検証・時点復元(バックアップA層)

> maintainer要求(2026-07-12)「タグ・階層ビュー・コレクションをバックアップ可能にしたい」を
> `/eco-file`で受理した新機能要求。仕様は maintainer+外部アドバイザーとの事前議論で
> **2層構成**として確定済み: A層=DBスナップショット(本ECO)、B層=コレクション論理
> 書き出し/取り込み(ECO-073 で分離起票)。

## 1. 要求(maintainer・2026-07-12)

- タグ定義・階層ビュー・コレクション(タグ付与含む)を任意時点へ戻せるバックアップが欲しい。
- 事前議論で確定した役割分担(仕様の核文):
  **DBスナップショットはライブラリ全体の時点復元を行う。論理パッケージは現在のライブラリへ
  非破壊で統合する。論理インポートは、バックアップ時点の共有タグ体系を厳密に巻き戻すものではない。**
- A層の保証範囲は「カタログ・メタデータの時点復元」。**画像実体は含まない**。作成/復元の
  両UIに「画像ファイルは含まれません」と明示する。
- 自動バックアップ・世代管理は本ECOの対象外(将来ECO。その際は SQLite Online Backup API を検討)。
- UIモック(CAD)は maintainer が作成する。必要な画面情報は本ECOの起票報告で提供する。

## 2. 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | **画面未定義・gate①必要** | `docs/screens/` にバックアップ/スナップショット画面は存在しない(7画面のみ)。既存 `SettingsWindow.axaml` は言語切替だけの最小ダイアログ(REQ-050/051、幅400)で、CAD 画面正典も持たない。新画面の mock 作成が製品コードに先行する。 |
| 要求・仕様 | **新機能、既存要求なし** | `10-requirements.yaml`/`20-spec.md` に DB バックアップ/スナップショット要求は存在しない。「復元」の既存出現はすべてトラッシュ復元(REQ-070/OC-21)や位置復元であり別概念。 |
| M-BOM・検査 | **未宣言** | バックアップに関する E/M-BOM・CP・テストは存在しない。 |
| 実装 | **未実装+孤立語彙が残存** | 非JSONソースに backup/restore 実装なし。一方 i18n に `settings.backup.*` 語彙群(en.json:376-482、ja.json 同様)が **V1 由来・実装未接続**で残存する。語彙は自動バックアップ間隔・保持世代数・カレンダー・検証ステータスまで含み、本ECO(手動のみ)より広い。再利用可否は CAD 裁定に従う。 |
| DB 基盤 | **前提は健全** | 単一 `viewprism2.db`(WAL、`DatabaseManager.cs:41-42`)。全テーブルが明示 TEXT(UUID)主キーで暗黙 ROWID を外部IDに使う箇所なし(VACUUM 安全)。`MigrationRunner` が未適用 migration を ID 昇順適用する(復元後の前進互換を既存機構が担う)。 |

## 3. 切り分け済みの事実

### 3.1 確定

- DB は `%APPDATA%\ViewPrism2\viewprism2.db` 単一ファイル、WAL モード。稼働中の単純ファイル
  コピーは `-wal`/`-shm` との不整合リスクがあり不可。`VACUUM INTO` は稼働中DBから一貫した
  コンパクトなスナップショットを作成できる(SQLite 公式)。ただし出力は中断時に不完全になり得るため、
  正式ファイル名へ直接出力してはならない。
- アプリは単一 `SqliteConnection` を `SemaphoreSlim(1,1)` で直列化して共有する
  (`DatabaseManager.cs:12-51`)。復元(DBファイル差し替え)は全接続クローズが前提となり、
  実質「再起動を伴う操作」として設計する必要がある。
- スナップショットは DB そのものであり、`migrations` テーブルを内包する。互換性判定は
  サイドカー不要で「スナップショット内の適用済み migration ID 集合が、現行アプリの既知
  migration 集合の部分集合か」で機械判定できる(未知IDを含む=新しいアプリ由来→拒否)。
- 復元後の前進(旧スナップショット→現行スキーマ)は既存 `MigrationRunner.Run` が昇順適用で担う。
- スコープ外(A層に含まない): 画像実体(パス参照のみ・INV-009)、サムネイルキャッシュ
  (`%APPDATA%\ViewPrism2\thumbnails`・再生成可)、`settings.json`(SettingsStore が `.bak`
  世代管理を既に持つ)。**DB 内の全テーブル**(merge_operations・類似度キャッシュ・workspaces 含む)
  は「DB丸ごと」の定義どおりすべて含む。
- 事前議論で確定済みの手順契約:
  - **作成**: 一意な `.partial` へ `VACUUM INTO` → 読み取り専用で開き `PRAGMA quick_check` +
    `PRAGMA foreign_key_check` → チェックサム計算 → 検証成功後に正式名へアトミックリネーム。
    検証済みになって初めて一覧(将来は世代管理)の対象とする。
  - **復元**: 形式・互換性確認 → バックアップDBの整合性検証(復元直前は `integrity_check`)→
    現行DBの安全スナップショット作成 → 全接続クローズ → 現行DB退避 → 差し替え →
    MigrationRunner → 起動検証 → 失敗時は退避DBへ戻す。

### 3.2 疑い・未検証(`/eco-fix` の probe で確定)

- `Microsoft.Data.Sqlite` での `VACUUM INTO` 実行(パスのエスケープ・パラメータ化不可の扱い)と、
  差し替え時のファイルロック解除(`SqliteConnection.ClearAllPools` の要否)。
- `VACUUM INTO` 実行中の書き込みブロック程度と所要時間(大規模DBでの UI 応答)。
- チェックサム方式(ファイル SHA-256 を想定)と、その保存先(ファイル名/サイドカー/一覧メタ)。
- 「復元予約→再起動時差し替え」のブートストラップ実装点(`App.axaml.cs` 起動シーケンス)と
  起動検証の判定基準。

### 3.3 未確定事項との関係

- 自動バックアップ・保持世代数・カレンダー UI(i18n 語彙に既存)は**本ECO対象外**。手動機能の
  安全性確立後に別ECOで起票する。
- B層(ECO-073)とは独立に受入可能。ただし UI 用語の対比(A層=「スナップショット作成/時点復元」、
  B層=「書き出す/取り込む」)は両ECO共通の契約とする。
- B層の「破壊的取り込み(置換)を提供する場合は直前にA層スナップショットを自動作成する」構想は
  将来ECOの依存であり、本ECOのスナップショット作成器はサービスとして再利用可能な形に置く。

## 4. 方針候補(gate①)

UI 配置・見た目は CAD(maintainer 作成の mock)が正。ここでの裁定は**復元の実行方式**。

### 案A — 復元予約+再起動時差し替え(推奨)

- アプリ内で復元を確定すると「予約」を永続化し、再起動を促す。次回起動の DB 接続確立**前**に
  ブートストラップが §3.1 の復元手順を実行する。
- 単一共有接続アーキテクチャと整合し、稼働中の VM/サービスが古い DB への参照を持ち続ける事故を
  構造的に排除する。失敗時は退避DBへ戻して通常起動へフォールバック。
- 規模: 中。Infrastructure(スナップショットサービス+復元ブートストラップ)、App(起動シーケンス、
  設定/バックアップ UI)、新 CP・unit(一時DB fixture で機械検査可能)。
- golden: 作成・検証・復元の一連 UI と「画像ファイルは含まれません」明示、復元失敗時の
  フォールバック起動。

### 案B — 稼働中に接続クローズ→差し替え→再オープン(再起動なし)

- UI 上はシームレスだが、単一接続を参照する全 VM・キャッシュ・監視(スキャン)を安全に停止・
  再初期化する機構が必要で、失敗モードが多い。
- 規模: 大。リスクに対して得られる体験差が小さい。

### 案C — アプリ外手順のみ(ドキュメント+手動ファイルコピー)

- 実装最小だが、WAL の罠(-wal 取り残し)をユーザーに負わせ、検証・アトミック性の保証がない。
  要求を満たさない。

## 5. 影響BOM(案A採用時)

- CAD: ViewPrismUI へ新画面(スナップショット作成/一覧/時点復元。名称・配置は mock が決定)。
  maintainer が mock 作成、`docs/screens/` へ画面正典を追加後に BOM 同期。
- 要求/仕様: 新 REQ 群(対象範囲と除外の明示・作成検証手順・復元手順・互換性判定)、
  `20-spec.md` へ新節(A層/B層の役割分担の核文を含む)。
- E-BOM: 新規(スナップショット作成器・検証器・復元ブートストラップ)。既存 E への変更なし予測。
- K-BOM: 追加依存なし(SQLite 標準機能のみ)。
- M-BOM: 新規(Infrastructure スナップショットサービス+App 起動シーケンス+バックアップ UI)。
- 実装: `ViewPrism2.Infrastructure`(サービス)、`ViewPrism2.App`(`App.axaml.cs` 起動、
  設定/バックアップ VM+View、i18n 配線)。DB スキーマ変更なし。
- 検査: 新 CP — 中断時に `.partial` が正式名にならない・検証失敗スナップショットが一覧に載らない・
  復元失敗時に退避DBへ戻り起動できる・復元後に未適用 migration が昇順適用される・
  未知 migration ID を含むスナップショットを拒否する。unit+一時DB fixture で機械検査、UI は golden。
- Oracle: 既存固定 Oracle 行は変更しない(R6)。

## 6. 残ゲート

1. ~~**gate① 裁定**: 復元方式 案A/B/C から選択(推奨は案A)+ CAD mock 作成
   (画面情報は起票報告で提供済み)。CAD 画面正典の追加が製品コードに先行する。~~ → 案A採用・CAD正典化完了(§7)
2. ~~**SS-001(UI 入口)の裁定**~~ → 裁定済み(2026-07-12)=(b) 分置: A層=設定のバックアップ節、
   B層=コレクション管理三点メニュー(ECO-073)。CAD 反映=ViewPrismUI `77e4f50`。
3. ~~`/eco-fix ECO-072` で probe(§3.2)→実装→機械受入。~~ → 完了(§8)
4. **gate② golden**: 作成・検証・復元・失敗フォールバックの実機確認(§8.5)。
5. `/eco-accept ECO-072` でクローズ。

## 8. 実施記録(2026-07-12 — 機械受入完了・golden待ち)

### 8.1 先行probe(R5)

- `CpSnapshot072Tests` へ、SnapshotService/SnapshotRestoreBootstrap の存在(reflection)と
  SettingsViewModel の入口コマンド(SS-001裁定(b))を要求する probe を製品コード変更前に追加した。
- 是正前実測は `ViewPrism2.Tests` **612件中2件不合格(610 pass)**。未実装(型不在・入口コマンド不在)を
  確認してから製品コードへ着手した。

### 8.2 §3.2 疑いの実測結果

- `Microsoft.Data.Sqlite` の `VACUUM INTO @path` はパラメータバインドで動作する(エスケープ不要)。
- 検証・差し替え用の接続は `Mode=ReadOnly;Pooling=False`(または使用後 ClearPool)でファイルハンドルを
  残さず、直後の File.Move/Copy が成功することを一時 DB fixture で確認した。
- チェックサムは SHA-256(既存 FileHasher)とし、サイドカーメタ `*.db.meta.json`
  (appVersion/createdAt/checksumSha256/sizeBytes・camelCase)へ保存。一覧の検証済み判定は
  メタ存在+サイズ一致(全量再ハッシュは復元直前検証に限定=一覧性能)。
- 復元予約の適用点は `App.OnFrameworkInitializationCompleted` の appDataDir 決定直後・
  `ConfigureServices`(=DatabaseManager.Open)前。結果は logger+ステータスバーへ報告する。

### 8.3 是正裁定とdiff

- **Infrastructure**: `SnapshotService.cs`(作成=共有接続上の VACUUM INTO→.partial→quick_check+
  foreign_key_check→SHA-256→アトミックリネーム+メタ/一覧/復元前検証=integrity_check+未知migration拒否/
  復元予約)+`SnapshotRestoreBootstrap.cs`(予約一回限り→検証→安全スナップショット pre-restore-*→
  現行退避→差し替え→検証→失敗時退避へ巻き戻し)。
- **App**: `SnapshotWindow`(A-1)+`SnapshotRestoreConfirmWindow`(A-2)+`SnapshotViewModel`
  (ダイアログ/フォルダ選択/再起動はデリゲート注入=unit検査可能)。`SettingsWindow` バックアップ節
  (既存 i18n settings.backup.title/description を配線)+`SettingsViewModel.OpenSnapshotsCommand`。
  `WindowService.ShowSnapshotsAsync`+`RestartApplication`(desktop.Exit で DB 破棄後に新プロセス起動)。
  `App.axaml.cs` 起動ブートストラップ+結果報告。`AppSettings.SnapshotDirectory`(SS-002=アプリ共通)。
- **i18n**: `snapshot.*` 31キー(ja/en)+`settings.backup.openSnapshots`。自動バックアップ語彙は
  未配線のまま温存(M2)。
- **M4 同期**: REQ-092 新設、仕様 §2.13 新設(A層/B層役割分担の核文)、E-SNAPSHOT-045/E-UI-SNAPSHOT-046、
  M-SNAPSHOT-040/M-UI-SNAPSHOT-041+沈黙次元5行、CP-SNAPSHOT-031(unit)+CP-UI-G12(golden)。
- DB スキーマ・Core・既存 Oracle 期待値・既存画面の視覚は変更していない。
- 実装判断(golden で確認): 作成進捗は不確定バー+件数表示(VACUUM INTO は百分率を提供しない—mock 68% は
  許容差分)。一覧空状態(SS-004)は muted テキスト 1 行。作成キャンセルは手順間で有効
  (VACUUM 実行中の中断は不可・.partial 非残置は契約どおり)。

### 8.4 機械受入

- 先行probe+挙動テスト6本を含む `ViewPrism2.Tests`: **618/618 pass**(probe 2 本は緑へ転じた)。
- `dotnet build ViewPrism2.sln --no-restore`: **0 warning / 0 error**。
- `ViewPrism2.Oracle`: **109 pass / 2 known skip**。既存固定期待値変更なし(R6)。
- `python bomdd/validate_bom.py`: **0 error / 0 warning**。
- `git diff --check`: clean。
- 記録: 全suite を `dotnet test` で流した1回目が testhost 側でハングした(killで回収)。exe 直接実行と
  再実行はともに全緑・11秒台であり、テスト自体の欠陥ではない(51 既知の実行規律メモと同族)。

### 8.5 gate② golden 操作手順(CP-UI-G12)

1. 設定を開き「バックアップ」節から「スナップショット管理...」で A-1 が開く。画像非含有の callout と
   保存先(既定 %APPDATA%\ViewPrism2\snapshots)が表示される。
2. タグ・ビュー・画像のあるプロファイルで「スナップショットを作成」→ 作成中表示(件数+バー+キャンセル)
   → 一覧最上段に 日時/app_version/サイズ/「検証済み」で追加される。保存先フォルダに snapshot-*.db と
   *.db.meta.json があり、.partial が残っていない。
3. 「変更」で保存先を別フォルダへ変更 → 以後の作成がそこへ入り、再起動後も保存先が保持される(SS-002)。
4. タグを 1 つ追加・リネームなど変更を加えてから、変更前スナップショットの「復元」→ A-2 で
   対象情報(日時/app_version/サイズ/検証)・取り消し不可警告・再起動/自動ロールバック/画像非含有の
   注意 3 項を確認 →「この時点に復元して再起動」→ アプリが自動再起動し、変更が巻き戻っている。
   ステータスバーに復元完了が表示される。保存先に pre-restore-*.db(安全スナップショット)が増えている。
5. 保存先に手作りの偽ファイル(例: テキストを snapshot-fake.db にリネーム)を置く → 一覧に「検証待ち」で
   列挙され、**復元ボタンが無効**であること。
6. 失敗フォールバック: 検証済みスナップショットの meta を残したまま db ファイル本体を壊す(バイナリ先頭を
   数バイト上書き)→ 復元実行 → 再起動後、**元の状態のまま起動**し、ステータスバーに復元失敗の報告が出る。
   再起動を繰り返しても復元を再試行しない(予約一回限り)。
7. 画像実体・サムネイルが作成/復元の前後で変更されないこと(フォルダをエクスプローラで確認)。
8. ja/en 切替で A-1/A-2 の文言が追随し生キーが出ない(G-5 回帰)。設定・画像タブ・ビューア等の既存画面に
   視覚回帰がない。

## 9. golden所見 GF-072-01 の是正(2026-07-12 — 再機械受入)

### 9.1 所見と工程診断

- maintainer が実機 A-1 と mock を並置比較し、実機が平板と指摘: 一覧が表形式(ヘッダ行+行グリフ)でない・
  検証状態がバッジピルでない・カード化なし・作成 CTA の「+」なし・復元ボタンが青 outline でない。
- 工程診断: **prose CAD が視覚言語を拘束契約に落とさず**(領域・状態・文言までを規定)、実装が既存
  SettingsWindow 流儀で行間を埋めた。さらに構造原因として、CAD にサーフェス別の参照キャプチャ(PNG)が
  同梱されておらず、実装者が視覚原器を持たなかった(BomDD 方法論の双子出力=DOM snapshot+PNG は
  本プロジェクト未導入 — bomdd-refmodel 3 点セット未設置と同根)。恒久対策は ViewPrismUI 側
  (captures 同梱手順)で実施。

### 9.2 先行probe(R5)

- `GfSnapshotVisualParityTests`(headless 実レイアウト・CP-UI-G12)を製品コード変更前に追加:
  ①表ヘッダ(ListHeader)存在 ②検証済み/検証待ちの状態バッジ(別配色・非透明)③行グリフ(DB シリンダー
  Path)×2 ④作成 CTA の「+」プレフィックス ⑤復元ボタン=青系 outline・検証済みのみ活性。
- 是正前実測: **2 件不合格**(視覚欠落を ground-truth 確認)。

### 9.3 是正diff

- `SnapshotWindow.axaml` を mock の視覚言語へ是正: 一覧をカード+表ヘッダ(作成日時/サイズ/状態)+
  行グリフ+monospace サイズ/版表記へ。検証状態は緑(✓)/黄(時計)のバッジピル
  (`SnapshotBadgeConverters` 新設 — CAD「状態バッジ配色」パターンの実体)。作成 CTA に「+」、
  復元/変更ボタンを mock scp1 の青 outline(#2F6BED/#D6E0EE・disabled=淡グレー)へ。callout に
  カメラ×斜線グリフ。余白・タイポグラフィ密度を mock へ寄せた。
- i18n `snapshot.colStatus`(状態/Status)追加。挙動・VM・Core・DB は不変(視覚のみ)。

### 9.4 再機械受入

- `ViewPrism2.Tests`: **620/620 pass**(GF probe 2 本は緑へ転じた)。
- `dotnet build`: 0 warning / 0 error。`ViewPrism2.Oracle`: 109 pass / 2 known skip(R6 不変)。
- `python bomdd/validate_bom.py`: 0 error / 0 warning。

### 9.5 gate②再操作(§8.5 に追加)

9. A-1 を mock と並置し、カード/表ヘッダ/行グリフ/状態バッジ(緑✓・黄時計)/+CTA/青 outline 復元の
   視覚言語が mock と同等であることを確認する(許容差分=擬似タイトルバー V1・幅 V2・進捗 68% 表示)。

## 10. gate②合格・クローズ(2026-07-12)

maintainer が `/eco-accept ECO-072` で §8.5 の 8 項目+§9.5 の mock 視覚並置(GF-072-01 是正後)を
実機承認した。設定バックアップ節から A-1 を開き、作成→検証済み一覧→保存先変更の永続、
変更を加えてからの時点復元(A-2 確認→予約→自動再起動)で変更が巻き戻り、pre-restore 安全
スナップショットが生成された。偽ファイルは検証待ちとして復元無効、破損スナップショットの復元は
元の状態で起動して失敗報告され、再起動を繰り返しても再試行しない(予約一回限り)。画像実体・
サムネイルは前後で不変、ja/en 切替と既存画面に回帰はない。

再発防止は CP-SNAPSHOT-031(partial 非昇格・未知 migration 拒否・差し替え往復・退避フォールバック・
予約一回限り)+ GfSnapshotVisualParityTests(headless 視覚言語 pin)+ CP-UI-G12 の潜伏実績つき観点へ
固定した。CAD は ViewPrismUI `1fc5aaa`(正典化)/`77e4f50`(SS-001)/`b8389f9`(captures 恒久対策)。
accept 時に 50-as-built golden_approvals へ実機承認を追記した。

**教訓**: prose CAD は「領域・状態・文言」を規定できても、**視覚言語(表/バッジ/カード/アイコン)は
参照キャプチャ(視覚原器の写し)とセットで初めて製造可能な CAD になる**。挙動契約だけを渡された実装は
既存流儀で行間を埋め、視覚検査観点が無いまま golden まで平板化が潜伏する — ECO-041(未配線 UI)・
ECO-056(モック詳細突合が観点に無く歴代 golden を素通し)と同族の「検査面に無い次元は素通しする」
read-across であり、本件はその視覚言語版。恒久対策として CAD 化手順へサーフェス別キャプチャ同梱を
必須化した(ViewPrismUI CLAUDE.md)。BomDD 方法論は双子出力(DOM snapshot+PNG)を既に規定しており、
**「方法論改善は既存プロジェクトの手順書へ明示的に遡及適用しない限り効かない」**という一般形は
方法論への教訓昇格候補とする。

残課題なし(A層スコープ)。後続= `/eco-fix ECO-073`(B層)、自動バックアップ・世代管理(将来 ECO)、
tag-catalog/view-set/workspace 可搬(将来 ECO)。

## 7. gate①裁定(2026-07-12)

- maintainer裁定: **案A=復元予約+再起動時差し替えを採用**。アプリ内で復元確定→予約を永続化→
  次回起動の DB 接続確立前にブートストラップが §3.1 復元手順を実行、失敗時は退避DBへ戻して
  通常起動へフォールバック。CAD の A-2 は「この時点に復元して再起動」+自動再起動+
  自動ロールバックの約束としてこの裁定を表現する。
- **CAD正典化**: デザイナー納品 mock を maintainer が3回のレビュー往復(乖離9点→全解消)を経て
  承認し、ViewPrismUI `1fc5aaa38dd2` で正典化した。
  画面正典=`docs/screens/snapshot_export_import.md`(A-1 スナップショット作成・一覧/
  A-2 時点復元の確認。B層 4面と1画面CADに集約)。一次資料=
  `資料/スナップショット・書き出し取り込み/`(SHA-256 `5104C8CE…2DC36CE7`)。
- CAD/mock 由来の追加契約: 検証待ちスナップショットは復元不可(復元ボタン無効)。
  作成中キャンセルは中途生成物を残さない(SS-005)。保存先既定=`%APPDATA%\ViewPrism2\snapshots`。
- **SS-003(検証タイミング)への回答**: §3.1 の作成手順では検証成功後に正式名リネームするため、
  作成フロー内で「検証待ち」は発生しない。発生源(外部持込ファイル・起動時再検証等)を
  設けるかは `/eco-fix` の probe と合わせて確定する。
- 残: SS-001(UI 入口)のみ maintainer 裁定が必要(§6-2)。SS-002/004/005 は fix 内で確定。
- gate①完了。次の明示入口は `/eco-fix ECO-072`。本裁定では src/tests を変更しない。
