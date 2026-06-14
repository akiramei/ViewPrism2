# BomDD Capability Discipline(工程能力の規律)

> 起案 2026-06-14。P-07 を「観測のみ→target+片側 Cpk」に格上げした実験(42-exploratory-probes P-07・tests/ViewPrism2.Oracle/P07CapabilityProbe.cs)と、その助言レビューから抽出した方法論。
> **輸入するのは Cp/Cpk という数式ではなく、能力を定義し・同一治具で反復測定し・A/B 比較可能にする規律。** 既存の「二軸品質(決定性×正しさ)」と 52-metrics を、測定可能な生産技術へ展開する言語。

## 中心命題

測っているのは「成果物の性能」だけではない。**製造装置としての AI の工程能力**——同じ charter/K-BOM/oracle/harness を与えたとき、AI 工場がどれだけ安定して・正しく・効率よく・再現性のある成果物を作れるか——である。P-07 の Cpk は**その測定治具の 1 つ**であって、Factory Capability そのものではない。

## 能力の 3 階層(指数を全特性へ広げない)

### 第1層 Artifact Correctness Capability(正しさ)
- 手段: **固定オラクル / 不変条件 / 回帰スイート**(pHash 16hex 一致・id 列一致・schema 同値・±1px・INV-012 緑・regression なし)
- 性質: **go/no-go ゲージ**。正しいか壊れているか。**ここに指数を入れない**(統計的見せかけになる)。

### 第2層 Artifact NFR Capability(成果物の連続量)
- 対象: latency / throughput / memory / peak RSS / allocation / GC pause / scale / cache warm-cold。**P-07 はここ。**
- 記録: μ・σ・CV・p50・**p95・p99・max**・peak memory・allocation・GC count/pause・**片側 Cpk**。
- 判断の役割分担(P-07 実測で確定):
  - **μ = 改善の主指標・A/B 選択の主レバー**
  - **p95/p99/max = 実運用リスク(tail)**
  - **memory = latency とは別能力**
  - **Cpk = USL 近傍/不安定実装を弾く資格ゲート・警告灯**(ランキング指標としては弱い)

### 第3層 Factory Loop Capability(製造装置としての AI)— 本来の問い
- 手段: **歩留り系**(Cp/Cpk ではない)。First-pass acceptance rate / Green-within-N-loops / Blocker cheat DPMO / Regression rate / Intervention count / Targeted-fix success / Fresh-regeneration pass / **K-BOM uplift rate**。
- これが「この AI 工場構成は、この BOM から、どの歩留りで、どの能力の成果物を、どれだけ少ない介入で製造できるか」を測る。

## Cpk の正しい使いどころ(P-07 実測の教訓)

- **決定的に近い性能測定は σ が小さくなりやすく Cpk は容易に巨大化する**(P-07: μ=1633ms・σ=18ms・Cpk=25.1)。→ Cpk は速度フロンティアを示さない。
- Cpk が**強い**のは: μ は速いが σ が大きい / p95・max が跳ねる / GC でたまに崩れる / メモリ圧で不安定 / 平均が USL 近傍。
- BomDD 固有の最良の住所候補: **第3層の "BOM 進化で安定性が劣化したか" の番兵**(synthesize 後の factory-C が同 μ でも Cpk 低下=σ 拡大なら統合が不安定を持ち込んだ証拠。μ では隠れる)。
- 教訓の核: 「Cpk は万能でない」ではなく「**Cpk を入れたことで、使うべき場所と使うべきでない場所が分かった**」。能力設計(どの特性・どの上限を管理するか)を強制すること自体が成果。

## A/B 生産技術(判定は単一スコアでなく Pareto)

固定オラクルは pass/fail しか言えず、正しさが同等な A/B を序列化できない。**capability がその判定関数群**。ただし主判定は Cpk 単体でなく、必須+比較の組:

- **必須条件**: oracle green / regression なし / USL 内 / Cpk が最低限割れない
- **比較条件**: cold μ / p95・max / peak memory / 実装の単純さ / **K-BOM へ抽出しやすいか**
- **結論の 4 分類**:
  - `select` — A または B を勝ち技術として採用
  - `reject` — oracle 赤・能力崩壊・仕様逸脱
  - `synthesize` — A/B それぞれの勝ち技術を K-BOM に統合
  - `defer` — 差が小さい / n 不足 / 原因分離不能
- **追加(tension)**: A の勝因と B の勝因が**両立不能**(例: A の latency 勝因=多くメモリ保持・B の memory 勝因=保持しない)なら synthesize 不可 → 製品目的で Pareto 選択。

### A/B の前提条件 — 同等性契約のオラクル分離(**2026-06-14 実施済み**)
性能 A/B を始める前に、**横断ゲートと工場固有を分離**した。精査の結果、過剰拘束は想定より狭く外科的だった:
- CPOL-103: pHash は preserve_with_adapter(ビット一致不要・検索体験の同等で可)
- 真に "このビルドの pHash 値" に依存していたのは **S-19 の単色 exact 16hex(0x8000…)だけ**。
  S-21/22/23/24 は pHash を**注入/論理のみ**で既に工場非依存、S-20 は spec 化された変換で横断的だった
  (当初「S-21 が特定結果を凍結」と見積もったが、S-21 は features を直接注入しておりエンジン論理の横断テスト)。
- **実施した分割**(41-fixed-oracle.yaml に scope 付与):
  - **(a) 横断正しさ** — S-19(決定性・距離の順序・類似/非類似の分類)+ **S-25 新設(順位等価オラクル・EQ-RANK)**:
    実 PHashImageReader 経由で「近傍=同一構造の再エンコード/微小変化が無関係より上位」を凍結。pHash 値に非依存。
  - **(b) このビルドの値** — **S-19b 新設**(単色=0x8000…・scope=this-build・横断ゲートにしない)。
- これで factory-B(DecodeToWidth 等)は、自前の pHash 値で S-19b を満たさなくても、**S-19/S-25(順位等価)を
  満たせば正しいと判定できる**。A/B の正しさゲートが工場間で共有可能になった(= A/B 着手の前提が整った)。

### 実 A/B 第1回 結果(2026-06-14・P-08)— 方法論の実証
前提(上記の同等性契約分離)が整ったので、単一仮説 **factory-B=DecodeToWidth 早期縮小**(SKCodec scaled-decode)を
別エージェント(Codex)に隔離製造させ、同ハーネスで μ 対決した(tests/ViewPrism2.Oracle/ABDecodeStrategyProbe.cs)。

- **correctness(必須条件)**: 代表サイズ 1280×960 で **両工場とも EQ-RANK 緑**(threshold 緩めの純粋順位)。回帰 59/59。
- **μ 対決(40 枚 2000×1500 jpeg)**: factory-A μ=506ms / **factory-B μ=80ms = 6.29× 高速**。σ_B=5.1<σ_A=12.3。
- **判定 = `select`(factory-B 技術を採用)**: Pareto 支配(μ・tail・安定性すべて B 優位)・correctness 保存・**tension なし**
  (早期縮小は decode 画素自体が減るため latency 勝因が memory/安定性を犠牲にしない)。synthesize 不要(B は A の上位互換)。
- **codify**: K-SKIA v3.1 に勝ち技術を織り込み(コード結合でなく fresh 再製造で native 採用)。

### P-09 production adoption(2026-06-14)— A/B の最後の一周
P-08 の select を **production adapter 世代交代**として接続した(単純な DI 差し替えでなく、cache/oracle/golden/index の再整備)。
- **adapter version**: IPHashImageReader.AdapterId(full=skia-full-decode-v1 / scaled=skia-scaled-decode-v1)。production DI を scaled-decode へ。
- **混在禁止**: image_features.hash_adapter(migration 003)+ freshness に adapter 一致を要求 → 旧値は自動無効化・連鎖無効化で旧類似度も purge。
- **2 層オラクル**: cross-factory(EQ-RANK・順位)/ this-build(production adapter の exact 値=ThisBuildGoldenTests・b7ff8800d0de12d5)を CI 分離。
- **latency guard**: scaled-decode ≥2× full-decode(default-on・相対比・machine 非依存)。
- 結果: 全緑(Tests 347 / Oracle 63)・回帰ゼロ・旧 cache/index 無効化を実テストで確認。
**生産技術の確証**: A/B(lesson 化)→ production adoption → oracle/golden/cache 移行まで**一周**できた。adapter version を成果物に刻むことで、
**pHash 値が動く性能改善を golden/cache を壊さず投入できる**(検索体験の契約=順位 と this-build の値を分離した step 1 が世代交代で効いた)。

**方法論が裏取りされた 3 点**:
1. **μ=A/B 選択の主レバー**(P-07 の発見)— Cpk は両工場巨大で序列化不能、μ だけが 6.29× の差を出した。
2. **Cpk=安定性の番兵**(警告灯)としてのみ機能 — σ_B<σ_A を見て「B が σ を膨らませていない(synthesize 健全)」を確認できた。
3. **adapter drift 16.6/64 が大きい**ことが、横断契約を**順位ベース**にした step 1 の判断を裏付けた(早期縮小は絶対値を動かすが順位は保存)。

## 統合 = コード結合でなく知識結合(BomDD 中核思想)

普通の A/B は「A と B のコードを見比べて手でマージ」。BomDD でそれをやると工場隔離・再現性・決定性が壊れる。だから:
- ×「勝ったコードを採る」
- ○「**勝った技術仮説を K-BOM に戻し、fresh 工場で再製造**」(既存の「ずる→BOM 強化→再走」の一般化)
- **コスト**: synthesize は第三の工場 factory-C と再測定を要する(lesson が干渉しうる)。タダではない。

## P-07 Capability Contract(第2層の整備例)

```
Correctness:   固定オラクル green / pHash 同等性契約 / 検索結果(順位)同等
Primary:       cold 1,000 images decode+pHash latency
  ranking metric: μ   |  USL: 3000ms(lower is better)
  guard: 片側 Cpk・p95・max  |  n: 20  |  run order: randomized / ABBA
Secondary:     warm cached lookup / peak memory / allocation / GC count・pause
gate_policy:   ゲートにしない・報告する(少数サンプル・偽精度回避)
baseline(factory-04, 2026-06-14): μ=1633ms σ=18ms Cpk=25.1 / warm 11.5ms / mem 3.7MB
```

## Factory Capability Dashboard(第3層・既存データで seed 可能)

各 factory run をこの形で記録すると 52-metrics の自然な発展形になる:
```
Run identity:     factory id / BOM version / model・toolchain version / oracle version
Correctness:      acceptance / regressions / invariant failures
Loop capability:  first-pass green(y/n) / loops-to-green / interventions /
                  blocker cheats / friction cheats / targeted-fix success
Artifact cap.:    P-07 μ / Cpk / p95・max / memory
Knowledge:        K-BOM lessons extracted / fresh-regeneration pass / uplift over prev BOM
```

### 本プロジェクト実測 seed(V1/V2/V3 既存ログから・新規ラン不要)
| loop | factory_runs | first-pass green | blocker/総 cheat | intervention | oracle | golden rounds |
|---|---|---|---|---|---|---|
| V1 loop-v1-core | 5(+ECO-002) | no | 0/71 | 0 | 20/20 | — |
| V2 loop-v2-viewer | 2(収束1) | no | 0/14 | 0 | 31/31 | 2 |
| **V3 loop-v3-similarity** | **1** | **yes** | **0/6** | 0 | 57/57 | 1 |

**観測される uplift**: V1(多ラン+ECO)→ V2(収束 1 回)→ V3(first-pass green・質問ゼロ)。BOM/K-BOM の知識蓄積 + G2/G3 ゲートが製造前に欠落を潰した効果と説明できる = **Factory Loop Capability の実測上昇**。
**正直な限界**: n=1/ループでは Factory Capability の "ばらつき"(同一 BOM への複数工場の σ=真の Factory Cpk)は測れない。既存データで取れるのは**ループ横断トレンド**であって**ループ内分散ではない**。真の Factory Cpk はマルチファクトリ実走が要る。
**P-08 で初のマルチファクトリ実走**: 同一特性(decode 経路)に対し 2 工場(A=Claude / B=Codex 隔離製造)を立て、同等性契約+μ で序列化できた。これは「複数工場の能力比較」という Factory Capability の核心動作の最小実証(各 n=1 だが工場数=2)。Codex を別工場として使うと **A/B の隔離純度が上がる**(同一 BOM・異なる製造装置)ことも確認。

## 導入方針(まとめ)

```
決定的正しさ:   固定オラクル(go/no-go・指数なし)
成果物 NFR:     μ / tail(p95,max)/ memory + 片側 Cpk(資格ゲート)
AI 工場能力:    first-pass yield / cheat rate / loops-to-green / regression rate / K-BOM uplift
A/B 統合:       コード結合でなく、勝ち技術を K-BOM へ戻して fresh 再製造(select/reject/synthesize/defer/tension)
```

工程能力指数は外来概念でなく、既にある「二軸品質」と 52-metrics を生産技術へ展開する言語として入れる。
