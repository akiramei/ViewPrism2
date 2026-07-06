# Change Order — ECO-054(applied): scaled-decode のフォーマット間系統誤差 — 経路対称化(P-09 世代交代を伴う品質改善)

> ECO-048 golden 実施時の R3 所見 #2(51-cheat-log 2026-07-06)から昇格起票。maintainer 所見
> 「png→jpg 変換複製が 78 点。直感では 90 以上のはず」— 実測切り分けで直感が正と確定済み。

## 1. 症状(2026-07-06 実測済み — ECO-048 golden 時)

- 見た目同一の PNG→JPG 変換複製ペア(orientation_fixture_06.png × orientation_fixture_06.jpg)が類似検索で **78 点**(距離 8)。
- 切り分け実測(4 象限):

  | 比較 | 距離 | スコア |
  |---|---|---|
  | production(scaled-decode)で jpg×png | 8 | 78 |
  | full-decode で jpg×png | **2** | **96**(=知覚的実距離) |
  | 同一 jpg の scaled×full(経路差) | 8 | 78 |
  | 同一 png の scaled×full(経路差) | **0** | 100 |

  → **誤差の全量が経路非対称に由来**: JPEG は SKCodec の DCT ネイティブ縮小(1/8 等)で中間 ~64px を
  経るが、PNG はスケール非対応のため**全解像度→一発 32×32 双線形**。異フォーマット複製ペアは
  系統的に約 -18 点を被る。

## 2. 工程診断 — K-BOM/仕様の沈黙次元(実装は規定に忠実)+P-08 の未測定側面

| 工程 | 判定 | 根拠 |
|---|---|---|
| 実装 | **K-BOM v3.1 に忠実(逸脱なし)** | PHashImageReaderScaledDecode は 31-kbom L43 の手順どおり(スケール非対応 codec は原寸 decode → 共通の最終 32×32) |
| K-BOM/仕様 | **沈黙次元** | L43 は「codec サポート寸法に丸め」とだけ規定し、**スケール非対応フォーマットでも中間縮小段を通すか(経路一貫性)を未規定**。adapter drift 注記は full↔scaled 間のみで、**同一 adapter 内のフォーマット間非対称**は未認識 |
| P-08(adapter 選定) | **未測定側面** | 速度(6.29×)と EQ-RANK(順位等価)は実測したが、**異フォーマット複製ペアの絶対スコア**は測定外だった |
| CAD | 無関係 | 表示・操作の変更なし |

- 是正は仕様上の欠陥是正ではなく**品質改善(裁定案件)**: コストが大きい(§3 — P-09 世代交代=
  全特徴量再計算+this-build golden 再凍結)ため gate① で採否を裁定する。

## 3. 切り分け済みの事実(確定と未検証を分離)

確定:

- **是正=経路対称化**: スケール非対応フォーマット(PNG/BMP 等)にも**中間縮小段(長辺 ~64px・
  双線形・短辺 32 クランプ= JPEG 経路と同一規則)**を挟む。JPEG 経路は不変。
- **P-09 世代交代が正規の伝搬機構**: AdapterId `skia-scaled-decode-v1` → `-v2`。既存 image_features は
  adapter 不一致で stale → 初回検索時に自動再計算+関与ペア連鎖削除(ECO-048 変種も同一行で再計算)。
  DB migration 不要(値の世代管理は既存機構)。
- **this-build golden の再凍結は台帳ライセンス済み**(R6 と衝突しない): ThisBuildGoldenTests 自身と
  CP-PHASH-ADAPTER-019(33:509)が「adapter 世代交代ごとに再凍結」を宣言。しかも fixture は
  512 JPEG q90= **JPEG 経路不変につき StructuredGolden 値は不変見込み**・単色 PNG(0x8000…)は
  縮小不変で不変 → **Oracle 側の実改変は ProductionAdapterId 定数 1 行のみ**の見込み(着手時実測)。
- 影響を受けない凍結面: S-19/S-20(Core 純関数)・S-21/S-22(fake reader)・S-40(変種契約)・
  S-25(EQ-RANK=順位等価 — 対称化は分離を改善する方向。着手時に実測確認)。
- full-decode reader(skia-full-decode-v1)は元から経路対称(常に原寸→一発 32×32)= 不変。
- 期待効果の上限は実測済み: jpg×png 複製 78 → **96 点**(full-decode 実測が証拠)。同一フォーマット
  ペアは不変(経路内対称のため)。

未検証(着手時プローブで実測):

- 回転複製(70 点台 — ECO-048)の改善幅: 縦横比入替+再エンコード由来のノイズが残るため
  **限定的の見込み**(過度に期待しない)。
- 全画像再計算の所要(初回検索の体感)— ECO-048 世代アップグレード時と同規模の見込み。
- 中間縮小段追加による PNG 側のレイテンシ増(全解像度 decode は元から発生・追加は ~64px への
  resize 1 回= 微小の見込み。PHashLatencyGuardTests の相対比ガードで機械確認)。

## 4. 是正方針(案 — gate① 裁定で確定)

| 案 | 内容 | コスト | 効果 |
|---|---|---|---|
| **A: 経路対称化(推奨)** | スケール非対応フォーマットに中間縮小段(JPEG と同一規則)。AdapterId v2・P-09 自動再計算・ThisBuildGolden の AdapterId 定数更新(値は不変見込み) | 中(reader 1 箇所+初回検索の全再計算)| 異フォーマット複製 78→96(実測済み上限)。既定 70 どころか 80 でも拾える帯へ |
| B: 見送り | 現状維持(順位等価は保たれ・しきい値 70 で実用上は拾えている) | 0 | なし(絶対スコアの直感乖離が残る) |
| C: full-decode へ回帰 | 6.29× 高速化の放棄 | 大(体感劣化) | 非推奨 |

- A のプローブ(R5): 合成 png/jpg 複製ペアの距離が現行 reader で大きい(スコア 70 台)ことを
  是正前に不合格テストで実測 → 対称化後に小距離(90 台)へ転化。
- A の台帳同期: K-BOM v3.1 追補(経路一貫性の規定=「中間縮小段は codec スケール対応の有無に
  よらず通す」)・spec §2.10 注記・33(adapter id 表記 v2)・41 新規行(S-42: フォーマット間
  経路一貫性 — 性質ベース)。

## 5. 影響 BOM(案 A)

- impacted: M-SIMSEARCH-021(PHashImageReaderScaledDecode)/ E-SIMSEARCH-032・E-SIMCACHE-033
  (世代交代の運用)/ 31-kbom K-SKIA v3.1 追補 / 20-spec §2.10 注記 / 33 CP-PHASH-ADAPTER-019(表記)/
  41(新規行 S-42)/ tests/ViewPrism2.Oracle/ThisBuildGoldenTests.cs(**AdapterId 定数 1 行 —
  台帳ライセンス済みの再凍結手続き。golden 値は不変見込み・変わった場合は停止して報告**)。
- 不変: Core 全域(PerceptualHash/変種/距離/スコア)・full-decode reader・DB スキーマ・UI・CAD。

## 6. 残ゲート

1. ~~**gate①(裁定・human)**: 案 A/B/C の選択~~ → **受領: 案 A**(maintainer 2026-07-06)
2. ~~/eco-fix eco-054 — プローブ → 対称化 → 機械受入~~ → 完了(§7)
3. **golden(maintainer 実機)**: §7 末尾の基準。
4. クローズ時: CP 観点明記+register applied+教訓。

## 7. 実施記録(2026-07-06 — 案 A 実装・機械受入完了・golden 待ち)

- **プローブの較正過程(R5 — 初回プローブは不合格にならず差し戻し規律を適用)**: 合成の同一サイズ・
  勾配ペアでは現象が再現せず(560/560 全緑)、規律どおりコードに触らず診断精密化へ。再現マトリクス
  3 round(コンテンツ×解像度差×アスペクト)で機構を特定 — **発散源は JPEG 側**(DCT 事前縮小=面積平均
  vs 全解像度直行=点サンプル。実ファイル経路間: jpg=8・png=0)。高周波成分を持つ内容でのみ顕在化
  (busy 合成: 経路間 34・ペア scaled=30/full=24)。**成立したプローブ**= busy ペア+性質
  「scaled のフォーマット間距離 ≤ full の距離+2」— 是正前不合格(30 > 26)を実測。
- **設計の実測選定(プロトタイプ 2 案を実ペア+合成 2 種で比較)**:
  | ペア | 現行 | pngOnly | **uniform(採用)** | full |
  |---|---|---|---|---|
  | 実ペア orientation_fixture_06 | 8(78 点) | 4 | **2(96 点)** | 2 |
  | art 合成 | 0 | 4(逆向き非対称で悪化) | **2** | 2 |
  | busy 合成 | 30 | 14 | **14(full の 24 より良い)** | 24 |
  pngOnly(起票時の想定)は逆向き非対称を作る欠陥があり不採用。**uniform**(全フォーマット一様:
  decode 後の長辺 >64 なら Mipmap Linear=面積平均近似で長辺 ~64 の中間段 → 共通の最終 32×32)を採用。
- **起票時見込みからの変更と再確認**: uniform は JPEG 経路も変え得るため「golden 値不変見込み」の前提が
  変わったが、**capture 実測で StructuredGolden 値は不変と確認**(512² fixture は codec 縮小でちょうど
  長辺 64 に到達し中間段が非発動)。Oracle の実改変は **ProductionAdapterId 定数 1 行のみ**
  (台帳ライセンス= CP-PHASH-ADAPTER-019「世代交代ごとに再凍結」・値の同一性を実測記録=教訓 3 の適用)。
- **是正**: PHashImageReaderScaledDecode — 一様中間段(GetIntermediateSize: 長辺 64・短辺 32 クランプ・
  拡大禁止・AwayFromZero)+ AdapterId `skia-scaled-decode-v2`(P-09 で旧特徴量は自動再計算・連鎖無効化。
  migration 不要)。Core・full-decode reader・UI は不変。
- **台帳同期**: K-SKIA v4.1 追補(経路一貫性の規定)・spec §2.10.3(v2 表記)・33(v2+S-42 ベクタ)・
  41 新規行 S-42(フォーマット間経路一貫性 — 性質ベース 2 種)+ Oracle S42FormatConsistencyTests 新設。
- **是正後の実測**: 実ペア orientation_fixture_06.png×jpg= **距離 2=96 点(full-decode と同値)**。
- **機械受入(4 点全緑+selftest)**: build 0/0・**Tests 561/561**(558+3: busy 性質=プローブ合格転化・
  勾配 scaled/full 対照)・**Oracle 109+2skip**(107+S-42 2 件。ThisBuildGolden= AdapterId v2 で全緑・
  golden 値不変・単色不変・EQ-RANK/S-25 回帰ゼロ・latency ガード緑)・validate_bom 0-0・selftest OK。
- diff: src 1 ファイル(reader)・tests 2(CP 1 新設+Oracle 1 新設)・Oracle 定数 1 行・bomdd 4。

### golden 合格基準(gate② — maintainer 実機)

1. **orientation_fixture_06.png × orientation_fixture_06.jpg が 90 点台(実測 96)で検出される**: どちらかを基準に類似検索
   (既定 70 のまま)→ 相手が 96 点で結果に出る。※初回検索は全画像の特徴量再計算(adapter v2 への
   世代交代)が走るため時間がかかります(2 回目以降は通常速度)。
2. 回帰: 回転複製(rot90)・鏡像複製(mirror)が引き続き検出される(ECO-048 の検出面の保存)。
3. 回帰: 無関係画像の混入なし(スコア序列の体感が崩れていない)。

## 8. クローズ(2026-07-06 golden 合格)

- **maintainer 実機確認(基準超え)**: png×jpg= 96%(基準どおり・是正前 78)・**rot90= 100%**(基準は
  「引き続き検出」だったが 70 台→満点へ改善 — 回転 jpg も中間段を通り基準 png と同質の入力が
  最終 32×32 に届くため、ECO-048 の変種照合が距離 0 で一致= 2 ECO の相乗効果)・mirror= 96%・
  無関係混入なし(結果 3 件のみ・しきい値 70= ECO-050 既定)。
- **再発防止**: 機械= S-42(経路一貫性の性質 2 種)+CP-PHASH-ADAPTER-019 ベクタ。golden= CP-UI-G9 に
  「異フォーマット複製 90 点台」観点(潜伏実績つき)。K-SKIA v4.1 に経路一貫性を規定(沈黙次元の解消)。
- **M4**: fix 時に K-BOM/spec/33/41 同期済み+accept で E-SIMCACHE-033 に v2 世代交代の as-built 追記。
- **教訓(一般形・昇格候補)**: **部品交換(A/B 選定)の同等性契約は「不変量の保存」と「誤差の系統性」の
  両輪で測る** — P-08 は速度と順位等価(EQ-RANK)を実測したが、誤差が入力クラス(フォーマット・
  高周波成分)で系統的に偏ることは測定外だった。ランダム誤差は順位等価で吸収できるが、**系統誤差は
  特定の入力クラスの体験だけを静かに劣化させる**(異フォーマット複製の -18 点)。同等性受入には
  「同一入力の経路間距離を入力クラス別に測る」対照群を含める。
  副教訓 2 件: ①**R5 差し戻し規律は診断を深める装置** — 初回プローブの不再現が「差し戻し→機構特定
  (発散源は JPEG 側・高周波でのみ顕在化)→より強い性質プローブ+設計選定」に繋がった(不合格に
  ならないプローブを黙って弱めてはならない)。②**設計はプロトタイプの実測比較で選ぶ** — 起票時想定
  (pngOnly)は実測で逆向き非対称を作ると判明し不採用(ECO-038「真因構造そのものを消す案を優先」の
  実測版 read-across)。
