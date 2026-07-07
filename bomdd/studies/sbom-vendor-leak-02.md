# sbom-vendor-leak-viewprism2-02 — vendor leak 遡及測定(2 例目・scratch)

目的: 1 例目(LibraryLending・match)の測定手順が部品数十・29 unit の実題材へ一般化するかの検証と、
宣言(substitutable)vs 実測(leak)の較正。対象= ViewPrism2 HEAD(As-Maintained)。
性質の異なる 4 部品を層別測定。測定日= 2026-07-08。

## 層別プロファイル(出現数)

| 部品 | contract(10/20/30) | oracle(41/42/治具) | K/M(31/32) | src | axaml | tests | 宣言(procurement) |
|---|---|---|---|---|---|---|---|
| SkiaSharp(+SK* 型) | 11 | **82** | 24 | 78 | 1 | 78 | license: MIT・substitutable 記載あり |
| Dapper | **0** | 3 | 3 | 14 | 0 | 6 | substitutable: **false** |
| Serilog | **0** | 0 | 3 | 5 | 2 | 3 | exact ピン(SB-F-01) |
| Avalonia | 9 | 1 | 16 | 36 | **55** | 24 | substitutable: false(ADR-0001 基盤) |

## 発見(1 例目と質的に異なる 4 点)

**F1: contract 層出現には「管理された provenance」と「契約汚染」の区別が要る。**
SkiaSharp の contract 11 件の中身は全て `external_source:`(出所トレース — 方法論が**要求する**参照)、
版 exact ピンの宣言、hash_adapter(P-09)世代識別子の定義 — つまり**依存を認識して管理する記述**であり
naive な「汚染」ではない。1 例目の procurement 区分(正常・数えない)の契約層版。leak 分類には
provenance(宣言された依存管理= 正常)の区分を追加する必要がある。

**F2(中心): oracle leak には除去不能な型があり、応答は「宣言された結合」への変換である。**
SkiaSharp の oracle 82 件の実体は 2 態: (a) 治具実装が部品を使う(pHash 計算等 — 治具も交換影響を
受ける= Loop6「治具も劣化対象」の静的版)(b) **期待値そのものが部品挙動の関数**(pHash 値は
decode 経路込みで定義される)。(b) は grep で消せる漏れではない — ViewPrism2 の実応答は
除去でなく**管理**だった: 版 exact ピン+hash_adapter 世代識別子+補間方式の仕様固定。
**較正(match 2 件目)**: この oracle 結合は ECO-054 で実コスト化した(adapter= decode 経路変更で
類似度スコアが系統的 -18 → 是正+世代管理の導入)。oracle leak 82 は ECO-054 の痛みを事前に
予測できていた指標である。交換設計の第 3 類型= **再認証つき移行**(トナー類比: 純正前提の
色校正 — 交換可能だが再校正が交換手順に含まれる)。

**F3: substitutable(宣言)は「方針」であって「コスト」ではない。**
Dapper は宣言 substitutable: false だが、実測プロファイルは contract 0・K/M-contained= **交換コスト最軽量級**。
false の実態は「ADR で決めたから変えない」(方針)であり「交換できない」(コスト)ではない。
53/32 のフィールド設計では **exchange_policy(方針)と measured_exchange_cost(実測)を別フィールド**に
すべき — 混同すると「方針 false だからコスト測定不要」という誤読を生む。

**F4: 識別子希薄部品は grep 偽陰性型。**
Dapper の構文は拡張メソッド(Query/Execute)で部品名を運ばない — src 14 は using/コメント程度で、
実際の使用面は識別子に現れない。1 例目 F1(部品同定の粒度)の裏面: 識別子集合の宣言だけでなく
「**この部品は識別子で測れない**」という測定可能性の宣言も要る(測れない部品は境界 interface
(ADR-0003 の抽象)で代替測定)。

## 交換クラスの 3 類型(2 例の統合)

| 類型 | プロファイル | 実証 |
|---|---|---|
| K/M-contained(軽) | contract 0・oracle 0(または治具実装のみ) | LibraryLending プロバイダ交換(match 1)・Dapper・Serilog |
| oracle-coupled(再認証つき移行) | 期待値が部品挙動の関数 | SkiaSharp(ECO-054 で実コスト実証= match 2) |
| structural(構造材・再製造級) | UI 定義層(axaml 等)へ大量拡散 | Avalonia(axaml 55・全層拡散) |

## A(53 テンプレ拡張)への設計入力(2 例で揃った)

- ViewPrism2 の procurement は**既に license: / substitutable: を持つ**(A の in-repo 前例)。
  欠けているのは maintenance freshness と、方針/実測コストの分離。
- フィールド候補の改訂: exchangeability は単値でなく
  `exchange_policy(fixed|substitutable)` + `measured_leak(none|contained|oracle-coupled|structural)` +
  `identifier_set(grep パターン or not-greppable 宣言)` + `last_measured_at` の組。
- license/commercial/freshness は 1 例目の記帳どおり(非挙動劣化)。

## 限界

N=2・遡及・出現数は行数ベース(意味重み無し)・Dapper/Avalonia は「交換しなかった部品」なので
コスト側の較正は SkiaSharp(ECO-054)経由のみ・分類の layer 写像はディレクトリ規約ベース。
