# Work Order — loop-v4-repair-lifecycle(v4.0)

> V1〜V3 の Work Order は git 履歴(tag loop-v1-r1〜r3 / loop-v2-r1 / loop-v3-r1）を参照。本書は V4 増分製造の指示書。

## 目的
ViewPrism2 の V1/V2/V3 完成コードベース(タグ × 仮想ビューの Windows 向け画像管理アプリ、.NET 10 / Avalonia 12 / SQLite、unit 347 全緑)に対し、**修復ライフサイクル**=「criteria 条件検索」「relink(再リンク)拡張」「トラッシュの復元・完全削除」を、本製造パッケージのみから増分製造する。状態機械(normal/pending/missing/deleted)の遷移 T1〜T8 を完成させる。判定系(criteria マッチ・relink 確定・復元の存在分岐・完全削除の CASCADE)は核、操作系(修復 UI)は表面。

## 入力(これがすべて。これ以外を参照しない)
- `bomdd/20-spec.md` v4.0(**§2.11 修復ライフサイクルが今回の中心**。状態機械 T1〜T8・観測契約 §2.8 OC-19〜22 を含む)
- `bomdd/30-ebom.yaml` / `31-kbom.yaml`(K-WINFS/K-SQLITE 既存で足りる)/ `32-mbom.yaml` / `33-control-plan.yaml` / `34-routing.yaml`(routing_v4)
- `docs/adr/`（既存。V4 で新規 ADR は不要）
- 既存コードベース `src/` / `tests/ViewPrism2.Tests`(V1/V2/V3 完成品 — 読解・拡張してよい）。特に **RelinkService(再リンク核)・ScanJudge(3a pending+candidate_link_id)・ImageStatus enum・ImageRepository.DeleteAsync(行削除→FK CASCADE)** を再利用する

原典 TypeScript 実装・設計対話・固定オラクル(`tests/ViewPrism2.Oracle` 含む）は供与されない。参照を試みないこと。

## 製造対象(増分）
| 製造単位 | 内容 | 受入 |
|---|---|---|
| M-CRITERIA-024 | criteria 条件検索(hash/名前/拡張子/mtime/サイズ AND・status 用途別・安定順・空条件非実行。純粋計算 CriteriaMatcher + サービス） | CP-CRITERIA-019(unit) |
| M-RELINK-025 | 既存 RelinkService を criteria 候補探索+**タグ安全ガード**へ拡張(候補=pending∪untagged-normal・タグ付きは拒否→マージ案内・missing 側 ID 不変・候補消費削除） | CP-RELINK-019(unit) |
| M-TRASH-026 | トラッシュ復元(存在=Normal/不在=Missing の分岐）+完全削除(images 行削除+FK CASCADE・**物理非破壊**)。IFilePresenceProbe(Core 抽象）/FilePresenceProbe(Infrastructure=File.Exists） | CP-TRASH-020(unit/L2)+**CP-TRASH-001(L3 物理差分)** |
| M-UI-REPAIR-027 | 修復 UI(criteria 検索フォーム・relink 候補/確定・トラッシュ復元/完全削除+非破壊明示文言）。V3 の TrashViewModel を拡張 | CP-UI-G10 / CP-L1-SMOKE |

## 回帰の規律(増分製造の絶対条件）
- **既存の意味論を変更しない**。既存 unit テスト 347 件(v1 234+v2 56+v3 56+P-09 1)の修正・削除は原則禁止。
  期待値変更が必要だと判断した場合は当該単位を `blocked` にして C6 報告(テストを書き換えて緑にしない）。
- **V4 はスキーマ変更なし**: criteria=既存列照会・relink=candidate_link_id 既存・復元=status 更新・完全削除=行削除+既存 FK CASCADE。
  **新規マイグレーション/列の追加は禁止**(必要だと判断したら blocked+C6）。CP-DB-006/スキーマ同値の回帰を壊さない
- 既存機能のリファクタリングは今回のスコープに必要な最小限に留める(不要改変はずる報告対象）

## 安全の規律(INV-009 — 第 2 の実アクション）
- **復元は物理ファイルの読み取り存在確認(File.Exists 相当）のみ**。書き込み・移動・削除・リネームを一切行わない。
  存在すれば status=Normal(T6）、不在なら status=Missing(T7=幽霊 normal 防止）
- **完全削除は DB レコード(images 行）の削除のみ**。物理画像ファイルへ一切触れない(image_tags/image_features/image_similarity は FK CASCADE で消える）
- `CP-TRASH-001`(L3 物理差分）が復元・完全削除の前後でファイル集合/SHA-256/mtime 不変を実証する。物理ファイル操作の実装を検知したら当該単位を `blocked` とし C6 報告(**最優先報告**）
- **層規律(ADR-0002）**: File.Exists 等の FS アクセスは Infrastructure(FilePresenceProbe）に閉じる。TrashService(Core）は `bool fileExists` を受けて遷移を判断する(IPHashImageReader と同型）。Core に System.IO の物理アクセスを持ち込まない

## 必須受入(自己受入）
- `dotnet build ViewPrism2.sln -c Release` が警告ゼロで成功する(TreatWarningsAsErrors）
- `dotnet test tests/ViewPrism2.Tests -c Release` が成功する(**既存 347 件+v4 新規の全緑 = 回帰ゼロ**）
- 受入ハーネスの必須範囲: CP-CRITERIA-019 / CP-RELINK-019 / CP-TRASH-020 / **CP-TRASH-001** の test_vectors 全被覆。CP-DB-006(スキーマ同値）は**不変のまま緑**(migration を足していない証拠）
- **L1 スモーク(CP-L1-SMOKE v4.0 経路)**: 起動→(missing/pending を含むコレクション）→**criteria 検索→missing を relink で復旧→トラッシュで deleted を復元(存在/不在）→完全削除→ディスク上の画像ファイルが残存することを確認**
- G 行(CP-UI-G10）は承認者(maintainer）の受入であり、工場の自己受入対象外。ただしチェックリスト項目(20-spec.md §2.6 G-10）を満たすよう実装する

## 調達部品の規律
依存パッケージは `32-mbom.yaml` の `procurement` に列挙されたものだけを使う(全 exact ピン）。**V4 で新規パッケージは追加しない**。OpenCV 系・サードパーティライブラリの採用は**ずる報告対象**。

## ずる報告(義務）
製造中に BOM/K-BOM/Control Plan から導けなかった判断は、**実装を止めずに**全件 cheat-log 形式で報告する:
```
### CHEAT-<ID> [C1〜C6] 一行要約
- 手法が与えなかったもの:
- 代替した判断(何をどう埋めたか):
- 重大度: blocker / friction / minor
```

特に以下の次元は判断したら必ず報告する(V4 重点）:
- 物理ファイルに触れる実装を入れた場合(**最優先報告** — INV-009/FMEA-027）
- **復元の存在分岐**(存在=Normal/不在=Missing）を変えた場合(FMEA-026/INV-013）
- **relink のタグ安全ガード**(タグ付き候補拒否・候補=pending∪untagged-normal）を変えた場合(FMEA-028/INV-015）
- **完全削除の対象**(deleted 限定）・CASCADE 依存を変えた場合(FMEA-030/INV-014）
- **criteria の AND・空条件非実行・status 対象**を仕様と変えた場合(FMEA-029）
- **スキーマ変更**(migration/列追加）を入れた場合(本来不要 — 入れたら必ず報告）
- UI レイアウトの寸法・配色・アイコン・**完全削除の非破壊明示文言**で K-DESIGN に無い値を使った場合
- i18n の新規キー追加・命名(repair.* / trash.restore.* / trash.purge.*）

## 進めない級の問題(blocker)を発見した場合
BOM の自己矛盾・実装不能を発見した場合は、**当該製造単位を `blocked` とマークして他の単位を続行**し、cheat-log に C6(手戻り）として記録して納品時に報告する。製造を中断しての質問往復はしない(隔離の維持。修正は設計者側が BOM を改訂し fresh re-run する）。

## 自己受入が赤のままの場合
**自己受入に FAIL が残る状態は「納品」ではない。** 緑にできない場合は `stop/report`: 製造を停止し、FAIL 一覧・原因の見立て・試した修正を報告して終了する(赤のまま納品しない）。
