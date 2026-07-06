# Change Order — ECO-054(staged): scaled-decode のフォーマット間系統誤差 — 経路対称化(P-09 世代交代を伴う品質改善)

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

1. **gate①(裁定・human)**: 案 A/B/C の選択(§4・推奨 A)。A は P-09 世代交代
   (初回検索の全再計算)と this-build golden 再凍結手続きの承認を含む。
2. 裁定後: /eco-fix eco-054 — プローブ(png/jpg 複製の 70 台を是正前実測)→ 対称化 → 機械受入
   (ThisBuildGolden 値が動いた場合は停止して報告)。
3. golden(maintainer 実機): orientation_fixture_06.png × orientation_fixture_06.jpg が 90 点台で検出+既存検出の回帰なし
   (初回検索は再計算で時間がかかる旨を基準に明記)。
4. クローズ時: CP 観点明記+register applied+教訓。
