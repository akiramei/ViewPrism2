# ECO-134 — 再スキャンの再リンク候補照合が二乗スケール(O(missing × new))

- 種別: 不具合(実装層・性能欠陥。挙動は仕様どおりでアルゴリズムのみ二乗)
- status: staged
- baseline: main `0947d0e`
- 報告者: maintainer(2026-07-22・スキャン処理調査)
- 優先度: 高

## §1 症状

再スキャンの新規ファイル判定で、missing 行と新規ファイルの候補照合が **O(missing × new)** で
スケールする。変更なし〜大量差分のいずれでも、新規ファイル 1 件ごとに missing 行全体を線形走査
するため、wrong-drive(別 HDD 同一ドライブレター)や大量移動のような **26 万件級**では
SHA-256 とは別に大きな CPU 時間が加算される。

### 実測(Release 合成計測・I/O・SQLite 除外)

| missing × new | 所要 |
|---|---|
| 1,000 × 1,000 | 11.8 ms |
| 2,000 × 2,000 | 46.7 ms |
| 4,000 × 4,000 | 269.1 ms |
| 16,000 × 16,000 | 2.18 秒 |

倍増ごとに概ね 4 倍 → 二乗増加を確認。16,000×16,000 の 2.18 秒は SHA-256/I-O を除いた
**純 CPU 照合コスト**であり、mass-missing × mass-new が同時成立する経路では体感遅延に直結する。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 健全 | スキャンの候補ヒント挙動は仕様どおり。視覚・操作の変更なし |
| BOM(spec/受入観点) | 部分欠落 | 挙動契約(同ハッシュ・id 昇順先頭)は健全。ただし候補照合の**計算量**は未封止(§4) |
| 実装 | **逸脱** | 結果は正しいが、候補照合を新規ファイルごとの線形走査で実装=二乗 |

工程診断の結論: **実装層の性能欠陥**。挙動は仕様(規則 3a=「同一フォルダ内の同ハッシュ
missing は candidate_link_id ヒント・複数一致時は id 昇順の先頭」)に忠実で、逸脱は
**アルゴリズムの計算量のみ**。CAD/仕様の意味論変更は不要 → gate①(裁定)不要。

## §3 切り分け済みの事実(確定と未検証を分離)

### 確定(コード実測)

1. [ScanService.cs:223](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L223) で
   `missingInFolder = current.Where(i => i.Status == Missing).ToList()` を作り、
   [ScanService.cs:243-245](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L243)
   で `ScanDbFacts(..., missingInFolder)` として **列挙対象の全新規ファイルに同一リストを渡す**。
2. [ScanJudge.cs:102-106](../src/ViewPrism2.Core/Services/ScanJudge.cs#L102) が、新規判定
   (`isInitialScan==false` の (3-再) 経路)で **新規ファイル 1 件ごとに**
   `db.MissingInFolder.Where(hash 一致).OrderBy(id).FirstOrDefault()` を実行 → 各回 O(missing)。
   新規 N 件 × missing M 行 = **O(M × N)**。
3. 挙動は正(hash 一致・id 昇順先頭)であり、**結果を変えずに** hash→最小 id 辞書を一度構築すれば
   O(M + N) にできる(§4 是正方針)。
4. 混入=**初回製造 `b1f13ec`(Phase 4 Run2)**。`ScanDbFacts.MissingInFolder`+規則 3a の
   線形走査はスキャンサービス初版から存在。二段階化(ECO-129 `4deebc2`/ECO-130 `7900c8b`)も
   この走査形を温存(候補照合ロジックは非改変)。
5. 潜伏マスキング=中規模では HDD の SHA-256/メタデータ I/O が支配的で二乗項が埋没。
   mass-missing × mass-new が同時成立する経路(wrong-drive・大量移動)でのみ CPU 項が顕在化。
6. 機械受入は現状 **Release `ViewPrism2.Tests` 921/921 合格**=機能契約は緑。この性能特性は
   テスト対象外(封止されていない)。

### 未検証(疑い)

- 実 HDD 26 万件級での候補照合 CPU 時間の絶対値(合成計測は I/O・SQLite 除外の下限。
  /eco-fix のスケールプローブで mass-missing × mass-new を実測裏取りする)。

## §4 是正方針(案・着手時確定)

- **案A(推奨)**: 候補照合を **hash→最小 id 辞書の一度構築**へ置換し O(M+N) 化。
  - `missingInFolder` から `Dictionary<hash, minId>`(id 昇順先頭=既存挙動と同値)を StageCore で
    1 回構築し、判定器へ辞書(または解決済み lookup)を渡す。**結果は現行と bit 一致**を維持。
  - 判定器 [ScanJudge.cs:102](../src/ViewPrism2.Core/Services/ScanJudge.cs#L102) の `Where→OrderBy→
    FirstOrDefault` を辞書 lookup へ。`ScanDbFacts` の候補提供口を List から解決済み写像へ変更。
- **プローブ先行(必須)**: 是正前に不合格となる **mass-missing × mass-new スケールプローブ**で
  二乗を実測裏取り(是正後は O(M+N) で線形域に収まることを確認)。挙動同値プローブ
  (同ハッシュ複数一致 → id 昇順先頭)で結果不変を pin。
- **検査工程の封止(§4 併修)**: [33-control-plan.yaml:100](33-control-plan.yaml#L100) 近傍の
  スキャン CP に、**候補照合の計算量(スケールプローブ)**を再発防止観点として追加。
  ※ピークメモリ(所見2)・区間別 throughput(所見4 の残り)は本 ECO スコープ外。

## §5 影響 BOM

- `src=ScanService.cs`(missingInFolder の写像化・StageCore で辞書構築)+
  `ScanJudge.cs`(候補 lookup 化)+`ScanDbFacts`(候補提供口の型変更)
- `spec`=候補照合の計算量特性(O(M+N))を挙動契約近傍へ明文化(立地は fix 時判断)
- `tests`=mass-missing × mass-new スケールプローブ+挙動同値プローブ(id 昇順先頭)
- `bomdd/33-control-plan.yaml`=スキャン CP へ候補照合計算量の再発防止観点を追加
- `CAD`=視覚・挙動変更なし(該当なし)

## §6 残ゲート

- **gate①(裁定)=不要**。実装層の性能欠陥(CAD/仕様の意味論変更なし)。着手条件=なし。
- **gate②(golden)=不要見込み**。挙動不変(結果 bit 一致)+視覚変更なし。機械受入
  (スケールプローブ緑+挙動同値プローブ緑+既存 921 緑維持)が納品条件。念のため
  維持者へ「再スキャンの候補ヒント(移動元→移動先の relink)が従来どおり効くか」の実機一瞥は
  推奨(必須ではない)。

## スコープ外所見(R3・本 ECO に混ぜない)

同一調査で観測。今回は起票せず(maintainer 裁定=所見1 のみ単独起票)。後続判断:

- **所見2(中)**: 二段階スキャンのステージングが変更全件をメモリ保持
  ([ScanService.cs:165](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L165) の
  add/meta/status リスト+[ScanStaging.cs:76](../src/ViewPrism2.Core/Models/ScanStaging.cs#L76)
  の完全 ImageRecord 群)。26 万件級全面差分でピークメモリ大・RSS 上限/計測ガードなし。
- **所見3(中・要実測)**: 再スキャンが DB 行ごとの `File.Exists`
  ([ScanService.cs:196](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L196))後に
  ディレクトリ全体を再列挙([ScanService.cs:231](../src/ViewPrism2.Infrastructure/Scanning/ScanService.cs#L231))
  =二重メタデータ走査。HDD ではランダム存在確認が支配的になり得る。
- **所見4の残り**: 検査工程([33-control-plan.yaml:100](33-control-plan.yaml#L100))の
  ピークメモリ・区間別 throughput 未封止(本 ECO は候補照合計算量のみ封止)。
