# ECO-082 — テスト並列フル run の間欠フレーク(Dapper InvalidCastException Int64→Int32)

- status: staged
- type: 不具合(テスト基盤 or 潜在製品欠陥=現時点で未確定。間欠・並列時のみ)
- baseline: main 988a01c
- 発端: ECO-079/080 作業中の観測(2026-07-13)を 51-cheat-log に R3 記録 → maintainer 裁定「D は別起票」(2026-07-13)

## §1 症状

`dotnet test`(MTP・全並列)のフル run で、以下が**間欠的に** fail する(2026-07-13 に 2 run で観測):

- run A: `CpWorkspace028Tests` の 4 テストが `System.InvalidCastException: Unable to cast object of type 'System.Int64' to type 'System.Int32'`(Dapper `SqlMapper.GenerateMapper` / `MultiMapImpl`)で fail。
- run B(別 run): `CpUiRepairViewModelTests` の 1 テストが fail(同族の疑い)。

いずれも **isolation(`-class` 指定)では常に緑**、直後の xUnit v3 exe フル run(全並列)も 664/664 緑で**再現せず**。run ごとに fail 対象が変わる。

## §2 工程診断(未確定 — 事実と疑いを分離して fix 時にプローブで確定)

| 対象 | 判定 | 根拠 |
|---|---|---|
| DB ファイル共有(テスト間) | **シロ(実測)** | TempDb はインスタンスごと `%TEMP%/ViewPrism2.Tests/<GUID>/viewprism2.db` の独立ファイル+独立 DatabaseManager(TempDb.cs 実測 2026-07-13)。テスト間で DB を共有していない。 |
| 発火面 | **特定済み** | 例外スタックは Dapper multi-map。該当は `WorkspaceRepository.GetAllWithNormalCountsAsync`(WorkspaceRepository.cs:41= `QueryAsync<WorkspaceRow, long, WorkspaceWithCount>` splitOn NormalCount)。決定的なら常に fail するはずのクエリが**並列時のみ間欠 fail** する点が異常。 |
| 真因 | **未確定** | 下記疑い。 |

### 疑い(未検証・fix 時にプローブで裏取り)

1. **Dapper のマッパー生成/キャッシュの並列競合**: 同一クエリ形状の初回マッピング生成が複数スレッドで同時に走った際の型解決レース(Int64→Int32 という「列型の取り違え」が間欠発火する説明として最有力)。
2. SQLitePCLRaw / プロバイダ初期化の並列競合。
3. MTP(dotnet test)と xUnit v3 exe の並列度・スケジューリング差が発火率に影響(exe で未再現の説明候補)。
4. 製品コードの潜在欠陥(`(int)count` キャストや WorkspaceRow の int プロパティが並列時のみ誤マップされる経路)— 可能性は低いが排除しない。

## §3 是正方針(案・着手時確定)

1. **プローブ先行(R5)**: 再現ハーネス=当該クエリ(GetAllWithNormalCountsAsync 等)を高並列で反復実行し発火率を実測(発火しない場合は並列度・初回性・複数 TempDb 同時生成の条件を掃引)。**再現できない場合は診断不能として停止し報告**(推測でコードを触らない)。
2. 再現後、真因(疑い 1〜4)を切り分けて最小是正。Dapper キャッシュ競合なら初期化の直列化 or マッピングの明示化(型ハンドラ/明示カラム)等 — 手段は真因確定後。
3. ECO-081(時間上限+診断採取)が先に入ると、フレーク発火時の証拠採取も改善される=順序は 081 先行が望ましい。

## §4 影響 BOM(見込み)

- 真因がテスト基盤なら: tests(再現ハーネス+是正)。
- 真因が製品(Infrastructure)なら: WorkspaceRepository 等+CP 追補(その時点で本 ECO の type を製品不具合へ改め、影響 BOM を追記)。
- 既存固定 Oracle 行は変更しない(R6)。

## §5 残ゲート

- gate① 裁定: 不要(欠陥是正・診断は §2 の実測で確定させる)。
- gate②: maintainer 実行確認(フル run 複数回で fail 0 or 発火率の有意な低下を実測提示)。

## §6 関連

- ECO-081(同時分離起票): テスト実行時間上限のハーネス宣言化。本件の観測性を改善する先行 ECO。
- 51-cheat-log「ECO-079 是正中に観測した並列実行フレーク」記録=本 ECO で処置着手(起票済みマーク)。

## §7 診断記録(2026-07-13 /eco-fix・再現プローブ→停止)

### 再現試行(実測)

| 試行 | 構成 | 結果 |
|---|---|---|
| 単独プローブ | 16 並列×25 反復×3 run=1,200 回の TempDb 新規生成+GetAllWithNormalCountsAsync 初回実行 | **InvalidCast 0 件** |
| exe フル run(全並列・プローブ入り 665 件) | ×5 | 0 件 |
| dotnet test フル run(観測時と同経路) | ×30(5+10+15) | **InvalidCast 0 件** |

**InvalidCastException は計 35 フル run+単独 1,200 回で一度も再現せず** — §3 の宣言どおり「再現不能=診断不能として停止」。真因候補(Dapper マッパーキャッシュ競合等)は未検証のまま確定不能。

### 診断中の副産物=別欠陥の実測捕獲(→ ECO-083 へ分離起票・R3)

dotnet test フル run 反復中に **exit=1 が 2/15 回発火**したが、内訳は InvalidCast ではなく**別種の間欠ハング**だった: Avalonia Headless(共有 `HeadlessApp.Session`)を使う UI テスト 8 本が「実行中」のまま無活動 4 分超 → **ECO-081 の HangDump(5 分)が発火**し、ハング中テスト名+mini ダンプを自動採取(fail-closed 化の初実運用が機能)。ダンプ解析(clrstack -all)で「実行スレッド不在・全ワーカーアイドル・セッションスレッド消滅」を確定し、Avalonia.Headless 12.0.4 のデコンパイルで**ディスパッチループが OperationCanceledException 以外の例外で死ぬ構造**(真因)を特定。詳細と是正案は ECO-083。

**過去の「サブタスクでテスト実行が応答なし」の実体は、InvalidCast フレークではなくこのハング(発火率 ~13%)だった可能性が高い。**

### 処置(maintainer 裁定 2026-07-13)

- **(a) 保留静置に裁定**(decide コミット)= ECO-081 の診断採取+trx 保全により再発時に証拠が自動的に残る→その時点で診断再開。クローズしない(症状の実績は確定しているため)。
- 観測手順の教訓: 再現ループでは**発火時に即停止しログ/ダンプを退避**する(本診断の初回ループは後続 run が TestResults を上書きし run4 の証拠を失った。2 回目から是正)。

## §8 保留静置中の新証拠(2026-07-13・ECO-083 fix 検証中に採取)

ECO-083(PerAssembly 化)検証のフル run ×12 中、**非ハング fail 2 回**を観測(同ファミリーの証拠として記録):

- **run2(スタック全文採取)**: `CpUiRepairViewModelTests.GF_V4_01` が `NullReferenceException` — `Microsoft.Data.Sqlite.SqliteCommand.DisposePreparedStatements` → `SqliteConnection.Close` → `DatabaseManager.Dispose`(:93)→ `TempDb.Dispose`(テスト末尾)。**接続 Close と prepared statement の並列競合**の形= **テスト内で起動した background タスク(疑い: ECO-075 で Task.Run 化した修復候補探索)がテスト終了後も残存し、TempDb.Dispose(接続破棄)と競合**する構造の直接証拠。
- run7: 詳細不明(後続 run がログ上書き・非ハングのみ確認=観測手順の教訓を再度踏んだ)。
- **診断仮説の更新**: 本 ECO の InvalidCast(Dapper multi-map)も「壊れた/破棄中の接続・reader を読んだ」結果である可能性が浮上(NRE と InvalidCast は同じ競合の別の顔)。**発火面はクエリでなく「テスト終了時の background 残タスク×TempDb.Dispose」の可能性が高い** — 再開時はこの線(CpUiRepairViewModelTests/CpWorkspace028Tests のテスト終了時の未 await タスク棚卸し+Dispose を gate 内に入れる是正)から。
