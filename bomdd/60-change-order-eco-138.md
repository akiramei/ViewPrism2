# ECO-138 — 二段階スキャンのステージングが変更全件をメモリ保持(26 万件級全面差分のピークメモリ未封止)

- 種別: 不具合候補(実装/設計層・性能特性・**要実測**。gate① 要否は工程診断で確定)
- status: staged(2026-07-23 起票。出典= ECO-134 R3 スコープ外所見2+所見4 残り・maintainer 裁定で単独起票)
- baseline: main `f217470`
- 優先度: 中(着手順序= ECO-137 クローズ後・実測先行)

## §1 症状

二段階スキャンのステージングが変更全件をメモリ保持する:

- [ScanService.cs:166-169](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L166) の
  statusUpdates/deletes/adds/metaUpdates リスト+
  [:186](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L186) の判定用ビュー `current`
  (existing 全行の ImageRecord)
- [ScanStaging.cs:83](../src/ViewPrism2.Core/Models/ScanStaging.cs#L83) の `Adds`= 完全 ImageRecord 群
  として、サマリー提示〜適用まで保持

26 万件級の全面差分(wrong-drive・大量移動)でピークメモリ大。**RSS 上限・計測ガードなし**。
既往同族= ECO-062(6,698MiB 消費・26 万件全走査)— メモリ×スケールの検査盲点クラスの 2 例目。

## §2 工程診断(**本 ECO の第 1 作業 — 完了 2026-07-23**)

staging 全件保持が二段階スキャンの意味論とどこまで結合しているかを確定してから案を出す:

1. **適用のアトミック性/部分失敗回復** — ECO-133 の回復モデル(部分適用あり得る・回復は再スキャン)
   はバッチ適用前提。ストリーミング化はコミット単位を変えうる。
2. **サマリー集計** — ECO-136 の TotalMissingAfterApply は staging 由来。集計確定時点が変わる案は
   率カードの意味論に触れる。
3. **キャンセル・進捗** — Phase 境界(Stage 完了→Summary→Apply)の観測点が変わるか。

結合が深い案(ストリーミング/バッチ化)は**意味論変更= gate① 裁定必要**。
軽量化のみ(下記案a)なら実装層で閉じる見込み。

### 診断結論(2026-07-23)

§3 の実測で **worst-case ピーク ≈ 300 MiB(既往同族 ECO-062=6,698 MiB の 1 桁下・一時)** と確定。
許容圏内につき**データ構造は改修せず封止のみ(案c)**を maintainer 裁定(§4)。結合3点(適用アトミック性/
サマリー集計/Phase 境界)には**一切触れない**ため gate① 不要・実装層(=検査+台帳)で閉じる。

## §3 切り分け済みの事実

### 確定(コード実測)

- 保持箇所は §1 のとおり(現 HEAD で確認済み)。ImageRecord 完全体を Add 対象全件で保持。

### 実測済み(2026-07-23・合成 staging N=260,000・I/O/SQLite 除外・workstation GC)

`GC.GetTotalMemory(true)` の live 差分で retained managed バイト数を採取(scratchpad 計測・下限値):

| 保持対象 | retained | per-row | 保持期間 |
|---|---|---|---|
| `adds` List\<ImageRecord\>(全新規=支配項) | 130.8 MiB | 528 B | summary→apply |
| `presentPaths` HashSet\<string\> | 30.9 MiB | 125 B | StageCore 中のみ |
| `existing` List\<ImageRecord\>(DB 全行) | 130.9 MiB | 528 B | StageCore 中のみ |
| **StageCore ピーク**(3 者同時) | **≈ 293 MiB** | — | 一時 |
| **summary→apply 保持**(`ScanStaging.Adds` のみ) | **≈ 131 MiB** | — | 裁定待ち中 |

- 補正: 合成では `CreatedDate/ModifiedDate` を共有リテラルにしたため下限。実 scan は per-row 生成で
  非共有 → 実ピークは **+10〜20% ≈ 330 MiB 程度**見込み。いずれも ECO-062(6,698 MiB)の 1 桁下。
- **案a の効果限定を実測が示す**: `adds` は DB へ INSERT するため全カラム必要(`Notes` は既に null)で
  落とせるフィールドがほぼ無い。軽量化が効くのは判定用の `existing`(Id/Hash/Status/RelativePath で足りる)
  だが、これは StageCore 中の一時保持で summary→apply には残らない。
- 結論: **実測が許容内 → 是正せず封止のみで閉じる(案c 確定)**。

## §4 是正方針(**確定= 案c 封止のみ・maintainer 裁定 2026-07-23**)

- **案a**(不採用): staging 行の軽量化。実測で `adds` は INSERT に全カラム必要=効果限定と判明(§3)。
- **案b**(不採用): バッチ/ストリーミング適用。§2 の結合が深く意味論変更= gate① 必要。300 MiB 一時
  ピークに対し過剰。
- **案c(採用)**: データ構造は改修せず封止のみ。製造物は以下の 2 点(いずれも実装/台帳層・挙動不変):
  1. **allocation 上限プローブ**(件数比例・**red-first でなく非空虚な回帰ガード**): staging メモリが
     件数 N に対し `O(N × 有界行サイズ)` であることを pin。`GC.GetAllocatedBytesForCurrentThread`
     等で採取し、**固定バイト/時間閾値は置かない**(件数比を検査)。行あたり保持が青天井化する回帰
     (例: 有界化済みの `Examples` を全件化・enumeration 全体を保持)で fail する形にする。
     プローブが空虚でない証明を添える(一時的に per-row を膨らませてガードが trip することを確認→戻す)。
  2. **control-plan 封止**(所見4 の残り): [bomdd/33-control-plan.yaml](33-control-plan.yaml) のスキャン CP へ
     **ピークメモリ特性 + 区間別 throughput** の 2 軸を追加(ECO-134=計算量・ECO-137=走査回数に続く
     性能 CP の多軸封止の完了)。
- 見送り: 実行時 soft-warning ログは**本 ECO では追加しない**(挙動/ログ出力が変わり golden を要するため。
  純封止=挙動不変を保ち gate② n/a を維持)。必要なら別 ECO 候補。
- **受入形式(R5 で設計)**: 走査回数型の決定論プローブは使えない — **件数比例の allocation 上限
  プローブ**(GC.GetAllocatedBytesForCurrentThread 等)で「staging メモリは件数 N に対し
  O(N×行サイズ上限)」を pin する形を設計(固定時間閾値を置かない方針のメモリ版)。
- **検査封止(所見4 の残りを本 ECO で併修)**: スキャン CP へ **ピークメモリ特性+区間別 throughput**
  を追加(ECO-134= 計算量・ECO-137= 走査回数に続く残り 2 軸 — 性能 CP の多軸封止)。
- **停止意味論の 2 不変条件(起票時から R5 対象)**: staging を触るため token 伝播・世代ガードの
  CP 行チェックを含める(ECO-075 昇格規則・control-plan)。

## §5 影響 BOM(案c 確定後)

- `src=` **変更なし**(案c=封止のみ。ScanService.cs / ScanStaging.cs は無改修)
- `tests=` allocation 上限プローブ 1 本(件数比例・非空虚)を新規追加(既存 scan テストに倣う)
- `bomdd/33-control-plan.yaml`= スキャン CP へピークメモリ特性+区間別 throughput の 2 行
- `CAD`= **なし**(進捗・サマリー表示は不変)

## §6 残ゲート(案c 確定後)

- **gate①(裁定)= n/a**(案c 確定で意味論不変・実装/台帳層で閉じる。診断結論=§2)。
- **gate②(golden)= n/a**(挙動/視覚不変=実機に照合対象なし。ECO-134 と同型の性能封止=プローブ+
  台帳で機械受入完結)。
- 着手条件: ECO-137 クローズ後+§3 実測先行 → **両条件充足済み**(ECO-137 accept `f4b7ffa`・実測 2026-07-23)。
- 製造委譲: 案c の狭義製造(プローブ+CP)を外部 AI 工場(Codex)へ委譲(/factory-delegate・2026-07-23)。
- 関連: ECO-134(R3 所見元)・**ECO-062(既往同族= メモリ×スケール)**・ECO-129/130(二段階スキャン)・
  ECO-133(部分失敗回復)・ECO-136(サマリー集計が staging 由来)。

## §7 クローズ(2026-07-23・案c=封止のみ・gate①/② n/a)

### 実施
- 製造= 外部 AI 工場 **Codex**(codex-cli 0.144.1・ChatGPT ログイン)へ /factory-delegate 委譲。src・凍結
  Oracle・work order 無改修。製造物 2 点:
  1. `tests/ViewPrism2.Tests/CpScanStagingMemoryTests.cs`(新規): `GC.GetAllocatedBytesForCurrentThread`
     で N=4096/2N=8192 の staging 構築 allocation を測り、2N/N 比 1.75〜2.25 + per-row ≤2048 B を
     決定論的に帯域判定。**非空虚証明済み**(per-row に enumeration 全体を保持する回帰を一時注入 → 比
     3.8867 で fail → 除去して全緑)。最終実測= 比 1.9932・per-row 940.6 B。
  2. `bomdd/33-control-plan.yaml` CP-SCAN-004 に ECO-138 行(ピークメモリ特性+区間別 throughput)を追加
     = ECO-134 計算量・ECO-137 走査回数に続く**性能 CP 多軸封止の完了**(所見4 消化)。

### 受入(機械・設計者再実測=工場自己申告の裏取り)
- `dotnet build` 0/0 ・ `dotnet test tests/ViewPrism2.Tests` 937/937 ・ `tests/ViewPrism2.Oracle`
  109 pass/4 skip(無接触・diff 空)・ `validate_bom.py` 0/0。
- 独立受入検査(異系統=Claude): 実 diff は §5 影響 BOM 内(src 空・Oracle 空)・案超過なし・プローブ非空虚を
  ログで裏取り。所見1(CP 文言「exact」→帯域判定へ是正)を反映。

### 教訓(CP 観点=再発防止)
- **性能封止は「壁時計」でなく「決定論的 seam」で pin する**(ECO-134 走査回数・ECO-137 File.Exists 計数に
  続く 3 例目。本件は `GC.GetAllocatedBytesForCurrentThread` = GC 非依存・スレッドローカルで flaky 化しない)。
- **是正の要否は実測で決める**: 疑い(26 万件でメモリ大)を合成 staging の allocation 実測で反証(ピーク
  ≈300 MiB= ECO-062 の 1 桁下・一時)し、**構造改修せず封止のみ(案c)**で閉じた。「疑わしきは是正」でなく
  「実測が許容内なら封止」= 過剰改修の回避(§2 の二段階スキャン意味論〔適用アトミック性/集計/Phase 境界〕を
  一切触らずに済んだ)。
- **合成プローブの被覆限界を明記する**: 案c は src に seam を足さないため、プローブは実 StageCore 経路でなく
  行形状(`List<ImageRecord>`/`HashSet`)の線形性を測る近似ガード。非空虚は証明済み・`Examples` 上限は既存
  `CpScanStagingTests` が別途 pin=被覆漏れなし、と限界を台帳に残す(meta-failure 予防=transfer-04 型)。
- **外部 AI 工場委譲の運用**(/factory-delegate 2 例目・ECO-137 に続く): 診断/実測/裁定は設計者(Claude)が
  保持し、確定 work order の狭義製造(コーディング)のみ Codex へ委譲。製造完了はツリー(mtime 安定+ロック
  不在)で判定し「completed」表示を一次真実にしない。受入 4 点は設計者が再実測(工場自己申告=比 1.9932 等を
  裏取り)。
