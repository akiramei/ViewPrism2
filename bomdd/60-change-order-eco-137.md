# ECO-137 — 再スキャンが DB 行ごと File.Exists+ディレクトリ全列挙の二重メタデータ走査

- 種別: 不具合候補(実装層・性能特性・**要実測**。挙動は仕様どおり)
- status: staged(2026-07-23 起票。出典= ECO-134 R3 スコープ外所見3・maintainer 裁定で単独起票)
- baseline: main `f217470`
- 優先度: 中(着手順序= ECO-138 より先。ECO-134 と同型の軽量クローズ見込み)

## §1 症状

再スキャン(StageCore)が同一フォルダ木に対しメタデータ走査を二重に行う:

1. [ScanService.cs:162](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L162) で
   `CreateEnumerable(folder, root)`(ディレクトリ全列挙)を用意
2. [ScanService.cs:197](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L197) で
   **existing 全行に対し 1 行ずつ `File.Exists`**(missing 判定)
3. [ScanService.cs:234](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L234) で列挙を walk

HDD では 2. の**行ごとランダム存在確認**が支配的になり得る(管理 26 万件級)。
ScanCore(初回経路)にも同型([:497](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L497)/
[:514](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L514))があるが、初回は existing≈0 で
実害小 — 主対象は再スキャン経路。

## §2 工程診断(起票時見込み)

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD | 健全 | 視覚・挙動変更なし |
| BOM(検査封止) | 部分欠落 | 存在確認の I/O 走査回数は CP 未封止(ECO-134 §4 計算量封止と同族の残り) |
| 実装 | 逸脱疑い | 結果は正・メタデータ走査の形のみ(列挙で得られる情報を行ごと再取得) |

## §3 切り分け済みの事実

### 確定(コード実測)

1. 走査 2 系統の位置は §1 のとおり(現 HEAD で確認済み)。
2. パス照合の比較子は `byPath` 構築(:223 近傍)で `StringComparer.OrdinalIgnoreCase` — 列挙由来の
   相対パス集合を同じ比較子で構築すれば既存の照合意味論と揃う。
3. ECO-134 教訓の read-across: 本件は「毎回走査」型のインデックス不在の一形態(File.Exists N 回
   → 列挙 1 回からの set 構築)。

### 未検証(疑い — /eco-fix の最初に実測)

- HDD 実測での 2. の支配性(合成計測は下限。mass 管理件数でのランダム存在確認 vs 全列挙の実測比較)。
- `File.Exists` と列挙の意味論差: 「列挙に現れないが File.Exists は真」のクラス(アクセス権・
  共有違反・隠し属性等)の存在と扱い。観測時点の差(TOCTOU)は現行も列挙(:162)と行ごと確認(:197)で
  二時点あり、統合はむしろ一時点化 — ただし R5 で挙動同値を pin してから是正する。

## §4 是正方針(案・着手時確定)

- **案A(推奨)**: 列挙 1 回から相対パス集合(`OrdinalIgnoreCase`)を構築し、行ごとの存在確認を
  set lookup 化 = メタデータ走査をディレクトリ列挙 1 回に統合。ECO-134 §7 と同型
  (一度構築した写像への O(1) 照合)。
- **プローブ先行(必須・ECO-134 の型)**:
  1. **計数プローブ**: 存在確認のファイルシステム呼び出し回数を数える抽象化で
     「行ごと File.Exists 呼び出し == 0(存在確認は列挙 1 回に集約)」を pin。
     決定論・固定時間閾値なし(走査回数 40000→200 反転と同じ受入形式)。
  2. **挙動同値プローブ**: missing 判定の結果不変(存在/不在・大文字小文字差・列挙外ファイルの
     各ケースで是正前後一致)。
- **検査封止(併修)**: スキャン CP へ「存在確認の走査回数(列挙 1 回)」観点を追加
  (ECO-134= 計算量に続く走査回数軸。ピークメモリ・区間別 throughput は ECO-138 で併修)。
- **停止意味論(起票時チェック済み)**: 既存 `ct.ThrowIfCancellationRequested` の粒度は不変とし、
  走査統合で cancel 到達性が変わらないことを R5 に含める(control-plan「停止意味論の 2 不変条件」)。

## §5 影響 BOM

- `src=ScanService.cs`(StageCore の存在確認を列挙集合 lookup へ。ScanCore 同型は実測で要否判断)
- `tests=` 計数プローブ+挙動同値プローブ
- `bomdd/33-control-plan.yaml`= スキャン CP へ存在確認走査回数の観点
- `CAD`= 該当なし(視覚・挙動変更なし)

## §6 残ゲート

- **gate①(裁定)= 不要見込み**(実装層・意味論変更なし)。ただし §3 未検証の
  「列挙に現れない存在」クラスで挙動差が出る場合は軽微裁定へ切替。
- **gate②(golden)= 不要見込み**(挙動不変+視覚変更なし= ECO-134 と同じく機械受入が納品条件の想定)。
- 着手条件: なし(プローブ先行のみ)。関連= ECO-134(同型・R3 所見元)・ECO-130(二段階スキャン)。

## §7 実施記録(fix・未コミット)

- **プローブ先行(R5)**: `CpScanFilePresenceTests` に、`Func<string,bool>` の存在確認 seam で
  StageCore の行ごと存在確認回数を数えるプローブを新設。「`File.Exists` 相当の呼び出し==0」を
  assert し、**是正前は DB 4 行に対して `Expected 0, Actual 4` で不合格**を実測
  (`ViewPrism2.Tests` 932 中 1 fail・他 931 pass)。同時に存在/不在・大小文字差・候補列挙外
  (除外名/対象外拡張子)の missing bit 同値プローブは是正前から合格し、既存意味論を固定した。
- **是正(案A 採択)**: StageCore は一度の `FileSystemEnumerable` 走査から
  `HashSet<string>(StringComparer.OrdinalIgnoreCase)` と後段の scan candidate を構築し、normal/pending
  各行の missing 判定を set lookup へ統合。候補外の既存物理ファイル(hidden・除外名・対象外拡張子・
  root 直下 reparse file)も presence 集合へ含め、scan candidate には従来の K-WINFS 条件だけを通す。
  `ScanService` 内の direct `File.Exists(` は source lint で 0 件を固定し、存在確認は計数 seam へ集約した。
- **R8 所見からの追加 red-first**:
  1. `IncludeSubfolders=false` へ変更後の既存配下行が false missing になる所見を受け、存在する
     `legacy/present.jpg` まで missing になる赤(`Expected [absent], Actual [absent,present]`)を実測。
     normal/pending 既存行の親 prefix だけを `OrdinalIgnoreCase` 集合化し、その prefix に限り存在確認列挙を
     再帰するよう是正。配下ファイルは presence にのみ使い scan candidate にはしないため、従来の
     `IncludeSubfolders=false` 判定も維持。
  2. 親 prefix 構築区間の cancel 到達性後退に対し、cancel 済み token で例外が出ない赤を実測後、
     既存行単位の `ThrowIfCancellationRequested` を追加して緑へ反転。列挙・既存行判定・候補判定の
     token 粒度と、Stage の DB 無変更/generation 契約を維持した。
- **ScanCore 同型の実測判断**: 製品呼出し側は `FolderManagementViewModel` が `LastScan==null` の初回だけ
  `ScanAsync`、再スキャンは `StageAsync` へ分岐する。初回 ScanCore の `existing==0` を計数 seam で駆動し
  存在確認 0 回を実測したため、ScanCore の missing ロジックは挙動不変の seam 化だけ行い、案Aは
  StageCore に限定した。
- **検査封止**: CP-SCAN-004 に、行ごと存在確認 0、direct 呼出し禁止 lint、missing bit 同値
  (存在/不在・大小文字・hidden・候補列挙外・非再帰変更後の既存配下行)、prefix 構築の cancel 到達、
  初回 ScanCore 0 回を追加。固定時間閾値は使用していない。
- **diff 規模**: `ScanService.cs` +101/-19、`CpScanFilePresenceTests.cs` 新設 249 行、
  `33-control-plan.yaml` test_vector 1 行追加。本 ECO 本文の実施記録を除き §5 影響 BOM 内のみ。
- **機械受入(4 点・全緑)**: `dotnet build --no-restore` 0 error/0 warning、
  `ViewPrism2.Tests` **936/936**、`ViewPrism2.Oracle` **109 pass/4 known skip/0 fail**かつ
  `tests/ViewPrism2.Oracle` diff 0=凍結オラクル無接触(R6)、`validate_bom` 0 error/0 warning。
  サンドボックスの外部ネットワーク制限により、復元は既存ローカル NuGet cache を指定し、受入本体は
  `--no-restore` で実行した。
- **R7(セルフゴールデン)=対象外**: Infrastructure 内部の走査形だけを変更し、UI/視覚変更なし。
- **R8(独立セルフレビュー)=実施・未処置スコープ内所見0**: fresh-context reviewer の初回所見は
  非再帰 false missing、prefix 構築 cancel、計数 seam の封止力の 3 件。上記の追加プローブ/是正と
  direct-call lint を再レビューし全件処置済み。reparse directory 配下は既存 K-WINFS
  「リパースポイントを辿らない」により正規スキャンが DB 行を生成しない母集団外、列挙不能は
  `IgnoreInaccessible` と `File.Exists=false` の既存意味論、locked は既存 staging probe の
  presence+read-failure 契約で封止済みと分類した。既知の scan candidate 保持メモリは後続 ECO-138 の範囲。

## §残ゲート(更新)

- **gate①(裁定)=軽微裁定済み(2026-07-23 maintainer 受理)**。是正は §4 案A(単純な set lookup)を
  超え、①ScanCore と共有する `CreateEnumerable` の reparse 扱いを `AttributesToSkip`→predicate へ
  移設 ②`BuildPresenceDirectories`(非再帰フォルダで既存行の親 prefix だけ存在確認列挙を再帰)を
  新設した。この拡張は §3 未検証の「列挙に現れないが File.Exists 真」クラス(IncludeSubfolders=false で
  サブフォルダ配下に既存行がある false-missing)の解消に必要で、§6 の escalation 条項どおり
  **独立レビューが案A 超過として maintainer へ上申→受理**。ScanCore パリティは変更保存(包含結果同一)で
  936+109 全緑が裏付け。独立レビュー註記: 初回レビューは Codex の書き込み途中版(4 テスト)を読み
  lint 不在・テスト数を誤検出したが、安定版で全件 refute(direct-call lint=test 6 実在・936 確定)。
  教訓候補=**異系統独立検査は製造完了の確認後に開始する**(製造と検査の時間的重なりが偽陽性 race を生む)。
- gate②(golden)=**n/a**。missing 判定は挙動 bit 一致(存在/不在・大小文字・hidden・除外/対象外拡張子・
  非再帰変更後の既存配下行をプローブで pin)+視覚変更なし=機械受入がクローズ条件(ECO-134 先例)。
- 機械受入(独立再実測・2026-07-23 stable state): build 0-0 / ViewPrism2.Tests **936-936** /
  Oracle 109pass+4skip+0fail(凍結無接触=R6) / validate_bom 0-0。
