# ECO-145 — M-BOM / Control Plan の欄の所有を「人の裁定層」と「AI の導出層」に分ける試行(2 製造単位・方法論 BomDD ECO-097)(implemented)

- 種別: 工程拡張(M-BOM・Control Plan の欄、テストの trait、承認に添える表。製品コード src は無変更)
- status: **implemented**(2026-10-05 起票 `9f239a8` → 同日 fix〔導出・工場 3 round・機械受入・リハーサル・R8・検査官 r1 REJECT → r2 ACCEPT〕→ gate② 待ち)
- baseline: main `f622c3c`
- 出典: 方法論リポ BomDD ECO-097(maintainer 2026-10-05「M-BOM / Control Plan 再設計に着手して。E-BOM は人間の裁定を行う場所、M-BOM / Control Plan はその裁定に基づいて AI が判断する場所」→ 設計の扱い「A」)。
  事前登録= `../BomDD/bomdd/reports/eco-097-mbom-cp-redesign/preregistration.md`(指標 R1〜R7・停止条件 S1〜S4・本起票より前の commit)。
- 優先度: 中(製品の挙動は変えない)

## §1 要求

対象 2 製造単位= **M-THUMB-008**(E-THUMB-020・surface)と **M-DB-007**(E-DB-010・core)について:

1. M-BOM が自分の言葉で持つ設計の内容(`invariants`)を解消する — 裁定層(要求 10・仕様 20・E-BOM 30・K-BOM 31)に同じ内容がある行は消して参照に任せ、作り方の行は `manufacturing_decisions` へ移す。
2. Control Plan の対象行(CP-THUMB-007・CP-DB-006)に、行が検査する裁定層の ID(`requirement_refs` / `invariant_refs`)と、測る時点(`when`)・落ちたときの振り分け(`on_fail`)を置く。
3. テストに、検査する裁定層の ID の trait(`req` / `inv`)を付ける。
4. 承認に添える表(`bomdd/cp_results.py`)に、**裁定層の ID ごとの結果**を足す(CP 行ごとの表は不変)。

**impact-prospective-01(手順 2b)**: 非適格・理由= 変更要求の原文は方法論側の設計(AI の提案)への選択「A」であり、R を maintainer 原文で固定できない(ECO-143・144 と同型)。機会あり・非起動として記録する。

## §2 工程診断

| 工程 | 判定 | 根拠 |
|---|---|---|
| CAD(ViewPrismUI) | 該当なし | UI・視覚の変更を含まない |
| 要求(10)・仕様(20)・E-BOM(30)・K-BOM(31) | **健全・変更しない** | 対象 2 単位の M の行 6 本の内容は、すべて裁定層に既にある(§3 ①) |
| **M-BOM(32)** | **裁定層の言い換えを持つ(本 ECO の対象)** | M の `invariants` 6 行は仕様・E-BOM・K-BOM の文の言い換え。機械で突き合わせられない並行の記述(BomDD ECO-090 §0.1) |
| **Control Plan(33)** | **裁定層の ID を持たない(本 ECO の対象)** | 65 行とも `requirement_refs`・`invariant_refs`・`verifies` なし。ECO-144 は REQ との対応を vector の文言に書いた |
| 表(cp_results) | キーが CP 行だけ(本 ECO の対象) | 1 つの約束の測定不能が行の区分に現れない(ECO-144 リハーサル 1/2) |
| 実装(src) | 変更しない | — |

## §3 切り分け済みの事実(2026-10-05・統括 AI の実測・HEAD f622c3c)

確定:

1. **対象 2 単位の M の `invariants` 6 行の分類**(BomDD 事前登録 R1・1 行 1 件):

   | 単位 | 行 | 分類 | 裁定層の所在 |
   |---|---|---|---|
   | M-THUMB-008 | INV-009 元画像へ書き込まない | 参照化 | E-THUMB-020 invariants に同文・仕様 INV-009 |
   | M-THUMB-008 | 読み取り不能キャッシュは削除して再生成 | 参照化 | 20-spec.md L346(REQ-040 の項)・REQ-106 rationale |
   | M-THUMB-008 | EXIF 適用は表示系のみ — pHash 入力には適用しない | 参照化 | 20-spec.md L352・REQ-085 statement |
   | M-DB-007 | 接続は単一共有+SemaphoreSlim シリアル化(K-SQLITE) | 製造手段 | 決定は 31-kbom K-SQLITE(ADR-0003)にある → `manufacturing_decisions` に K の参照として残す |
   | M-DB-007 | migrations テーブル契約は REQ-004 のとおり | 参照化 | REQ-004(E-DB-010 requirement_refs) |
   | M-DB-007 | (ECO-059)スキャンバッチの失敗は当該バッチを全ロールバック | 参照化 | E-DB-010 invariants に同旨 |

   **参照化 5・製造手段 1・人へ戻す 0・分類不能 0**。人が新しく裁定する E 側の文は無い → **gate① で決める設計の内容は無い**(裁定層は不変・bom_version と tag は動かさない)。
   方法論側の起票時は E-BOM だけを読んで 2 行を「人へ戻す」と仮に分類していたが、仕様に既にあった(BomDD 事前登録の変更の履歴で訂正済み)。
2. **CP 対象行とテスト**: CP-THUMB-007= 3 クラス・18 テスト(CpThumb007Tests 9・CpThumb049ExifTests 6・CpThumb144VersionPinTests 3)。CP-DB-006= 1 クラス・8 テスト(CpDb006Tests)。trait は `cp` だけ(クラス単位)。
3. **テスト → 裁定層の ID の対応(導出・§4.2 の表)**: 26 テスト中 24 に REQ を特定できる。特定できない 2= 「解像度取得はフルデコードなしで寸法を返す」(EXIF なしの寸法取得を述べる REQ が無い)・
   「COLLATE_NOCASE が主要列に付与されている」(仕様 §2.0 のスキーマ。REQ・ID なし)。INV-009(サムネイル生成が元画像へ書き込まない)を検査するテストは対象行に無い。
4. **裁定層から M への名指し**: 20-spec.md L1345 と 30-ebom.yaml L379 が「単一共有接続 M-DB-007」と M の ID を本文で名指す(参照の欄ではなく文中)。BomDD 事前登録 R5(a) の起票時の値として記録(本 ECO では直さない— 裁定層の文の変更になる)。
5. **M の `interface_contract` に混ざる裁定層の内容**(件数の記録のみ・移さない): M-THUMB-008 の `cache_key`(MD5+`-v2` 世代サフィックス= 仕様 L342〜344・REQ-105)・`params`(長辺 256 等= REQ-040)/ M-DB-007 の `schema`(テーブル一覧・仕様 §2.0)。
6. **validate_bom.py は 32 の unit・33 の行の未知のキーを拒否しない**(起票時 0-0。新しい欄を足した後に再実行して確認する)。

疑い(未検証):

- trait `req` / `inv` をメソッドに付けても xUnit の XML に `cp` と並んで出る(ECO-143 の selftest は複数 trait を扱うが、メソッド単位とクラス単位の混在は未実測)— 製造後の表で確認する。

スコープ外所見(R3・記録のみ): 上記 4(裁定層が M の ID を名指す)・00-manifest の eco_range / open_eco の乖離(ECO-144 と同じ)。

## §4 是正方針(/eco-fix で実施・統括 AI が導出し、工場が製造する)

### 4.1 M-BOM(32)

- M-THUMB-008: `invariants` 3 行を削除(ebom_refs → E-THUMB-020 → REQ-040/085/104〜106・INV-009 を引き継ぐ)。
- M-DB-007: `invariants` 3 行を削除し、`manufacturing_decisions: ["接続戦略は K-SQLITE(ADR-0003)のとおり(単一共有+SemaphoreSlim シリアル化)"]` を置く。
- `interface_contract` は変更しない(§3 ⑤ は件数の記録のみ)。

### 4.2 Control Plan(33)とテストの trait

- CP-THUMB-007: `requirement_refs: [REQ-040, REQ-085, REQ-104, REQ-105, REQ-106]`・`invariant_refs: [INV-009]`・`when`・`on_fail`。vector の文言の【REQ・落ちたら…】(ECO-144)は残す(消すと ECO-144 の記録と食い違う)。
- CP-DB-006: `requirement_refs: [REQ-003, REQ-004, REQ-010, REQ-028]`・`when`・`on_fail`。REQ-005(DB ファイルの場所)は本行のテストが検査しないため入れない(E-DB-010 の requirement_refs には残る= 表では現れない。§4.4 の R5(c) に記録)。
- `when` の語= `acceptance`(機械受入のたび)。`on_fail` の語= `product-fix`(製品修正)/ `instrument-recovery`(測定系復旧)/ `human-approval`(人の承認)。1 行に複数を書ける(例: `on_fail: {red: product-fix, unmeasurable: instrument-recovery}`)。
- テスト → ID(メソッドに `[Trait("req", "REQ-NNN")]`。クラスの `cp` trait は不変):

  | クラス | テスト | req |
  |---|---|---|
  | CpThumb007Tests | Jpg1920x1080は256x144のJpegになる / Png100x50は拡大されずPngのまま_FMEA012 / GifBmpWebpはJpeg出力になる / 縦横比維持で縮小し丸めはHalfAwayFromZero最小1px / キャッシュキーはMD5小文字絶対パスで大文字小文字を同一視する | REQ-040 |
  | CpThumb007Tests | キャッシュヒットで再生成しない | REQ-040, REQ-105 |
  | CpThumb007Tests | 壊れたJpgはNullでキャッシュ記録なし_FMEA012 / 破損キャッシュは削除して再生成する | REQ-040, REQ-106 |
  | CpThumb007Tests | 解像度取得はフルデコードなしで寸法を返す | (なし) |
  | CpThumb049ExifTests | 対照_EXIFなしjpgのサムネと寸法は従来どおり / EXIF回転6のjpgはサムネが正立_縦長になる / EXIF回転6のjpgの寸法メタは実効寸法を返す / 正立ローダ_EXIF回転6は正立ピクセルを返し向きと内容が一致する / 正立ローダ_EXIFなしはnull_従来の直読経路を変えない | REQ-085 |
  | CpThumb049ExifTests | キャッシュ世代移行_旧世代ファイルは参照されず新世代で正立生成される | REQ-085, REQ-105 |
  | CpThumb144VersionPinTests | 3 本すべて | REQ-104 |
  | CpDb006Tests | 新規DBはWALかつFK有効 | REQ-003 |
  | CpDb006Tests | 新規DBはmigrations行数が定義数と一致し全id記録済み / v0DBに全マイグレーション適用で新規DBとスキーマ同値 / ランナーは未適用分をID昇順で適用し新規DBと同値にする_合成マイグレーション | REQ-004 |
  | CpDb006Tests | タグ削除カスケード_4テーブルの状態が仕様どおり | REQ-028 |
  | CpDb006Tests | フォルダ削除でimagesと付与が連鎖削除される / パスの大文字小文字違いは重複として拒否される | REQ-010 |
  | CpDb006Tests | COLLATE_NOCASEが主要列に付与されている | (なし) |

### 4.3 表(cp_results.py)— 裁定層の ID ごとの結果

- 33 の行の `requirement_refs` / `invariant_refs` に現れる ID を集め、ID ごとに、trait `req` / `inv` がその ID のテストの結果を数える。区分は CP 行と同じ定義:
  違反(Fail 1 件以上)/ 合格(Pass 1 件以上・Fail 0・Skip は件数を併記)/ 測定不能(テストはあるが全件 Skip・未実行)/ 未実行(人の承認で検査)(テストが無く、その ID を参照する行がすべて depth G のみ)/ 未実行(検査なし)(テストが無く、それ以外)。
- 別欄: trait にあるが、どの行の refs にも無い ID。
- CP 行ごとの表の出力は 1 文字も変えない(新しい節を後ろに足す)。`--json` は新しいキーを足す。`--selftest` に ID ごとの陽性対照を足す(合格・違反・**行は合格だが ID は測定不能**・検査なし・人の承認・別欄)。
- 判定ではない(ECO-143 の裁定 A のまま・終了コードの意味は不変)。

### 4.4 試行の評価(事前登録= BomDD preregistration.md・本 ECO では再定義しない)

R1 M の行の分類 / R2 人へ戻した判断 / R3 ID ごとの可視性(赤 1 回・Skip 1 回のリハーサル・ECO-144 と同じ注入)/ R4 CP 行の when・on_fail / R5 参照の向きと到達 / R6 異系統の検査官による意味の審査 / R7 対象外の行の区分の不変。
停止条件 S1〜S4 のいずれかが起きたら止めて maintainer に相談する。本 ECO は ECO-143 §4.4「次の 3 件の承認」の 2 件目。

## §5 影響 BOM(案)

| 成果物 | 変更 |
|---|---|
| 32-mbom.yaml | M-THUMB-008・M-DB-007 の invariants 削除・M-DB-007 に manufacturing_decisions |
| 33-control-plan.yaml | CP-THUMB-007・CP-DB-006 に requirement_refs / invariant_refs / when / on_fail |
| bomdd/cp_results.py | 裁定層の ID ごとの表・selftest |
| tests(4 ファイル) | メソッドに trait `req`(属性の追加のみ・テスト本体は不変) |
| .claude/skills/eco-fix | 手順 3.1 に「ID ごとの表も添える」の 1 文(受理側) |
| 10・20・30・31・53・00-manifest | **0**(裁定層は不変) |
| src・既存固定オラクル(41) | **0**(R6) |

## §6 残ゲート

- gate①(maintainer): **決める設計の内容なし**(§3 ①: 人へ戻す 0 行・裁定層は不変)。方法論側の設計の採用は BomDD ECO-097 の「A」(2026-10-05)で済んでいる → /eco-fix へ進む。
- ~~fix(/eco-fix)~~: 2026-10-05 実施(§7)。
- gate②(maintainer): 視覚・挙動の変更なし= golden n/a の受理を依頼する。添えるもの= 機械受入 4 点・CP 行ごとの表・**裁定層の ID ごとの表**・検査官の判定。

## §7 実施記録(/eco-fix・2026-10-05)

**役割**: 裁定層= 不変(maintainer が裁定済みの版)/ 導出(32・33・テスト → ID の対応・表の仕様)= 統括 AI(BomDD producer・claude-fable-5-1)/ 製造(trait の付与・cp_results)= 工場 Codex(gpt-5.6-sol・3 round)/
導出の意味の審査= 異系統の検査官(Codex・EQ-002・製造者の導出記録を見せない)/ 独立レビュー(R8)= 別文脈の Claude(読み取りのみ)。ブリーフ・報告・導出記録・リハーサルは BomDD `bomdd/reports/eco-097-mbom-cp-redesign/`。

**diff**(fix commit): `bomdd/32-mbom.yaml`(M-THUMB-008・M-DB-007 の invariants 6 行を削除・M-DB-007 に manufacturing_decisions 1 行)/ `bomdd/33-control-plan.yaml`(CP-THUMB-007・CP-DB-006 に requirement_refs / invariant_refs / when / on_fail・
CP-DB-006 の characteristic・CP-THUMB-007 の vector 1 本の対応を REQ-040 へ)/ `bomdd/cp_results.py`(裁定層の ID ごとの表・selftest)/ テスト 4 ファイル(メソッドに trait `req` 29 個・本体不変)/ `.claude/skills/eco-fix/SKILL.md`(手順 3.1 に 3 行)/ 本 order・register。
**src・10・20・30・31・53・00-manifest・41 は無変更**(R6)。

**導出の記録**(BomDD `derivation.md`): M の invariants 6 行= 参照化 5・製造手段 1・人へ戻す 0・分類不能 0。人へ戻さず統括 AI が決めた判断 9 種を列挙。

**工場の報告**: r1(trait 28・表の拡張)DONE — selftest OK・build とフィルタ実行は sandbox の NuGet 拒否(NU1301)で走らず(受理側で全件実行)/ r2(表の ID の集合に E 品目が負う ID を足す— 受理側の発見: 行の refs に入れなかった要求が表から消える)DONE /
r3(R8 所見の是正)DONE・自己受入で F2 の変異が FAILED になることを確認。ずる報告= 実装の形のみ(関数の分け方・JSON の細部)。

**R5(プローブ先行)**: 欠陥是正ではなく工程の拡張 — R5 対象外を宣言し、代替= リハーサル(下記)で「測定不能・違反になるべき入力で、表の ID の区分が変わる」ことを実測した。

**機械受入(4 点)**(受理側・最終版): `dotnet build` 0 エラー / 0 警告 / `dotnet test tests/ViewPrism2.Tests` **977/977**(skip 0)/ `dotnet test tests/ViewPrism2.Oracle` 109 合格+skip 4 / `python bomdd/validate_bom.py` 0-0。`python bomdd/cp_results.py --selftest` OK。

**表(eco-fix 3.1)**: cp-results.xml **sha256 bfc6aa81e93d**・Debug・終了 2026-10-05T05:27:03+09:00・総数 977(dotnet test の合計と一致)。
- CP 行ごと: **違反 0 / 測定不能 0 / 未実行(検査なし)4 / 未実行(人の承認で検査)3 / 合格 57**(ECO-143・144 と同じ区分・件数。別欄: retired 1・台帳に無い ID 2)。
- **裁定層の ID ごと: 違反 0 / 測定不能 0 / 未実行(検査なし)6 / 人の承認 0 / 合格 10**。
  合格= REQ-003(1)・REQ-004(3)・REQ-010(3)・REQ-014(1)・REQ-028(1)・REQ-040(8)・REQ-085(6)・REQ-104(3)・REQ-105(2)・REQ-106(1)。
  検査なし= INV-009(CP-THUMB-007 の refs・サムネイル生成が元画像へ書き込まないことを検査するテストが本行に無い)/ REQ-005・INV-W1(E-DB-010 が負うが CP-DB-006 のテストは検査しない)/
  REQ-063・REQ-084・REQ-090(E-SIMCACHE-033 が CP-DB-006 を acceptance_refs に持つため表に入る。他の行のテストが検査している可能性があるが trait `req` が無いので見えない)。

**リハーサル(BomDD `rehearsal.md`・最終版で再実行)**: ①trait `req` を持つ 25 テストを Skip → CP 行ごとの表= CP-THUMB-007 は合格のまま(18 本中 1 本が残る)・CP-DB-006 は測定不能(8 本すべて)/ **ID ごとの表= 10 ID すべて測定不能**
②10 ID を覆う 9 テストに Assert.Fail+53 の版を 3.119.5 → **ID ごとの表= 10 ID すべて違反**。BomDD 事前登録 R3= 10/10 の ID で 2/2(ECO-144 は行の区分で 1/2)。
限界= ID のテストの一部だけが Skip のときは合格のまま(行と同じ定義)。一時変更は実行後に復元(`git diff` の sha256 前後同一)・結果ファイルは最終の受入実行のものへ戻した(sha256 bfc6aa81e93d)。

**検査官の意味の審査(BomDD 事前登録 R6・`inspection-report-eco-145*.md`)**: r1 **REJECT IA-01**(blocking: 「破損キャッシュは削除して再生成する」の REQ-106 trait は REQ-106 の statement と対応しない= 検査しているのは REQ-040)・
non-blocking(COLLATE のテストは REQ-010・REQ-014 に対応・CP-DB-006 の refs に REQ-014 が無い)→ **是正**(受理側・属性 3 行と 33)→ r2 **ACCEPT**(所見なし)。ID ごとの判定(r2)= 合 5(REQ-003・004・028・040・105)・条件付き 5(REQ-010・014・085・104・106)・否 0。
M の 6 行の削除は「裁定層に同内容あり・意味の喪失なし」と確認(所在つき)。
**maintainer へ戻す 1 件(裁定層の文の食い違い・AI は直さない)**: REQ-106 の rationale の受入記載(ECO-144 gate① で確定)は「キャッシュファイル破損 → 削除+再生成」を REQ-106 の受入に挙げるが、statement(壊れた画像があってもスキャンと一覧は止まらない)はそれを述べない。
検査官は statement を基準に「対応しない」と判定した。今回は statement に合わせて trait を外した(検査自体は REQ-040 の検査として残る)。rationale を直すか・statement を広げるかは maintainer の裁定。
**検査官が列挙した「届いているが一部しか測っていない」**(表では合格と出る): REQ-106 の「スキャンと一覧の継続・他の画像の処理と表示の継続」(trait を持つテストは「壊れた jpg → null・例外なし」の 1 本)/ REQ-085 の Orientation 2〜5・7・8 とビューア表示 / REQ-104 の更新時の再検査(人の承認)/ REQ-010・REQ-014 の本行で扱わない部分。

**R8 セルフレビュー**(別文脈・読み取りのみ・実行による再現つき・BomDD `review-and-inspection.md`): **blocking 2・non-blocking 6・info 5**。処置:
1. [blocking] ID ごとの表の経路の例外で、既存の行ごとの表まで出なくなる・終了コードが変わる(list を含む acceptance_refs・UTF-8 として不正な 30・整数の id)→ **是正**(工場 r3: ID ごとの表の全体を保護)。受理側の再現 4 入力すべて 終了 0・行ごとの表あり。
2. [blocking] selftest に「行は合格だが ID は測定不能」の腕が無かった(本拡張の目的の腕が空)→ **是正**(合格の行に同居)。受理側の変異(ID の測定不能 → 合格)で selftest FAILED。
3〜7. [non-blocking] selftest の抜け / refs を持つ行が無いとき別欄が隠れる / `INV-\d+` が INV-W1 を拾わない / ID の前後の空白 / ebom_path なしの作り物のパス → **是正**(r3)。
8. [non-blocking] `lifecycle_state` の除外は本リポの 30 では効かない(欄が無い)→ 現状維持(害なし)・記録のみ。
9〜13. [info] fail-open の経路なし / 行ごとの出力は HEAD 版と先頭一致 / テストは Trait 行の追加のみ / YAML 正常 / E 品目経由の ID の表示 → 11 のみ是正(「経由する行」を表示)。
未処置のスコープ内所見= **0**。是正後に機械受入 4 点と表を再実行(上記が最終)。

**停止条件(BomDD 事前登録)**: S1(一括の書き換え)発生せず / S2 該当行 0 / S3(対象外の行の区分・機械受入の変化)発生せず / S4(検査官の否の是正に対象を超える再裁定)発生せず(IA-01 の是正は対象行の内側・裁定層の食い違いは 1 件を maintainer へ戻す)。
